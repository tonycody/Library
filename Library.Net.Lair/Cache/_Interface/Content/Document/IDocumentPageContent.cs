using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "HypertextFormatType", Namespace = "http://Library/Net/Lair")]
    public enum HypertextFormatType
    {
        [EnumMember(Value = "MiniWiki")]
        MiniWiki = 0,
    }

    interface IDocumentPageContent
    {
        HypertextFormatType FormatType { get; }
        string Hypertext { get; }
        string Comment { get; }
    }
}
