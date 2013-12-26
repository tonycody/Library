using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "HashAlgorithm", Namespace = "http://Library/Net/Amoeba")]
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
