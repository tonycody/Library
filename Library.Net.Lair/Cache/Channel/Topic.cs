using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Library;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "Topic", Namespace = "http://Library/Net/Lair")]
    public sealed class Topic : ReadOnlyCertificateItemBase<Topic>, ITopic<Channel>
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

        public const int MaxContentLength = 1024 * 8;

        public Topic(Channel channel, string content, DigitalSignature digitalSignature)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentNullException("content");

            this.Channel = channel;
            this.CreationTime = DateTime.UtcNow;
            this.Content = content;

            this.CreateCertificate(digitalSignature);
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

        public override Stream Export(BufferManager bufferManager)
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

                streams.Add(new JoinStream(bufferStream, exportStream));
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

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            if (_content == null) return 0;
            else return _content.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Topic)) return false;

            return this.Equals((Topic)obj);
        }

        public override bool Equals(Topic other)
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
            return this.Content;
        }

        public override Topic DeepClone()
        {
            using (var bufferManager = new BufferManager())
            using (var stream = this.Export(bufferManager))
            {
                return Topic.Import(stream, bufferManager);
            }
        }

        protected override Stream GetCertificateStream()
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

        public override Certificate Certificate
        {
            get
            {
                return _certificate;
            }
            protected set
            {
                _certificate = value;
            }
        }

        private byte[] _sha512_hash = null;

        public byte[] GetHash(HashAlgorithm hashAlgorithm)
        {
            if (_sha512_hash == null)
            {
                using (BufferManager bufferManager = new BufferManager())
                using (Stream stream = this.Export(bufferManager))
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

        #region ITopic<Channel>

        [DataMember(Name = "Channel")]
        public Channel Channel
        {
            get
            {
                return _channel;
            }
            private set
            {
                _channel = value;
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                return _creationTime;
            }
            private set
            {
                var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
            }
        }

        [DataMember(Name = "Content")]
        public string Content
        {
            get
            {
                return _content;
            }
            private set
            {
                if (value != null && value.Length > Topic.MaxContentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _content = value;
                }
            }
        }

        #endregion
    }
}
