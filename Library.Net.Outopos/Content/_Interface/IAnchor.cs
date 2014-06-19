
namespace Library.Net.Outopos
{
    public interface IAnchor : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
