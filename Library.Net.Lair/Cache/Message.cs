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
    [DataContract(Name = "Message", Namespace = "http://Library/Net/Lair")]
    public class Message : CertificateItemBase<Message>, IMessage<Channel>, IThisLock
    {
        private enum SerializeId : byte
        {
            Channel = 0,
            CreationTime = 1,
            Content = 2,

            Certificate = 3,
        }

        private Channel _channel = null;
        private DateTime _creationTime = DateTime.MinValue;
        private string _content = null;

        private Certificate _certificate;

        private bool _hash_recache = false;
        private byte[] _hash_sha512 = null;
        private int _hashCode = 0;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public const int MaxContentLength = 1024;

        public Message()
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
                        else if (id == (byte)SerializeId.Content)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Content = reader.ReadToEnd();
                            }
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
                // Content
                if (this.Content != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.Content);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Content);

                    streams.Add(bufferStream);
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
            if ((object)obj == null || !(obj is Message)) return false;

            return this.Equals((Message)obj);
        }

        public override bool Equals(Message other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Channel != other.Channel
                || this.CreationTime != other.CreationTime
                || this.Content != other.Content

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            lock (this.ThisLock)
            {
                return this.Content;
            }
        }

        public override Message DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return Message.Import(stream, bufferManager);
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
                    _hash_recache = true;

                    _certificate = value;
                }
            }
        }

        public bool VerifyKey(Key key)
        {
            lock (this.ThisLock)
            {
                if (_hash_recache)
                {
                    using (BufferManager bufferManager = new BufferManager())
                    using (Stream stream = this.Export(bufferManager))
                    {
                        if (key.HashAlgorithm == HashAlgorithm.Sha512)
                        {
                            _hash_sha512 = Sha512.ComputeHash(stream);
                        }
                    }

                    _hash_recache = false;
                }

                if (key.HashAlgorithm == HashAlgorithm.Sha512)
                {
                    return Collection.Equals(key.Hash, _hash_sha512);
                }

                return false;
            }
        }

        #region IMessage<Channel>

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
                    _hash_recache = true;

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
                    _hash_recache = true;

                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

        [DataMember(Name = "Content")]
        public string Content
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _content;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Message.MaxContentLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _hash_recache = true;

                        _content = value;
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
