using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Id = 1,
        }

        private string _type;
        private byte[] _id;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxTypeLength = 256;
        public static readonly int MaxIdLength = 64;

        public Tag(string type, byte[] id)
        {
            this.Type = type;
            this.Id = id;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
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
                        if (id == (byte)SerializeId.Type)
                        {
                            this.Type = ItemUtility.GetString(rangeStream);
                        }
                        else if (id == (byte)SerializeId.Id)
                        {
                            this.Id = ItemUtility.GetByteArray(rangeStream);
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Type
                if (this.Type != null)
                {
                    ItemUtility.Write(bufferStream, (byte)SerializeId.Type, this.Type);
                }
                // Id
                if (this.Id != null)
                {
                    ItemUtility.Write(bufferStream, (byte)SerializeId.Id, this.Id);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
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
            if ((object)obj == null || !(obj is Tag)) return false;

            return this.Equals((Tag)obj);
        }

        public override bool Equals(Tag other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Type != other.Type
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

        #region ITag

        [DataMember(Name = "Type")]
        public string Type
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _type;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
        }

        [DataMember(Name = "Id")]
        public byte[] Id
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _id;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
        }

        #endregion
    }
}
