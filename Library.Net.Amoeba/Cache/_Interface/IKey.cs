
namespace Library.Net.Amoeba
{
    interface IKey : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
