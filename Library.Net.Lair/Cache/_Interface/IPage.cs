using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "HypertextFormatType", Namespace = "http://Library/Net/Lair")]
    public enum HypertextFormatType
    {
        [EnumMember(Value = "MiniWiki")]
        MiniWiki = 0,
    }

    public interface IPage : IComputeHash
    {
        string Name { get; }
        HypertextFormatType FormatType { get; }
        string Hypertext { get; }
    }
}
