using System.Security.Cryptography;
using System.Text;

namespace Library.Net.Connection
{
    static class Rsa2048
    {
        public static void CreateKeys(out byte[] publicKey, out byte[] privateKey)
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
            {
                publicKey = Encoding.ASCII.GetBytes(rsa.ToXmlString(false));
                privateKey = Encoding.ASCII.GetBytes(rsa.ToXmlString(true));
            }
        }

        public static byte[] Encrypt(byte[] publicKey, byte[] value)
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
            {
                rsa.FromXmlString(Encoding.ASCII.GetString(publicKey));
                return rsa.Encrypt(value, true);
            }
        }

        public static byte[] Decrypt(byte[] privateKey, byte[] value)
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
            {
                rsa.FromXmlString(Encoding.ASCII.GetString(privateKey));
                return rsa.Decrypt(value, true);
            }
        }
    }
}
