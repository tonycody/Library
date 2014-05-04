using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion3
{
    [Flags]
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion3")]
    enum CryptoAlgorithm
    {
        [EnumMember(Value = "Aes256")]
        Aes256 = 0x02,
    }
}
