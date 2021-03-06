﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using Gwupe.Cloud.Messaging.Elements;

namespace Gwupe.Cloud.Messaging.Response
{
    [DataContract]
    public class LoginRs : API.Response
    {
        public override String type
        {
            get { return "Login-RS"; }
            set { }
        }
        [DataMember]
        public bool loggedIn { get; set; }
        [DataMember]
        public String profileId { get; set; }
        [DataMember]
        public String shortCode { get; set; }
        [DataMember]
        public UserElement userElement { get; set; }
        [DataMember]
        public PartnerElement partnerElement { get; set; }
        [DataMember]
        public String guestSecretKey { get; set; }
    }
}
