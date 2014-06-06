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

namespace Library.Net.Outopos
{
    public static class ContentConverter
    {
        private enum ConvertCompressionAlgorithm : byte
        {
            None = 0,
            Xz = 1,
        }

        private enum ConvertCryptoAlgorithm : byte
        {
            Rijndael256 = 0,
        }

        private enum ConvertHashAlgorithm : byte
        {
            Sha512 = 0,
        }

        private static BufferManager _bufferManager = BufferManager.Instance;
        private static RNGCryptoServiceProvider _random = new RNGCryptoServiceProvider();

        private static Stream Compress(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            var targetStream = new RangeStream(stream, true);

            List<KeyValuePair<byte, Stream>> list = new List<KeyValuePair<byte, Stream>>();

            try
            {
                targetStream.Seek(0, SeekOrigin.Begin);

                BufferStream lzmaBufferStream = null;

                try
                {
                    lzmaBufferStream = new BufferStream(_bufferManager);

                    using (var inStream = new WrapperStream(targetStream, true))
                    using (var outStream = new WrapperStream(lzmaBufferStream, true))
                    {
                        Xz.Compress(inStream, outStream, _bufferManager);
                    }

                    lzmaBufferStream.Seek(0, SeekOrigin.Begin);
                    list.Add(new KeyValuePair<byte, Stream>((byte)ConvertCompressionAlgorithm.Xz, lzmaBufferStream));
                }
                catch (Exception)
                {
                    if (lzmaBufferStream != null)
                    {
                        lzmaBufferStream.Dispose();
                    }
                }
            }
            catch (Exception)
            {

            }

            list.Add(new KeyValuePair<byte, Stream>((byte)ConvertCompressionAlgorithm.None, targetStream));

            list.Sort((x, y) =>
            {
                int c = x.Value.Length.CompareTo(y.Value.Length);
                if (c != 0) return c;

                return x.Key.CompareTo(y.Key);
            });

#if DEBUG
            if (list[0].Value.Length != targetStream.Length)
            {
                Debug.WriteLine("ContentConverter Compress {3} : {0}→{1} {2}",
                    NetworkConverter.ToSizeString(targetStream.Length),
                    NetworkConverter.ToSizeString(list[0].Value.Length),
                    NetworkConverter.ToSizeString(list[0].Value.Length - targetStream.Length),
                    (ConvertCompressionAlgorithm)list[0].Key);
            }
#endif

            for (int i = 1; i < list.Count; i++)
            {
                list[i].Value.Dispose();
            }

            BufferStream headerStream = new BufferStream(_bufferManager);
            headerStream.WriteByte((byte)list[0].Key);

            return new UniteStream(headerStream, list[0].Value);
        }

        private static Stream Decompress(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            try
            {
                var targetStream = new RangeStream(stream, true);

                byte type = (byte)targetStream.ReadByte();

                if (type == (byte)ConvertCompressionAlgorithm.None)
                {
                    return new RangeStream(targetStream);
                }
                else if (type == (byte)ConvertCompressionAlgorithm.Xz)
                {
                    using (Stream dataStream = new WrapperStream(targetStream, true))
                    {
                        BufferStream lzmaBufferStream = null;

                        try
                        {
                            lzmaBufferStream = new BufferStream(_bufferManager);

                            using (var inStream = new WrapperStream(dataStream, true))
                            using (var outStream = new WrapperStream(lzmaBufferStream, true))
                            {
                                Xz.Decompress(inStream, outStream, _bufferManager);
                            }

#if DEBUG
                            Debug.WriteLine("ContentConverter Decompress {3} : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(dataStream.Length),
                                NetworkConverter.ToSizeString(lzmaBufferStream.Length),
                                NetworkConverter.ToSizeString(dataStream.Length - lzmaBufferStream.Length),
                                ConvertCompressionAlgorithm.Xz);
#endif

                            lzmaBufferStream.Seek(0, SeekOrigin.Begin);

                            return lzmaBufferStream;
                        }
                        catch (Exception)
                        {
                            if (lzmaBufferStream != null)
                            {
                                lzmaBufferStream.Dispose();
                            }
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
                            byte[] buffer = null;

                            try
                            {
                                buffer = _bufferManager.TakeBuffer(1024 * 4);

                                int i = -1;

                                while ((i = cs.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    outStream.Write(buffer, 0, i);
                                }
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

                    outStream.Seek(0, SeekOrigin.Begin);
                }
                catch (Exception)
                {
                    if (outStream != null)
                    {
                        outStream.Dispose();
                    }

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
                                byte[] buffer = null;

                                try
                                {
                                    buffer = _bufferManager.TakeBuffer(1024 * 4);

                                    int i = -1;

                                    while ((i = cs.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        outStream.Write(buffer, 0, i);
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
                        {
                            outStream.Dispose();
                        }

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

                return new UniteStream(headerStream, new WrapperStream(stream, true), paddingStream);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream RemovePadding(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            try
            {
                byte[] lengthBuffer = new byte[4];
                stream.Read(lengthBuffer, 0, lengthBuffer.Length);
                int length = NetworkConverter.ToInt32(lengthBuffer);

                return new RangeStream(stream, 4, length, true);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream AddType(Stream stream, string type)
        {
            if (stream == null) throw new ArgumentNullException("stream");

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

            streams.Add(new WrapperStream(stream, true));

            return new UniteStream(streams);
        }

        private static Stream RemoveType(Stream stream, string type)
        {
            if (stream == null) throw new ArgumentNullException("stream");

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

            return new RangeStream(stream, true);
        }

        private static Stream AddHash(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            try
            {
                var targetStream = new RangeStream(stream, true);

                BufferStream headerStream = new BufferStream(_bufferManager);
                headerStream.WriteByte((byte)ConvertHashAlgorithm.Sha512);

                targetStream.Seek(0, SeekOrigin.Begin);
                var hash = Sha512.ComputeHash(targetStream);

                BufferStream hashStream = new BufferStream(_bufferManager);
                hashStream.Write(hash, 0, hash.Length);

                return new UniteStream(headerStream, targetStream, hashStream);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream RemoveHash(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

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
            using (Stream paddingStream = ContentConverter.AddPadding(compressStream, 1024 * 256))
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
            using (Stream typeStream = ContentConverter.AddType(compressStream, "WikiPage"))
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
                using (Stream compressStream = ContentConverter.RemoveType(typeStream, "WikiPage"))
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
