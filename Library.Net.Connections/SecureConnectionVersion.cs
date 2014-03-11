using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections
{
    [Flags]
    [DataContract(Name = "SecureConnectionVersion", Namespace = "http://Library/Net/Connection")]
    public enum SecureConnectionVersion
    {
        //[EnumMember(Value = "Version1")]
        //Version1 = 0x01,

        [EnumMember(Value = "Version2")]
        Version2 = 0x02,

        [EnumMember(Value = "Version3")]
        Version3 = 0x04,
    }
}
