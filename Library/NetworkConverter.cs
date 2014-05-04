using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Library
{
    public unsafe static class NetworkConverter
    {
        internal static byte[] GetReverse(byte[] value, int offset, int length)
        {
            var buffer = new byte[length];

            fixed (byte* p1 = value)
            fixed (byte* p2 = buffer)
            {
                var t_p1 = p1 + offset; // Start point
                var t_p2 = p2 + (length - 1); // End point

                for (int i = length - 1; i >= 0; i--)
                {
                    *t_p2-- = *t_p1++;
                }
            }

            return buffer;
        }

        private static void Reverse(byte[] value)
        {
            fixed (byte* p = value)
            {
                byte swap;

                for (int i = 0, j = value.Length - 1; i < j; i++, j--)
                {
                    swap = p[i];
                    p[i] = p[j];
                    p[j] = swap;
                }
            }
        }

        public static string ToBase64UrlString(byte[] value)
        {
            return NetworkConverter.ToBase64UrlString(value, 0, value.Length);
        }

        public static string ToBase64UrlString(byte[] value, int offset, int length)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (offset < 0 || value.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || (value.Length - offset) < length) throw new ArgumentOutOfRangeException("length");

            var charArray = System.Convert.ToBase64String(value, offset, length).ToCharArray();
            var charLength = charArray.Length;

            fixed (char* p_charArray = charArray)
            {
                var t_charArray = p_charArray;

                for (int i = charArray.Length - 1; i >= 0; i--)
                {
                    switch (*t_charArray)
                    {
                        case '+':
                            *t_charArray = '-';
                            break;
                        case '/':
                            *t_charArray = '_';
                            break;
                        case '=':
                            charLength--;
                            break;
                    }

                    t_charArray++;
                }
            }

            return new string(charArray, 0, charLength);
        }

        public static byte[] FromBase64UrlString(string value)
        {
            if (value == null) throw new ArgumentNullException("value");

            char[] charArray = null;

            switch (value.Length % 4)
            {
                case 1:
                case 3:
                    charArray = new char[value.Length + 1];
                    break;
                case 2:
                    charArray = new char[value.Length + 2];
                    break;
                default:
                    charArray = new char[value.Length];
                    break;
            }

            fixed (char* p_value = value.ToCharArray())
            fixed (char* p_charArray = charArray)
            {
                var t_value = p_value;
                var t_charArray = p_charArray;

                for (int i = value.Length - 1; i >= 0; i--)
                {
                    switch (*t_value)
                    {
                        case '-':
                            *t_charArray = '+';
                            break;
                        case '_':
                            *t_charArray = '/';
                            break;
                        default:
                            *t_charArray = *t_value;
                            break;
                    }

                    t_value++;
                    t_charArray++;
                }

                for (int i = (charArray.Length - value.Length) - 1; i >= 0; i--)
                {
                    *t_charArray++ = '=';
                }
            }

            return System.Convert.FromBase64CharArray(charArray, 0, charArray.Length);
        }

        /// <summary>
        /// バイト列を16進数表記の文字列に変換
        /// </summary>
        /// <param name="value">文字列に変換するバイト配列</param>
        /// <returns>変換された文字列</returns>
        public static string ToHexString(byte[] value)
        {
            return NetworkConverter.ToHexString(value, 0, value.Length);
        }

        /// <summary>
        /// バイト列を16進数表記の文字列に変換
        /// </summary>
        /// <param name="value">文字列に変換するバイト配列</param>
        /// <returns>変換された文字列</returns>
        public static string ToHexString(byte[] value, int offset, int length)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (offset < 0 || value.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || (value.Length - offset) < length) throw new ArgumentOutOfRangeException("length");

            char[] array = new char[length * 2];

            fixed (byte* p_value = value)
            fixed (char* p_array = array)
            {
                var t_value = p_value + offset;
                var t_array = p_array;

                for (int i = length - 1; i >= 0; i--)
                {
                    byte b = *t_value++;

                    *t_array++ = NetworkConverter.GetHexValue(b >> 4);
                    *t_array++ = NetworkConverter.GetHexValue(b & 0x0F);
                }
            }

            return new string(array);
        }

        internal static string ToHexString_2(byte[] value, int offset, int length)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (offset < 0 || value.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || (value.Length - offset) < length) throw new ArgumentOutOfRangeException("length");

            char[] array = new char[length * 2];

            fixed (byte* p_value = value)
            fixed (char* p_array = array)
            {
                var t_value = p_value + offset;
                var t_array = p_array;

                for (int i = length - 1; i >= 0; i--)
                {
                    byte b = *t_value++;

                    *t_array++ = NetworkConverter.GetHexValue(b / 16);
                    *t_array++ = NetworkConverter.GetHexValue(b % 16);
                }
            }

            return new string(array);
        }

        private static char GetHexValue(int c)
        {
            if (c < 10) return (char)(c + '0');
            else return (char)(c - 10 + 'a');
        }

        /// <summary>
        /// 16進数表記の文字列をバイト列に変換
        /// </summary>
        /// <param name="value">バイト配列に変換する文字列</param>
        /// <returns>変換されたバイト配列</returns>
        public static byte[] FromHexString(string value)
        {
            if (value == null) throw new ArgumentNullException("value");

            if (value.Length % 2 != 0)
            {
                value = "0" + value;
            }

            byte[] buffer = new byte[value.Length / 2];

            fixed (byte* p_buffer = buffer)
            fixed (char* p_value = value.ToCharArray())
            {
                var t_buffer = p_buffer;
                var t_value = p_value;

                for (int i = buffer.Length - 1; i >= 0; i--)
                {
                    int i1 = 0, i2 = 0;

                    if ('0' <= *t_value && *t_value <= '9')
                    {
                        i1 = *t_value - '0';
                    }
                    else if ('a' <= *t_value && *t_value <= 'f')
                    {
                        i1 = (*t_value - 'a') + 10;
                    }
                    else if ('A' <= *t_value && *t_value <= 'F')
                    {
                        i1 = (*t_value - 'A') + 10;
                    }

                    t_value++;

                    if ('0' <= *t_value && *t_value <= '9')
                    {
                        i2 = *t_value - '0';
                    }
                    else if ('a' <= *t_value && *t_value <= 'f')
                    {
                        i2 = (*t_value - 'a') + 10;
                    }
                    else if ('A' <= *t_value && *t_value <= 'F')
                    {
                        i2 = (*t_value - 'A') + 10;
                    }

                    t_value++;

                    *t_buffer++ = (byte)((i1 << 4) | i2);
                }
            }

            return buffer;
        }

        internal static byte[] FromHexString_2(string value)
        {
            if (value == null) throw new ArgumentNullException("value");

            if (value.Length % 2 != 0)
            {
                value = "0" + value;
            }

            byte[] buffer = new byte[value.Length / 2];

            fixed (byte* p_buffer = buffer)
            fixed (char* p_value = value.ToCharArray())
            {
                var t_buffer = p_buffer;
                var t_value = p_value;

                for (int i = buffer.Length - 1; i >= 0; i--)
                {
                    int i1 = 0, i2 = 0;

                    if ('0' <= *t_value && *t_value <= '9')
                    {
                        i1 = *t_value - '0';
                    }
                    else if ('a' <= *t_value && *t_value <= 'f')
                    {
                        i1 = (*t_value - 'a') + 10;
                    }
                    else if ('A' <= *t_value && *t_value <= 'F')
                    {
                        i1 = (*t_value - 'A') + 10;
                    }

                    t_value++;

                    if ('0' <= *t_value && *t_value <= '9')
                    {
                        i2 = *t_value - '0';
                    }
                    else if ('a' <= *t_value && *t_value <= 'f')
                    {
                        i2 = (*t_value - 'a') + 10;
                    }
                    else if ('A' <= *t_value && *t_value <= 'F')
                    {
                        i2 = (*t_value - 'A') + 10;
                    }

                    t_value++;

                    *t_buffer++ = (byte)((i1 * 16) + i2);
                }
            }

            return buffer;
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

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToBoolean(NetworkConverter.GetReverse(value, offset, 1), 0);
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

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToChar(NetworkConverter.GetReverse(value, offset, 2), 0);
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

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToSingle(NetworkConverter.GetReverse(value, offset, 4), 0);
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

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToDouble(NetworkConverter.GetReverse(value, offset, 8), 0);
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

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToInt16(NetworkConverter.GetReverse(value, offset, 2), 0);
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

            if (System.BitConverter.IsLittleEndian)
            {
                fixed (byte* p = value)
                {
                    var t_p = p + offset;

                    return ((int)*t_p++ << (8 * 3))
                        | ((int)*t_p++ << (8 * 2))
                        | ((int)*t_p++ << (8 * 1))
                        | (int)*t_p;
                }
            }
            else
            {
                return System.BitConverter.ToInt32(value, offset);
            }
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

            if (System.BitConverter.IsLittleEndian)
            {
                fixed (byte* p = value)
                {
                    var t_p = p + offset;

                    return ((long)*t_p++ << (8 * 7))
                        | ((long)*t_p++ << (8 * 6))
                        | ((long)*t_p++ << (8 * 5))
                        | ((long)*t_p++ << (8 * 4))
                        | ((long)*t_p++ << (8 * 3))
                        | ((long)*t_p++ << (8 * 2))
                        | ((long)*t_p++ << (8 * 1))
                        | (long)*t_p;
                }
            }
            else
            {
                return System.BitConverter.ToInt64(value, offset);
            }
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

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToUInt16(NetworkConverter.GetReverse(value, offset, 2), 0);
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

            if (System.BitConverter.IsLittleEndian)
            {
                fixed (byte* p = value)
                {
                    var t_p = p + offset;

                    return (uint)(*t_p++ << (8 * 3))
                        | (uint)(*t_p++ << (8 * 2))
                        | (uint)(*t_p++ << (8 * 1))
                        | (uint)*t_p;
                }
            }
            else
            {
                return System.BitConverter.ToUInt32(value, offset);
            }
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

            if (System.BitConverter.IsLittleEndian)
            {
                fixed (byte* p = value)
                {
                    var t_p = p + offset;

                    return ((ulong)*t_p++ << (8 * 7))
                        | ((ulong)*t_p++ << (8 * 6))
                        | ((ulong)*t_p++ << (8 * 5))
                        | ((ulong)*t_p++ << (8 * 4))
                        | ((ulong)*t_p++ << (8 * 3))
                        | ((ulong)*t_p++ << (8 * 2))
                        | ((ulong)*t_p++ << (8 * 1))
                        | (ulong)*t_p;
                }
            }
            else
            {
                return System.BitConverter.ToUInt64(value, offset);
            }
        }

        public static byte[] GetBytes(bool value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(char value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(float value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(double value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(short value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(int value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(long value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(ushort value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(uint value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }

        public static byte[] GetBytes(ulong value)
        {
            byte[] buffer = System.BitConverter.GetBytes(value);
            if (System.BitConverter.IsLittleEndian) NetworkConverter.Reverse(buffer);
            return buffer;
        }
    }
}
