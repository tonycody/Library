using System;
using System.Collections.Generic;
using System.IO;

namespace Library.UnitTest
{
    static class T_Crc32_Castagnoli
    {
        private static readonly uint[] _table;

        static T_Crc32_Castagnoli()
        {
            //uint poly = 0xedb88320;
            uint poly = 0x82F63B78;
            _table = new uint[256];

            for (uint i = 0; i < 256; i++)
            {
                uint x = i;

                for (int j = 0; j < 8; j++)
                {
                    if ((x & 1) != 0)
                    {
                        x = (x >> 1) ^ poly;
                    }
                    else
                    {
                        x >>= 1;
                    }
                }

                _table[i] = x;
            }
        }

        public static byte[] ComputeHash(byte[] buffer, int offset, int length)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            else if (offset < 0 || buffer.Length < offset)
            {
                throw new ArgumentOutOfRangeException("offset");
            }
            else if (length < 0 || (buffer.Length - offset) < length)
            {
                throw new ArgumentOutOfRangeException("length");
            }

            uint x = 0xFFFFFFFF;

            for (int i = 0; i < length; i++)
            {
                x = _table[(x ^ buffer[offset + i]) & 0xFF] ^ (x >> 8);
            }

            return NetworkConverter.GetBytes(x ^ 0xFFFFFFFF);
        }

        /// <summary>
        /// ハッシュを生成する
        /// </summary>
        /// <param name="buffer">ハッシュ値を計算するbyte配列</param>
        public static byte[] ComputeHash(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }

            return T_Crc32_Castagnoli.ComputeHash(buffer, 0, buffer.Length);
        }

        public static byte[] ComputeHash(ArraySegment<byte> value)
        {
            if (value.Array == null)
            {
                throw new ArgumentNullException("value");
            }

            return T_Crc32_Castagnoli.ComputeHash(value.Array, value.Offset, value.Count);
        }

        public static byte[] ComputeHash(Stream inputStream)
        {
            if (inputStream == null)
            {
                throw new ArgumentNullException("inputStream");
            }

            uint x = 0xFFFFFFFF;

            byte[] buffer = new byte[1024 * 4];
            int length = 0;

            while (0 < (length = inputStream.Read(buffer, 0, buffer.Length)))
            {
                for (int i = 0; i < length; i++)
                {
                    x = _table[(x ^ buffer[i]) & 0xFF] ^ (x >> 8);
                }
            }

            return NetworkConverter.GetBytes(x ^ 0xFFFFFFFF);
        }

        public static byte[] ComputeHash(IList<ArraySegment<byte>> value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            uint x = 0xFFFFFFFF;

            for (int i = 0; i < value.Count && value[i].Array != null; i++)
            {
                for (int j = 0; j < value[i].Count; j++)
                {
                    x = _table[(x ^ value[i].Array[value[i].Offset + j]) & 0xFF] ^ (x >> 8);
                }
            }

            return NetworkConverter.GetBytes(x ^ 0xFFFFFFFF);
        }
    }
}
