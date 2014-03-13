using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net
{
    [DataContract(Name = "Key", Namespace = "http://Library/Net/Outopos")]
    public sealed class Key : ItemBase<Key>, IKey
    {
        private enum SerializeId : byte
        {
            Hash = 0,

            HashAlgorithm = 1,
        }

        private volatile byte[] _hash;

        private volatile HashAlgorithm _hashAlgorithm = 0;

        private volatile int _hashCode;

        public static readonly int MaxHashLength = 64;

        public Key(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            this.Hash = hash;

            this.HashAlgorithm = hashAlgorithm;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            Encoding encoding = new UTF8Encoding(false);
            byte[] lengthBuffer = new byte[4];

            for (; ; )
            {
                if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                int length = NetworkConverter.ToInt32(lengthBuffer);
                byte id = (byte)stream.ReadByte();

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.Hash)
                    {
                        byte[] buffer = new byte[rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.Hash = buffer;
                    }

                    else if (id == (byte)SerializeId.HashAlgorithm)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.HashAlgorithm = (HashAlgorithm)Enum.Parse(typeof(HashAlgorithm), reader.ReadToEnd());
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);
            Encoding encoding = new UTF8Encoding(false);

            // Hash
            if (this.Hash != null)
            {
                bufferStream.Write(NetworkConverter.GetBytes((int)this.Hash.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Hash);
                bufferStream.Write(this.Hash, 0, this.Hash.Length);
            }

            // HashAlgorithm
            if (this.HashAlgorithm != 0)
            {
                byte[] buffer = null;

                try
                {
                    var value = this.HashAlgorithm.ToString();

                    buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                    var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                    bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.HashAlgorithm);
                    bufferStream.Write(buffer, 0, length);
                }
                finally
                {
                    if (buffer != null)
                    {
                        bufferManager.ReturnBuffer(buffer);
                    }
                }
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Key)) return false;

            return this.Equals((Key)obj);
        }

        public override bool Equals(Key other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if ((this.Hash == null) != (other.Hash == null)

                || this.HashAlgorithm != other.HashAlgorithm)
            {
                return false;
            }

            if (this.Hash != null && other.Hash != null)
            {
                if (!Unsafe.Equals(this.Hash, other.Hash)) return false;
            }

            return true;
        }

        #region IKey

        [DataMember(Name = "Hash")]
        public byte[] Hash
        {
            get
            {
                return _hash;
            }
            private set
            {
                if (value != null && value.Length > Key.MaxHashLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _hash = value;
                }

                if (value != null && value.Length != 0)
                {
                    if (value.Length >= 4) _hashCode = BitConverter.ToInt32(value, 0) & 0x7FFFFFFF;
                    else if (value.Length >= 2) _hashCode = BitConverter.ToUInt16(value, 0);
                    else _hashCode = value[0];
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        #endregion

        #region IHashAlgorithm

        [DataMember(Name = "HashAlgorithm")]
        public HashAlgorithm HashAlgorithm
        {
            get
            {
                return _hashAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(HashAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _hashAlgorithm = value;
                }
            }
        }

        #endregion
    }
}
