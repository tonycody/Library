using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ProfileHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class ProfileHeader : BroadcastHeader<ProfileHeader>
    {
        public ProfileHeader(DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(creationTime, key, miner, digitalSignature)
        {

        }
    }
}
