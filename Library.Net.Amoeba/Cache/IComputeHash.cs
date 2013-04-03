
namespace Library.Net.Amoeba
{
    interface IComputeHash
    {
        byte[] GetHash(HashAlgorithm hashAlgorithm);
        bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm);
    }
}
