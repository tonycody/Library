using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Library.Net.Amoeba;
using System.IO.Compression;
using System.Reflection;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    public static class AmoebaConverter
    {
        private enum CompressionAlgorithm
        {
            None = 0,
            XZ = 1,
        }

        private static BufferManager _bufferManager = new BufferManager();

        static AmoebaConverter()
        {

        }

        private static Stream ToStream<T>(ItemBase<T> item)
                where T : ItemBase<T>
        {
            Stream stream = null;
            BufferStream lzmaBufferStream = null;

            try
            {
                stream = item.Export(_bufferManager);
                lzmaBufferStream = new BufferStream(_bufferManager);

                if (System.Environment.Is64BitProcess)
                {
                    SevenZip.SevenZipCompressor.SetLibraryPath("7z64.dll");
                }
                else
                {
                    SevenZip.SevenZipCompressor.SetLibraryPath("7z86.dll");
                }

                var compressor = new SevenZip.SevenZipCompressor();
                compressor.ArchiveFormat = SevenZip.OutArchiveFormat.XZ;
                compressor.CompressionMethod = SevenZip.CompressionMethod.Lzma2;
                compressor.CompressionLevel = SevenZip.CompressionLevel.Low;

                compressor.CompressStream(stream, lzmaBufferStream);

                lzmaBufferStream.Seek(0, SeekOrigin.Begin);

                BufferStream headerStream = new BufferStream(_bufferManager);
                Stream dataStream = null;

                if (stream.Length < lzmaBufferStream.Length)
                {
                    headerStream.WriteByte((byte)CompressionAlgorithm.None);
                    dataStream = new AddStream(headerStream, stream);

                    lzmaBufferStream.Dispose();
                }
                else
                {
                    headerStream.WriteByte((byte)CompressionAlgorithm.XZ);
                    dataStream = new AddStream(headerStream, lzmaBufferStream);

                    stream.Dispose();
                }

                MemoryStream crcStream = new MemoryStream(Crc32_Castagnoli.ComputeHash(dataStream));
                return new AddStream(dataStream, crcStream);
            }
            catch (Exception ex)
            {
                if (stream != null)
                    stream.Dispose();
                if (lzmaBufferStream != null)
                    lzmaBufferStream.Dispose();

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
                byte version = (byte)stream.ReadByte();

                using (Stream dataStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position - 4, true))
                {
                    if (version == (byte)CompressionAlgorithm.None)
                    {
                        return ItemBase<T>.Import(dataStream, _bufferManager);
                    }
                    else if (version == (byte)CompressionAlgorithm.XZ)
                    {
                        using (BufferStream lzmaBufferStream = new BufferStream(_bufferManager))
                        {
                            if (System.Environment.Is64BitProcess)
                            {
                                SevenZip.SevenZipCompressor.SetLibraryPath("7z64.dll");
                            }
                            else
                            {
                                SevenZip.SevenZipCompressor.SetLibraryPath("7z86.dll");
                            }

                            var decompressor = new SevenZip.SevenZipExtractor(dataStream);
                            decompressor.ExtractFile(decompressor.ArchiveFileNames[0], lzmaBufferStream);

                            lzmaBufferStream.Seek(0, SeekOrigin.Begin);

                            return ItemBase<T>.Import(lzmaBufferStream, _bufferManager);
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

        private static string ToBase64String(Stream stream)
        {
            byte[] buffer = null;

            try
            {
                buffer = _bufferManager.TakeBuffer((int)stream.Length);
                stream.Seek(0, SeekOrigin.Begin);
                stream.Read(buffer, 0, (int)stream.Length);

                return NetworkConverter.ToBase64String(buffer, 0, (int)stream.Length);
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
            return new MemoryStream(NetworkConverter.FromBase64String(value));
        }

        public static string ToNodeString(Node item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                using (Stream stream = AmoebaConverter.ToStream<Node>(item))
                {
                    return "Node@" + AmoebaConverter.ToBase64String(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Node FromNodeString(string item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!item.StartsWith("Node@")) throw new ArgumentException("item");

            try
            {
                using (Stream stream = AmoebaConverter.FromBase64String(item.Remove(0, 5)))
                {
                    return AmoebaConverter.FromStream<Node>(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string ToSeedString(Seed item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                using (Stream stream = AmoebaConverter.ToStream<Seed>(item))
                {
                    return "Seed@" + AmoebaConverter.ToBase64String(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Seed FromSeedString(string item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!item.StartsWith("Seed@")) throw new ArgumentException("item");

            try
            {
                using (Stream stream = AmoebaConverter.FromBase64String(item.Remove(0, 5)))
                {
                    return AmoebaConverter.FromStream<Seed>(stream);
                }
            }
            catch (Exception)
            {
                return null;
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
                return null;
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
                return null;
            }
        }

        public static Stream ToSignatureStream(DigitalSignature item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                return AmoebaConverter.ToStream<DigitalSignature>(item);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static DigitalSignature FromSignatureStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            try
            {
                return AmoebaConverter.FromStream<DigitalSignature>(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
