using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    static class ContentConverter
    {
        private enum CompressionAlgorithm
        {
            None = 0,
            Deflate = 1,
            Lzma = 2,
        }

        private static BufferManager _bufferManager = BufferManager.Instance;
        private static RNGCryptoServiceProvider _random = new RNGCryptoServiceProvider();

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
                    list.Add(new KeyValuePair<int, Stream>(1, deflateBufferStream));
                }
                catch (Exception)
                {

                }

                list.Add(new KeyValuePair<int, Stream>(0, stream));

                list.Sort((x, y) =>
                {
                    return x.Value.Length.CompareTo(y.Value.Length);
                });

#if DEBUG
                if (list[0].Value.Length != stream.Length)
                {
                    Debug.WriteLine("ContentConverter ToStream : {0}→{1} {2}",
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

                return new JoinStream(headerStream, list[0].Value);
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
                byte version = (byte)stream.ReadByte();

                using (Stream dataStream = new WrapperStream(stream, true))
                {
                    if (version == (byte)CompressionAlgorithm.None)
                    {
                        return ItemBase<T>.Import(dataStream, _bufferManager);
                    }
                    else if (version == (byte)CompressionAlgorithm.Deflate)
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
                            Debug.WriteLine("ContentConverter FromStream : {0}→{1} {2}",
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
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream ToPaddingStream(Stream stream, int size)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length + 4 > size) throw new ArgumentOutOfRangeException("size");

            try
            {
                byte[] seedBuffer = new byte[4];
                _random.GetBytes(seedBuffer);
                Random random = new Random(NetworkConverter.ToInt32(seedBuffer));

                BufferStream headerStream = new BufferStream(_bufferManager);
                byte[] lengthBuffer = NetworkConverter.GetBytes((int)stream.Length);
                headerStream.Write(lengthBuffer, 0, lengthBuffer.Length);

                int paddinglength = size - ((int)stream.Length + 4);

                BufferStream paddingStream = new BufferStream(_bufferManager);

                {
                    byte[] buffer = null;

                    try
                    {
                        buffer = _bufferManager.TakeBuffer(1024);

                        while (paddinglength > 0)
                        {
                            int writeSize = Math.Min(paddinglength, buffer.Length);

                            random.NextBytes(buffer);
                            paddingStream.Write(buffer, 0, writeSize);

                            paddinglength -= writeSize;
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

        private static Stream FromPaddingStream(Stream stream)
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

        private static Stream ToCryptoContent(Stream stream, IExchangeEncrypt exchangeEncrypt)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (exchangeEncrypt == null) throw new ArgumentNullException("exchangeEncrypt");

            try
            {
                ArraySegment<byte> value = new ArraySegment<byte>();

                try
                {
                    byte[] cryptoKey = new byte[64];
                    _random.GetBytes(cryptoKey);

                    using (BufferStream outStream = new BufferStream(_bufferManager))
                    {
                        using (Stream inStream = new WrapperStream(stream, true))
                        using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                        using (CryptoStream cs = new CryptoStream(new WrapperStream(outStream, true), rijndael.CreateEncryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Write))
                        {
                            byte[] buffer = _bufferManager.TakeBuffer(1024 * 32);

                            try
                            {
                                int length = 0;

                                while (0 < (length = inStream.Read(buffer, 0, buffer.Length)))
                                {
                                    cs.Write(buffer, 0, length);
                                }
                            }
                            finally
                            {
                                _bufferManager.ReturnBuffer(buffer);
                            }
                        }

                        outStream.Seek(0, SeekOrigin.Begin);

                        value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)outStream.Length), 0, (int)outStream.Length);
                        outStream.Read(value.Array, value.Offset, value.Count);
                    }

                    var encryptedCryptoKey = Exchange.Encrypt(exchangeEncrypt, cryptoKey);

                    return new CryptoContent(value, CryptoAlgorithm.Rijndael256, encryptedCryptoKey).Export(_bufferManager);
                }
                finally
                {
                    if (value.Array != null)
                    {
                        _bufferManager.ReturnBuffer(value.Array);
                    }
                }
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream FromCryptoContent(Stream stream, IExchangeDecrypt exchangeDecrypt)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (exchangeDecrypt == null) throw new ArgumentNullException("exchangeDecrypt");

            try
            {
                var cryptoContent = CryptoContent.Import(stream, _bufferManager);

                try
                {
                    byte[] cryptoKey = Exchange.Decrypt(exchangeDecrypt, cryptoContent.CryptoKey);
                    var outStream = new BufferStream(_bufferManager);

                    if (cryptoContent.CryptoAlgorithm == CryptoAlgorithm.Rijndael256)
                    {
                        using (var inStream = new MemoryStream(cryptoContent.Content.Array, cryptoContent.Content.Offset, cryptoContent.Content.Count))
                        using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                        using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
                        {
                            byte[] buffer = _bufferManager.TakeBuffer(1024 * 32);

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

                    return outStream;
                }
                finally
                {
                    _bufferManager.ReturnBuffer(cryptoContent.Content.Array);
                }
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        public static ArraySegment<byte> ToProfileContentBlock(ProfileContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream stream = ContentConverter.ToStream<ProfileContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static ProfileContent FromProfileContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<ProfileContent>(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToDocumentContentBlock(DocumentContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream stream = ContentConverter.ToStream<DocumentContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static DocumentContent FromDocumentContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<DocumentContent>(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToTopicContentBlock(TopicContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream stream = ContentConverter.ToStream<TopicContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static TopicContent FromTopicContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<TopicContent>(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToMessageContentBlock(MessageContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream stream = ContentConverter.ToStream<MessageContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static MessageContent FromMessageContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<MessageContent>(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToMailContentBlock(MailContent content, IExchangeEncrypt exchangeEncrypt)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (exchangeEncrypt == null) throw new ArgumentNullException("exchangeEncrypt");

            ArraySegment<byte> value;

            using (Stream contentStream = ContentConverter.ToStream<MailContent>(content))
            using (Stream paddingStream = ContentConverter.ToPaddingStream(contentStream, 1024 * 16))
            using (Stream cryptostream = ContentConverter.ToCryptoContent(paddingStream, exchangeEncrypt))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)cryptostream.Length), 0, (int)cryptostream.Length);
                cryptostream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static MailContent FromMailContentBlock(ArraySegment<byte> content, IExchangeDecrypt exchangeDecrypt)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");
            if (exchangeDecrypt == null) throw new ArgumentNullException("exchangeDecrypt");

            try
            {
                using (Stream cryptoStream = new MemoryStream(content.Array, content.Offset, content.Count))
                using (Stream paddingStream = ContentConverter.FromCryptoContent(cryptoStream, exchangeDecrypt))
                using (Stream contentStream = ContentConverter.FromPaddingStream(paddingStream))
                {
                    return ContentConverter.FromStream<MailContent>(contentStream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
