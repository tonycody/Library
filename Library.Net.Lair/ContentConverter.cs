using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Library.Compression;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    static class ContentConverter
    {
        private enum ConvertCompressionAlgorithm
        {
            None = 0,
            Deflate = 1,
            Lzma = 2,
        }

        private enum ConvertCryptoAlgorithm
        {
            Rijndael256 = 0,
        }

        private enum ConvertHashAlgorithm
        {
            Sha512 = 0,
        }

        private static BufferManager _bufferManager = BufferManager.Instance;
        private static RNGCryptoServiceProvider _random = new RNGCryptoServiceProvider();
        private static Lzma _lzma = new Lzma(1 << 20, 1 << 20);

        private static Stream Compress(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            List<KeyValuePair<int, Stream>> list = new List<KeyValuePair<int, Stream>>();

            try
            {
                stream.Seek(0, SeekOrigin.Begin);

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
                list.Add(new KeyValuePair<int, Stream>((int)ConvertCompressionAlgorithm.Deflate, deflateBufferStream));
            }
            catch (Exception)
            {

            }

            try
            {
                stream.Seek(0, SeekOrigin.Begin);

                BufferStream lzmaBufferStream = new BufferStream(_bufferManager);

                using (var inStream = new WrapperStream(stream, true))
                using (var outStream = new WrapperStream(lzmaBufferStream, true))
                {
                    _lzma.Compress(inStream, outStream, _bufferManager);
                }

                lzmaBufferStream.Seek(0, SeekOrigin.Begin);
                list.Add(new KeyValuePair<int, Stream>((int)ConvertCompressionAlgorithm.Lzma, lzmaBufferStream));
            }
            catch (Exception)
            {

            }

            list.Add(new KeyValuePair<int, Stream>((int)ConvertCompressionAlgorithm.None, new WrapperStream(stream, true)));

            list.Sort((x, y) =>
            {
                return x.Value.Length.CompareTo(y.Value.Length);
            });

#if DEBUG
            if (list[0].Value.Length != stream.Length)
            {
                Debug.WriteLine("ContentConverter Compress {3} : {0}→{1} {2}",
                    NetworkConverter.ToSizeString(stream.Length),
                    NetworkConverter.ToSizeString(list[0].Value.Length),
                    NetworkConverter.ToSizeString(list[0].Value.Length - stream.Length),
                    (ConvertCompressionAlgorithm)list[0].Key);
            }
#endif

            for (int i = 1; i < list.Count; i++)
            {
                list[i].Value.Dispose();
            }

            BufferStream headerStream = new BufferStream(_bufferManager);
            headerStream.WriteByte((byte)list[0].Key);

            return new JoinStream(headerStream, list[0].Value);
        }

        private static Stream Decompress(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            try
            {
                byte type = (byte)stream.ReadByte();

                if (type == (byte)ConvertCompressionAlgorithm.None)
                {
                    return new RangeStream(stream, 1, stream.Length - 1, true);
                }
                else if (type == (byte)ConvertCompressionAlgorithm.Lzma)
                {
                    using (Stream dataStream = new WrapperStream(stream, true))
                    {
                        BufferStream lzmaBufferStream = null;

                        try
                        {
                            lzmaBufferStream = new BufferStream(_bufferManager);

                            using (var inStream = new WrapperStream(dataStream, true))
                            using (var outStream = new WrapperStream(lzmaBufferStream, true))
                            {
                                _lzma.Decompress(inStream, outStream, _bufferManager);
                            }

#if DEBUG
                            Debug.WriteLine("ContentConverter Decompress {3} : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(dataStream.Length),
                                NetworkConverter.ToSizeString(lzmaBufferStream.Length),
                                NetworkConverter.ToSizeString(dataStream.Length - lzmaBufferStream.Length),
                                ConvertCompressionAlgorithm.Lzma);
#endif

                            lzmaBufferStream.Seek(0, SeekOrigin.Begin);

                            return lzmaBufferStream;
                        }
                        catch (Exception)
                        {
                            if (lzmaBufferStream != null)
                                lzmaBufferStream.Dispose();
                        }
                    }
                }
                else if (type == (byte)ConvertCompressionAlgorithm.Deflate)
                {
                    using (Stream dataStream = new WrapperStream(stream, true))
                    {
                        BufferStream deflateBufferStream = null;

                        try
                        {
                            deflateBufferStream = new BufferStream(_bufferManager);

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
                            Debug.WriteLine("ContentConverter Decompress {3} : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(dataStream.Length),
                                NetworkConverter.ToSizeString(deflateBufferStream.Length),
                                NetworkConverter.ToSizeString(dataStream.Length - deflateBufferStream.Length),
                                ConvertCompressionAlgorithm.Deflate);
#endif

                            deflateBufferStream.Seek(0, SeekOrigin.Begin);

                            return deflateBufferStream;
                        }
                        catch (Exception)
                        {
                            if (deflateBufferStream != null)
                                deflateBufferStream.Dispose();
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

        private static Stream Encrypt(Stream stream, IExchangeEncrypt publicKey)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (publicKey == null) throw new ArgumentNullException("publicKey");

            try
            {
                BufferStream outStream = null;

                try
                {
                    outStream = new BufferStream(_bufferManager);
                    outStream.WriteByte((byte)ConvertCryptoAlgorithm.Rijndael256);

                    byte[] cryptoKey = new byte[32];
                    _random.GetBytes(cryptoKey);

                    {
                        var encryptedBuffer = Exchange.Encrypt(publicKey, cryptoKey);
                        outStream.Write(NetworkConverter.GetBytes((int)encryptedBuffer.Length), 0, 4);
                        outStream.Write(encryptedBuffer, 0, encryptedBuffer.Length);
                    }

                    byte[] iv = new byte[32];
                    _random.GetBytes(iv);
                    outStream.Write(iv, 0, iv.Length);

                    using (Stream inStream = new WrapperStream(stream, true))
                    {
                        using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                        using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateEncryptor(cryptoKey, iv), CryptoStreamMode.Read))
                        {
                            byte[] buffer = _bufferManager.TakeBuffer(1024 * 4);

                            try
                            {
                                int length = 0;

                                while (0 != (length = cs.Read(buffer, 0, buffer.Length)))
                                {
                                    outStream.Write(buffer, 0, length);
                                }
                            }
                            finally
                            {
                                _bufferManager.ReturnBuffer(buffer);
                            }
                        }
                    }

                    outStream.Seek(0, SeekOrigin.Begin);
                }
                catch (Exception)
                {
                    if (outStream != null)
                        outStream.Dispose();

                    throw;
                }

                return outStream;
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream Decrypt(Stream stream, IExchangeDecrypt privateKey)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (privateKey == null) throw new ArgumentNullException("privateKey");

            try
            {
                byte type = (byte)stream.ReadByte();

                if (type == (byte)ConvertCryptoAlgorithm.Rijndael256)
                {
                    byte[] cryptoKey;

                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) throw new ArgumentException();
                        int length = NetworkConverter.ToInt32(lengthBuffer);

                        byte[] encryptedBuffer = new byte[length];
                        if (stream.Read(encryptedBuffer, 0, encryptedBuffer.Length) != encryptedBuffer.Length) throw new ArgumentException();

                        cryptoKey = Exchange.Decrypt(privateKey, encryptedBuffer);
                    }

                    BufferStream outStream = null;

                    try
                    {
                        outStream = new BufferStream(_bufferManager);

                        using (Stream dataStream = new WrapperStream(stream, true))
                        {
                            var iv = new byte[32];
                            dataStream.Read(iv, 0, iv.Length);

                            using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                            using (var inStream = new RangeStream(dataStream, dataStream.Position, dataStream.Length - dataStream.Position))
                            using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoKey, iv), CryptoStreamMode.Read))
                            {
                                byte[] buffer = _bufferManager.TakeBuffer(1024 * 4);

                                try
                                {
                                    int length = 0;

                                    while (0 != (length = cs.Read(buffer, 0, buffer.Length)))
                                    {
                                        outStream.Write(buffer, 0, length);
                                    }
                                }
                                finally
                                {
                                    _bufferManager.ReturnBuffer(buffer);
                                }
                            }
                        }

                        outStream.Seek(0, SeekOrigin.Begin);
                    }
                    catch (Exception)
                    {
                        if (outStream != null)
                            outStream.Dispose();

                        throw;
                    }

                    return outStream;
                }

                throw new NotSupportedException();
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream AddPadding(Stream stream, int size)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");
            if (stream.Length + 4 > size) throw new ArgumentOutOfRangeException("size");

            try
            {
                byte[] seedBuffer = new byte[4];
                _random.GetBytes(seedBuffer);
                Random random = new Random(NetworkConverter.ToInt32(seedBuffer));

                BufferStream headerStream = new BufferStream(_bufferManager);
                byte[] lengthBuffer = NetworkConverter.GetBytes((int)stream.Length);
                headerStream.Write(lengthBuffer, 0, lengthBuffer.Length);

                int paddingLength = size - ((int)stream.Length + 4);

                BufferStream paddingStream = new BufferStream(_bufferManager);

                {
                    byte[] buffer = null;

                    try
                    {
                        buffer = _bufferManager.TakeBuffer(1024);

                        while (paddingLength > 0)
                        {
                            int writeSize = Math.Min(paddingLength, buffer.Length);

                            random.NextBytes(buffer);
                            paddingStream.Write(buffer, 0, writeSize);

                            paddingLength -= writeSize;
                        }
                    }
                    finally
                    {
                        _bufferManager.ReturnBuffer(buffer);
                    }
                }

                return new JoinStream(headerStream, new WrapperStream(stream, true), paddingStream);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream RemovePadding(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length <= 4) throw new ArgumentOutOfRangeException("stream");

            try
            {
                byte[] lengthBuffer = new byte[4];
                stream.Read(lengthBuffer, 0, lengthBuffer.Length);
                int length = NetworkConverter.ToInt32(lengthBuffer);

                return new RangeStream(stream, 4, length);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream AddType(Stream stream, string type)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Type
            if (type != null)
            {
                BufferStream bufferStream = new BufferStream(_bufferManager);
                bufferStream.SetLength(4);
                bufferStream.Seek(4, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(type);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 4), 0, 4);

                streams.Add(bufferStream);
            }

            streams.Add(stream);

            return new JoinStream(streams);
        }

        private static Stream RemoveType(Stream stream, string type)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length <= 4) throw new ArgumentOutOfRangeException("stream");

            Encoding encoding = new UTF8Encoding(false);

            byte[] lengthBuffer = new byte[4];
            if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) throw new FormatException();
            int length = NetworkConverter.ToInt32(lengthBuffer);

            using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
            {
                using (StreamReader reader = new StreamReader(rangeStream, encoding))
                {
                    if (type != reader.ReadToEnd()) throw new FormatException();
                }
            }

            return new RangeStream(stream, stream.Position, stream.Length - stream.Position);
        }

        private static Stream AddHash(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            try
            {
                BufferStream headerStream = new BufferStream(_bufferManager);
                headerStream.WriteByte((byte)ConvertHashAlgorithm.Sha512);

                stream.Seek(0, SeekOrigin.Begin);
                var hash = Sha512.ComputeHash(stream);

                BufferStream hashStream = new BufferStream(_bufferManager);
                hashStream.Write(hash, 0, hash.Length);

                return new JoinStream(headerStream, new WrapperStream(stream, true), hashStream);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream RemoveHash(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length <= 64) throw new ArgumentOutOfRangeException("stream");

            byte type = (byte)stream.ReadByte();

            if (type == (byte)ConvertHashAlgorithm.Sha512)
            {
                Stream dataStream = null;

                try
                {
                    byte[] hash = new byte[64];

                    using (RangeStream hashStream = new RangeStream(stream, stream.Length - 64, 64, true))
                    {
                        hashStream.Read(hash, 0, hash.Length);
                    }

                    dataStream = new RangeStream(stream, 1, stream.Length - (1 + 64));
                    if (!Unsafe.Equals(hash, Sha512.ComputeHash(dataStream))) throw new FormatException();

                    dataStream.Seek(0, SeekOrigin.Begin);

                    return dataStream;
                }
                catch (Exception)
                {
                    if (dataStream != null)
                    {
                        dataStream.Dispose();
                    }

                    throw;
                }
            }

            throw new NotSupportedException();
        }

        public static ArraySegment<byte> ToSectionProfileContentBlock(SectionProfileContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream contentStream = content.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(contentStream))
            using (Stream typeStream = ContentConverter.AddType(compressStream, "SectionProfile"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static SectionProfileContent FromSectionProfileContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream typeStream = new MemoryStream(content.Array, content.Offset, content.Count))
                using (Stream compressStream = ContentConverter.RemoveType(typeStream, "SectionProfile"))
                using (Stream contentStream = ContentConverter.Decompress(compressStream))
                {
                    return SectionProfileContent.Import(contentStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToSectionMessageContentBlock(SectionMessageContent content, IExchangeEncrypt publicKey)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (publicKey == null) throw new ArgumentNullException("publicKey");

            ArraySegment<byte> value;

            using (Stream contentStream = content.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(contentStream))
            using (Stream paddingStream = ContentConverter.AddPadding(compressStream, 1024 * 32))
            using (Stream hashStream = ContentConverter.AddHash(paddingStream))
            using (Stream cryptostream = ContentConverter.Encrypt(hashStream, publicKey))
            using (Stream typeStream = ContentConverter.AddType(cryptostream, "SectionMessage"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static SectionMessageContent FromSectionMessageContentBlock(ArraySegment<byte> content, IExchangeDecrypt privateKey)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");
            if (privateKey == null) throw new ArgumentNullException("privateKey");

            try
            {
                using (Stream typeStream = new MemoryStream(content.Array, content.Offset, content.Count))
                using (Stream cryptoStream = ContentConverter.RemoveType(typeStream, "SectionMessage"))
                using (Stream hashStream = ContentConverter.Decrypt(cryptoStream, privateKey))
                using (Stream paddingStream = ContentConverter.RemoveHash(hashStream))
                using (Stream compressStream = ContentConverter.RemovePadding(paddingStream))
                using (Stream contentStream = ContentConverter.Decompress(compressStream))
                {
                    return SectionMessageContent.Import(contentStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToWikiPageContentBlock(WikiPageContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream contentStream = content.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(contentStream))
            using (Stream typeStream = ContentConverter.AddType(compressStream, "WikiDocument"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static WikiPageContent FromWikiPageContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream typeStream = new MemoryStream(content.Array, content.Offset, content.Count))
                using (Stream compressStream = ContentConverter.RemoveType(typeStream, "WikiDocument"))
                using (Stream contentStream = ContentConverter.Decompress(compressStream))
                {
                    return WikiPageContent.Import(contentStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToWikiVoteContentBlock(WikiVoteContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream contentStream = content.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(contentStream))
            using (Stream typeStream = ContentConverter.AddType(compressStream, "WikiVote"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static WikiVoteContent FromWikiVoteContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream typeStream = new MemoryStream(content.Array, content.Offset, content.Count))
                using (Stream compressStream = ContentConverter.RemoveType(typeStream, "WikiVote"))
                using (Stream contentStream = ContentConverter.Decompress(compressStream))
                {
                    return WikiVoteContent.Import(contentStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToChatTopicContentBlock(ChatTopicContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream contentStream = content.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(contentStream))
            using (Stream typeStream = ContentConverter.AddType(compressStream, "ChatTopic"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static ChatTopicContent FromChatTopicContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream typeStream = new MemoryStream(content.Array, content.Offset, content.Count))
                using (Stream compressStream = ContentConverter.RemoveType(typeStream, "ChatTopic"))
                using (Stream contentStream = ContentConverter.Decompress(compressStream))
                {
                    return ChatTopicContent.Import(contentStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToChatMessageContentBlock(ChatMessageContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream contentStream = content.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(contentStream))
            using (Stream typeStream = ContentConverter.AddType(compressStream, "ChatMessage"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static ChatMessageContent FromChatMessageContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream typeStream = new MemoryStream(content.Array, content.Offset, content.Count))
                using (Stream compressStream = ContentConverter.RemoveType(typeStream, "ChatMessage"))
                using (Stream contentStream = ContentConverter.Decompress(compressStream))
                {
                    return ChatMessageContent.Import(contentStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
