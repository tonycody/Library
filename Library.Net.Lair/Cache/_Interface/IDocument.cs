using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "DocumentFormatType", Namespace = "http://Library/Net/Lair")]
    public enum DocumentFormatType
    {
        [EnumMember(Value = "Raw")]
        Raw = 0,

        [EnumMember(Value = "MiniWiki")]
        MiniWiki = 1,
    }

    interface IDocument<TArchive> : IComputeHash
        where TArchive : IArchive
    {
        TArchive Archive { get; }
        DateTime CreationTime { get; }
        DocumentFormatType FormatType { get; }
        string Content { get; }
    }
}
