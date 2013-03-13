using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using Library.Io;

namespace Library.Security
{
    public static class Signature
    {
        private static BufferManager _bufferManager = new BufferManager();
        private static Regex _signatureRegex = new Regex(@"^(.*)@([a-zA-Z0-9\-_]*)$", RegexOptions.Compiled | RegexOptions.Singleline);

        public static string GetSignature(DigitalSignature digitalSignature)
        {
            if (digitalSignature == null || digitalSignature.Nickname == null || digitalSignature.PublicKey == null) return null;

            try
            {
                if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.ECDsaP521_Sha512
                    || digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
                {
                    using (BufferStream bufferStream = new BufferStream(_bufferManager))
                    {
                        var nicknameBuffer = new UTF8Encoding(false).GetBytes(digitalSignature.Nickname);

                        bufferStream.Write(nicknameBuffer, 0, nicknameBuffer.Length);
                        bufferStream.Write(digitalSignature.PublicKey, 0, digitalSignature.PublicKey.Length);
                        bufferStream.Seek(0, SeekOrigin.Begin);

                        return digitalSignature.Nickname + "@" + NetworkConverter.ToBase64String(Sha512.ComputeHash(bufferStream));
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        public static string GetSignature(Certificate certificate)
        {
            if (certificate == null || certificate.Nickname == null || certificate.PublicKey == null) return null;

            try
            {
                if (certificate.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.ECDsaP521_Sha512
                    || certificate.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
                {
                    using (BufferStream bufferStream = new BufferStream(_bufferManager))
                    {
                        var nicknameBuffer = new UTF8Encoding(false).GetBytes(certificate.Nickname);

                        bufferStream.Write(nicknameBuffer, 0, nicknameBuffer.Length);
                        bufferStream.Write(certificate.PublicKey, 0, certificate.PublicKey.Length);
                        bufferStream.Seek(0, SeekOrigin.Begin);

                        return certificate.Nickname + "@" + NetworkConverter.ToBase64String(Sha512.ComputeHash(bufferStream));
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        public static bool HasSignature(string item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                var match = _signatureRegex.Match(item);
                if (!match.Success) return false;

                if (match.Groups[1].Value.Length > 256) return false;
                if (match.Groups[2].Value.Length != 86) return false;
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public static string GetSignatureNickname(string item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                var match = _signatureRegex.Match(item);
                if (!match.Success) throw new ArgumentNullException("item");

                if (match.Groups[1].Value.Length > 256) throw new ArgumentNullException("item");
                if (match.Groups[2].Value.Length > 128) throw new ArgumentNullException("item");

                return match.Groups[1].Value;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] GetSignatureHash(string item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!Signature.HasSignature(item)) throw new ArgumentException("item");

            try
            {
                var match = _signatureRegex.Match(item.Split('@')[1]);
                if (!match.Success) throw new ArgumentException();

                var value = match.Groups[1].Value;

                return NetworkConverter.FromBase64String(value);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
