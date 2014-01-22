using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    public static class LairConverter
    {
        private enum ConvertCompressionAlgorithm
        {
            None = 0,
            Deflate = 1,
        }

        private static BufferManager _bufferManager = BufferManager.Instance;
        private static Regex _base64Regex = new Regex(@"^([a-zA-Z0-9\-_]*).*?$", RegexOptions.Compiled | RegexOptions.Singleline);

        private static Stream ToStream<T>(ItemBase<T> item)
                where T : ItemBase<T>
        {
            Stream stream = null;

            try
            {
                stream = item.Export(_bufferManager);
                List<KeyValuePair<int, Stream>> list = new List<KeyValuePair<int, Stream>>();

                try
                {
                    BufferStream deflateBufferStream = new BufferStream(_bufferManager);
                    byte[] compressBuffer = null;

                    try
                    {
                        compressBuffer = _bufferManager.TakeBuffer(1024 * 32);

                        using (DeflateStream deflateStream = new DeflateStream(deflateBufferStream, CompressionMode.Compress, true))
                        {
                            int i = -1;

                            while ((i = stream.Read(compressBuffer, 0, compressBuffer.Length)) > 0)
                            {
                                deflateStream.Write(compressBuffer, 0, i);
                            }
                        }
                    }
                    finally
                    {
                        _bufferManager.ReturnBuffer(compressBuffer);
                    }

                    deflateBufferStream.Seek(0, SeekOrigin.Begin);

                    if (deflateBufferStream.Length < stream.Length)
                    {
                        list.Add(new KeyValuePair<int, Stream>(1, deflateBufferStream));
                    }
                    else
                    {
                        deflateBufferStream.Dispose();
                    }
                }
                catch (Exception)
                {

                }

                list.Add(new KeyValuePair<int, Stream>((int)ConvertCompressionAlgorithm.None, stream));

                list.Sort((x, y) =>
                {
                    return x.Value.Length.CompareTo(y.Value.Length);
                });

#if DEBUG
                if (list[0].Value.Length != stream.Length)
                {
                    Debug.WriteLine("LairConverter ToStream : {0}→{1} {2}",
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
                    stream.Dispose();

                throw new ArgumentException(ex.Message, ex);
            }
        }

        private static T FromStream<T>(Stream stream)
            where T : ItemBase<T>
        {
            try
            {
                using (Stream verifyStream = new RangeStream(stream, 0, stream.Length - 4, true))
                {
                    byte[] verifyCrc = Crc32_Castagnoli.ComputeHash(verifyStream);
                    byte[] orignalCrc = new byte[4];

                    using (RangeStream crcStream = new RangeStream(stream, stream.Length - 4, 4, true))
                    {
                        crcStream.Read(orignalCrc, 0, orignalCrc.Length);
                    }

                    if (!Collection.Equals(verifyCrc, orignalCrc))
                        throw new ArgumentException("Crc Error");
                }

                stream.Seek(0, SeekOrigin.Begin);
                byte type = (byte)stream.ReadByte();

                using (Stream dataStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position - 4, true))
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
                                decompressBuffer = _bufferManager.TakeBuffer(1024 * 32);

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
                                _bufferManager.ReturnBuffer(decompressBuffer);
                            }

#if DEBUG
                            Debug.WriteLine("LairConverter FromStream : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(dataStream.Length),
                                NetworkConverter.ToSizeString(deflateBufferStream.Length),
                                NetworkConverter.ToSizeString(dataStream.Length - deflateBufferStream.Length));
#endif

                            deflateBufferStream.Seek(0, SeekOrigin.Begin);

                            return ItemBase<T>.Import(deflateBufferStream, _bufferManager);
                        }
                    }
                }

                throw new ArgumentException("ArgumentException");
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static string ToBase64String(Stream stream)
        {
            byte[] buffer = null;

            try
            {
                buffer = _bufferManager.TakeBuffer((int)stream.Length);
                stream.Seek(0, SeekOrigin.Begin);
                stream.Read(buffer, 0, (int)stream.Length);

                return NetworkConverter.ToBase64UrlString(buffer, 0, (int)stream.Length);
            }
            finally
            {
                if (buffer != null)
                {
                    _bufferManager.ReturnBuffer(buffer);
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
                using (Stream stream = LairConverter.ToStream<Node>(item))
                {
                    return "Node:" + LairConverter.ToBase64String(stream);
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
            if (!item.StartsWith("Node:") && !item.StartsWith("Node@")) throw new ArgumentException("item");

            try
            {
                using (Stream stream = LairConverter.FromBase64String(item.Remove(0, "Node:".Length)))
                {
                    return LairConverter.FromStream<Node>(stream);
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static string ToSectionString(Section item, string option)
        {
            if (item == null) throw new ArgumentNullException("Section");

            try
            {
                if (option != null)
                {
                    using (Stream stream = LairConverter.ToStream<Section>(item))
                    {
                        return "Section:" + LairConverter.ToBase64String(stream) + "," + option;
                    }
                }
                else
                {
                    using (Stream stream = LairConverter.ToStream<Section>(item))
                    {
                        return "Section:" + LairConverter.ToBase64String(stream);
                    }
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static Section FromSectionString(string item, out string option)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!item.StartsWith("Section:") && !item.StartsWith("Section@")) throw new ArgumentException("item");

            option = null;

            try
            {
                if (item.Contains(","))
                {
                    var list = item.Split(new char[] { ',' }, 2);

                    option = list[1];

                    using (Stream stream = LairConverter.FromBase64String(list[0].Remove(0, "Section:".Length)))
                    {
                        return LairConverter.FromStream<Section>(stream);
                    }
                }
                else
                {
                    using (Stream stream = LairConverter.FromBase64String(item.Remove(0, "Section:".Length)))
                    {
                        return LairConverter.FromStream<Section>(stream);
                    }
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static string ToWikiString(Wiki item, string option)
        {
            if (item == null) throw new ArgumentNullException("Wiki");

            try
            {
                if (option != null)
                {
                    using (Stream stream = LairConverter.ToStream<Wiki>(item))
                    {
                        return "Wiki:" + LairConverter.ToBase64String(stream) + "," + option;
                    }
                }
                else
                {
                    using (Stream stream = LairConverter.ToStream<Wiki>(item))
                    {
                        return "Wiki:" + LairConverter.ToBase64String(stream);
                    }
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static Wiki FromWikiString(string item, out string option)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!item.StartsWith("Wiki:") && !item.StartsWith("Wiki@")) throw new ArgumentException("item");

            option = null;

            try
            {
                if (item.Contains(","))
                {
                    var list = item.Split(new char[] { ',' }, 2);

                    option = list[1];

                    using (Stream stream = LairConverter.FromBase64String(list[0].Remove(0, "Wiki:".Length)))
                    {
                        return LairConverter.FromStream<Wiki>(stream);
                    }
                }
                else
                {
                    using (Stream stream = LairConverter.FromBase64String(item.Remove(0, "Wiki:".Length)))
                    {
                        return LairConverter.FromStream<Wiki>(stream);
                    }
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static string ToChatString(Chat item, string option)
        {
            if (item == null) throw new ArgumentNullException("Chat");

            try
            {
                if (option != null)
                {
                    using (Stream stream = LairConverter.ToStream<Chat>(item))
                    {
                        return "Chat:" + LairConverter.ToBase64String(stream) + "," + option;
                    }
                }
                else
                {
                    using (Stream stream = LairConverter.ToStream<Chat>(item))
                    {
                        return "Chat:" + LairConverter.ToBase64String(stream);
                    }
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }

        public static Chat FromChatString(string item, out string option)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!item.StartsWith("Chat:") && !item.StartsWith("Chat@")) throw new ArgumentException("item");

            option = null;

            try
            {
                if (item.Contains(","))
                {
                    var list = item.Split(new char[] { ',' }, 2);

                    option = list[1];

                    using (Stream stream = LairConverter.FromBase64String(list[0].Remove(0, "Chat:".Length)))
                    {
                        return LairConverter.FromStream<Chat>(stream);
                    }
                }
                else
                {
                    using (Stream stream = LairConverter.FromBase64String(item.Remove(0, "Chat:".Length)))
                    {
                        return LairConverter.FromStream<Chat>(stream);
                    }
                }
            }
            catch (Exception)
            {
                throw new FormatException();
            }
        }
    }
}
