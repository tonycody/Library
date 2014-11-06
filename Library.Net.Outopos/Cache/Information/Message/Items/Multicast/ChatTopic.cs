using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ChatTopic", Namespace = "http://Library/Net/Outopos")]
    public sealed class ChatTopic : ImmutableCertificateItemBase<ChatTopic>, IMulticastHeader<Chat>, IChatTopicContent
    {
        private enum SerializeId : byte
        {
            Tag = 0,
            CreationTime = 1,

            FormatType = 2,
            Hypertext = 3,

            Certificate = 3,
        }

        private volatile Chat _tag;
        private DateTime _creationTime;

        private volatile HypertextFormatType _formatType;
        private volatile string _hypertext;

        private volatile Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxCommentLength = 1024 * 4;

        internal ChatTopic(Chat tag, DateTime creationTime, HypertextFormatType formatType, string hypertext, DigitalSignature digitalSignature)
        {
            this.Tag = tag;
            this.CreationTime = creationTime;

            this.FormatType = formatType;
            this.Hypertext = hypertext;

            this.CreateCertificate(digitalSignature);
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
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
                    if (id == (byte)SerializeId.Tag)
                    {
                        this.Tag = Chat.Import(rangeStream, bufferManager);
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

                    else if (id == (byte)SerializeId.Certificate)
                    {
                        this.Certificate = Certificate.Import(rangeStream, bufferManager);
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

        public override int GetHashCode()
        {
            return this.CreationTime.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ChatTopic)) return false;

            return this.Equals((ChatTopic)obj);
        }

        public override bool Equals(ChatTopic other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Tag != other.Tag
                || this.CreationTime != other.CreationTime

                || this.FormatType != other.FormatType
                || this.Hypertext != other.Hypertext

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            return true;
        }

        protected override void CreateCertificate(DigitalSignature digitalSignature)
        {
            base.CreateCertificate(digitalSignature);
        }

        public override bool VerifyCertificate()
        {
            return base.VerifyCertificate();
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

        #region IMulticastHeader<Chat>

        [DataMember(Name = "Tag")]
        public Chat Tag
        {
            get
            {
                return _tag;
            }
            private set
            {
                _tag = value;
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

        #endregion

        #region IHypertext

        [DataMember(Name = "FormatType")]
        public HypertextFormatType FormatType
        {
            get
            {
                return _formatType;
            }
            set
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

        [DataMember(Name = "Hypertext")]
        public string Hypertext
        {
            get
            {
                return _hypertext;
            }
            private set
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

        #endregion

        #region IComputeHash

        private volatile byte[] _Sha256_hash;

        public byte[] CreateHash(HashAlgorithm hashAlgorithm)
        {
            if (_Sha256_hash == null)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    _Sha256_hash = Sha256.ComputeHash(stream);
                }
            }

            if (hashAlgorithm == HashAlgorithm.Sha256)
            {
                return _Sha256_hash;
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
