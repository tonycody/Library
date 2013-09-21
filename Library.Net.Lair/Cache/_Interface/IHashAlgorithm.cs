using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "HashAlgorithm", Namespace = "http://Library/Net/Lair")]
    public enum HashAlgorithm
    {
        [EnumMember(Value = "Sha512")]
        Sha512 = 0,
    }

    public interface IHashAlgorithm
    {
        HashAlgorithm HashAlgorithm { get; }
    }
}
