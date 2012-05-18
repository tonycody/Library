using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Library.Security
{
    internal static class ECDsaP521_Sha512
    {
        /// <summary>
        /// 公開鍵と秘密鍵を作成して返す
        /// </summary>
        /// <param name="publicKey">作成された公開鍵</param>
        /// <param name="privateKey">作成された秘密鍵</param>
        public static void CreateKeys(out byte[] publicKey, out byte[] privateKey)
        {
            CngKeyCreationParameters ckcp = new CngKeyCreationParameters();
            ckcp.ExportPolicy = CngExportPolicies.AllowPlaintextExport;
            ckcp.KeyUsage = CngKeyUsages.Signing;

            using (CngKey ck = CngKey.Create(CngAlgorithm.ECDsaP521, null, ckcp))
            using (ECDsaCng ecdsa = new ECDsaCng(ck))
            {
                publicKey = Encoding.ASCII.GetBytes(ecdsa.ToXmlString(ECKeyXmlFormat.Rfc4050));
                privateKey = ecdsa.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob);
            }
        }

        public static byte[] Sign(byte[] privateKey, Stream stream)
        {
            using (CngKey ck = CngKey.Import(privateKey, CngKeyBlobFormat.Pkcs8PrivateBlob))
            using (ECDsaCng ecdsa = new ECDsaCng(ck))
            {
                ecdsa.HashAlgorithm = CngAlgorithm.Sha512;
                return ecdsa.SignData(stream);
            }
        }

        public static bool Verify(byte[] publicKey, byte[] signature, Stream stream)
        {
            try
            {
                using (ECDsaCng ecdsa = new ECDsaCng())
                {
                    ecdsa.FromXmlString(Encoding.ASCII.GetString(publicKey), ECKeyXmlFormat.Rfc4050);
                    ecdsa.HashAlgorithm = CngAlgorithm.Sha512;
                    return ecdsa.VerifyData(stream, signature);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
