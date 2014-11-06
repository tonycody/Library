using System.IO;
using System.Security.Cryptography;

namespace Library.Security
{
    public class Kdf
    {
        HashAlgorithm _hashAlgorithm;

        public Kdf(HashAlgorithm hashAlgorithm)
        {
            _hashAlgorithm = hashAlgorithm;
        }

        public byte[] GetBytes(byte[] value, int length)
        {
            byte[] buffer = new byte[value.Length + 4];
            Unsafe.Copy(value, 0, buffer, 0, value.Length);

            using (MemoryStream stream = new MemoryStream())
            {
                for (int i = 0; stream.Length < length; i++)
                {
                    Unsafe.Copy(NetworkConverter.GetBytes(i), 0, buffer, value.Length, 4);

                    var hash = _hashAlgorithm.ComputeHash(buffer);

                    stream.Write(hash, 0, hash.Length);
                    stream.Flush();
                }

                return stream.ToArray();
            }
        }
    }
}
