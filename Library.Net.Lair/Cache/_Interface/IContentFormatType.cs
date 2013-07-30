using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "ContentFormatType", Namespace = "http://Library/Net/Lair")]
    public enum ContentFormatType
    {
        [EnumMember(Value = "Raw")]
        Raw = 0,

        [EnumMember(Value = "MiniWiki")]
        MiniWiki = 1,
    }

    interface IContentFormatType
    {
        ContentFormatType FormatType { get; }
    }
}
