using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiPageHeader", Namespace = "http://Library/Net/Outopos")]
    sealed class WikiPageHeader : MulticastHeader<WikiPageHeader, Wiki>
    {
        public WikiPageHeader(Wiki tag, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(tag, creationTime, key, miner, digitalSignature)
        {

        }
    }
}
