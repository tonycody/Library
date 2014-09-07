using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "SignatureMessageHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class SignatureMessageHeader : UnicastHeader<SignatureMessageHeader>
    {
        public SignatureMessageHeader(string signature, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(signature, creationTime, key, miner, digitalSignature)
        {

        }
    }
}
