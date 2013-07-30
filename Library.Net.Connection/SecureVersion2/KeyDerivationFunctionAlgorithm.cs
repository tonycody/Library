using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Connection.SecureVersion2
{
    [Flags]
    [DataContract(Name = "KeyDerivationFunctionAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion2")]
    enum KeyDerivationFunctionAlgorithm
    {
        [EnumMember(Value = "ANSI_X963")]
        ANSI_X963 = 0x01,
    }
}
