using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion3
{
    [Flags]
    [DataContract(Name = "KeyDerivationAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion3")]
    enum KeyDerivationAlgorithm
    {
        [EnumMember(Value = "Pbkdf2")]
        Pbkdf2 = 0x01,
    }
}
