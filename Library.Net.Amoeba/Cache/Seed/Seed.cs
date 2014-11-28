using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Seed", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Seed : MutableCertificateItemBase<Seed>, ISeed<Key>, ICloneable<Seed>, IThisLock
    {
        private enum SerializeId : byte
        {
            Name = 0,
            Length = 1,
            CreationTime = 2,
            Comment = 3,
            Rank = 4,
            Key = 5,

            Keyword = 6,

            CompressionAlgorithm = 7,

            CryptoAlgorithm = 8,
            CryptoKey = 9,

            Certificate = 10,
        }

        private string _name;
        private long _length;
        private DateTime _creationTime;
        private string _comment;
        private int _rank;
        private Key _key;

        private KeywordCollection _keywords;

        private CompressionAlgorithm _compressionAlgorithm = 0;

        private CryptoAlgorithm _cryptoAlgorithm = 0;
        private byte[] _cryptoKey;

        private Certificate _certificate;

        private volatile int _hashCode;

        private volatile object _thisLock;

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxCommentLength = 1024;

        public static readonly int MaxKeywordCount = 3;

        public static readonly int MaxCryptoKeyLength = 256;

        public Seed()
        {

        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                for (; ; )
                {
                    byte id;
                    {
                        byte[] idBuffer = new byte[1];
                        if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                        id = idBuffer[0];
                    }

                    int length;
                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                        length = NetworkConverter.ToInt32(lengthBuffer);
                    }

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Name)
                        {
                            this.Name = ItemUtilities.GetString(rangeStream);
                        }
                        else if (id == (byte)SerializeId.Length)
                        {
                            this.Length = ItemUtilities.GetLong(rangeStream);
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                        else if (id == (byte)SerializeId.Comment)
                        {
                            this.Comment = ItemUtilities.GetString(rangeStream);
                        }
                        else if (id == (byte)SerializeId.Rank)
                        {
                            this.Rank = ItemUtilities.GetInt(rangeStream);
                        }
                        else if (id == (byte)SerializeId.Key)
                        {
                            this.Key = Key.Import(rangeStream, bufferManager);
                        }

                        else if (id == (byte)SerializeId.Keyword)
                        {
                            this.Keywords.Add(ItemUtilities.GetString(rangeStream));
                        }

                        else if (id == (byte)SerializeId.CompressionAlgorithm)
                        {
                            this.CompressionAlgorithm = (CompressionAlgorithm)Enum.Parse(typeof(CompressionAlgorithm), ItemUtilities.GetString(rangeStream));
                        }

                        else if (id == (byte)SerializeId.CryptoAlgorithm)
                        {
                            this.CryptoAlgorithm = (CryptoAlgorithm)Enum.Parse(typeof(CryptoAlgorithm), ItemUtilities.GetString(rangeStream));
                        }
                        else if (id == (byte)SerializeId.CryptoKey)
                        {
                            this.CryptoKey = ItemUtilities.GetByteArray(rangeStream);
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
            lock (this.ThisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Name
                if (this.Name != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Name, this.Name);
                }
                // Length
                if (this.Length != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Length, this.Length);
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                }
                // Comment
                if (this.Comment != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Comment, this.Comment);
                }
                // Rank
                if (this.Rank != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Rank, this.Rank);
                }
                // Key
                if (this.Key != null)
                {
                    using (var stream = this.Key.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                    }
                }

                // Keywords
                foreach (var value in this.Keywords)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Keyword, value);
                }

                // CompressionAlgorithm
                if (this.CompressionAlgorithm != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CompressionAlgorithm, this.CompressionAlgorithm.ToString());
                }

                // CryptoAlgorithm
                if (this.CryptoAlgorithm != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CryptoAlgorithm, this.CryptoAlgorithm.ToString());
                }
                // CryptoKey
                if (this.CryptoKey != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CryptoKey, this.CryptoKey);
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
            lock (this.ThisLock)
            {
                return _hashCode;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Seed)) return false;

            return this.Equals((Seed)obj);
        }

        public override bool Equals(Seed other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Name != other.Name
                || this.Length != other.Length
                || this.CreationTime != other.CreationTime
                || this.Comment != other.Comment
                || this.Rank != other.Rank
                || this.Key != other.Key

                || !CollectionUtilities.Equals(this.Keywords, other.Keywords)

                || this.CompressionAlgorithm != other.CompressionAlgorithm

                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || (this.CryptoKey == null) != (other.CryptoKey == null)

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.CryptoKey != null && other.CryptoKey != null)
            {
                if (!Unsafe.Equals(this.CryptoKey, other.CryptoKey)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            lock (this.ThisLock)
            {
                return this.Name;
            }
        }

        public override void CreateCertificate(DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                base.CreateCertificate(digitalSignature);
            }
        }

        public override bool VerifyCertificate()
        {
            lock (this.ThisLock)
            {
                return base.VerifyCertificate();
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
                    return this.Export(BufferManager.Instance);
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

        #region ISeed<Key>

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _name;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Seed.MaxNameLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _name = value;
                    }
                }
            }
        }

        [DataMember(Name = "Length")]
        public long Length
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _length;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _length = value;
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
                    var utc = value.ToUniversalTime();
                    _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
            }
        }

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
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Seed.MaxCommentLength)
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

        [DataMember(Name = "Rank")]
        public int Rank
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _rank;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _rank = value;
                }
            }
        }

        [DataMember(Name = "Key")]
        public Key Key
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _key;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _key = value;

                    if (value != null)
                    {
                        _hashCode = value.GetHashCode();
                    }
                    else
                    {
                        _hashCode = 0;
                    }
                }
            }
        }

        #endregion

        #region IKeywords

        ICollection<string> IKeywords.Keywords
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Keywords;
                }
            }
        }

        [DataMember(Name = "Keywords")]
        public KeywordCollection Keywords
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_keywords == null)
                        _keywords = new KeywordCollection(Seed.MaxKeywordCount);

                    return _keywords;
                }
            }
        }

        #endregion

        #region ICompressionAlgorithm

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _compressionAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(CompressionAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _compressionAlgorithm = value;
                    }
                }
            }
        }

        #endregion

        #region ICryptoAlgorithm

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(CryptoAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _cryptoAlgorithm = value;
                    }
                }
            }
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Seed.MaxCryptoKeyLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _cryptoKey = value;
                    }
                }
            }
        }

        #endregion

        #region ICloneable<Seed>

        public Seed Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Seed.Import(stream, BufferManager.Instance);
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }
}
