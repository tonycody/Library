using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Nest
{
    [Flags]
    [DataContract(Name = "ProtocolVersion", Namespace = "http://Library/Net/Nest")]
    public enum ProtocolVersion
    {
        [EnumMember(Value = "Version1")]
        Version1 = 0x01,
    }
}
