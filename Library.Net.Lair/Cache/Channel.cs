﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "Channel", Namespace = "http://Library/Net/Lair")]
    public class Channel : ItemBase<Channel>, IChannel, IThisLock
    {
        private enum SerializeId : byte
        {
            Id = 0,
            Name = 1,
        }

        private byte[] _id;
        private string _name = null;

        private int _hashCode = 0;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public const int MaxIdLength = 64;
        public const int MaxNameLength = 256;

        public Channel()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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
        }

        public override Stream Export(BufferManager bufferManager)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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

                return new AddStream(streams);
            }
        }

        public override int GetHashCode()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return _hashCode;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Channel)) return false;

            return this.Equals((Channel)obj);
        }

        public override bool Equals(Channel other)
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
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.Name;
            }
        }

        public override Channel DeepClone()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return Channel.Import(stream, bufferManager);
                }
            }
        }

        #region IChannel

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
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && (value.Length > Channel.MaxIdLength))
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
        }

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _name;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Channel.MaxNameLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _name = value;
                    }
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                using (DeadlockMonitor.Lock(_thisStaticLock))
                {
                    if (_thisLock == null)
                        _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }
}
