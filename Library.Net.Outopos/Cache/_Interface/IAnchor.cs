
namespace Library.Net.Lair
{
    interface IAnchor : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
