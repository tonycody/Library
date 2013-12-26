using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion1
{
    [Flags]
    [DataContract(Name = "KeyExchangeAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion1")]
    enum KeyExchangeAlgorithm
    {
        [EnumMember(Value = "Rsa2048")]
        Rsa2048 = 0x01,

        [EnumMember(Value = "ECDiffieHellmanP521_Sha512")]
        ECDiffieHellmanP521_Sha512 = 0x02,
    }
}
