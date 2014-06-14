using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "BroadcastProfileHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class BroadcastProfileHeader : BroadcastHeader<BroadcastProfileHeader>
    {
        public BroadcastProfileHeader(DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(creationTime, key, miner, digitalSignature)
        {

        }
    }
}
