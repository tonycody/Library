
namespace Library.Net.Lair
{
    interface IKey : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
