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
using Library.Compression;

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

        private enum CryptoAlgorithm
        {
            Rijndael256 = 0,
        }

        private static BufferManager _bufferManager = BufferManager.Instance;
        private static Lzma _lzma = new Lzma(1 << 20, 1 << 20);
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
                    list.Add(new KeyValuePair<int, Stream>((int)CompressionAlgorithm.Deflate, deflateBufferStream));
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
                    list.Add(new KeyValuePair<int, Stream>((int)CompressionAlgorithm.Lzma, lzmaBufferStream));
                }
                catch (Exception)
                {

                }

                list.Add(new KeyValuePair<int, Stream>((int)CompressionAlgorithm.None, stream));

                list.Sort((x, y) =>
                {
                    return x.Value.Length.CompareTo(y.Value.Length);
                });

#if DEBUG
                if (list[0].Value.Length != stream.Length)
                {
                    Debug.WriteLine("ContentConverter ToStream {3} : {0}→{1} {2}",
                        NetworkConverter.ToSizeString(stream.Length),
                        NetworkConverter.ToSizeString(list[0].Value.Length),
                        NetworkConverter.ToSizeString(list[0].Value.Length - stream.Length),
                        (CompressionAlgorithm)list[0].Key);
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
                byte type = (byte)stream.ReadByte();

                using (Stream dataStream = new WrapperStream(stream, true))
                {
                    if (type == (byte)CompressionAlgorithm.None)
                    {
                        return ItemBase<T>.Import(dataStream, _bufferManager);
                    }
                    else if (type == (byte)CompressionAlgorithm.Lzma)
                    {
                        using (BufferStream lzmaBufferStream = new BufferStream(_bufferManager))
                        {
                            using (var inStream = new WrapperStream(dataStream, true))
                            using (var outStream = new WrapperStream(lzmaBufferStream, true))
                            {
                                _lzma.Decompress(inStream, outStream, _bufferManager);
                            }

#if DEBUG
                            Debug.WriteLine("ContentConverter FromStream {3} : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(dataStream.Length),
                                NetworkConverter.ToSizeString(lzmaBufferStream.Length),
                                NetworkConverter.ToSizeString(dataStream.Length - lzmaBufferStream.Length),
                                CompressionAlgorithm.Lzma);
#endif

                            lzmaBufferStream.Seek(0, SeekOrigin.Begin);

                            return ItemBase<T>.Import(lzmaBufferStream, _bufferManager);
                        }
                    }
                    else if (type == (byte)CompressionAlgorithm.Deflate)
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
                            Debug.WriteLine("ContentConverter FromStream {3} : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(dataStream.Length),
                                NetworkConverter.ToSizeString(deflateBufferStream.Length),
                                NetworkConverter.ToSizeString(dataStream.Length - deflateBufferStream.Length),
                                CompressionAlgorithm.Deflate);
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

        private static Stream Encrypt(Stream stream, IExchangeEncrypt exchangeEncrypt)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (exchangeEncrypt == null) throw new ArgumentNullException("exchangeEncrypt");

            try
            {
                BufferStream outStream = null;

                try
                {
                    outStream = new BufferStream(_bufferManager);

                    // Version
                    outStream.WriteByte((byte)0);

                    byte[] cryptoKey = new byte[32];
                    _random.GetBytes(cryptoKey);

                    {
                        byte[] headerBuffer = new byte[1 + cryptoKey.Length];
                        headerBuffer[0] = (byte)CryptoAlgorithm.Rijndael256;
                        Array.Copy(cryptoKey, 0, headerBuffer, 1, cryptoKey.Length);

                        var encryptedBuffer = Exchange.Encrypt(exchangeEncrypt, headerBuffer);
                        outStream.Write(NetworkConverter.GetBytes((int)encryptedBuffer.Length), 0, 4);
                        outStream.Write(encryptedBuffer, 0, encryptedBuffer.Length);
                    }

                    byte[] iv = new byte[32];
                    _random.GetBytes(iv);
                    outStream.Write(iv, 0, iv.Length);

                    using (Stream inStream = new WrapperStream(stream, true))
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

        private static Stream Decrypt(Stream stream, IExchangeDecrypt exchangeDecrypt)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (exchangeDecrypt == null) throw new ArgumentNullException("exchangeDecrypt");

            try
            {
                byte version = (byte)stream.ReadByte();

                if (version == 0)
                {
                    byte type;
                    byte[] cryptoKey;

                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) throw new ArgumentException();
                        int length = NetworkConverter.ToInt32(lengthBuffer);

                        byte[] encryptedBuffer = new byte[length];
                        if (stream.Read(encryptedBuffer, 0, encryptedBuffer.Length) != encryptedBuffer.Length) throw new ArgumentException();

                        byte[] headerBuffer = Exchange.Decrypt(exchangeDecrypt, encryptedBuffer);
                        type = headerBuffer[0];
                        cryptoKey = new byte[headerBuffer.Length - 1];
                        Array.Copy(headerBuffer, 1, cryptoKey, 0, cryptoKey.Length);
                    }

                    BufferStream outStream = null;

                    try
                    {
                        using (Stream inStream = new WrapperStream(stream, true))
                        {
                            if (type == (byte)CryptoAlgorithm.Rijndael256)
                            {
                                outStream = new BufferStream(_bufferManager);

                                var iv = new byte[32];
                                inStream.Read(iv, 0, iv.Length);

                                using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
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

                                outStream.Seek(0, SeekOrigin.Begin);
                            }
                        }
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

        //private static Stream Encrypt(Stream stream, ICryptoAlgorithm cryptoInformation)
        //{
        //    if (stream == null) throw new ArgumentNullException("stream");
        //    if (cryptoInformation == null) throw new ArgumentNullException("cryptoInformation");

        //    try
        //    {
        //        BufferStream outStream = null;

        //        try
        //        {
        //            outStream = new BufferStream(_bufferManager);

        //            var iv = new byte[32];
        //            _random.GetBytes(iv);
        //            outStream.Write(iv, 0, iv.Length);

        //            if (cryptoInformation.CryptoAlgorithm == Library.Net.Lair.CryptoAlgorithm.Rijndael256)
        //            {
        //                using (Stream inStream = new WrapperStream(stream, true))
        //                using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
        //                using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateEncryptor(cryptoInformation.CryptoKey, iv), CryptoStreamMode.Read))
        //                {
        //                    byte[] buffer = _bufferManager.TakeBuffer(1024 * 4);

        //                    try
        //                    {
        //                        int length = 0;

        //                        while (0 != (length = cs.Read(buffer, 0, buffer.Length)))
        //                        {
        //                            outStream.Write(buffer, 0, length);
        //                        }
        //                    }
        //                    finally
        //                    {
        //                        _bufferManager.ReturnBuffer(buffer);
        //                    }
        //                }
        //            }

        //            outStream.Seek(0, SeekOrigin.Begin);
        //        }
        //        catch (Exception)
        //        {
        //            if (outStream != null)
        //                outStream.Dispose();

        //            throw;
        //        }

        //        return outStream;
        //    }
        //    catch (Exception e)
        //    {
        //        throw new ArgumentException(e.Message, e);
        //    }
        //}

        //private static Stream Decrypt(Stream stream, ICryptoAlgorithm cryptoInformation)
        //{
        //    if (stream == null) throw new ArgumentNullException("stream");
        //    if (cryptoInformation == null) throw new ArgumentNullException("cryptoInformation");

        //    try
        //    {
        //        BufferStream outStream = null;

        //        try
        //        {
        //            outStream = new BufferStream(_bufferManager);

        //            if (cryptoInformation.CryptoAlgorithm == Library.Net.Lair.CryptoAlgorithm.Rijndael256)
        //            {
        //                using (Stream inStream = new WrapperStream(stream, true))
        //                {
        //                    var iv = new byte[32];
        //                    inStream.Read(iv, 0, iv.Length);

        //                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
        //                    using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoInformation.CryptoKey, iv), CryptoStreamMode.Read))
        //                    {
        //                        byte[] buffer = _bufferManager.TakeBuffer(1024 * 4);

        //                        try
        //                        {
        //                            int length = 0;

        //                            while (0 != (length = cs.Read(buffer, 0, buffer.Length)))
        //                            {
        //                                outStream.Write(buffer, 0, length);
        //                            }
        //                        }
        //                        finally
        //                        {
        //                            _bufferManager.ReturnBuffer(buffer);
        //                        }
        //                    }
        //                }
        //            }

        //            outStream.Seek(0, SeekOrigin.Begin);
        //        }
        //        catch (Exception)
        //        {
        //            if (outStream != null)
        //                outStream.Dispose();

        //            throw;
        //        }

        //        return outStream;
        //    }
        //    catch (Exception e)
        //    {
        //        throw new ArgumentException(e.Message, e);
        //    }
        //}

        public static ArraySegment<byte> ToSectionProfileContentBlock(SectionProfileContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream stream = ContentConverter.ToStream<SectionProfileContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static SectionProfileContent FromSectionProfileContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<SectionProfileContent>(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToSectionMessageContentBlock(SectionMessageContent content, IExchangeEncrypt exchangeEncrypt)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (exchangeEncrypt == null) throw new ArgumentNullException("exchangeEncrypt");

            ArraySegment<byte> value;

            using (Stream contentStream = ContentConverter.ToStream<SectionMessageContent>(content))
            using (Stream paddingStream = ContentConverter.ToPaddingStream(contentStream, 1024 * 32))
            using (Stream cryptostream = ContentConverter.Encrypt(paddingStream, exchangeEncrypt))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)cryptostream.Length), 0, (int)cryptostream.Length);
                cryptostream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static SectionMessageContent FromSectionMessageContentBlock(ArraySegment<byte> content, IExchangeDecrypt exchangeDecrypt)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");
            if (exchangeDecrypt == null) throw new ArgumentNullException("exchangeDecrypt");

            try
            {
                using (Stream cryptoStream = new MemoryStream(content.Array, content.Offset, content.Count))
                using (Stream paddingStream = ContentConverter.Decrypt(cryptoStream, exchangeDecrypt))
                using (Stream contentStream = ContentConverter.FromPaddingStream(paddingStream))
                {
                    return ContentConverter.FromStream<SectionMessageContent>(contentStream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToDocumentPageContentBlock(DocumentPageContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream stream = ContentConverter.ToStream<DocumentPageContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static DocumentPageContent FromDocumentPageContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<DocumentPageContent>(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToDocumentOpinionContentBlock(DocumentOpinionContent content)
        {
            if (content == null) throw new ArgumentNullException("content");

            ArraySegment<byte> value;

            using (Stream stream = ContentConverter.ToStream<DocumentOpinionContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static DocumentOpinionContent FromDocumentOpinionContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<DocumentOpinionContent>(stream);
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

            using (Stream stream = ContentConverter.ToStream<ChatTopicContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static ChatTopicContent FromChatTopicContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<ChatTopicContent>(stream);
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

            using (Stream stream = ContentConverter.ToStream<ChatMessageContent>(content))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)stream.Length), 0, (int)stream.Length);
                stream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static ChatMessageContent FromChatMessageContentBlock(ArraySegment<byte> content)
        {
            if (content.Array == null) throw new ArgumentNullException("content.Array");

            try
            {
                using (Stream stream = new MemoryStream(content.Array, content.Offset, content.Count))
                {
                    return ContentConverter.FromStream<ChatMessageContent>(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
