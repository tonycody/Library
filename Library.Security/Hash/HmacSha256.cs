using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Library.Io;

namespace Library.Security
{
    public static class HmacSha256
    {
        private static readonly int _blockLength = 64;
        private static readonly byte[] _ipad;
        private static readonly byte[] _opad;

        static HmacSha256()
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
                buffer = bufferManager.TakeBuffer(1024 * 4);

                using (var hashAlgorithm = SHA256.Create())
                {
                    if (key.Length > _blockLength)
                    {
                        key = hashAlgorithm.ComputeHash(key);
                    }

                    var ixor = new byte[_blockLength];
                    Unsafe.Xor(_ipad, key, ixor);

                    var oxor = new byte[_blockLength];
                    Unsafe.Xor(_opad, key, oxor);

                    byte[] ihash;

                    {
                        hashAlgorithm.Initialize();
                        hashAlgorithm.TransformBlock(ixor, 0, ixor.Length, ixor, 0);

                        {
                            int length = 0;

                            while ((length = inputStream.Read(buffer, 0, buffer.Length)) > 0)
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
