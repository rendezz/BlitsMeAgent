﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Gwupe.Communication.P2P.P2P.Socket.API;
using Gwupe.Communication.P2P.P2P.Tunnel;

namespace Gwupe.Communication.P2P.P2P.Socket
{
    public interface ITunnelEndpoint : ISocket
    {
        PeerInfo Wave(IPEndPoint facilitator);
        IPEndPoint Sync(PeerInfo peer, String syncId, List<SyncType> syncTypes);
        IPEndPoint WaitForSync(PeerInfo peer, String syncId, List<SyncType> syncTypes);
    }
}
