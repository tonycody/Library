using System.Runtime.Serialization;

namespace Library.Net.Outopos
{
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Outopos")]
    public enum CryptoAlgorithm : byte
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
