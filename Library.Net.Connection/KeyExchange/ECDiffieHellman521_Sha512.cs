using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace Library.Net.Connection
{
    class ECDiffieHellman521_Sha512
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

            using (CngKey ck = CngKey.Create(CngAlgorithm.ECDiffieHellmanP521, null, ckcp))
            using (ECDiffieHellmanCng ecdh = new ECDiffieHellmanCng(ck))
            {
                publicKey = Encoding.ASCII.GetBytes(ecdh.ToXmlString(ECKeyXmlFormat.Rfc4050));
                privateKey = ecdh.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob);
            }
        }

        public static byte[] DeriveKeyMaterial(byte[] privateKey, byte[] otherPublicKey)
        {
            using (CngKey ck = CngKey.Import(privateKey, CngKeyBlobFormat.Pkcs8PrivateBlob))
            using (ECDiffieHellmanCng ecdh = new ECDiffieHellmanCng(ck))
            {
                ecdh.HashAlgorithm = CngAlgorithm.Sha512;
                return ecdh.DeriveKeyMaterial(ECDiffieHellmanCngPublicKey.FromXmlString(Encoding.ASCII.GetString(otherPublicKey)));
            }
        }
    }
}
