using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "ExchangeAlgorithm", Namespace = "http://Library/Security")]
    public enum ExchangeAlgorithm
    {
        [EnumMember(Value = "Rsa2048")]
        Rsa2048 = 0,
    }

    public interface IExchangeAlgorithm
    {
        ExchangeAlgorithm ExchangeAlgorithm { get; }
    }
}
