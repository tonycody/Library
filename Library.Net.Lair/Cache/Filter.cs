using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Collections.Generic;
using Library;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "Filter", Namespace = "http://Library/Net/Lair")]
    public class Filter : CertificateItemBase<Filter>, IFilter<Key, Channel>, IThisLock
    {
        private enum SerializeId : byte
        {
            Channel = 0,
            CreationTime = 1,
            Key = 2,

            Certificate = 3,
        }

        private Channel _channel = null;
        private DateTime _creationTime = DateTime.MinValue;
        private KeyCollection _keys = null;

        private Certificate _certificate;

        private int _hashCode = 0;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public Filter()
        {

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
                        if (id == (byte)SerializeId.Channel)
                        {
                            this.Channel = Channel.Import(rangeStream, bufferManager);
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CreationTime = DateTime.ParseExact(reader.ReadToEnd(), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                            }
                        }
                        else if (id == (byte)SerializeId.Key)
                        {
                            this.Keys.Add(Key.Import(rangeStream, bufferManager));
                        }

                        else if (id == (byte)SerializeId.Certificate)
                        {
                            this.Certificate = Certificate.Import(rangeStream, bufferManager);
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

                // Channel
                if (this.Channel != null)
                {
                    Stream exportStream = this.Channel.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Channel);

                    streams.Add(new AddStream(bufferStream, exportStream));
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CreationTime);

                    streams.Add(bufferStream);
                }
                // Keys
                foreach (var k in this.Keys)
                {
                    Stream exportStream = k.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Key);

                    streams.Add(new AddStream(bufferStream, exportStream));
                }

                // Certificate
                if (this.Certificate != null)
                {
                    Stream exportStream = this.Certificate.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Certificate);

                    streams.Add(new AddStream(bufferStream, exportStream));
                }

                return new AddStream(streams);
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
            if ((object)obj == null || !(obj is Filter)) return false;

            return this.Equals((Filter)obj);
        }

        public override bool Equals(Filter other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Channel != other.Channel
                || this.CreationTime != other.CreationTime

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (!Collection.Equals(this.Keys, other.Keys)) return false;

            return true;
        }

        public override string ToString()
        {
            lock (this.ThisLock)
            {
                return this.Channel.Name;
            }
        }

        public override Filter DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return Filter.Import(stream, bufferManager);
                }
            }
        }

        protected override Stream GetCertificateStream()
        {
            lock (this.ThisLock)
            {
                var temp = this.Certificate;
                this.Certificate = null;

                try
                {
                    using (BufferManager bufferManager = new BufferManager())
                    {
                        return this.Export(bufferManager);
                    }
                }
                finally
                {
                    this.Certificate = temp;
                }
            }
        }

        public override Certificate Certificate
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _certificate;
                }
            }
            protected set
            {
                lock (this.ThisLock)
                {
                    _certificate = value;
                }
            }
        }

        #region IFilter<Channel>

        [DataMember(Name = "Channel")]
        public Channel Channel
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _channel;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _channel = value;
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _creationTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

        IList<Key> IFilter<Key, Channel>.Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Keys;
                }
            }
        }

        [DataMember(Name = "Keys")]
        public KeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_keys == null)
                        _keys = new KeyCollection();

                    return _keys;
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                lock (_thisStaticLock)
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
