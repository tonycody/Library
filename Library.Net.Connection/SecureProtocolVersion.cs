using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Connection
{
    [Flags]
    [DataContract(Name = "SecureProtocolVersion", Namespace = "http://Library/Net/Connection")]
    enum SecureProtocolVersion
    {
        [EnumMember(Value = "Version1")]
        Version1 = 0x01,
    }
}
