using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Library.Io;

namespace Library.Net.Outopos
{
    static class ItemUtility
    {
        private static readonly ThreadLocal<Encoding> _threadLocalEncoding = new ThreadLocal<Encoding>(() => new UTF8Encoding(false));

        public static void Write<T>(Stream stream, byte type, ItemBase<T> value, BufferManager bufferManager)
            where T : ItemBase<T>
        {
            using (Stream exportStream = value.Export(bufferManager))
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

        public static void Write(Stream stream, byte type, string value, BufferManager bufferManager)
        {
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

        public static void Write(Stream stream, byte type, byte[] value, BufferManager bufferManager)
        {
            stream.Write(NetworkConverter.GetBytes((int)value.Length), 0, 4);
            stream.WriteByte(type);
            stream.Write(value, 0, value.Length);
        }

        public static void Write(Stream stream, byte type, int value, BufferManager bufferManager)
        {
            stream.Write(NetworkConverter.GetBytes((int)4), 0, 4);
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes(value), 0, 4);
        }

        public static void Write(Stream stream, byte type, long value, BufferManager bufferManager)
        {
            stream.Write(NetworkConverter.GetBytes((int)8), 0, 4);
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes(value), 0, 8);
        }
    }
}
