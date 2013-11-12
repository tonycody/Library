using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion2
{
    [Flags]
    [DataContract(Name = "KeyDerivationFunctionAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion2")]
    enum KeyDerivationFunctionAlgorithm
    {
        [EnumMember(Value = "ANSI_X963")]
        ANSI_X963 = 0x01,
    }
}
