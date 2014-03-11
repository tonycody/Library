using System;
using System.Runtime.Serialization;

namespace Library.Net.Outopos
{
    [Flags]
    [DataContract(Name = "ProtocolVersion", Namespace = "http://Library/Net/Outopos")]
    enum ProtocolVersion
    {
        //[EnumMember(Value = "Version1")]
        //Version1 = 0x01,

        //[EnumMember(Value = "Version2")]
        //Version2 = 0x02,

        [EnumMember(Value = "Version3")]
        Version3 = 0x04,
    }
}
