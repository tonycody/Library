using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Library.Io;

namespace Library.Security
{
    public static class HmacSha512
    {
        private static readonly int _blockLength = 128;
        private static readonly byte[] _ipad;
        private static readonly byte[] _opad;

        static HmacSha512()
        {
            _ipad = new byte[_blockLength];
            _opad = new byte[_blockLength];

            for (int i = 0; i < _blockLength; i++)
            {
                _ipad[i] = 0x36;
                _opad[i] = 0x5C;
            }
        }

        public static byte[] ComputeHash(Stream inputStream, byte[] key)
        {
            if (inputStream == null) throw new ArgumentNullException("inputStream");
            if (key == null) throw new ArgumentNullException("key");

            var bufferManager = BufferManager.Instance;

            byte[] buffer = null;

            try
            {
                buffer = bufferManager.TakeBuffer(1024 * 32);

                using (var hashAlgorithm = SHA512.Create())
                {
                    if (key.Length > _blockLength)
                    {
                        key = hashAlgorithm.ComputeHash(key);
                    }

                    var ixor = Native.Xor(_ipad, key);
                    var oxor = Native.Xor(_opad, key);

                    byte[] ihash;

                    {
                        hashAlgorithm.Initialize();
                        hashAlgorithm.TransformBlock(ixor, 0, ixor.Length, ixor, 0);

                        {
                            int length = 0;

                            while (0 < (length = inputStream.Read(buffer, 0, buffer.Length)))
                            {
                                hashAlgorithm.TransformBlock(buffer, 0, length, buffer, 0);
                            }
                        }

                        hashAlgorithm.TransformFinalBlock(new byte[0], 0, 0);

                        ihash = hashAlgorithm.Hash;
                    }

                    byte[] ohash;

                    {
                        hashAlgorithm.Initialize();
                        hashAlgorithm.TransformBlock(oxor, 0, oxor.Length, oxor, 0);
                        hashAlgorithm.TransformBlock(ihash, 0, ihash.Length, ihash, 0);
                        hashAlgorithm.TransformFinalBlock(new byte[0], 0, 0);

                        ohash = hashAlgorithm.Hash;
                    }

                    return ohash;
                }
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