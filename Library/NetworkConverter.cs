using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Library
{
    public static class NetworkConverter
    {
        public static string ToBase64String(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");

            return NetworkConverter.ToBase64String(bytes, 0, bytes.Length);
        }

        public static string ToBase64String(byte[] bytes, int offset, int length)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (offset < 0 || bytes.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || (bytes.Length - offset) < length) throw new ArgumentOutOfRangeException("length");

            return System.Convert.ToBase64String(bytes, offset, length);
        }

        public static byte[] FromBase64String(string value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return System.Convert.FromBase64String(value);
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

            StringBuilder sb = new StringBuilder();

            for (int i = offset; i < length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
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

            while (b >= 1024 && (b / 1024) >= 1)
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
                if (value.ToLower().Contains("y"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024 * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("z"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("e"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("p"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("t"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("g"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024 * 1024;
                }
                else if (value.ToLower().Contains("m"))
                {
                    return f * decimal.Parse(builder.ToString()) * 1024 * 1024;
                }
                else if (value.ToLower().Contains("k"))
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
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToBoolean(value, 0);
        }

        public static bool ToBoolean(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 1) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 1) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToBoolean(value.Reverse().ToArray(), (value.Length - 1) - offset);
            else return System.BitConverter.ToBoolean(value, offset);
        }

        public static char ToChar(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToChar(value, 0);
        }

        public static char ToChar(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 2) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 2) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToChar(value.Reverse().ToArray(), (value.Length - 2) - offset);
            else return System.BitConverter.ToChar(value, offset);
        }

        public static float ToSingle(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToSingle(value, 0);
        }

        public static float ToSingle(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 4) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 4) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToSingle(value.Reverse().ToArray(), (value.Length - 4) - offset);
            else return System.BitConverter.ToSingle(value, offset);
        }

        public static double ToDouble(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToDouble(value, 0);
        }

        public static double ToDouble(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 8) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 8) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToDouble(value.Reverse().ToArray(), (value.Length - 8) - offset);
            else return System.BitConverter.ToDouble(value, offset);
        }

        public static short ToInt16(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToInt16(value, 0);
        }

        public static short ToInt16(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 2) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 2) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToInt16(value.Reverse().ToArray(), (value.Length - 2) - offset);
            else return System.BitConverter.ToInt16(value, offset);
        }

        public static int ToInt32(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToInt32(value, 0);
        }

        public static int ToInt32(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 4) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 4) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToInt32(value.Reverse().ToArray(), (value.Length - 4) - offset);
            else return System.BitConverter.ToInt32(value, offset);
        }

        public static long ToInt64(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToInt64(value, 0);
        }

        public static long ToInt64(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 8) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 8) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToInt64(value.Reverse().ToArray(), (value.Length - 8) - offset);
            else return System.BitConverter.ToInt64(value, offset);
        }

        public static ushort ToUInt16(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToUInt16(value, 0);
        }

        public static ushort ToUInt16(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 2) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 2) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToUInt16(value.Reverse().ToArray(), (value.Length - 2) - offset);
            else return System.BitConverter.ToUInt16(value, offset);
        }

        public static uint ToUInt32(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToUInt32(value, 0);
        }

        public static uint ToUInt32(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 4) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 4) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToUInt32(value.Reverse().ToArray(), (value.Length - 4) - offset);
            else return System.BitConverter.ToUInt32(value, offset);
        }

        public static ulong ToUInt64(byte[] value)
        {
            if (value == null) throw new ArgumentNullException("value");

            return NetworkConverter.ToUInt64(value, 0);
        }

        public static ulong ToUInt64(byte[] value, int offset)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (value.Length < 8) throw new ArgumentOutOfRangeException("value");
            if ((value.Length - offset) < 8) throw new ArgumentOutOfRangeException("offset");

            if (System.BitConverter.IsLittleEndian) return System.BitConverter.ToUInt64(value.Reverse().ToArray(), (value.Length - 8) - offset);
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
