using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiPageMetadata", Namespace = "http://Library/Net/Outopos")]
    public sealed class WikiPageMetadata : Metadata<WikiPageMetadata, Wiki>
    {
        public WikiPageMetadata(Wiki tag, string signature, DateTime creationTime, Key key, Miner miner)
            : base(tag, signature, creationTime, key, miner)
        {

        }
    }

    [DataContract(Name = "WikiPageHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class WikiPageHeader : Header<WikiPageHeader, WikiPageMetadata, Wiki>
    {
        public WikiPageHeader(WikiPageMetadata metadata, DigitalSignature digitalSignature)
            : base(metadata, digitalSignature)
        {

        }
    }
}
