using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Library.Security
{
    class Rsa2048_Sha512
    {
        public static void CreateKeys(out byte[] publicKey, out byte[] privateKey)
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                publicKey = Encoding.ASCII.GetBytes(rsa.ToXmlString(false));
                privateKey = Encoding.ASCII.GetBytes(rsa.ToXmlString(true));
            }
        }

        public static byte[] Sign(byte[] privateKey, Stream stream)
        {
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(Encoding.ASCII.GetString(privateKey));

                RSAPKCS1SignatureFormatter rsaFormatter = new RSAPKCS1SignatureFormatter(rsa);
                rsaFormatter.SetHashAlgorithm("SHA512");

                using (var sha512 = SHA512.Create())
                {
                    return rsaFormatter.CreateSignature(sha512.ComputeHash(stream));
                }
            }
        }

        public static bool Verify(byte[] publicKey, byte[] signature, Stream stream)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Encoding.ASCII.GetString(publicKey));

                    RSAPKCS1SignatureDeformatter rsaDeformatter = new RSAPKCS1SignatureDeformatter(rsa);
                    rsaDeformatter.SetHashAlgorithm("SHA512");

                    using (var sha512 = SHA512.Create())
                    {
                        return rsaDeformatter.VerifySignature(sha512.ComputeHash(stream), signature);
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
