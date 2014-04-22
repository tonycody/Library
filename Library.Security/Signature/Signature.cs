using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Library.Io;

namespace Library.Security
{
    public unsafe static class Signature
    {
#if Mono

#else
        private static NativeLibraryManager _nativeLibraryManager;

        [return: MarshalAs(UnmanagedType.U1)]
        delegate bool CheckBase64Delegate(ushort* source, int length);
        private static CheckBase64Delegate _checkBase64;
#endif

        private static InternPool<string> _signatureCache = new InternPool<string>();
        private static ConditionalWeakTable<string, byte[]> _signatureHashCache = new ConditionalWeakTable<string, byte[]>();
        private static object _signatureHashCacheLockObject = new object();

        private static BufferManager _bufferManager = BufferManager.Instance;

        static Signature()
        {
#if Mono

#else
            try
            {
                if (System.Environment.Is64BitProcess)
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assembly/Library_Security_x64.dll");
                }
                else
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assembly/Library_Security_x86.dll");
                }

                _checkBase64 = _nativeLibraryManager.GetMethod<CheckBase64Delegate>("checkBase64");
            }
            catch (Exception e)
            {
                Log.Warning(e);
            }
#endif
        }

        internal static bool CheckBase64(string value)
        {
            fixed (char* t_value = value)
            {
                return _checkBase64((ushort*)t_value, value.Length);
            }
        }

        public static string GetSignature(DigitalSignature digitalSignature)
        {
            if (digitalSignature == null || digitalSignature.Nickname == null || digitalSignature.PublicKey == null) return null;

            try
            {
                if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha512
                    || digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
                {
                    using (BufferStream bufferStream = new BufferStream(_bufferManager))
                    {
                        var nicknameBuffer = new UTF8Encoding(false).GetBytes(digitalSignature.Nickname);

                        bufferStream.Write(nicknameBuffer, 0, nicknameBuffer.Length);
                        bufferStream.Write(digitalSignature.PublicKey, 0, digitalSignature.PublicKey.Length);
                        bufferStream.Seek(0, SeekOrigin.Begin);

                        var signature = digitalSignature.Nickname + "@" + NetworkConverter.ToBase64UrlString(Sha512.ComputeHash(bufferStream));
                        return _signatureCache.GetValue(signature, digitalSignature);
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
                if (certificate.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha512
                    || certificate.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
                {
                    using (BufferStream bufferStream = new BufferStream(_bufferManager))
                    {
                        var nicknameBuffer = new UTF8Encoding(false).GetBytes(certificate.Nickname);

                        bufferStream.Write(nicknameBuffer, 0, nicknameBuffer.Length);
                        bufferStream.Write(certificate.PublicKey, 0, certificate.PublicKey.Length);
                        bufferStream.Seek(0, SeekOrigin.Begin);

                        var signature = certificate.Nickname + "@" + NetworkConverter.ToBase64UrlString(Sha512.ComputeHash(bufferStream));
                        return _signatureCache.GetValue(signature, certificate);
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
                var index = item.LastIndexOf('@');
                if (index == -1) return false;

                var nickname = item.Substring(0, index);
                var hash = item.Substring(index + 1);

                if (nickname.Length > 256) return false;
                if (!Signature.CheckBase64(hash)) return false;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string GetSignatureNickname(string item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                var index = item.LastIndexOf('@');
                if (index == -1) return null;

                var nickname = item.Substring(0, index);
                var hash = item.Substring(index + 1);

                if (nickname.Length > 256) return null;
                if (!Signature.CheckBase64(hash)) return null;

                return nickname;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] GetSignatureHash(string item)
        {
            if (item == null) return null;

            try
            {
                lock (_signatureHashCacheLockObject)
                {
                    byte[] value;

                    if (!_signatureHashCache.TryGetValue(item, out value))
                    {
                        var index = item.LastIndexOf('@');
                        if (index == -1) return null;

                        var nickname = item.Substring(0, index);
                        var hash = item.Substring(index + 1);

                        if (nickname.Length > 256) return null;
                        if (!Signature.CheckBase64(hash)) return null;

                        value = NetworkConverter.FromBase64UrlString(hash);
                        _signatureHashCache.Add(item, value);
                    }

                    return value;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
