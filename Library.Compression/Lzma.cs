using System;
using System.IO;
using Library.Io;

namespace Library.Compression
{
    public static class Lzma
    {
        private static object _encodeLockObject = new object();
        private static object _decodeLockObject = new object();

        private static Lazy<LzmaEncoder> _lzmaEncoder = new Lazy<LzmaEncoder>(() =>
        {
            Int32 dictionary = 1 << 20;
            Int32 posStateBits = 2;
            Int32 litContextBits = 3; // for normal files
            // UInt32 litContextBits = 0; // for 32-bit data
            Int32 litPosBits = 0;
            // UInt32 litPosBits = 2; // for 32-bit data
            Int32 algorithm = 2;
            Int32 numFastBytes = 128;

            string mf = "bt4";
            bool eos = true;
            //bool stdInMode = false;

            CoderPropID[] propIDs =  {
                CoderPropID.DictionarySize,
                CoderPropID.PosStateBits,
                CoderPropID.LitContextBits,
                CoderPropID.LitPosBits,
                CoderPropID.Algorithm,
                CoderPropID.NumFastBytes,
                CoderPropID.MatchFinder,
                CoderPropID.EndMarker
            };

            object[] properties = {
                (Int32)(dictionary),
                (Int32)(posStateBits),
                (Int32)(litContextBits),
                (Int32)(litPosBits),
                (Int32)(algorithm),
                (Int32)(numFastBytes),
                mf,
                eos
            };

            var encoder = new LzmaEncoder();
            encoder.SetCoderProperties(propIDs, properties);

            return encoder;
        });

        private static Lazy<LzmaDecoder> _lzmaDecoder = new Lazy<LzmaDecoder>(() =>
        {
            var decoder = new LzmaDecoder();

            return decoder;
        });

        public static void Compress(Stream inStream, Stream outStream, BufferManager bufferManager)
        {
            lock (_lzmaEncoder)
            {
                var encoder = _lzmaEncoder.Value;
                encoder.WriteCoderProperties(outStream);

                Int64 fileSize = -1;

                for (int i = 0; i < 8; i++)
                    outStream.WriteByte((Byte)(fileSize >> (8 * i)));

                using (var cis = new CacheStream(inStream, 1024 * 1024, bufferManager))
                using (var cos = new CacheStream(outStream, 1024 * 1024, bufferManager))
                {
                    encoder.Code(cis, new LzmaStreamWriter(cos), -1, -1, null);
                }
            }
        }

        public static void Decompress(Stream inStream, Stream outStream, BufferManager bufferManager)
        {
            lock (_lzmaDecoder)
            {
                var decoder = _lzmaDecoder.Value;

                byte[] properties = new byte[5];
                if (inStream.Read(properties, 0, 5) != 5)
                    throw (new Exception("input .lzma is too short"));

                decoder.SetDecoderProperties(properties);

                long outSize = 0;

                for (int i = 0; i < 8; i++)
                {
                    int v = inStream.ReadByte();
                    if (v < 0) throw (new Exception("Can't Read 1"));

                    outSize |= ((long)(byte)v) << (8 * i);
                }

                using (var cis = new CacheStream(inStream, 1024 * 1024, bufferManager))
                using (var cos = new CacheStream(outStream, 1024 * 1024, bufferManager))
                {
                    decoder.Code(cis, new LzmaStreamWriter(cos), -1, outSize, null);
                }
            }
        }
    }
}
