using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Library.Security
{
    internal static class EcDsaP521_Sha256
    {
        /// <summary>
        /// 公開鍵と秘密鍵を作成して返す
        /// </summary>
        /// <param name="publicKey">作成された公開鍵</param>
        /// <param name="privateKey">作成された秘密鍵</param>
        public static void CreateKeys(out byte[] publicKey, out byte[] privateKey)
        {
#if Mono
            throw new NotSupportedException();
#else
            CngKeyCreationParameters ckcp = new CngKeyCreationParameters();
            ckcp.ExportPolicy = CngExportPolicies.AllowPlaintextExport;
            ckcp.KeyUsage = CngKeyUsages.Signing;

            using (CngKey ck = CngKey.Create(CngAlgorithm.ECDsaP521, null, ckcp))
            using (ECDsaCng ecdsa = new ECDsaCng(ck))
            {
                publicKey = Encoding.ASCII.GetBytes(ecdsa.ToXmlString(ECKeyXmlFormat.Rfc4050));
                privateKey = ecdsa.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob);
            }
#endif
        }

        public static byte[] Sign(byte[] privateKey, Stream stream)
        {
#if Mono
            throw new NotSupportedException();
#else
            using (CngKey ck = CngKey.Import(privateKey, CngKeyBlobFormat.Pkcs8PrivateBlob))
            using (ECDsaCng ecdsa = new ECDsaCng(ck))
            {
                ecdsa.HashAlgorithm = CngAlgorithm.Sha256;
                return ecdsa.SignData(stream);
            }
#endif
        }

        public static bool Verify(byte[] publicKey, byte[] signature, Stream stream)
        {
#if Mono
            throw new NotSupportedException();
#else
            try
            {
                using (ECDsaCng ecdsa = new ECDsaCng())
                {
                    ecdsa.FromXmlString(Encoding.ASCII.GetString(publicKey), ECKeyXmlFormat.Rfc4050);
                    ecdsa.HashAlgorithm = CngAlgorithm.Sha256;
                    return ecdsa.VerifyData(stream, signature);
                }
            }
            catch (Exception)
            {
                return false;
            }
#endif
        }
    }
}
