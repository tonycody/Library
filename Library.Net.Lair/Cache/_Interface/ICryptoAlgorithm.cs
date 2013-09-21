using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Lair")]
    public enum CryptoAlgorithm
    {
        [EnumMember(Value = "Rijndael256")]
        Rijndael256 = 0,
    }

    public interface ICryptoAlgorithm
    {
        CryptoAlgorithm CryptoAlgorithm { get; }
        byte[] CryptoKey { get; }
    }
}
