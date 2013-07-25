using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "ProfileHeader", Namespace = "http://Library/Net/Lair")]
    public sealed class ProfileHeader : ReadOnlyCertificateItemBase<ProfileHeader>, IProfileHeader<Section, Key>
    {
        private enum SerializeId : byte
        {
            Section = 0,
            CreationTime = 1,
            Content = 2,

            Certificate = 3,
        }

        private Section _section = null;
        private DateTime _creationTime = DateTime.MinValue;
        private Key _content = null;

        private Certificate _certificate;

        public ProfileHeader(Section section, Key content, DigitalSignature digitalSignature)
        {
            this.Section = section;
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
                    if (id == (byte)SerializeId.Section)
                    {
                        this.Section = Section.Import(rangeStream, bufferManager);
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
                        this.Content = Key.Import(rangeStream, bufferManager);
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

            // Section
            if (this.Section != null)
            {
                Stream exportStream = this.Section.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Section);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }
            // CreationTime
            if (this.CreationTime != DateTime.MinValue)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
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
                Stream exportStream = this.Content.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Content);

                streams.Add(new JoinStream(bufferStream, exportStream));
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
            if (this.Certificate == null) return 0;
            else return this.Certificate.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ProfileHeader)) return false;

            return this.Equals((ProfileHeader)obj);
        }

        public override bool Equals(ProfileHeader other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (!Collection.Equals(this.GetHash(HashAlgorithm.Sha512), other.GetHash(HashAlgorithm.Sha512))) return false;

            return true;
        }

        public override string ToString()
        {
            if (this.Certificate == null) return "";
            else return this.Certificate.ToString();
        }

        public override ProfileHeader DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return ProfileHeader.Import(stream, BufferManager.Instance);
            }
        }

        protected override Stream GetCertificateStream()
        {
            var temp = this.Certificate;
            this.Certificate = null;

            try
            {
                return this.Export(BufferManager.Instance);
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

        #region IProfileHeader<Section, Key>

        [DataMember(Name = "Section")]
        public Section Section
        {
            get
            {
                return _section;
            }
            private set
            {
                _section = value;
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
        public Key Content
        {
            get
            {
                return _content;
            }
            private set
            {
                _content = value;
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

    [DataContract(Name = "ProfileContent", Namespace = "http://Library/Net/Lair")]
    public sealed class ProfileContent : ItemBase<ProfileContent>, IProfileContent<Channel>
    {
        private enum SerializeId : byte
        {
            TrustSignature = 0,
            Channel = 1,

            ExchangeAlgorithm = 2,
            ExchangeKey = 3,
        }

        private SignatureCollection _trustSignatures = null;
        private ChannelCollection _channels = null;

        private ExchangeAlgorithm _exchangeAlgorithm = 0;
        private byte[] _exchangeKey = null;

        private Certificate _certificate;

        public static readonly int MaxTrustSignaturesCount = 1024;
        public static readonly int MaxChannelsCount = 1024;

        public static readonly int MaxExchangeKeyLength = 1024 * 8;

        public ProfileContent(SignatureCollection trustSignatures, ChannelCollection channels, ExchangeAlgorithm exchangeAlgorithm, byte[] exchangeKey, DigitalSignature digitalSignature)
        {
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (channels != null) this.ProtectedChannels.AddRange(channels);

            this.ExchangeAlgorithm = exchangeAlgorithm;
            this.ExchangeKey = exchangeKey;
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
                    if (id == (byte)SerializeId.TrustSignature)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ProtectedTrustSignatures.Add(reader.ReadToEnd());
                        }
                    }
                    else if (id == (byte)SerializeId.Channel)
                    {
                        this.ProtectedChannels.Add(Channel.Import(rangeStream, bufferManager));
                    }

                    else if (id == (byte)SerializeId.ExchangeAlgorithm)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ExchangeAlgorithm = (ExchangeAlgorithm)Enum.Parse(typeof(ExchangeAlgorithm), reader.ReadToEnd());
                        }
                    }
                    else if (id == (byte)SerializeId.ExchangeKey)
                    {
                        byte[] buffer = new byte[(int)rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.ExchangeKey = buffer;
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // TrustSignatures
            foreach (var t in this.TrustSignatures)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(t);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.TrustSignature);

                streams.Add(bufferStream);
            }
            // Channels
            foreach (var c in this.ProtectedChannels)
            {
                Stream exportStream = c.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Channel);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            // ExchangeAlgorithm
            if (this.ExchangeAlgorithm != 0)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.ExchangeAlgorithm.ToString());
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.ExchangeAlgorithm);

                streams.Add(bufferStream);
            }
            // ExchangeKey
            if (this.ExchangeKey != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)this.ExchangeKey.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.ExchangeKey);
                bufferStream.Write(this.ExchangeKey, 0, this.ExchangeKey.Length);

                streams.Add(bufferStream);
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            if (this.Certificate == null) return 0;
            else return this.Certificate.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ProfileContent)) return false;

            return this.Equals((ProfileContent)obj);
        }

        public override bool Equals(ProfileContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.Channels == null) != (other.Channels == null)

                || this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || (this.ExchangeKey == null) != (other.ExchangeKey == null))
            {
                return false;
            }

            if (this.TrustSignatures != null && other.TrustSignatures != null)
            {
                if (!Collection.Equals(this.TrustSignatures, other.TrustSignatures)) return false;
            }

            if (this.Channels != null && other.Channels != null)
            {
                if (!Collection.Equals(this.Channels, other.Channels)) return false;
            }

            if (this.ExchangeKey != null && other.ExchangeKey != null)
            {
                if (!Collection.Equals(this.ExchangeKey, other.ExchangeKey)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            if (this.Certificate == null) return "";
            else return this.Certificate.ToString();
        }

        public override ProfileContent DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return ProfileContent.Import(stream, BufferManager.Instance);
            }
        }

        protected override Stream GetCertificateStream()
        {
            var temp = this.Certificate;
            this.Certificate = null;

            try
            {
                return this.Export(BufferManager.Instance);
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

        #region IProfileContent<Channel, Archive>

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                return this.ProtectedTrustSignatures;
            }
        }

        [DataMember(Name = "TrustSignatures")]
        private SignatureCollection ProtectedTrustSignatures
        {
            get
            {
                if (_trustSignatures == null)
                    _trustSignatures = new SignatureCollection(ProfileContent.MaxTrustSignaturesCount);

                return _trustSignatures;
            }
        }

        public IEnumerable<Channel> Channels
        {
            get
            {
                return this.ProtectedChannels;
            }
        }

        [DataMember(Name = "Channels")]
        private ChannelCollection ProtectedChannels
        {
            get
            {
                if (_channels == null)
                    _channels = new ChannelCollection(ProfileContent.MaxChannelsCount);

                return _channels;
            }
        }

        #endregion

        #region IExchangeAlgorithm

        [DataMember(Name = "ExchangeAlgorithm")]
        public ExchangeAlgorithm ExchangeAlgorithm
        {
            get
            {
                return _exchangeAlgorithm;
            }
            set
            {
                if (!Enum.IsDefined(typeof(ExchangeAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _exchangeAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "ExchangeKey")]
        public byte[] ExchangeKey
        {
            get
            {
                return _exchangeKey;
            }
            set
            {
                if (value != null && value.Length > ProfileContent.MaxExchangeKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _exchangeKey = value;
                }
            }
        }

        #endregion
    }
}
