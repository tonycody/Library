﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Library
{
    public static class NetworkConverter
    {
        private static readonly string[] _toHexStringHashtable;

        static NetworkConverter()
        {
            _toHexStringHashtable = new string[256];

            for (int i = 0; i < 256; i++)
            {
                _toHexStringHashtable[i] = i.ToString("x2");
            }
        }

        private static byte[] Reverse(byte[] value, int offset, int length)
        {
            byte[] buffer = new byte[length];
            int r = length - 1;

            for (int i = 0; i < length; i++)
            {
                buffer[r - i] = value[offset + i];
            }

            return buffer;
        }

        public static string ToBase64UrlString(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");

            return NetworkConverter.ToBase64UrlString(bytes, 0, bytes.Length);
        }

        public static string ToBase64UrlString(byte[] bytes, int offset, int length)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (offset < 0 || bytes.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || (bytes.Length - offset) < length) throw new ArgumentOutOfRangeException("length");

            var value = System.Convert.ToBase64String(bytes, offset, length);
            StringBuilder sb = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case '+':
                        sb.Append('-');
                        break;
                    case '/':
                        sb.Append('_');
                        break;
                    case '=':
                        break;
                    default:
                        sb.Append(value[i]);
                        break;
                }
            }

            return sb.ToString();
        }

        public static byte[] FromBase64UrlString(string value)
        {
            if (value == null) throw new ArgumentNullException("value");

            string padding = "";

            switch (value.Length % 4)
            {
                case 1:
                case 3:
                    padding = "=";
                    break;

                case 2:
                    padding = "==";
                    break;
            }

            StringBuilder sb = new StringBuilder(value);
            sb.Replace('-', '+');
            sb.Replace('_', '/');
            sb.Append(padding);

            return System.Convert.FromBase64String(sb.ToString());
        }

        /// <summary>
        /// バイト列を16進数表記の文字列に変換
        /// </summary>
        /// <param name="bytes">文字列に変換するバイト配列</param>
        /// <returns>変換された文字列</returns>
        public static string ToHexString(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");

            return NetworkConverter.ToHexString(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// バイト列を16進数表記の文字列に変換
        /// </summary>
        /// <param name="bytes">文字列に変換するバイト配列</param>
        /// <returns>変換された文字列</returns>
        public static string ToHexString(byte[] bytes, int offset, int length)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (offset < 0 || bytes.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || (bytes.Length - offset) < length) throw new ArgumentOutOfRangeException("length");

            StringBuilder sb = new StringBuilder(length);

            for (int i = offset; i < length; i++)
            {
                sb.Append(_toHexStringHashtable[bytes[i]]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 16進数表記の文字列をバイト列に変換
        /// </summary>
        /// <param name="byteString">バイト配列に変換する文字列</param>
        /// <returns>変換されたバイト配列</returns>
        public static byte[] FromHexString(string byteString)
        {
            if (byteString == null) throw new ArgumentNullException("byteString");

            if (byteString.Length % 2 != 0)
            {
                byteString = "0" + byteString;
            }

            List<byte> data = new List<byte>();

            for (int i = 0; i < byteString.Length - 1; i += 2)
            {
                string buf = byteString.Substring(i, 2);

                if (Regex.IsMatch(buf, @"^[0-9a-fA-F]{2}$"))
                {
                    data.Add(System.Convert.ToByte(buf, 16));
                }
                else
                {
                    data.Add(System.Convert.ToByte("00", 16));
                }
            }

            return data.ToArray();
        }

        public static string ToSizeString(decimal b)
        {
            string f = (b < 0) ? "-" : "";
            b = Math.Abs(b);

            List<string> u = new List<string> { "Byte", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
            int i = 0;

            while (b >= 1024)
            {
                b /= (decimal)1024;
                i++;
            }

            var value = Math.Round(b, 2).ToString().Trim();

            if (value.Contains("."))
            {
                value = value.TrimEnd('0').TrimEnd('.');
            }

            return f + value + " " + u[i];
        }

        public static string ToSizeString(decimal b, string unit)
        {
            string f = (b < 0) ? "-" : "";
            b = Math.Abs(b);

            List<string> u = new List<string> { "Byte", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
            int i = 0;

            while (u[i] != unit)
            {
                if (b != 0) b /= (decimal)1024;
                i++;
            }

            var value = Math.Round(b, 2).ToString().Trim();

            if (value.Contains("."))
            {
                value = value.TrimEnd('0').TrimEnd('.');
            }

            return f + value + " " + u[i];
        }

        public static decimal FromSizeString(string value)
        {
            decimal f = value.StartsWith("-") ? -1 : 1;
            value = value.TrimStart('-');

            StringBuilder builder = new StringBuilder("0");

            foreach (var item in value)
            {
                if (Regex.IsMatch(item.ToString(), @"([0-9])|(\.)"))
                {
                    builder.Append(item.ToString());
                }
            }

            try
            {
                if (value.ToLower().Contains("yb"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024 * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("zb"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("eb"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("pb"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("tb"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("gb"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("mb"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024;
                }
                else if (value.ToLower().Contains("kb"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024;
                }
                else
                {
                    return f * decimal.Parse(builder.ToString());
                }
            }
            catch (Exception)
            {
                if (f == -1) return decimal.MinValue;
                else return decimal.MaxValue;
            }
        }

        public static bool ToBoolean(byte[] value)
        {
            return NetworkConverter.ToBoolean(value, 0);
        }

        public static bool ToBoolean(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 1) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 1) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToBoolean(NetworkConverter.Reverse(value, offset, 1), 0);
            else return System.BitConverter.ToBoolean(value, offset);
        }

        public static char ToChar(byte[] value)
        {
            return NetworkConverter.ToChar(value, 0);
        }

        public static char ToChar(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 2) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 2) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToChar(NetworkConverter.Reverse(value, offset, 2), 0);
            else return System.BitConverter.ToChar(value, offset);
        }

        public static float ToSingle(byte[] value)
        {
            return NetworkConverter.ToSingle(value, 0);
        }

        public static float ToSingle(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 4) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 4) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToSingle(NetworkConverter.Reverse(value, offset, 4), 0);
            else return System.BitConverter.ToSingle(value, offset);
        }

        public static double ToDouble(byte[] value)
        {
            return NetworkConverter.ToDouble(value, 0);
        }

        public static double ToDouble(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 8) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 8) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToDouble(NetworkConverter.Reverse(value, offset, 8), 0);
            else return System.BitConverter.ToDouble(value, offset);
        }

        public static short ToInt16(byte[] value)
        {
            return NetworkConverter.ToInt16(value, 0);
        }

        public static short ToInt16(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 2) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 2) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToInt16(NetworkConverter.Reverse(value, offset, 2), 0);
            else return System.BitConverter.ToInt16(value, offset);
        }

        public static int ToInt32(byte[] value)
        {
            return NetworkConverter.ToInt32(value, 0);
        }

        public static int ToInt32(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 4) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 4) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToInt32(NetworkConverter.Reverse(value, offset, 4), 0);
            else return System.BitConverter.ToInt32(value, offset);
        }

        public static long ToInt64(byte[] value)
        {
            return NetworkConverter.ToInt64(value, 0);
        }

        public static long ToInt64(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 8) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 8) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToInt64(NetworkConverter.Reverse(value, offset, 8), 0);
            else return System.BitConverter.ToInt64(value, offset);
        }

        public static ushort ToUInt16(byte[] value)
        {
            return NetworkConverter.ToUInt16(value, 0);
        }

        public static ushort ToUInt16(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 2) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 2) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToUInt16(NetworkConverter.Reverse(value, offset, 2), 0);
            else return System.BitConverter.ToUInt16(value, offset);
        }

        public static uint ToUInt32(byte[] value)
        {
            return NetworkConverter.ToUInt32(value, 0);
        }

        public static uint ToUInt32(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 4) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 4) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToUInt32(NetworkConverter.Reverse(value, offset, 4), 0);
            else return System.BitConverter.ToUInt32(value, offset);
        }

        public static ulong ToUInt64(byte[] value)
        {
            return NetworkConverter.ToUInt64(value, 0);
        }

        public static ulong ToUInt64(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 8) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 8) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToUInt64(NetworkConverter.Reverse(value, offset, 8), 0);
            else return System.BitConverter.ToUInt64(value, offset);
        }

        public static byte[] GetBytes(bool value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(char value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(float value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(double value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(short value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(int value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(long value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(ushort value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(uint value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(ulong value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return buffer;
        }
    }
}
