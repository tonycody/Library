using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiDocumentMetadata", Namespace = "http://Library/Net/Outopos")]
    public sealed class WikiDocumentMetadata : MulticastMetadata<WikiDocumentMetadata, Wiki>
    {
        public WikiDocumentMetadata(Wiki tag, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(tag, creationTime, key, miner, digitalSignature)
        {

        }
    }
}
