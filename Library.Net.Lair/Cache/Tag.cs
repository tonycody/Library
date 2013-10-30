using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "Tag", Namespace = "http://Library/Net/Lair")]
    public sealed class Tag : ItemBase<Tag>, ITag
    {
        private enum SerializeId : byte
        {
            Type = 0,
            Id = 1,
            Argument = 2,
        }

        private string _type = null;
        private byte[] _id = null;
        private ArgumentCollection _arguments = null;

        private int _hashCode = 0;

        public static readonly int MaxTypeLength = 256;
        public static readonly int MaxIdLength = 64;
        public static readonly int MaxArgumentCount = 32;

        public Tag(string type, byte[] id, IEnumerable<string> arguments)
        {
            this.Type = type;
            this.Id = id;
            if (arguments != null) this.ProtectedArguments.AddRange(arguments);
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
                    if (id == (byte)SerializeId.Type)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.Type = reader.ReadToEnd();
                        }
                    }
                    else if (id == (byte)SerializeId.Id)
                    {
                        byte[] buffer = new byte[rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.Id = buffer;
                    }
                    else if (id == (byte)SerializeId.Argument)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ProtectedArguments.Add(reader.ReadToEnd());
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Type
            if (this.Type != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.Type);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Type);

                streams.Add(bufferStream);
            }
            // Id
            if (this.Id != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)this.Id.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Id);
                bufferStream.Write(this.Id, 0, this.Id.Length);

                streams.Add(bufferStream);
            }
            // Arguments
            foreach (var a in this.Arguments)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(a);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Argument);

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
            if ((object)obj == null || !(obj is Tag)) return false;

            return this.Equals((Tag)obj);
        }

        public override bool Equals(Tag other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Type != other.Type
                || (this.Id == null) != (other.Id == null)
                || (this.Arguments == null) != (other.Arguments == null))
            {
                return false;
            }

            if (this.Id != null && other.Id != null)
            {
                if (!Collection.Equals(this.Id, other.Id)) return false;
            }

            if (this.Arguments != null && other.Arguments != null)
            {
                if (!Collection.Equals(this.Arguments, other.Arguments)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Type;
        }

        public override Tag DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return Tag.Import(stream, BufferManager.Instance);
            }
        }

        #region ITag

        [DataMember(Name = "Type")]
        public string Type
        {
            get
            {
                return _type;
            }
            private set
            {
                if (value != null && value.Length > Tag.MaxTypeLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _type = value;
                }
            }
        }

        [DataMember(Name = "Id")]
        public byte[] Id
        {
            get
            {
                return _id;
            }
            private set
            {
                if (value != null && (value.Length > Tag.MaxIdLength))
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

        public IEnumerable<string> Arguments
        {
            get
            {
                return this.ProtectedArguments;
            }
        }

        [DataMember(Name = "Arguments")]
        private ArgumentCollection ProtectedArguments
        {
            get
            {
                if (_arguments == null)
                    _arguments = new ArgumentCollection(Tag.MaxArgumentCount);

                return _arguments;
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
