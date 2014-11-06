using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion3
{
    [Flags]
    [DataContract(Name = "HashAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion3")]
    enum HashAlgorithm
    {
        [EnumMember(Value = "Sha256")]
        Sha256 = 0x01,
    }
}
