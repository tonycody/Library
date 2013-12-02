using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "SectionProfileContent", Namespace = "http://Library/Net/Lair")]
    internal sealed class SectionProfileContent : ItemBase<SectionProfileContent>, ISectionProfileContent<ILink<Tag>>
    {
        private enum SerializeId : byte
        {
            Comment = 0,
            TrustSignature = 1,
            Link = 2,

            ExchangeAlgorithm = 3,
            PublicKey = 4,
        }

        private string _comment;
        private SignatureCollection _trustSignatures;
        private LinkCollection _links;

        private ExchangeAlgorithm _exchangeAlgorithm;
        private byte[] _publicKey;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxCommentLength = 1024 * 4;
        public static readonly int MaxTrustSignaturesCount = 1024;
        public static readonly int MaxLinksCount = 1024;

        public static readonly int MaxPublickeyLength = 1024 * 8;

        public SectionProfileContent(string comment, IEnumerable<string> trustSignatures, IEnumerable<Link> links, IExchangeEncrypt exchangeEncrypt)
        {
            this.Comment = comment;
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (links != null) this.ProtectedLinks.AddRange(links);

            this.ExchangeAlgorithm = exchangeEncrypt.ExchangeAlgorithm;
            this.PublicKey = exchangeEncrypt.PublicKey;
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
                        if (id == (byte)SerializeId.Comment)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Comment = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.TrustSignature)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.ProtectedTrustSignatures.Add(reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.Link)
                        {
                            this.ProtectedLinks.Add(Link.Import(rangeStream, bufferManager));
                        }

                        else if (id == (byte)SerializeId.ExchangeAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.ExchangeAlgorithm = (ExchangeAlgorithm)Enum.Parse(typeof(ExchangeAlgorithm), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.PublicKey)
                        {
                            byte[] buffer = new byte[(int)rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.PublicKey = buffer;
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

                // Comment
                if (this.Comment != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.Comment);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Comment);

                    streams.Add(bufferStream);
                }
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
                // Links
                foreach (var l in this.ProtectedLinks)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(l);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.TrustSignature);

                    streams.Add(bufferStream);
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
                // PublicKey
                if (this.PublicKey != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.PublicKey.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.PublicKey);
                    bufferStream.Write(this.PublicKey, 0, this.PublicKey.Length);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
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
            if ((object)obj == null || !(obj is SectionProfileContent)) return false;

            return this.Equals((SectionProfileContent)obj);
        }

        public override bool Equals(SectionProfileContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Comment != other.Comment
                || (this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.Links == null) != (other.Links == null)

                || this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null)))
            {
                return false;
            }

            if (this.TrustSignatures != null && other.TrustSignatures != null)
            {
                if (!Collection.Equals(this.TrustSignatures, other.TrustSignatures)) return false;
            }

            if (this.Links != null && other.Links != null)
            {
                if (!Collection.Equals(this.Links, other.Links)) return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Collection.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            return true;
        }

        public override SectionProfileContent DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return SectionProfileContent.Import(stream, BufferManager.Instance);
                }
            }
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

        #region ISectionProfile<Tag>

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _comment;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {

                    if (value != null && value.Length > SectionProfileContent.MaxCommentLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _comment = value;
                    }
                }
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyTrustSignatures;

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyTrustSignatures == null)
                        _readOnlyTrustSignatures = new ReadOnlyCollection<string>(this.ProtectedTrustSignatures);

                    return _readOnlyTrustSignatures;
                }
            }
        }

        [DataMember(Name = "TrustSignatures")]
        private SignatureCollection ProtectedTrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_trustSignatures == null)
                        _trustSignatures = new SignatureCollection(SectionProfileContent.MaxTrustSignaturesCount);

                    return _trustSignatures;
                }
            }
        }

        private volatile ReadOnlyCollection<Link> _readOnlyLinks;

        public IEnumerable<Link> Links
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyLinks == null)
                        _readOnlyLinks = new ReadOnlyCollection<Link>(this.ProtectedLinks);

                    return _readOnlyLinks;
                }
            }
        }

        [DataMember(Name = "Links")]
        private LinkCollection ProtectedLinks
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_links == null)
                        _links = new LinkCollection(SectionProfileContent.MaxLinksCount);

                    return _links;
                }
            }
        }

        #endregion

        #region  IExchangeEncrypt

        [DataMember(Name = "ExchangeAlgorithm")]
        public ExchangeAlgorithm ExchangeAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _exchangeAlgorithm;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
        }

        [DataMember(Name = "PublicKey")]
        public byte[] PublicKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _publicKey;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    if (value != null && (value.Length > Exchange.MaxPublickeyLength))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _publicKey = value;
                    }

                    if (value != null && value.Length != 0)
                    {
                        _hashCode = BitConverter.ToInt32(Crc32_Castagnoli.ComputeHash(value), 0) & 0x7FFFFFFF;
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
