using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Library.Io;

namespace Library.Net.Connections
{
    static class ItemUtility
    {
        private static readonly ThreadLocal<Encoding> _threadLocalEncoding = new ThreadLocal<Encoding>(() => new UTF8Encoding(false));

        public static void Write(Stream stream, byte type, Stream exportStream)
        {
            BufferManager bufferManager = BufferManager.Instance;

            {
                stream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                stream.WriteByte(type);

                byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                try
                {
                    int length = 0;

                    while (0 < (length = exportStream.Read(buffer, 0, buffer.Length)))
                    {
                        stream.Write(buffer, 0, length);
                    }
                }
                finally
                {
                    bufferManager.ReturnBuffer(buffer);
                }
            }
        }

        public static void Write(Stream stream, byte type, string value)
        {
            BufferManager bufferManager = BufferManager.Instance;
            Encoding encoding = _threadLocalEncoding.Value;

            byte[] buffer = null;

            try
            {
                buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                stream.Write(NetworkConverter.GetBytes(length), 0, 4);
                stream.WriteByte(type);
                stream.Write(buffer, 0, length);
            }
            finally
            {
                if (buffer != null)
                {
                    bufferManager.ReturnBuffer(buffer);
                }
            }
        }

        public static void Write(Stream stream, byte type, byte[] value)
        {
            stream.Write(NetworkConverter.GetBytes((int)value.Length), 0, 4);
            stream.WriteByte(type);
            stream.Write(value, 0, value.Length);
        }

        public static void Write(Stream stream, byte type, int value)
        {
            stream.Write(NetworkConverter.GetBytes((int)4), 0, 4);
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes(value), 0, 4);
        }

        public static void Write(Stream stream, byte type, long value)
        {
            stream.Write(NetworkConverter.GetBytes((int)8), 0, 4);
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes(value), 0, 8);
        }

        public static byte[] GetByteArray(Stream stream)
        {
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public static string GetString(Stream stream)
        {
            BufferManager bufferManager = BufferManager.Instance;
            Encoding encoding = _threadLocalEncoding.Value;

            byte[] buffer = null;

            try
            {
                var length = (int)stream.Length;
                buffer = bufferManager.TakeBuffer(length);

                stream.Read(buffer, 0, length);

                return encoding.GetString(buffer, 0, length);
            }
            finally
            {
                if (buffer != null)
                {
                    bufferManager.ReturnBuffer(buffer);
                }
            }
        }

        public static int GetInt(Stream stream)
        {
            if (stream.Length != 4) throw new ArgumentException();

            BufferManager bufferManager = BufferManager.Instance;
            byte[] buffer = null;

            try
            {
                buffer = bufferManager.TakeBuffer(4);

                stream.Read(buffer, 0, 4);

                return NetworkConverter.ToInt32(buffer);
            }
            finally
            {
                if (buffer != null)
                {
                    bufferManager.ReturnBuffer(buffer);
                }
            }
        }

        public static long GetLong(Stream stream)
        {
            if (stream.Length != 8) throw new ArgumentException();

            BufferManager bufferManager = BufferManager.Instance;
            byte[] buffer = null;

            try
            {
                buffer = bufferManager.TakeBuffer(8);

                stream.Read(buffer, 0, 8);

                return NetworkConverter.ToInt64(buffer);
            }
            finally
            {
                if (buffer != null)
                {
                    bufferManager.ReturnBuffer(buffer);
                }
            }
        }
    }
}
