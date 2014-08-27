using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "SignatureMessageMetadata", Namespace = "http://Library/Net/Outopos")]
    sealed class SignatureMessageMetadata : UnicastMetadata<SignatureMessageMetadata>
    {
        public SignatureMessageMetadata(string signature, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(signature, creationTime, key, miner, digitalSignature)
        {

        }
    }
}
