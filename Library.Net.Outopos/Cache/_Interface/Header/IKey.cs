
namespace Library.Net.Outopos
{
    public interface IKey : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
