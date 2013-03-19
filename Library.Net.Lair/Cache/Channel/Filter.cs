﻿using System;
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
    public sealed class Filter : ReadOnlyCertificateItemBase<Filter>, IFilter<Channel, Key>
    {
        private enum SerializeId : byte
        {
            Channel = 0,
            CreationTime = 1,
            TrustSignature = 2,
            Anchor = 3,

            Certificate = 4,
        }

        private Channel _channel = null;
        private DateTime _creationTime = DateTime.MinValue;
        private SignatureCollection _trustSignatures = null;
        private KeyCollection _anchors = null;

        private Certificate _certificate;

        public const int MaxTrustSignaturesCount = 128;
        public const int MaxAnchorsCount = 1024;

        public Filter(Channel channel, IEnumerable<Key> anchors, DigitalSignature digitalSignature)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            this.Channel = channel;
            this.CreationTime = DateTime.UtcNow;
            if (anchors != null) this.ProtectedAnchors.AddRange(anchors);

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
                    else if (id == (byte)SerializeId.TrustSignature)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ProtectedTrustSignatures.Add(reader.ReadToEnd());
                        }
                    }
                    else if (id == (byte)SerializeId.Anchor)
                    {
                        this.ProtectedAnchors.Add(Key.Import(rangeStream, bufferManager));
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
            // TrustSignatures
            foreach (var c in this.TrustSignatures)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                {
                    writer.Write(c);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.TrustSignature);

                streams.Add(bufferStream);
            }
            // Anchors
            foreach (var a in this.Anchors)
            {
                Stream exportStream = a.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Anchor);

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

            if (this.ProtectedTrustSignatures != null && other.ProtectedTrustSignatures != null)
            {
                if (this.ProtectedTrustSignatures.Count != other.ProtectedTrustSignatures.Count) return false;

                for (int i = 0; i < this.ProtectedTrustSignatures.Count; i++) if (this.ProtectedTrustSignatures[i] != other.ProtectedTrustSignatures[i]) return false;
            }

            if (this.ProtectedAnchors != null && other.ProtectedAnchors != null)
            {
                if (this.ProtectedAnchors.Count != other.ProtectedAnchors.Count) return false;

                for (int i = 0; i < this.ProtectedAnchors.Count; i++) if (this.ProtectedAnchors[i] != other.ProtectedAnchors[i]) return false;
            }

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

        #region IFilter<Channel>

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
                    _trustSignatures = new SignatureCollection(Filter.MaxTrustSignaturesCount);

                return _trustSignatures;
            }
        }

        public IEnumerable<Key> Anchors
        {
            get
            {
                return this.ProtectedAnchors;
            }
        }

        [DataMember(Name = "Anchors")]
        private KeyCollection ProtectedAnchors
        {
            get
            {
                if (_anchors == null)
                    _anchors = new KeyCollection(Filter.MaxAnchorsCount);

                return _anchors;
            }
        }

        #endregion

        #region IComputeHash

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

        #endregion
    }
}