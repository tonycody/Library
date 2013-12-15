
namespace Library.Net.Amoeba
{
    public interface IKey : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
