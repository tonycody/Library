
namespace Library.Security
{
    public interface IExchangeEncrypt : IExchangeAlgorithm
    {
        byte[] PublicKey { get; }
    }
}
