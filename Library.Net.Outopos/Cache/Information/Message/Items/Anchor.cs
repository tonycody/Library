using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Anchor", Namespace = "http://Library/Net/Outopos")]
    public sealed class Anchor : ItemBase<Anchor>, IAnchor
    {
        private enum SerializeId : byte
        {
            Hash = 0,

            HashAlgorithm = 1,
        }

        private volatile byte[] _hash;

        private volatile HashAlgorithm _hashAlgorithm = 0;

        private volatile int _hashCode;

        public static readonly int MaxHashLength = 32;

        public Anchor(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            this.Hash = hash;

            this.HashAlgorithm = hashAlgorithm;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            for (; ; )
            {
                byte id;
                {
                    byte[] idBuffer = new byte[1];
                    if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                    id = idBuffer[0];
                }

                int length;
                {
                    byte[] lengthBuffer = new byte[4];
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    length = NetworkConverter.ToInt32(lengthBuffer);
                }

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
            if ((object)obj == null || !(obj is Anchor)) return false;

            return this.Equals((Anchor)obj);
        }

        public override bool Equals(Anchor other)
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

        #region IAnchor

        [DataMember(Name = "Hash")]
        public byte[] Hash
        {
            get
            {
                return _hash;
            }
            private set
            {
                if (value != null && value.Length > Anchor.MaxHashLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _hash = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtilities.GetHashCode(value);
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
