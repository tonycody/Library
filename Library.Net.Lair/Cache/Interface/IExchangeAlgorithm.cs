using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Lair")]
    public enum ExchangeAlgorithm
    {
        [EnumMember(Value = "Rsa2048")]
        Rsa2048 = 0,
    }

    interface IExchangeAlgorithm
    {
        ExchangeAlgorithm ExchangeAlgorithm { get; }
        byte[] ExchangeKey { get; }
    }
}
