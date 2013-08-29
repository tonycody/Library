using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "HypertextFormatType", Namespace = "http://Library/Net/Lair")]
    public enum HypertextFormatType
    {
        [EnumMember(Value = "Raw")]
        Raw = 0,

        [EnumMember(Value = "MiniWiki")]
        MiniWiki = 1,
    }

    interface IDocumentContent
    {
        HypertextFormatType FormatType { get; }
        string Hypertext { get; }
        string Comment { get; }
    }
}
