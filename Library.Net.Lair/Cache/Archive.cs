using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "Archive", Namespace = "http://Library/Net/Lair")]
    public sealed class Archive : ItemBase<Archive>, IArchive
    {
        private enum SerializeId : byte
        {
            Id = 0,
            Name = 1,
        }

        private byte[] _id;
        private string _name = null;

        private int _hashCode = 0;

        public static readonly int MaxIdLength = 64;
        public static readonly int MaxNameLength = 256;

        public Archive(byte[] id, string name)
        {
            this.Id = id;
            this.Name = name;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
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
                    if (id == (byte)SerializeId.Id)
                    {
                        byte[] buffer = new byte[rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.Id = buffer;
                    }
                    else if (id == (byte)SerializeId.Name)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.Name = reader.ReadToEnd();
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Id
            if (this.Id != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)this.Id.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Id);
                bufferStream.Write(this.Id, 0, this.Id.Length);

                streams.Add(bufferStream);
            }
            // Name
            if (this.Name != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.Name);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Name);

                streams.Add(bufferStream);
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Archive)) return false;

            return this.Equals((Archive)obj);
        }

        public override bool Equals(Archive other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Id == null) != (other.Id == null)
                || this.Name != other.Name)
            {
                return false;
            }

            if (this.Id != null && other.Id != null)
            {
                if (!Collection.Equals(this.Id, other.Id)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Name;
        }

        public override Archive DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return Archive.Import(stream, BufferManager.Instance);
            }
        }

        #region IArchive

        [DataMember(Name = "Id")]
        public byte[] Id
        {
            get
            {
                return _id;
            }
            private set
            {
                if (value != null && (value.Length > Archive.MaxIdLength))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _id = value;
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

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                return _name;
            }
            private set
            {
                if (value != null && value.Length > Archive.MaxNameLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _name = value;
                }
            }
        }

        #endregion

        #region IComputeHash

        private byte[] _sha512_hash = null;

        public byte[] GetHash(HashAlgorithm hashAlgorithm)
        {
            if (_sha512_hash == null)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    _sha512_hash = Sha512.ComputeHash(stream);
                }
            }

            if (hashAlgorithm == HashAlgorithm.Sha512)
            {
                return _sha512_hash;
            }

            return null;
        }

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            return Collection.Equals(this.GetHash(hashAlgorithm), hash);
        }

        #endregion
    }
}
