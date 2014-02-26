
namespace Library.Security
{
    public interface IExchangeEncrypt
    {
        ExchangeAlgorithm ExchangeAlgorithm { get; }
        byte[] PublicKey { get; }
    }
}
