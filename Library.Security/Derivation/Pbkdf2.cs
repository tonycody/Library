//Copyright (c) 2012 Josip Medved <jmedved@jmedved.com>

//2012-04-12: Initial version.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Library.Security
{
    /// <summary>
    /// Generic PBKDF2 implementation.
    /// </summary>
    /// <example>This sample shows how to initialize class with SHA-256 HMAC.
    /// <code>
    /// using (var hmac = new HMACSHA256()) {
    ///     var df = new Pbkdf2(hmac, "password", "salt");
    ///     var bytes = df.GetBytes();
    /// }
    /// </code>
    /// </example>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Pbkdf", Justification = "Spelling is correct.")]
    public unsafe class Pbkdf2
    {
        private readonly int _blockSize;
        private uint _blockIndex = 1;

        private byte[] _bufferBytes;
        private int _bufferStartIndex = 0;
        private int _bufferEndIndex = 0;

        /// <summary>
        /// Creates new instance.
        /// </summary>
        /// <param name="algorithm">HMAC algorithm to use.</param>
        /// <param name="password">The password used to derive the key.</param>
        /// <param name="salt">The key salt used to derive the key.</param>
        /// <param name="iterations">The number of iterations for the operation.</param>
        /// <exception cref="System.ArgumentNullException">Algorithm cannot be null - Password cannot be null. -or- Salt cannot be null.</exception>
        public Pbkdf2(HMAC algorithm, byte[] password, byte[] salt, int iterations)
        {
            if (algorithm == null) throw new ArgumentNullException("algorithm", "Algorithm cannot be null.");
            if (password == null) throw new ArgumentNullException("password", "Password cannot be null.");
            if (salt == null) throw new ArgumentNullException("salt", "Salt cannot be null.");

            this.Algorithm = algorithm;
            this.Algorithm.Key = password;
            this.Salt = salt;
            this.IterationCount = iterations;

            _blockSize = this.Algorithm.HashSize / 8;
            _bufferBytes = new byte[_blockSize];
        }

        /// <summary>
        /// Gets algorithm used for generating key.
        /// </summary>
        public HMAC Algorithm { get; private set; }

        /// <summary>
        /// Gets salt bytes.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Byte array is proper return value in this case.")]
        public byte[] Salt { get; private set; }

        /// <summary>
        /// Gets iteration count.
        /// </summary>
        public int IterationCount { get; private set; }

        /// <summary>
        /// Returns a pseudo-random key from a password, salt and iteration count.
        /// </summary>
        /// <param name="count">Number of bytes to return.</param>
        /// <returns>Byte array.</returns>
        public byte[] GetBytes(int count)
        {
            byte[] result = new byte[count];
            int resultOffset = 0;
            int bufferCount = _bufferEndIndex - _bufferStartIndex;

            if (bufferCount > 0)
            { //if there is some data in buffer
                if (count < bufferCount)
                { //if there is enough data in buffer
                    Buffer.BlockCopy(_bufferBytes, _bufferStartIndex, result, 0, count);
                    _bufferStartIndex += count;

                    return result;
                }

                Buffer.BlockCopy(_bufferBytes, _bufferStartIndex, result, 0, bufferCount);
                _bufferStartIndex = _bufferEndIndex = 0;
                resultOffset += bufferCount;
            }

            while (resultOffset < count)
            {
                int needCount = count - resultOffset;
                _bufferBytes = this.Func();

                if (needCount > _blockSize)
                { //we one (or more) additional passes
                    Buffer.BlockCopy(_bufferBytes, 0, result, resultOffset, _blockSize);
                    resultOffset += _blockSize;
                }
                else
                {
                    Buffer.BlockCopy(_bufferBytes, 0, result, resultOffset, needCount);
                    _bufferStartIndex = needCount;
                    _bufferEndIndex = _blockSize;

                    return result;
                }
            }
            return result;
        }

        private byte[] Func()
        {
            if (_blockIndex == uint.MaxValue) { throw new InvalidOperationException("Derived key too long."); }

            byte[] currentHash;

            {
                var input = new byte[this.Salt.Length + 4];
                Buffer.BlockCopy(this.Salt, 0, input, 0, this.Salt.Length);
                Buffer.BlockCopy(NetworkConverter.GetBytes(_blockIndex), 0, input, this.Salt.Length, 4);

                _blockIndex++;

                currentHash = this.Algorithm.ComputeHash(input);
            }

            byte[] finalHash = currentHash;

            for (int i = 2; i <= this.IterationCount; i++)
            {
                currentHash = this.Algorithm.ComputeHash(currentHash, 0, currentHash.Length);

                {
                    //for (int j = 0; j < this.BlockSize; j++)
                    //{
                    //    finalHash[j] = (byte)(finalHash[j] ^ hash1[j]);
                    //}
                }

                {
                    int length = _blockSize;

                    fixed (byte* p_x = finalHash, p_y = currentHash)
                    {
                        byte* t_x = p_x, t_y = p_y;

                        for (int j = (length / 8) - 1; j >= 0; j--, t_x += 8, t_y += 8)
                        {
                            *((long*)t_x) ^= *((long*)t_y);
                        }

                        if ((length & 4) != 0)
                        {
                            *((int*)t_x) ^= *((int*)t_y);
                            t_x += 4; t_y += 4;
                        }

                        if ((length & 2) != 0)
                        {
                            *((short*)t_x) ^= *((short*)t_y);
                            t_x += 2; t_y += 2; ;
                        }

                        if ((length & 1) != 0)
                        {
                            *((byte*)t_x) ^= *((byte*)t_y);
                        }
                    }
                }
            }

            return finalHash;
        }
    }
}
