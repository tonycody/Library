using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Amoeba")]
    public enum CryptoAlgorithm
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Rijndael256")]
        Rijndael256 = 1,
    }

    interface ICryptoAlgorithm
    {
        CryptoAlgorithm CryptoAlgorithm { get; }
        byte[] CryptoKey { get; }
    }
}
