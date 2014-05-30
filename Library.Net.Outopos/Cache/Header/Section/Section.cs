using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Section", Namespace = "http://Library/Net/Outopos")]
    public sealed class Section : TagBase<Section>
    {

    }
}
