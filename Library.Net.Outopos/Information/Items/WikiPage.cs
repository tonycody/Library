using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using System.Collections.ObjectModel;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiPage", Namespace = "http://Library/Net/Outopos")]
    public sealed class WikiPage : ImmutableCertificateItemBase<WikiPage>, IWikiPage
    {
        private enum SerializeId : byte
        {
            Tag = 0,
            CreationTime = 1,
            FormatType = 2,
            Hypertext = 3,
        }

        private Wiki _tag;
        private DateTime _creationTime;
        private HypertextFormatType _formatType;
        private string _hypertext;

        private Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxHypertextLength = 1024 * 32;

        public WikiPage(Wiki tag, DateTime creationTime, HypertextFormatType formatType, string hypertext)
        {
            this.Tag = tag;
            this.CreationTime = creationTime;
            this.FormatType = formatType;
            this.Hypertext = hypertext;
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
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
                    else if (id == (byte)SerializeId.FormatType)
                    {
                        this.FormatType = (HypertextFormatType)Enum.Parse(typeof(HypertextFormatType), ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.Hypertext)
                    {
                        this.Hypertext = ItemUtilities.GetString(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
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
            // FormatType
            if (this.FormatType != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.FormatType, this.FormatType.ToString());
            }
            // Hypertext
            if (this.Hypertext != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Hypertext, this.Hypertext);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            if (this.Hypertext == null) return 0;
            else return this.Hypertext.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is WikiPage)) return false;

            return this.Equals((WikiPage)obj);
        }

        public override bool Equals(WikiPage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Tag != other.Tag
                || this.CreationTime != other.CreationTime
                || this.FormatType != other.FormatType
                || this.Hypertext != other.Hypertext)
            {
                return false;
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
                        ItemUtilities.Write(stream, byte.MaxValue, "WikiPage");
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

        [DataMember(Name = "FormatType")]
        public HypertextFormatType FormatType
        {
            get
            {
                lock (_thisLock)
                {
                    return _formatType;
                }
            }
            set
            {
                lock (_thisLock)
                {
                    if (!Enum.IsDefined(typeof(HypertextFormatType), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _formatType = value;
                    }
                }
            }
        }

        [DataMember(Name = "Hypertext")]
        public string Hypertext
        {
            get
            {
                lock (_thisLock)
                {
                    return _hypertext;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    if (value != null && value.Length > WikiPage.MaxHypertextLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _hypertext = value;
                    }
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
