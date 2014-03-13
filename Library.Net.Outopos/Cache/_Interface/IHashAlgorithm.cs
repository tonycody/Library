using System.Runtime.Serialization;

namespace Library.Net.Outopos
{
    [DataContract(Name = "HashAlgorithm", Namespace = "http://Library/Net/Outopos")]
    public enum HashAlgorithm : byte
    {
        [EnumMember(Value = "Sha512")]
        Sha512 = 0,
    }

    interface IHashAlgorithm
    {
        HashAlgorithm HashAlgorithm { get; }
    }
}
