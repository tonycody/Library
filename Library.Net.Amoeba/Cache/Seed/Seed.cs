using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Seed", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Seed : CertificateItemBase<Seed>, ISeed<Key>, ICloneable<Seed>, IThisLock
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

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxCommentLength = 1024;

        public static readonly int MaxKeywordCount = 3;

        public static readonly int MaxCryptoKeyLength = 64;

        public Seed()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
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
                        if (id == (byte)SerializeId.Name)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Name = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.Length)
                        {
                            byte[] buffer = bufferManager.TakeBuffer((int)rangeStream.Length);
                            rangeStream.Read(buffer, 0, 8);

                            this.Length = NetworkConverter.ToInt64(buffer);

                            bufferManager.ReturnBuffer(buffer);
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
                        else if (id == (byte)SerializeId.Rank)
                        {
                            byte[] buffer = bufferManager.TakeBuffer((int)rangeStream.Length);
                            rangeStream.Read(buffer, 0, 4);

                            this.Rank = NetworkConverter.ToInt32(buffer);

                            bufferManager.ReturnBuffer(buffer);
                        }
                        else if (id == (byte)SerializeId.Key)
                        {
                            this.Key = Key.Import(rangeStream, bufferManager);
                        }

                        else if (id == (byte)SerializeId.Keyword)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Keywords.Add(reader.ReadToEnd());
                            }
                        }

                        else if (id == (byte)SerializeId.CompressionAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CompressionAlgorithm = (CompressionAlgorithm)Enum.Parse(typeof(CompressionAlgorithm), reader.ReadToEnd());
                            }
                        }

                        else if (id == (byte)SerializeId.CryptoAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CryptoAlgorithm = (CryptoAlgorithm)Enum.Parse(typeof(CryptoAlgorithm), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.CryptoKey)
                        {
                            byte[] buffer = new byte[(int)rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.CryptoKey = buffer;
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
                Encoding encoding = new UTF8Encoding(false);

                // Name
                if (this.Name != null)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.Name.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Name);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                // Length
                if (this.Length != 0)
                {
                    bufferStream.Write(NetworkConverter.GetBytes((int)8), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Length);
                    bufferStream.Write(NetworkConverter.GetBytes(this.Length), 0, 8);
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.CreationTime);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                // Comment
                if (this.Comment != null)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.Comment.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Comment);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                // Rank
                if (this.Rank != 0)
                {
                    bufferStream.Write(NetworkConverter.GetBytes((int)4), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Rank);
                    bufferStream.Write(NetworkConverter.GetBytes(this.Rank), 0, 4);
                }
                // Key
                if (this.Key != null)
                {
                    using (Stream exportStream = this.Key.Export(bufferManager))
                    {
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Key);

                        byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                        try
                        {
                            int length = 0;

                            while (0 < (length = exportStream.Read(buffer, 0, buffer.Length)))
                            {
                                bufferStream.Write(buffer, 0, length);
                            }
                        }
                        finally
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }

                // Keywords
                foreach (var value in this.Keywords)
                {
                    byte[] buffer = null;

                    try
                    {
                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Keyword);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }

                // CompressionAlgorithm
                if (this.CompressionAlgorithm != 0)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.CompressionAlgorithm.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.CompressionAlgorithm);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }

                // CryptoAlgorithm
                if (this.CryptoAlgorithm != 0)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.CryptoAlgorithm.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.CryptoAlgorithm);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                // CryptoKey
                if (this.CryptoKey != null)
                {
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.CryptoKey.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CryptoKey);
                    bufferStream.Write(this.CryptoKey, 0, this.CryptoKey.Length);
                }

                // Certificate
                if (this.Certificate != null)
                {
                    using (Stream exportStream = this.Certificate.Export(bufferManager))
                    {
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Certificate);

                        byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                        try
                        {
                            int length = 0;

                            while (0 < (length = exportStream.Read(buffer, 0, buffer.Length)))
                            {
                                bufferStream.Write(buffer, 0, length);
                            }
                        }
                        finally
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
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

                || !Collection.Equals(this.Keywords, other.Keywords)

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
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
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

                    if (_key == null)
                    {
                        _hashCode = 0;
                    }
                    else
                    {
                        _hashCode = _key.GetHashCode();
                    }
                }
            }
        }

        #endregion

        #region IKeywords

        IList<string> IKeywords.Keywords
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

        #endregion
    }
}
