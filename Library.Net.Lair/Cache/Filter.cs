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
    public class Filter : ReadOnlyCertificateItemBase<Filter>, IFilter<Key, Channel>
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

        public const int MaxKeysCount = 256;

        public Filter(Channel channel, IEnumerable<Key> keys, DigitalSignature digitalSignature)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            if (channel.Name == null) throw new ArgumentNullException("channel.Name");
            if (channel.Id == null) throw new ArgumentNullException("channel.Id");
            if (keys == null) throw new ArgumentNullException("keys");
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            this.Channel = channel;
            this.CreationTime = DateTime.UtcNow;
            this.ProtectedKeys.AddRange(keys);

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
                    else if (id == (byte)SerializeId.Key)
                    {
                        this.ProtectedKeys.Add(Key.Import(rangeStream, bufferManager));
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

        public override int GetHashCode()
        {
            return _creationTime.GetHashCode();
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
            return this.Channel.Name;
        }

        public override Filter DeepClone()
        {
            using (var bufferManager = new BufferManager())
            using (var stream = this.Export(bufferManager))
            {
                return Filter.Import(stream, bufferManager);
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

        public bool VerifyHash(HashAlgorithm hashAlgorithm, byte[] hash)
        {
            return Collection.Equals(this.GetHash(hashAlgorithm), hash);
        }

        #region IFilter<Channel>

        [DataMember(Name = "Channel")]
        public Channel Channel
        {
            get
            {
                return _channel;
            }
            protected set
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
            protected set
            {
                var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
            }
        }

        public IEnumerable<Key> Keys
        {
            get
            {
                return this.ProtectedKeys;
            }
        }

        [DataMember(Name = "Keys")]
        protected KeyCollection ProtectedKeys
        {
            get
            {
                if (_keys == null)
                    _keys = new KeyCollection(Filter.MaxKeysCount);

                return _keys;
            }
        }

        #endregion
    }
}
