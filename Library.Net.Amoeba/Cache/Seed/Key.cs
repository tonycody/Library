using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Key", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Key : ItemBase<Key>, IKey
    {
        private enum SerializeId : byte
        {
            Hash = 0,

            HashAlgorithm = 1,
        }

        private byte[] _hash;

        private HashAlgorithm _hashAlgorithm = 0;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxHashLength = 64;

        public Key(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            this.Hash = hash;

            this.HashAlgorithm = hashAlgorithm;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            lock (this.ThisLock)
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
        }

        public override Stream Export(BufferManager bufferManager)
        {
            lock (this.ThisLock)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Hash
                if (this.Hash != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Hash.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Hash);
                    bufferStream.Write(this.Hash, 0, this.Hash.Length);

                    streams.Add(bufferStream);
                }

                // HashAlgorithm
                if (this.HashAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.HashAlgorithm.ToString());
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.HashAlgorithm);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return _hashCode;
            }
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
            if (this.GetHashCode() != other.GetHashCode()) return false;

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

        private object ThisLock
        {
            get
            {
                if (_thisLock == null)
                {
                    lock (_initializeLock)
                    {
                        if (_thisLock == null)
                        {
                            _thisLock = new object();
                        }
                    }
                }

                return _thisLock;
            }
        }

        #region IKey

        [DataMember(Name = "Hash")]
        public byte[] Hash
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hash;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
        }

        #endregion

        #region IHashAlgorithm

        [DataMember(Name = "HashAlgorithm")]
        public HashAlgorithm HashAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hashAlgorithm;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
        }

        #endregion
    }
}
