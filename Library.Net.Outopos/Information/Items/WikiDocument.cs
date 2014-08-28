using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiDocument", Namespace = "http://Library/Net/Outopos")]
    public sealed class WikiDocument : ImmutableCertificateItemBase<WikiDocument>, IMulticastHeader<Wiki>
    {
        private enum SerializeId : byte
        {
            Tag = 0,
            CreationTime = 1,
            WikiPage = 2,

            Certificate = 3,
        }

        private Wiki _tag;
        private DateTime _creationTime;
        private WikiPageCollection _wikiPages;

        private Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxWikiPageCount = 256;

        public WikiDocument(Wiki tag, DateTime creationTime, IEnumerable<WikiPage> wikiPages)
        {
            this.Tag = tag;
            this.CreationTime = creationTime;
            if (wikiPages != null) this.ProtectedWikiPages.AddRange(wikiPages);
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (_thisLock)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Tag)
                        {
                            this.Tag = Wiki.Import(rangeStream, bufferManager);
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                        else if (id == (byte)SerializeId.WikiPage)
                        {
                            this.ProtectedWikiPages.Add(WikiPage.Import(rangeStream, bufferManager));
                        }

                        else if (id == (byte)SerializeId.Certificate)
                        {
                            this.Certificate = Certificate.Import(rangeStream, bufferManager);
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (_thisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Tag
                if (this.Tag != null)
                {
                    using (var stream = this.Tag.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Tag, stream);
                    }
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                }
                // WikiPages
                foreach (var value in this.WikiPages)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.WikiPage, stream);
                    }
                }

                // Certificate
                if (this.Certificate != null)
                {
                    using (var stream = this.Certificate.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Certificate, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }
        }

        public override int GetHashCode()
        {
            lock (_thisLock)
            {
                return this.CreationTime.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is WikiDocument)) return false;

            return this.Equals((WikiDocument)obj);
        }

        public override bool Equals(WikiDocument other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Tag != other.Tag
                || this.CreationTime != other.CreationTime
                || (this.WikiPages == null) != (other.WikiPages == null))
            {
                return false;
            }

            if (this.WikiPages != null && other.WikiPages != null)
            {
                if (!CollectionUtilities.Equals(this.WikiPages, other.WikiPages)) return false;
            }

            return true;
        }

        protected override void CreateCertificate(DigitalSignature digitalSignature)
        {
            lock (_thisLock)
            {
                base.CreateCertificate(digitalSignature);
            }
        }

        public override bool VerifyCertificate()
        {
            lock (_thisLock)
            {
                return base.VerifyCertificate();
            }
        }

        protected override Stream GetCertificateStream()
        {
            lock (_thisLock)
            {
                var temp = this.Certificate;
                this.Certificate = null;

                try
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        stream.Seek(0, SeekOrigin.End);
                        ItemUtilities.Write(stream, byte.MaxValue, "WikiDocument");
                        stream.Seek(0, SeekOrigin.Begin);

                        return this.Export(BufferManager.Instance);
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
                lock (_thisLock)
                {
                    return _certificate;
                }
            }
            protected set
            {
                lock (_thisLock)
                {
                    _certificate = value;
                }
            }
        }

        [DataMember(Name = "Tag")]
        public Wiki Tag
        {
            get
            {
                lock (_thisLock)
                {
                    return _tag;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    _tag = value;
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (_thisLock)
                {
                    return _creationTime;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    var utc = value.ToUniversalTime();
                    _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
            }
        }

        private volatile ReadOnlyCollection<WikiPage> _readOnlyWikiPages;

        public IEnumerable<WikiPage> WikiPages
        {
            get
            {
                lock (_thisLock)
                {
                    if (_readOnlyWikiPages == null)
                        _readOnlyWikiPages = new ReadOnlyCollection<WikiPage>(this.ProtectedWikiPages.ToArray());

                    return _readOnlyWikiPages;
                }
            }
        }

        [DataMember(Name = "WikiPages")]
        private WikiPageCollection ProtectedWikiPages
        {
            get
            {
                lock (_thisLock)
                {
                    if (_wikiPages == null)
                        _wikiPages = new WikiPageCollection(WikiDocument.MaxWikiPageCount);

                    return _wikiPages;
                }
            }
        }

        #region IComputeHash

        private volatile byte[] _sha512_hash;

        public byte[] CreateHash(HashAlgorithm hashAlgorithm)
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
            return Unsafe.Equals(this.CreateHash(hashAlgorithm), hash);
        }

        #endregion
    }
}
