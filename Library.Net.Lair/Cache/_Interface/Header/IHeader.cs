using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "ContentFormatType", Namespace = "http://Library/Net/Lair")]
    public enum ContentFormatType
    {
        [EnumMember(Value = "Raw")]
        Raw = 0,

        [EnumMember(Value = "Key")]
        Key = 1,
    }

    interface IHeader<TTag> : IComputeHash
        where TTag : ITag
    {
        TTag Tag { get; }
        string Type { get; }
        IEnumerable<string> Options { get; }
        DateTime CreationTime { get; }
        ContentFormatType FormatType { get; }
        byte[] Content { get; }
    }
}
