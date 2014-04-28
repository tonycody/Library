using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
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

        private static InternPool<byte[]> _hashCache = new InternPool<byte[]>(new ByteArrayEqualityComparer());
        private volatile byte[] _hash;

        private volatile HashAlgorithm _hashAlgorithm = 0;

        private volatile int _hashCode;

        public static readonly int MaxHashLength = 64;

        public Key(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            this.Hash = hash;

            this.HashAlgorithm = hashAlgorithm;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
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
                        this.Hash = ItemUtilities.GetByteArray(rangeStream);
                    }

                    else if (id == (byte)SerializeId.HashAlgorithm)
                    {
                        this.HashAlgorithm = (HashAlgorithm)Enum.Parse(typeof(HashAlgorithm), ItemUtilities.GetString(rangeStream));
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // Hash
            if (this.Hash != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Hash, this.Hash);
            }

            // HashAlgorithm
            if (this.HashAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.HashAlgorithm, this.HashAlgorithm.ToString());
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

            if (!object.ReferenceEquals(this.Hash, other.Hash)
                || this.HashAlgorithm != other.HashAlgorithm)
            {
                return false;
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
                    _hash = _hashCache.GetValue(value, this);
                    //_hash = value;
                }

                if (value != null)
                {
                    _hashCode = RuntimeHelpers.GetHashCode(_hash);
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

        public class Comparer : IComparer<Key>
        {
            public int Compare(Key x, Key y)
            {
                int c = x._hashCode.CompareTo(y._hashCode);
                if (c != 0) return c;

                c = x._hashAlgorithm.CompareTo(y._hashAlgorithm);
                if (c != 0) return c;

                c = CollectionUtilities.Compare(x._hash, y._hash);
                if (c != 0) return c;

                return 0;
            }
        }
    }
}
