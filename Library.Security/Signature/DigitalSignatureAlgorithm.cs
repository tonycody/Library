using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "DigitalSignatureAlgorithm", Namespace = "http://Library/Security")]
    public enum DigitalSignatureAlgorithm : byte
    {
        [EnumMember(Value = "Rsa2048_Sha512")]
        Rsa2048_Sha512 = 0,

        [EnumMember(Value = "ECDsaP521_Sha512")]
        ECDsaP521_Sha512 = 1,
    }
}
