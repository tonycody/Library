﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "Section", Namespace = "http://Library/Net/Lair")]
    public sealed class Section : ItemBase<Section>, ISection
    {
        private enum SerializeId : byte
        {
            Id = 0,
            Name = 1,
        }

        private byte[] _id;
        private string _name = null;

        private int _hashCode = 0;

        public const int MaxIdLength = 64;
        public const int MaxNameLength = 256;

        public Section(byte[] id, string name)
        {
            if (id == null) throw new ArgumentNullException("id");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException("name");

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

                using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
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
            if ((object)obj == null || !(obj is Section)) return false;

            return this.Equals((Section)obj);
        }

        public override bool Equals(Section other)
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
                if (this.Id.Length != other.Id.Length) return false;

                for (int i = 0; i < this.Id.Length; i++) if (this.Id[i] != other.Id[i]) return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Name;
        }

        public override Section DeepClone()
        {
            using (var bufferManager = new BufferManager())
            using (var stream = this.Export(bufferManager))
            {
                return Section.Import(stream, bufferManager);
            }
        }

        #region ISection

        [DataMember(Name = "Id")]
        public byte[] Id
        {
            get
            {
                return _id;
            }
            private set
            {
                if (value != null && (value.Length > Section.MaxIdLength))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _id = value;
                }

                if (value != null && value.Length != 0)
                {
                    try
                    {
                        if (value.Length >= 4) _hashCode = Math.Abs(BitConverter.ToInt32(value, 0));
                        else if (value.Length >= 2) _hashCode = BitConverter.ToUInt16(value, 0);
                        else _hashCode = value[0];
                    }
                    catch
                    {
                        _hashCode = 0;
                    }
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
                if (value != null && value.Length > Section.MaxNameLength)
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
    }
}