
namespace Library.Security
{
    public interface IExchangeDecrypt
    {
        ExchangeAlgorithm ExchangeAlgorithm { get; }
        byte[] PrivateKey { get; }
    }
}
