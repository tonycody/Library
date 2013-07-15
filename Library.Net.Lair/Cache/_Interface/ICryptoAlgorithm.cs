using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Lair")]
    public enum CryptoAlgorithm
    {
        [EnumMember(Value = "Rsa2048")]
        Rsa2048 = 0,
    }

    interface ICryptoAlgorithm
    {
        CryptoAlgorithm CryptoAlgorithm { get; }
        byte[] CryptoKey { get; }
    }
}
