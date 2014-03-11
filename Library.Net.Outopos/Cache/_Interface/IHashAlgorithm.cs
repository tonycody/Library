using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "HashAlgorithm", Namespace = "http://Library/Net/Lair")]
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
