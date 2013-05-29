﻿using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using BlitsMe.Agent.Components.Chat;
using BlitsMe.Agent.Components.Functions.API;
using BlitsMe.Agent.Components.Functions.FileSend;
using BlitsMe.Agent.Components.Functions.RemoteDesktop.Notification;
using BlitsMe.Agent.Components.Notification;
using BlitsMe.Cloud.Messaging.API;
using BlitsMe.Cloud.Messaging.Request;
using BlitsMe.Cloud.Messaging.Response;
using log4net;
using System.Runtime.InteropServices;



namespace BlitsMe.Agent.Components.Functions.RemoteDesktop
{
    internal delegate void RDPSessionRequestResponseEvent(object sender, RDPSessionRequestResponseArgs args);
    internal delegate void RDPIncomingRequestEvent(object sender, RDPIncomingRequestArgs args);

    class Function : IFunction
    {
        [DllImportAttribute("User32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImportAttribute("User32.dll")]
        static extern bool SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
        [DllImportAttribute("User32.dll")]
        static extern bool IsWindow(IntPtr hWnd);

        private readonly BlitsMeClientAppContext _appContext;
        private readonly Engagement _engagement;

        private static readonly ILog Logger = LogManager.GetLogger(typeof(FileSend.Function));
        private Process _bmssHandle = null;

        internal Function(BlitsMeClientAppContext appContext, Engagement engagement)
        {
            _appContext = appContext;
            _engagement = engagement;
        }

        // event handler to get an acceptance of RDP Session
        internal event RDPSessionRequestResponseEvent RDPSessionRequestResponse;
        internal event RDPIncomingRequestEvent RDPIncomingRequestEvent;

        internal event EventHandler RDPConnectionAccepted { add { Server.ConnectionAccepted += value; } remove { Server.ConnectionAccepted -= value; } }
        internal event EventHandler RDPConnectionClosed { add { Server.ConnectionClosed += value; } remove { Server.ConnectionClosed -= value; } }

        internal void OnRDPIncomingRequestEvent(RDPIncomingRequestArgs args)
        {
            RDPIncomingRequestEvent handler = RDPIncomingRequestEvent;
            if (handler != null) handler(this, args);
        }

        internal void OnRDPSessionRequestResponse(RDPSessionRequestResponseArgs args)
        {
            RDPSessionRequestResponseEvent handler = RDPSessionRequestResponse;
            if (handler != null) handler(this, args);
        }

        internal void ProcessIncomingRDPRequest(String shortCode)
        {
            bool bExists = false;
            // Loop through existing notifications to see if we already have a remote desktop request
            // from the SecondParty. If the .From and Type of the notification mathc the SecondParty.UserName
            // and RDPNotification type
            foreach (TrueFalseNotification n in _appContext.NotificationManager.Notifications)
            {
                if (n.AssociatedUsername == _engagement.SecondParty.Username && n.GetType() == typeof(RDPNotification))
                {
                    // if the notification exists set flag.
                    bExists = true;
                }
            }
            // only process this notification if they don't already exist AND 
            // we are not currently in a remote session
            if (!bExists && !Server.Started)
            {
                // Set the shortcode, to make sure we connect to the right caller.
                _engagement.SecondParty.ShortCode = shortCode;
                RDPNotification rdpNotification = new RDPNotification()
                {
                    Message = _engagement.SecondParty.Name + " would like to access your desktop.",
                    AssociatedUsername = _engagement.SecondParty.Username,
                    DeleteTimeout = 300
                };
                rdpNotification.AnsweredTrue += delegate { ProcessAnswer(true); };
                rdpNotification.AnsweredFalse += delegate { ProcessAnswer(false); };
                _appContext.NotificationManager.AddNotification(rdpNotification);
                _engagement.Chat.LogSystemMessage(_engagement.SecondParty.Name + " sent you a request to control your desktop.");
                OnRDPIncomingRequestEvent(new RDPIncomingRequestArgs(_engagement));
            }
        }

        private void ProcessAnswer(bool accept)
        {
            if (accept)
            {
                _engagement.Chat.LogSystemMessage("You accepted the desktop assistance request from " +
                                                  _engagement.SecondParty.Name);
                try
                {
                    if (_appContext.BlitsMeServiceProxy.VNCStartService())
                    {
                        Server.Start();
                    }
                    else
                    {
                        Logger.Error("Failed to start the TVN Service. FIXME - not enough info.");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to start server : " + e.Message, e);
                }
                RDPRequestResponseRq request = new RDPRequestResponseRq()
                    {
                        accepted = true,
                        shortCode = _engagement.SecondParty.ShortCode,
                        username = _engagement.SecondParty.Username
                    };
                try
                {
                    _appContext.ConnectionManager.Connection.RequestAsync<RDPRequestResponseRq, RDPRequestResponseRs>(
                        request, delegate { });
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to send a RDP acceptance request to " + _engagement.SecondParty.Username, e);
                }
            }
            else
            {
                _engagement.Chat.LogSystemMessage("You denied the desktop assistance request from " + _engagement.SecondParty.Name);
                RDPRequestResponseRq request = new RDPRequestResponseRq() { accepted = false, shortCode = _engagement.SecondParty.ShortCode, username = _engagement.SecondParty.Username };
                try
                {
                    _appContext.ConnectionManager.Connection.RequestAsync<RDPRequestResponseRq, RDPRequestResponseRs>(request, delegate { });
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to send a RDP denial request to " + _engagement.SecondParty.Username, e);
                }
            }
        }

        private Client _client;
        public Client Client
        {
            get { return _client ?? (_client = new Client(_engagement.TransportManager)); }
        }

        private Server _server;
        public Server Server
        {
            get
            {
                if (_server == null)
                {
                    _server = new Server(_engagement.TransportManager);
                    _server.ConnectionClosed += ServerOnConnectionClosed;
                }
                return _server;
            }
        }

        private void ServerOnConnectionClosed(object sender, EventArgs eventArgs)
        {
#if DEBUG
            Logger.Debug("Server connection closed, notifying end of service.");
#endif
            _engagement.Chat.LogServiceCompleteMessage("You were just helped by " + _engagement.SecondParty.Name + ", please rate his service below.");
        }

        internal void RequestRDPSession()
        {
            // Added: JH 2013-04-26
            // _bmssHandle stores the process information of the launched bmss process
            // if that is null - no session is in progress otherwise check that window is open and that its title contains BlitsMe
            
            if (_bmssHandle != null && IsWindow(_bmssHandle.MainWindowHandle) && _bmssHandle.MainWindowTitle.Contains("BlitsMe")) 
            {
                // First call SwitchToThisWindow to unminimize it if it is minimized
                SwitchToThisWindow(_bmssHandle.MainWindowHandle, true);
                // Then foreground it so that it is at the front.
                SetForegroundWindow(_bmssHandle.MainWindowHandle);
                // we are done - window acticated nothing more to do so bug out
                return;
            }
            // if we get this far then _bmssHandle is not valid so null it to be sure.
            _bmssHandle = null;
            RDPRequestRq request = new RDPRequestRq() { shortCode = _engagement.SecondParty.ShortCode, username = _engagement.SecondParty.Username };
            try
            {
                ChatElement chatElement = _engagement.Chat.LogSystemMessage("You sent " + _engagement.SecondParty.Name + " a request to control their desktop.");
                _appContext.ConnectionManager.Connection.RequestAsync<RDPRequestRq, RDPRequestRs>(request, (req, res, ex) => ProcessRequestRDPSessionResponse(req, res, ex, chatElement));
            }
            catch (Exception ex)
            {
                Logger.Error("Error during request for RDP Session : " + ex.Message, ex);
                _engagement.Chat.LogSystemMessage("An error occured trying to send " + _engagement.SecondParty.Name + " a request to control their desktop.");
            }
        }

        private void ProcessRequestRDPSessionResponse(RDPRequestRq request, RDPRequestRs response, Exception e, ChatElement chatElement)
        {
            if (e != null)
            {
                Logger.Error("Received a async response to " + request.id + " that is an error", e);
                chatElement.DeliveryState = ChatDeliveryState.Failed;
            }
            else
            {
                chatElement.DeliveryState = ChatDeliveryState.Delivered;
            }
        }

        internal void ProcessRemoteDesktopRequestResponse(RDPRequestResponseRq request)
        {
            if (request.accepted)
            {
                _engagement.Chat.LogSystemMessage(_engagement.SecondParty.Name + " accepted your remote assistance request.");
                try
                {
                    int port = Client.Start();
                    String viewerExe = System.IO.Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]) +
                                       "\\bmss.exe";
                    Logger.Debug("Checking the following location for the exe " + viewerExe);
                    _bmssHandle = Process.Start(viewerExe, "-username=\"" + _engagement.SecondParty.Name + "\" -scale=auto 127.0.0.1:" + port);

                }
                catch (Exception e)
                {
                    Logger.Error("Failed to start RDP client : " + e.Message, e);
                    throw e;
                }
            }
            else
            {
                _engagement.Chat.LogSystemMessage(_engagement.SecondParty.Name + " did not accept your remote assistance request.");
            }
        }
    }
}
