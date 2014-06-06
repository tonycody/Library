using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "SectionProfileMetadata", Namespace = "http://Library/Net/Outopos")]
    public sealed class SectionProfileMetadata : Metadata<SectionProfileMetadata, Section>
    {
        public SectionProfileMetadata(Section tag, string signature, DateTime creationTime, Key key, Miner miner)
            : base(tag, signature, creationTime, key, miner)
        {

        }
    }

    [DataContract(Name = "SectionProfileHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class SectionProfileHeader : Header<SectionProfileHeader, SectionProfileMetadata, Section>
    {
        public SectionProfileHeader(SectionProfileMetadata metadata, DigitalSignature digitalSignature)
            : base(metadata, digitalSignature)
        {

        }
    }
}
