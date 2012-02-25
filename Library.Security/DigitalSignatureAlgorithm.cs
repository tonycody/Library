using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Library.Security
{
    [DataContract(Name = "DigitalSignatureAlgorithm", Namespace = "http://Library/Security")]
    public enum DigitalSignatureAlgorithm
    {
        [EnumMember(Value = "Rsa2048_Sha512")]
        Rsa2048_Sha512 = 0,

        [EnumMember(Value = "ECDsa521_Sha512")]
        ECDsa521_Sha512 = 1,
    }
}
