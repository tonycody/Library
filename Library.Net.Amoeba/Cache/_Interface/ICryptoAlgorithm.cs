using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Amoeba")]
    public enum CryptoAlgorithm : byte
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Rijndael256")]
        Rijndael256 = 1,
    }

    public interface ICryptoAlgorithm
    {
        CryptoAlgorithm CryptoAlgorithm { get; }
        byte[] CryptoKey { get; }
    }
}
