using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
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

        private static InternPool<string> _nicknameCache = new InternPool<string>();
        private volatile string _nickname;
        private volatile DigitalSignatureAlgorithm _digitalSignatureAlgorithm = 0;
        private static InternPool<byte[]> _publicKeyCache = new InternPool<byte[]>(new ByteArrayEqualityComparer());
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

        protected override void Initialize()
        {

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
                    if (id == (byte)SerializeId.Nickname)
                    {
                        this.Nickname = ItemUtilities.GetString(rangeStream);
                    }
                    else if (id == (byte)SerializeId.DigitalSignatureAlgorithm)
                    {
                        this.DigitalSignatureAlgorithm = (DigitalSignatureAlgorithm)Enum.Parse(typeof(DigitalSignatureAlgorithm), ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.PublicKey)
                    {
                        this.PublicKey = ItemUtilities.GetByteArray(rangeStream);
                    }
                    else if (id == (byte)SerializeId.Signature)
                    {
                        this.Signature = ItemUtilities.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // Nickname
            if (this.Nickname != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Nickname, this.Nickname);
            }
            // DigitalSignatureAlgorithm
            if (this.DigitalSignatureAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.DigitalSignatureAlgorithm, this.DigitalSignatureAlgorithm.ToString());
            }
            // PublicKey
            if (this.PublicKey != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.PublicKey, this.PublicKey);
            }
            // Signature
            if (this.Signature != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Signature, this.Signature);
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

            if (!object.ReferenceEquals(this.Nickname, other.Nickname)
                || this.DigitalSignatureAlgorithm != other.DigitalSignatureAlgorithm
                || !object.ReferenceEquals(this.PublicKey, other.PublicKey)
                || ((this.Signature == null) != (other.Signature == null)))
            {
                return false;
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
                    _nickname = _nicknameCache.GetValue(value, this);
                    //_nickname = value;
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
                    _publicKey = _publicKeyCache.GetValue(value, this);
                    //_publicKey = value;
                }

                if (value != null)
                {
                    _hashCode = RuntimeHelpers.GetHashCode(_publicKey);
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
