using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Tag", Namespace = "http://Library/Net/Outopos")]
    public sealed class Tag : ItemBase<Tag>, ITag
    {
        private enum SerializeId : byte
        {
            Type = 0,
            Name = 1,
            Id = 2,
        }

        private volatile string _type;
        private volatile string _name;
        private volatile byte[] _id;

        private volatile int _hashCode;

        public static readonly int MaxTypeLength = 256;
        public static readonly int MaxNameLength = 256;
        public static readonly int MaxIdLength = 64;

        public Tag(string type, string name, byte[] id)
        {
            this.Type = type;
            this.Name = name;
            this.Id = id;
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
                    if (id == (byte)SerializeId.Type)
                    {
                        this.Type = ItemUtilities.GetString(rangeStream);
                    }
                    else if (id == (byte)SerializeId.Name)
                    {
                        this.Name = ItemUtilities.GetString(rangeStream);
                    }
                    else if (id == (byte)SerializeId.Id)
                    {
                        this.Id = ItemUtilities.GetByteArray(rangeStream);
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // Type
            if (this.Type != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Type, this.Type);
            }
            // Name
            if (this.Name != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Name, this.Name);
            }
            // Id
            if (this.Id != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Id, this.Id);
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
            if ((object)obj == null || !(obj is Tag)) return false;

            return this.Equals((Tag)obj);
        }

        public override bool Equals(Tag other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Type != other.Type
                || this.Name != other.Name
                || (this.Id == null) != (other.Id == null))
            {
                return false;
            }

            if (this.Id != null && other.Id != null)
            {
                if (!Unsafe.Equals(this.Id, other.Id)) return false;
            }

            return true;
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

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                return _name;
            }
            private set
            {
                if (value != null && value.Length > Tag.MaxNameLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _name = value;
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

                if (value != null)
                {
                    _hashCode = ItemUtilities.GetHashCode(_id);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        #endregion
    }
}
