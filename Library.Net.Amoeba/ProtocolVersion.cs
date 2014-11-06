using System;
using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [Flags]
    [DataContract(Name = "ProtocolVersion", Namespace = "http://Library/Net/Amoeba")]
    enum ProtocolVersion
    {
        //[EnumMember(Value = "Version1")]
        //Version1 = 0x01,

        [EnumMember(Value = "Version2")]
        Version2 = 0x02,
    }
}
