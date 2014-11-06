using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    public static class AmoebaConverter
    {
        enum ConvertCompressionAlgorithm : byte
        {
            None = 0,
            Deflate = 1,
        }

        private static readonly BufferManager _bufferManager = BufferManager.Instance;
        private static readonly Regex _base64Regex = new Regex(@"^([a-zA-Z0-9\-_]*).*?$", RegexOptions.Compiled | RegexOptions.Singleline);

        private static Stream ToStream<T>(ItemBase<T> item)
                where T : ItemBase<T>
        {
            Stream stream = null;

            try
            {
                stream = new RangeStream(item.Export(_bufferManager));

                List<KeyValuePair<byte, Stream>> list = new List<KeyValuePair<byte, Stream>>();

                try
                {
                    stream.Seek(0, SeekOrigin.Begin);

                    BufferStream deflateBufferStream = null;

                    try
                    {
                        deflateBufferStream = new BufferStream(_bufferManager);

                        using (DeflateStream deflateStream = new DeflateStream(deflateBufferStream, CompressionMode.Compress, true))
                        {
                            byte[] compressBuffer = null;

                            try
                            {
                                compressBuffer = _bufferManager.TakeBuffer(1024 * 4);

                                int i = -1;

                                while ((i = stream.Read(compressBuffer, 0, compressBuffer.Length)) > 0)
                                {
                                    deflateStream.Write(compressBuffer, 0, i);
                                }
                            }
                            finally
                            {
                                if (compressBuffer != null)
                                {
                                    _bufferManager.ReturnBuffer(compressBuffer);
                                }
                            }
                        }

                        deflateBufferStream.Seek(0, SeekOrigin.Begin);

                        list.Add(new KeyValuePair<byte, Stream>((byte)ConvertCompressionAlgorithm.Deflate, deflateBufferStream));
                    }
                    catch (Exception)
                    {
                        if (deflateBufferStream != null)
                        {
                            deflateBufferStream.Dispose();
                        }
                    }
                }
                catch (Exception)
                {

                }

                list.Add(new KeyValuePair<byte, Stream>((byte)ConvertCompressionAlgorithm.None, stream));

                list.Sort((x, y) =>
                {
                    int c = x.Value.Length.CompareTo(y.Value.Length);
                    if (c != 0) return c;

                    return x.Key.CompareTo(y.Key);
                });

#if DEBUG
                if (list[0].Value.Length != stream.Length)
                {
                    Debug.WriteLine("AmoebaConverter ToStream : {0}→{1} {2}",
                        NetworkConverter.ToSizeString(stream.Length),
                        NetworkConverter.ToSizeString(list[0].Value.Length),
                        NetworkConverter.ToSizeString(list[0].Value.Length - stream.Length));
                }
#endif

                for (int i = 1; i < list.Count; i++)
                {
                    list[i].Value.Dispose();
                }

                BufferStream headerStream = new BufferStream(_bufferManager);
                headerStream.WriteByte((byte)list[0].Key);

                var dataStream = new UniteStream(headerStream, list[0].Value);

                MemoryStream crcStream = new MemoryStream(Crc32_Castagnoli.ComputeHash(dataStream));
                return new UniteStream(dataStream, crcStream);
            }
            catch (Exception ex)
            {
                if (stream != null)
                {
                    stream.Dispose();
                }

                throw new ArgumentException(ex.Message, ex);
            }
        }

        private static T FromStream<T>(Stream stream)
            where T : ItemBase<T>
        {
            try
            {
                using (var targetStream = new RangeStream(stream, true))
                {
                    using (Stream verifyStream = new RangeStream(targetStream, 0, targetStream.Length - 4, true))
                    {
                        byte[] verifyCrc = Crc32_Castagnoli.ComputeHash(verifyStream);
                        byte[] orignalCrc = new byte[4];

                        using (RangeStream crcStream = new RangeStream(targetStream, targetStream.Length - 4, 4, true))
                        {
                            crcStream.Read(orignalCrc, 0, orignalCrc.Length);
                        }

                        if (!Unsafe.Equals(verifyCrc, orignalCrc))
                        {
                            throw new ArgumentException("Crc Error");
                        }
                    }

                    targetStream.Seek(0, SeekOrigin.Begin);
                    byte type = (byte)targetStream.ReadByte();

                    using (Stream dataStream = new RangeStream(targetStream, targetStream.Position, targetStream.Length - targetStream.Position - 4, true))
                    {
                        if (type == (byte)ConvertCompressionAlgorithm.None)
                        {
                            return ItemBase<T>.Import(dataStream, _bufferManager);
                        }
                        else if (type == (byte)ConvertCompressionAlgorithm.Deflate)
                        {
                            using (BufferStream deflateBufferStream = new BufferStream(_bufferManager))
                            {
                                byte[] decompressBuffer = null;

                                try
                                {
                                    decompressBuffer = _bufferManager.TakeBuffer(1024 * 4);

                                    using (DeflateStream deflateStream = new DeflateStream(dataStream, CompressionMode.Decompress, true))
                                    {
                                        int i = -1;

                                        while ((i = deflateStream.Read(decompressBuffer, 0, decompressBuffer.Length)) > 0)
                                        {
                                            deflateBufferStream.Write(decompressBuffer, 0, i);
                                        }
                                    }
                                }
                                finally
                                {
                                    if (decompressBuffer != null)
                                    {
                                        _bufferManager.ReturnBuffer(decompressBuffer);
                                    }
                                }

#if DEBUG
                                Debug.WriteLine("AmoebaConverter FromStream : {0}→{1} {2}",
                                    NetworkConverter.ToSizeString(dataStream.Length),
                                    NetworkConverter.ToSizeString(deflateBufferStream.Length),
                                    NetworkConverter.ToSizeString(dataStream.Length - deflateBufferStream.Length));
#endif

                                deflateBufferStream.Seek(0, SeekOrigin.Begin);

                                return ItemBase<T>.Import(deflateBufferStream, _bufferManager);
                            }
                        }
                        else
                        {
                            throw new ArgumentException("ArgumentException");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static string ToBase64String(Stream stream)
        {
            using (var targetStream = new RangeStream(stream, true))
            {
                byte[] buffer = null;

                try
                {
                    buffer = _bufferManager.TakeBuffer((int)targetStream.Length);
                    targetStream.Seek(0, SeekOrigin.Begin);
                    targetStream.Read(buffer, 0, (int)targetStream.Length);

                    return NetworkConverter.ToBase64UrlString(buffer, 0, (int)targetStream.Length);
                }
                finally
                {
                    if (buffer != null)
                    {
                        _bufferManager.ReturnBuffer(buffer);
                    }
                }
            }
        }

        private static Stream FromBase64String(string value)
        {
            var match = _base64Regex.Match(value);
            if (!match.Success) throw new ArgumentException();

            value = match.Groups[1].Value;

            return new MemoryStream(NetworkConverter.FromBase64UrlString(value));
        }

        public static string ToNodeString(Node item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                using (Stream stream = AmoebaConverter.ToStream<Node>(item))
                {
                    return "Node:" + AmoebaConverter.ToBase64String(stream);
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static Node FromNodeString(string item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!item.StartsWith("Node:")) throw new ArgumentException("item");

            try
            {
                using (Stream stream = AmoebaConverter.FromBase64String(item.Remove(0, "Node:".Length)))
                {
                    return AmoebaConverter.FromStream<Node>(stream);
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static string ToSeedString(Seed item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                using (Stream stream = AmoebaConverter.ToStream<Seed>(item))
                {
                    return "Seed:" + AmoebaConverter.ToBase64String(stream);
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static Seed FromSeedString(string item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!item.StartsWith("Seed:")) throw new ArgumentException("item");

            try
            {
                using (Stream stream = AmoebaConverter.FromBase64String(item.Remove(0, "Seed:".Length)))
                {
                    return AmoebaConverter.FromStream<Seed>(stream);
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static Stream ToBoxStream(Box item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                return AmoebaConverter.ToStream<Box>(item);
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static Box FromBoxStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            try
            {
                return AmoebaConverter.FromStream<Box>(stream);
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }
    }
}
