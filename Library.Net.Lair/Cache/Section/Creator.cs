using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "Creator", Namespace = "http://Library/Net/Lair")]
    public sealed class Creator : ReadOnlyCertificateItemBase<Creator>, ICreator<Section, Channel>
    {
        private enum SerializeId : byte
        {
            Section = 0,
            CreationTime = 1,
            Comment = 2,
            Channel = 3,

            Certificate = 4,
        }

        private Section _section = null;
        private DateTime _creationTime = DateTime.MinValue;
        private string _comment = null;
        private ChannelCollection _channels = null;

        private Certificate _certificate;

        public static readonly int MaxCommentLength = 1024 * 4;
        public static readonly int MaxChannelsCount = 1024;

        public Creator(Section section, string comment, ChannelCollection channels, DigitalSignature digitalSignature)
        {
            this.Section = section;
            this.CreationTime = DateTime.UtcNow;
            this.Comment = comment;
            if (channels != null) this.ProtectedChannels.AddRange(channels);

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
                    else if (id == (byte)SerializeId.Comment)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.Comment = reader.ReadToEnd();
                        }
                    }
                    else if (id == (byte)SerializeId.Channel)
                    {
                        this.ProtectedChannels.Add(Channel.Import(rangeStream, bufferManager));
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
            // Comment
            if (this.Comment != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                {
                    writer.Write(this.Comment);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Comment);

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
            if (_comment == null) return 0;
            else return _comment.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Creator)) return false;

            return this.Equals((Creator)obj);
        }

        public override bool Equals(Creator other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Section != other.Section
                || this.CreationTime != other.CreationTime
                || this.Comment != other.Comment

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.ProtectedChannels != null && other.ProtectedChannels != null)
            {
                if (this.ProtectedChannels.Count != other.ProtectedChannels.Count) return false;

                for (int i = 0; i < this.ProtectedChannels.Count; i++) if (this.ProtectedChannels[i] != other.ProtectedChannels[i]) return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Comment;
        }

        public override Creator DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return Creator.Import(stream, BufferManager.Instance);
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

        #region ICreator<Section>

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

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                return _comment;
            }
            private set
            {
                if (value != null && value.Length > Creator.MaxCommentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _comment = value;
                }
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
                    _channels = new ChannelCollection(Creator.MaxChannelsCount);

                return _channels;
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
