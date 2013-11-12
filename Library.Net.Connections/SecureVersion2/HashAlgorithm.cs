using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion2
{
    [Flags]
    [DataContract(Name = "HashAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion2")]
    enum HashAlgorithm
    {
        [EnumMember(Value = "Sha512")]
        Sha512 = 0x01,
    }
}
