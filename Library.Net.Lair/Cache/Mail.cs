using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "Mail", Namespace = "http://Library/Net/Lair")]
    public sealed class Mail : ReadOnlyCertificateItemBase<Mail>, IMail<Section>
    {
        private enum SerializeId : byte
        {
            Section = 0,
            CreationTime = 1,
            RecipientSignature = 2,
            Content = 3,

            Certificate = 4,
        }

        private Section _section = null;
        private DateTime _creationTime = DateTime.MinValue;
        private string _recipientSignature = null;
        private byte[] _content = null;

        private Certificate _certificate;

        public static readonly int MaxRecipientSignatureLength = 1024;
        public static readonly int MaxContentLength = 1024 * 8;

        public Mail(Section section, string recipientSignature, byte[] content, DigitalSignature digitalSignature)
        {
            this.Section = section;
            this.CreationTime = DateTime.UtcNow;
            this.RecipientSignature = recipientSignature;
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
                    else if (id == (byte)SerializeId.RecipientSignature)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.RecipientSignature = reader.ReadToEnd();
                        }
                    }
                    else if (id == (byte)SerializeId.Content)
                    {
                        byte[] buffer = new byte[(int)rangeStream.Length];
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
            // RecipientSignature
            if (this.RecipientSignature != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.RecipientSignature);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.RecipientSignature);

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

        public override int GetHashCode()
        {
            if (this.Certificate == null) return 0;
            else return this.Certificate.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Mail)) return false;

            return this.Equals((Mail)obj);
        }

        public override bool Equals(Mail other)
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

        public override Mail DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return Mail.Import(stream, BufferManager.Instance);
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

        #region IMail<Section>

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

        [DataMember(Name = "RecipientSignature")]
        public string RecipientSignature
        {
            get
            {
                return _recipientSignature;
            }
            private set
            {
                if (value != null && value.Length > Mail.MaxRecipientSignatureLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _recipientSignature = value;
                }
            }
        }

        [DataMember(Name = "Content")]
        public byte[] Content
        {
            get
            {
                return _content;
            }
            set
            {
                if (value != null && value.Length > Mail.MaxContentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _content = value;
                }
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

        public bool VerifyHash(HashAlgorithm hashAlgorithm, byte[] hash)
        {
            return Collection.Equals(this.GetHash(hashAlgorithm), hash);
        }

        #endregion
    }
}
