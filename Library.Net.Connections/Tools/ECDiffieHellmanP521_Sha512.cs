using System;
using System.Security.Cryptography;
using System.Text;

namespace Library.Net.Connections
{
    class EcDiffieHellmanP521
    {
        /// <summary>
        /// 公開鍵と秘密鍵を作成して返す
        /// </summary>
        /// <param name="publicKey">作成された公開鍵</param>
        /// <param name="privateKey">作成された秘密鍵</param>
        public static void CreateKeys(out byte[] publicKey, out byte[] privateKey)
        {
#if !MONO
            CngKeyCreationParameters ckcp = new CngKeyCreationParameters();
            ckcp.ExportPolicy = CngExportPolicies.AllowPlaintextExport;
            ckcp.KeyUsage = CngKeyUsages.KeyAgreement;

            using (CngKey ck = CngKey.Create(CngAlgorithm.ECDiffieHellmanP521, null, ckcp))
            using (ECDiffieHellmanCng ecdh = new ECDiffieHellmanCng(ck))
            {
                publicKey = Encoding.ASCII.GetBytes(ecdh.ToXmlString(ECKeyXmlFormat.Rfc4050));
                privateKey = ecdh.Key.Export(CngKeyBlobFormat.Pkcs8PrivateBlob);
            }
#else
            throw new NotSupportedException();
#endif
        }

        public static byte[] DeriveKeyMaterial(byte[] privateKey, byte[] otherPublicKey, CngAlgorithm hashAlgorithm)
        {
#if !MONO
            using (CngKey ck = CngKey.Import(privateKey, CngKeyBlobFormat.Pkcs8PrivateBlob))
            using (ECDiffieHellmanCng ecdh = new ECDiffieHellmanCng(ck))
            {
                ecdh.HashAlgorithm = hashAlgorithm;
                return ecdh.DeriveKeyMaterial(ECDiffieHellmanCngPublicKey.FromXmlString(Encoding.ASCII.GetString(otherPublicKey)));
            }
#else
            throw new NotSupportedException();
#endif
        }
    }
}
