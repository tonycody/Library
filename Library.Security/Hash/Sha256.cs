using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Library.Security
{
    public static class Sha256
    {
        private static readonly ThreadLocal<SHA256> _threadLocalSha256 = new ThreadLocal<SHA256>(() => SHA256.Create());
        private static readonly ThreadLocal<Encoding> _threadLocalEncoding = new ThreadLocal<Encoding>(() => new UTF8Encoding(false));

        public static byte[] ComputeHash(byte[] buffer, int offset, int length)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || (buffer.Length - offset) < length) throw new ArgumentOutOfRangeException("length");

            return _threadLocalSha256.Value.ComputeHash(buffer, offset, length);
        }

        public static byte[] ComputeHash(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");

            return _threadLocalSha256.Value.ComputeHash(buffer, 0, buffer.Length);
        }

        public static byte[] ComputeHash(string value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return _threadLocalSha256.Value.ComputeHash(_threadLocalEncoding.Value.GetBytes(value));
        }

        public static byte[] ComputeHash(ArraySegment<byte> value)
        {
            if (value.Array == null) throw new ArgumentNullException("value");

            return _threadLocalSha256.Value.ComputeHash(value.Array, value.Offset, value.Count);
        }

        public static byte[] ComputeHash(Stream inputStream)
        {
            if (inputStream == null) throw new ArgumentNullException("inputStream");

            return _threadLocalSha256.Value.ComputeHash(inputStream);
        }

        public static byte[] ComputeHash(IList<ArraySegment<byte>> value)
        {
            if (value == null) throw new ArgumentNullException("value");

            if (value.Count == 1) return _threadLocalSha256.Value.ComputeHash(value[0].Array, value[0].Offset, value[0].Count);

            using (var Sha256 = SHA256.Create())
            {
                for (int i = 0; i < value.Count; i++)
                {
                    Sha256.TransformBlock(value[i].Array, value[i].Offset, value[i].Count, value[i].Array, value[i].Offset);
                }

                Sha256.TransformFinalBlock(new byte[0], 0, 0);

                return Sha256.Hash;
            }
        }
    }
}
