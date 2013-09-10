using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "MailMessage", Namespace = "http://Library/Net/Lair")]
    public sealed class MailMessage : ReadOnlyCertificateItemBase<MailMessage>, IMailMessage<Mail, Key>
    {
        private enum SerializeId : byte
        {
            Mail = 0,
            CreationTime = 1,
            Content = 2,

            Certificate = 3,
        }

        private Mail _mail = null;
        private DateTime _creationTime = DateTime.MinValue;
        private Key _content = null;

        private Certificate _certificate;

        public MailMessage(Mail mail, Key content, DigitalSignature digitalSignature)
        {
            this.Mail = mail;
            this.CreationTime = DateTime.UtcNow;
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
                    if (id == (byte)SerializeId.Mail)
                    {
                        this.Mail = Mail.Import(rangeStream, bufferManager);
                    }
                    else if (id == (byte)SerializeId.CreationTime)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.CreationTime = DateTime.ParseExact(reader.ReadToEnd(), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                    }
                    else if (id == (byte)SerializeId.Content)
                    {
                        this.Content = Key.Import(rangeStream, bufferManager);
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

            // Mail
            if (this.Mail != null)
            {
                Stream exportStream = this.Mail.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Mail);

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
            // Content
            if (this.Content != null)
            {
                Stream exportStream = this.Content.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Content);

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
            if (_content == null) return 0;
            else return _content.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is MailMessage)) return false;

            return this.Equals((MailMessage)obj);
        }

        public override bool Equals(MailMessage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (!Collection.Equals(this.GetHash(HashAlgorithm.Sha512), other.GetHash(HashAlgorithm.Sha512))) return false;

            return true;
        }

        public override MailMessage DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return MailMessage.Import(stream, BufferManager.Instance);
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

        #region IMessage<Mail, Key>

        [DataMember(Name = "Mail")]
        public Mail Mail
        {
            get
            {
                return _mail;
            }
            private set
            {
                _mail = value;
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

        [DataMember(Name = "Content")]
        public Key Content
        {
            get
            {
                return _content;
            }
            private set
            {
                _content = value;
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
