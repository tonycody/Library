
namespace Library.Net.Lair
{
    interface IComputeHash
    {
        byte[] GetHash(HashAlgorithm hashAlgorithm);
        bool VerifyHash(HashAlgorithm hashAlgorithm, byte[] hash);
    }
}
