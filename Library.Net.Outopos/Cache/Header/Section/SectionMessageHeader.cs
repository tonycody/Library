using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "SectionMessageMetadata", Namespace = "http://Library/Net/Outopos")]
    public sealed class SectionMessageMetadata : Metadata<SectionMessageMetadata, Section>
    {
        public SectionMessageMetadata(Section tag, string signature, DateTime creationTime, Key key, Miner miner)
            : base(tag, signature, creationTime, key, miner)
        {

        }
    }

    [DataContract(Name = "SectionMessageHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class SectionMessageHeader : Header<SectionMessageHeader, SectionMessageMetadata, Section>
    {
        public SectionMessageHeader(SectionMessageMetadata metadata, DigitalSignature digitalSignature)
            : base(metadata, digitalSignature)
        {

        }
    }
}
