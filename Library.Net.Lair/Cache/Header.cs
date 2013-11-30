using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "Header", Namespace = "http://Library/Net/Lair")]
    public sealed class Header : ReadOnlyCertificateItemBase<Header>, IHeader<Tag>, IThisLock
    {
        private enum SerializeId : byte
        {
            Tag = 0,
            Type = 1,
            Option = 2,
            CreationTime = 3,
            FormatType = 4,
            Content = 5,

            Certificate = 6,
        }

        private Tag _tag;
        private string _type;
        private OptionCollection _options;
        private DateTime _creationTime = DateTime.MinValue;
        private ContentFormatType _formatType;
        private byte[] _content;

        private Certificate _certificate;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxTypeLength = 256;
        public static readonly int MaxOptionCount = 32;
        public static readonly int MaxContentLength = 256;

        public Header(Tag tag, string type, IEnumerable<string> options, ContentFormatType formatType, byte[] content, DigitalSignature digitalSignature)
        {
            this.Tag = tag;
            this.Type = type;
            if (options != null) this.ProtectedOptions.AddRange(options);
            this.CreationTime = DateTime.UtcNow;
            this.FormatType = formatType;
            this.Content = content;

            this.CreateCertificate(digitalSignature);
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
                        if (id == (byte)SerializeId.Tag)
                        {
                            this.Tag = Tag.Import(rangeStream, bufferManager);
                        }
                        else if (id == (byte)SerializeId.Type)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Type = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.Option)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.ProtectedOptions.Add(reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CreationTime = DateTime.ParseExact(reader.ReadToEnd(), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                            }
                        }
                        else if (id == (byte)SerializeId.FormatType)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.FormatType = (ContentFormatType)Enum.Parse(typeof(ContentFormatType), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.Content)
                        {
                            byte[] buffer = new byte[rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.Content = buffer;
                        }

                        else if (id == (byte)SerializeId.Certificate)
                        {
                            this.Certificate = Certificate.Import(rangeStream, bufferManager);
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

                // Tag
                if (this.Tag != null)
                {
                    Stream exportStream = this.Tag.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Tag);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // Type
                if (this.Type != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.Type);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Type);

                    streams.Add(bufferStream);
                }
                // Options
                foreach (var o in this.Options)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(o);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Option);

                    streams.Add(bufferStream);
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
                // FormatType
                if (this.FormatType != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.FormatType.ToString());
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.FormatType);

                    streams.Add(bufferStream);
                }
                // Content
                if (this.Content != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Content.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Content);
                    bufferStream.Write(this.Content, 0, this.Content.Length);

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
            if ((object)obj == null || !(obj is Header)) return false;

            return this.Equals((Header)obj);
        }

        public override bool Equals(Header other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Tag != other.Tag
                || this.Type != other.Type
                || (this.Options == null) != (other.Options == null)
                || this.CreationTime != other.CreationTime
                || this.FormatType != other.FormatType
                || (this.Content == null) != (other.Content == null)

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.Options != null && other.Options != null)
            {
                if (!Collection.Equals(this.Options, other.Options)) return false;
            }

            if (this.Content != null && other.Content != null)
            {
                if (!Unsafe.Equals(this.Content, other.Content)) return false;
            }

            return true;
        }

        public override Header DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Header.Import(stream, BufferManager.Instance);
                }
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

        #region IHeader<Tag>

        [DataMember(Name = "Tag")]
        public Tag Tag
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _tag;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _tag = value;
                }
            }
        }

        [DataMember(Name = "Type")]
        public string Type
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _type;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Header.MaxTypeLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _type = value;
                    }
                }
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyOptions;

        public IEnumerable<string> Options
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyOptions == null)
                        _readOnlyOptions = new ReadOnlyCollection<string>(this.ProtectedOptions);

                    return _readOnlyOptions;
                }
            }
        }

        [DataMember(Name = "Options")]
        private OptionCollection ProtectedOptions
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_options == null)
                        _options = new OptionCollection(Header.MaxOptionCount);

                    return _options;
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
            private set
            {
                lock (this.ThisLock)
                {
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

        [DataMember(Name = "FormatType")]
        public ContentFormatType FormatType
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _formatType;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(ContentFormatType), value))
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

        [DataMember(Name = "Content")]
        public byte[] Content
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _content;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Header.MaxContentLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _content = value;
                    }

                    if (value != null && value.Length != 0)
                    {
                        if (value.Length >= 4) _hashCode = BitConverter.ToInt32(value, 0) & 0x7FFFFFFF;
                        else if (value.Length >= 2) _hashCode = BitConverter.ToUInt16(value, 0);
                        else _hashCode = value[0];
                    }
                    else
                    {
                        _hashCode = 0;
                    }
                }
            }
        }

        #endregion

        #region IComputeHash

        private byte[] _sha512_hash;

        public byte[] GetHash(HashAlgorithm hashAlgorithm)
        {
            lock (this.ThisLock)
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
        }

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            lock (this.ThisLock)
            {
                return Collection.Equals(this.GetHash(hashAlgorithm), hash);
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
