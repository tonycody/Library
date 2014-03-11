using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "Certificate", Namespace = "http://Library/Security")]
    public sealed class Certificate : ItemBase<Certificate>
    {
        private enum SerializeId : byte
        {
            Nickname = 0,
            DigitalSignatureAlgorithm = 1,
            PublicKey = 2,
            Signature = 3,
        }

        private volatile string _nickname;
        private volatile DigitalSignatureAlgorithm _digitalSignatureAlgorithm = 0;
        private volatile byte[] _publicKey;
        private volatile byte[] _signature;

        private volatile int _hashCode;
        private volatile string _toString;

        public static readonly int MaxNickNameLength = 256;
        public static readonly int MaxPublickeyLength = 1024 * 8;
        public static readonly int MaxSignatureLength = 1024 * 8;

        internal Certificate(DigitalSignature digitalSignature, Stream stream)
        {
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            byte[] signature;

            if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha512)
            {
                signature = EcDsaP521_Sha512.Sign(digitalSignature.PrivateKey, stream);
            }
            else if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
            {
                signature = Rsa2048_Sha512.Sign(digitalSignature.PrivateKey, stream);
            }
            else
            {
                return;
            }

            this.Nickname = digitalSignature.Nickname;
            this.DigitalSignatureAlgorithm = digitalSignature.DigitalSignatureAlgorithm;
            this.PublicKey = digitalSignature.PublicKey;
            this.Signature = signature;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
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
                    if (id == (byte)SerializeId.Nickname)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.Nickname = reader.ReadToEnd();
                        }
                    }
                    else if (id == (byte)SerializeId.DigitalSignatureAlgorithm)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.DigitalSignatureAlgorithm = (DigitalSignatureAlgorithm)Enum.Parse(typeof(DigitalSignatureAlgorithm), reader.ReadToEnd());
                        }
                    }
                    else if (id == (byte)SerializeId.PublicKey)
                    {
                        byte[] buffer = new byte[(int)rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.PublicKey = buffer;
                    }
                    else if (id == (byte)SerializeId.Signature)
                    {
                        byte[] buffer = new byte[(int)rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.Signature = buffer;
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);
            Encoding encoding = new UTF8Encoding(false);

            // Nickname
            if (this.Nickname != null)
            {
                byte[] buffer = null;

                try
                {
                    var value = this.Nickname;

                    buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                    var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                    bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Nickname);
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
            // DigitalSignatureAlgorithm
            if (this.DigitalSignatureAlgorithm != 0)
            {
                byte[] buffer = null;

                try
                {
                    var value = this.DigitalSignatureAlgorithm.ToString();

                    buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                    var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                    bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.DigitalSignatureAlgorithm);
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
            // PublicKey
            if (this.PublicKey != null)
            {
                bufferStream.Write(NetworkConverter.GetBytes((int)this.PublicKey.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.PublicKey);
                bufferStream.Write(this.PublicKey, 0, this.PublicKey.Length);
            }
            // Signature
            if (this.Signature != null)
            {
                bufferStream.Write(NetworkConverter.GetBytes((int)this.Signature.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Signature);
                bufferStream.Write(this.Signature, 0, this.Signature.Length);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Certificate)) return false;

            return this.Equals((Certificate)obj);
        }

        public override bool Equals(Certificate other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Nickname != other.Nickname
                || this.DigitalSignatureAlgorithm != other.DigitalSignatureAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null))
                || ((this.Signature == null) != (other.Signature == null)))
            {
                return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Unsafe.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            if (this.Signature != null && other.Signature != null)
            {
                if (!Unsafe.Equals(this.Signature, other.Signature)) return false;
            }

            return true;
        }

        public Certificate Clone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return Certificate.Import(stream, BufferManager.Instance);
            }
        }

        public override string ToString()
        {
            if (_toString == null)
                _toString = Library.Security.Signature.GetSignature(this);

            return _toString;
        }

        internal bool Verify(Stream stream)
        {
            if (this.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha512)
            {
                return EcDsaP521_Sha512.Verify(this.PublicKey, this.Signature, stream);
            }
            else if (this.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
            {
                return Rsa2048_Sha512.Verify(this.PublicKey, this.Signature, stream);
            }
            else
            {
                return false;
            }
        }

        [DataMember(Name = "Nickname")]
        public string Nickname
        {
            get
            {
                return _nickname;
            }
            private set
            {
                if (value != null && value.Length > Certificate.MaxNickNameLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _nickname = value;
                }
            }
        }

        [DataMember(Name = "DigitalSignatureAlgorithm")]
        public DigitalSignatureAlgorithm DigitalSignatureAlgorithm
        {
            get
            {
                return _digitalSignatureAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(DigitalSignatureAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _digitalSignatureAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "PublicKey")]
        public byte[] PublicKey
        {
            get
            {
                return _publicKey;
            }
            private set
            {
                if (value != null && (value.Length > Certificate.MaxPublickeyLength))
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

        [DataMember(Name = "Signature")]
        public byte[] Signature
        {
            get
            {
                return _signature;
            }
            private set
            {
                if (value != null && (value.Length > Certificate.MaxSignatureLength))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _signature = value;
                }
            }
        }
    }
}
