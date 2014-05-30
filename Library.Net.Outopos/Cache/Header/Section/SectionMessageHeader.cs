using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "SectionMessageHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class SectionMessageHeader : HeaderBase<SectionMessageHeader, Section>
    {

    }
}
