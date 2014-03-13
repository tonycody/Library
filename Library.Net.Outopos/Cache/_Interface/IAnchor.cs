
namespace Library.Net.Outopos
{
    interface IAnchor : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
