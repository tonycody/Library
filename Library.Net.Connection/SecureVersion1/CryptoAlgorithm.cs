using System;
using System.Runtime.Serialization;

namespace Library.Net.Connection.SecureVersion1
{
    [Flags]
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion1")]
    enum CryptoAlgorithm
    {
        [EnumMember(Value = "Rijndael256")]
        Rijndael256 = 0x01,
    }
}
