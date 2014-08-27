using System;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiDocumentMetadata", Namespace = "http://Library/Net/Outopos")]
    sealed class WikiDocumentMetadata : MulticastMetadata<WikiDocumentMetadata, Wiki>
    {
        public WikiDocumentMetadata(Wiki tag, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(tag, creationTime, key, miner, digitalSignature)
        {

        }
    }
}
