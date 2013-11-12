
namespace Library.Security
{
    public interface IExchangeDecrypt : IExchangeAlgorithm
    {
        byte[] PrivateKey { get; }
    }
}
