﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Fiddler;
using System.Net;

namespace FiddlerExample
{
    class Program
    {
        static Proxy oSecureEndpoint;
        static string sSecureEndpointHostname = "localhost";
        static int iSecureEndpointPort = 7777;

        public static void WriteCommandResponse(string s)
        {
            ConsoleColor oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(s);
            Console.ForegroundColor = oldColor;
        }

        public static void DoQuit()
        {
            WriteCommandResponse("Shutting down...");
            if (null != oSecureEndpoint) oSecureEndpoint.Dispose();
            Fiddler.FiddlerApplication.Shutdown();
            Thread.Sleep(500);
        }
        private static string Ellipsize(string s, int iLen)
        {
            if (s.Length <= iLen) return s;
            return s.Substring(0, iLen - 3) + "...";
        }

        private static void WriteSessionList(List<Fiddler.Session> oAllSessions)
        {
            ConsoleColor oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Session list contains...");
            try
            {
                Monitor.Enter(oAllSessions);
                foreach (Session oS in oAllSessions)
                {
                    //if (oS.oResponse.MIMEType.IndexOf("application/json") > -1)
                        Console.Write(String.Format("{0} {1} {2}\n{3} {4}\n\n", oS.id, oS.oRequest.headers.HTTPMethod, Ellipsize(oS.fullUrl, 60), oS.responseCode, oS.oResponse.MIMEType));
                    //Console.WriteLine(oS.GetRequestBodyAsString());
                }
            }
            finally
            {
                Monitor.Exit(oAllSessions);
            }
            Console.WriteLine();
            Console.ForegroundColor = oldColor;
        }

        static void Main(string[] args)
        {
            List<Fiddler.Session> oAllSessions = new List<Fiddler.Session>();

            // <-- Personalize for your Application, 64 chars or fewer
            Fiddler.FiddlerApplication.SetAppDisplayName("FiddlerCoreDemoApp");

            #region AttachEventListeners
            Fiddler.FiddlerApplication.OnNotification += delegate(object sender, NotificationEventArgs oNEA) { 
                Console.WriteLine("** NotifyUser: " + oNEA.NotifyString); 
            };
            Fiddler.FiddlerApplication.Log.OnLogString += delegate(object sender, LogEventArgs oLEA) { 
                Console.WriteLine("** LogString: " + oLEA.LogString); 
            };

            Fiddler.FiddlerApplication.BeforeRequest += delegate(Fiddler.Session oS)
            {
                oS.bBufferResponse = false;
                Monitor.Enter(oAllSessions);
                oAllSessions.Add(oS);
                Monitor.Exit(oAllSessions);

                // Set this property if you want FiddlerCore to automatically authenticate by
                // answering Digest/Negotiate/NTLM/Kerberos challenges itself
                // oS["X-AutoAuth"] = "(default)";

                /* If the request is going to our secure endpoint, we'll echo back the response.
                
                Note: This BeforeRequest is getting called for both our main proxy tunnel AND our secure endpoint, 
                so we have to look at which Fiddler port the client connected to (pipeClient.LocalPort) to determine whether this request 
                was sent to secure endpoint, or was merely sent to the main proxy tunnel (e.g. a CONNECT) in order to *reach* the secure endpoint.

                As a result of this, if you run the demo and visit https://localhost:7777 in your browser, you'll see

                Session list contains...
                 
                    1 CONNECT http://localhost:7777
                    200                                         <-- CONNECT tunnel sent to the main proxy tunnel, port 8877

                    2 GET https://localhost:7777/
                    200 text/html                               <-- GET request decrypted on the main proxy tunnel, port 8877

                    3 GET https://localhost:7777/               
                    200 text/html                               <-- GET request received by the secure endpoint, port 7777
                */

                if ((oS.oRequest.pipeClient.LocalPort == iSecureEndpointPort) && (oS.hostname == sSecureEndpointHostname))
                {
                    oS.utilCreateResponseAndBypassServer();
                    oS.oResponse.headers.SetStatus(200, "Ok");
                    oS.oResponse["Content-Type"] = "text/html; charset=UTF-8";
                    oS.oResponse["Cache-Control"] = "private, max-age=0";
                    oS.utilSetResponseBody("<html><body>Request for httpS://" + sSecureEndpointHostname + ":" + iSecureEndpointPort.ToString() + " received. Your request was:<br /><plaintext>" + oS.oRequest.headers.ToString());
                }
            };


            Fiddler.FiddlerApplication.AfterSessionComplete += delegate(Fiddler.Session oS)
            {
                //Console.WriteLine("Finished session:\t" + oS.fullUrl); 
                Console.Title = ("Session list contains: " + oAllSessions.Count.ToString() + " sessions");
            };

            // Tell the system console to handle CTRL+C by calling our method that
            // gracefully shuts down the FiddlerCore.
            //
            // Note, this doesn't handle the case where the user closes the window with the close button.
            // See http://geekswithblogs.net/mrnat/archive/2004/09/23/11594.aspx for info on that...
            //
            Console.CancelKeyPress += new ConsoleCancelEventHandler(Console_CancelKeyPress);
            #endregion AttachEventListeners

            string sSAZInfo = "NoSAZ";

            Console.WriteLine(String.Format("Starting {0} ({1})...", Fiddler.FiddlerApplication.GetVersionString(), sSAZInfo));

            // For the purposes of this demo, we'll forbid connections to HTTPS 
            // sites that use invalid certificates. Change this from the default only
            // if you know EXACTLY what that implies.
            Fiddler.CONFIG.IgnoreServerCertErrors = true;
            Fiddler.CONFIG.bCaptureCONNECT = true;

            // ... but you can allow a specific (even invalid) certificate by implementing and assigning a callback...
            // FiddlerApplication.OnValidateServerCertificate += new System.EventHandler<ValidateServerCertificateEventArgs>(CheckCert);

            FiddlerApplication.Prefs.SetBoolPref("fiddler.network.streaming.abortifclientaborts", true);

            // For forward-compatibility with updated FiddlerCore libraries, it is strongly recommended that you 
            // start with the DEFAULT options and manually disable specific unwanted options.
            FiddlerCoreStartupFlags oFCSF = FiddlerCoreStartupFlags.Default;

            int iPort = 8877;
            Fiddler.FiddlerApplication.Startup(iPort, oFCSF);

            FiddlerApplication.Log.LogFormat("Created endpoint listening on port {0}", iPort);

            FiddlerApplication.Log.LogFormat("Starting with settings: [{0}]", oFCSF);
            FiddlerApplication.Log.LogFormat("Gateway: {0}", CONFIG.UpstreamGateway.ToString());

            Console.WriteLine("Hit CTRL+C to end session.");

            // We'll also create a HTTPS listener, useful for when FiddlerCore is masquerading as a HTTPS server
            // instead of acting as a normal CERN-style proxy server.
            oSecureEndpoint = FiddlerApplication.CreateProxyEndpoint(iSecureEndpointPort, true, sSecureEndpointHostname);
            if (null != oSecureEndpoint)
            {
                FiddlerApplication.Log.LogFormat("Created secure endpoint listening on port {0}, using a HTTPS certificate for '{1}'", iSecureEndpointPort, sSecureEndpointHostname);
            }

            bool bDone = false;
            do
            {
                Console.WriteLine("\nEnter a command [C=Clear; L=List; G=Collect Garbage; W=write SAZ; R=read SAZ;\n\tS=Toggle Forgetful Streaming; T=Trust Root Certificate; Q=Quit]:");
                Console.Write(">");
                ConsoleKeyInfo cki = Console.ReadKey();
                Console.WriteLine();
                switch (Char.ToLower(cki.KeyChar))
                {
                    case 'c':
                        Monitor.Enter(oAllSessions);
                        oAllSessions.Clear();
                        Monitor.Exit(oAllSessions);
                        WriteCommandResponse("Clear...");
                        FiddlerApplication.Log.LogString("Cleared session list.");
                        break;

                    case 'd':
                        FiddlerApplication.Log.LogString("FiddlerApplication::Shutdown.");
                        FiddlerApplication.Shutdown();
                        break;

                    case 'l':
                        WriteSessionList(oAllSessions);
                        break;

                    case 'g':
                        Console.WriteLine("Working Set:\t" + Environment.WorkingSet.ToString("n0"));
                        Console.WriteLine("Begin GC...");
                        GC.Collect();
                        Console.WriteLine("GC Done.\nWorking Set:\t" + Environment.WorkingSet.ToString("n0"));
                        break;

                    case 'q':
                        bDone = true;
                        DoQuit();
                        break;

                    case 'r':
#if SAZ_SUPPORT
                        ReadSessions(oAllSessions);
#else
                        WriteCommandResponse("This demo was compiled without SAZ_SUPPORT defined");
#endif
                        break;

                    case 'w':
#if SAZ_SUPPORT
                        if (oAllSessions.Count > 0)
                        {
                            SaveSessionsToDesktop(oAllSessions);
                        }
                        else
                        {
                            WriteCommandResponse("No sessions have been captured");
                        }
#else
                        WriteCommandResponse("This demo was compiled without SAZ_SUPPORT defined");
#endif
                        break;

                    case 't':
                        try
                        {
                            WriteCommandResponse("Result: " + Fiddler.CertMaker.trustRootCert().ToString());
                        }
                        catch (Exception eX)
                        {
                            WriteCommandResponse("Failed: " + eX.ToString());
                        }
                        break;

                    // Forgetful streaming
                    case 's':
                        bool bForgetful = !FiddlerApplication.Prefs.GetBoolPref("fiddler.network.streaming.ForgetStreamedData", false);
                        FiddlerApplication.Prefs.SetBoolPref("fiddler.network.streaming.ForgetStreamedData", bForgetful);
                        Console.WriteLine(bForgetful ? "FiddlerCore will immediately dump streaming response data." : "FiddlerCore will keep a copy of streamed response data.");
                        break;

                }
            } while (!bDone);
        }


        /// <summary>
        /// When the user hits CTRL+C, this event fires.  We use this to shut down and unregister our FiddlerCore.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            DoQuit();
        }
    }
}
