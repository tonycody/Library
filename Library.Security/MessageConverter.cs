using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Library.Io;
using Library.Net.Lair;
using Library.Security;

namespace Library.Security
{
    static class MessageConverter
    {
        public static string ToSignatureString(DigitalSignature digitalSignature)
        {
            if (digitalSignature == null || digitalSignature.Nickname == null || digitalSignature.PublicKey == null) return null;

            try
            {
                if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.ECDsaP521_Sha512
                    || digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
                {
                    using (var sha512 = new SHA512Managed())
                    {
                        using (MemoryStream memoryStream = new MemoryStream())
                        {
                            var nicknameBuffer = new UTF8Encoding(false).GetBytes(digitalSignature.Nickname);

                            memoryStream.Write(nicknameBuffer, 0, nicknameBuffer.Length);
                            memoryStream.Write(digitalSignature.PublicKey, 0, digitalSignature.PublicKey.Length);
                            memoryStream.Seek(0, SeekOrigin.Begin);

                            return digitalSignature.Nickname.Replace("@", "_") + "@" + Convert.ToBase64String(sha512.ComputeHash(memoryStream).ToArray())
                                .Replace('+', '-').Replace('/', '_').TrimEnd('=').Substring(0, 64);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        public static string ToSignatureString(Certificate certificate)
        {
            if (certificate == null || certificate.Nickname == null || certificate.PublicKey == null) return null;

            try
            {
                if (certificate.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.ECDsaP521_Sha512
                    || certificate.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
                {
                    using (var sha512 = new SHA512Managed())
                    {
                        using (MemoryStream memoryStream = new MemoryStream())
                        {
                            var nicknameBuffer = new UTF8Encoding(false).GetBytes(certificate.Nickname);

                            memoryStream.Write(nicknameBuffer, 0, nicknameBuffer.Length);
                            memoryStream.Write(certificate.PublicKey, 0, certificate.PublicKey.Length);
                            memoryStream.Seek(0, SeekOrigin.Begin);

                            return certificate.Nickname.Replace("@", "_") + "@" + Convert.ToBase64String(sha512.ComputeHash(memoryStream).ToArray())
                                .Replace('+', '-').Replace('/', '_').TrimEnd('=').Substring(0, 64);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }
    }
}
