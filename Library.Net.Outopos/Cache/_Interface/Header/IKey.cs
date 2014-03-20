
namespace Library.Net.Outopos
{
    interface IKey : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
