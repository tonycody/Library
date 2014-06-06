using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "DigitalSignature", Namespace = "http://Library/Security")]
    public sealed class DigitalSignature : ItemBase<DigitalSignature>
    {
        private enum SerializeId : byte
        {
            Nickname = 0,
            DigitalSignatureAlgorithm = 1,
            PublicKey = 2,
            PrivateKey = 3,
        }

        private enum FileSerializeId : byte
        {
            Name = 0,
            Stream = 1,
        }

        private volatile string _nickname;
        private volatile DigitalSignatureAlgorithm _digitalSignatureAlgorithm = 0;
        private volatile byte[] _publicKey;
        private volatile byte[] _privateKey;

        private volatile int _hashCode;
        private volatile string _toString;

        public static readonly int MaxNickNameLength = 256;
        public static readonly int MaxPublicKeyLength = 1024 * 8;
        public static readonly int MaxPrivateKeyLength = 1024 * 8;

        public DigitalSignature(string nickname, DigitalSignatureAlgorithm digitalSignatureAlgorithm)
        {
            this.Nickname = nickname;
            this.DigitalSignatureAlgorithm = digitalSignatureAlgorithm;

            if (digitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha512)
            {
                byte[] publicKey, privateKey;

                EcDsaP521_Sha512.CreateKeys(out publicKey, out privateKey);

                this.PublicKey = publicKey;
                this.PrivateKey = privateKey;
            }
            else if (digitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
            {
                byte[] publicKey, privateKey;

                Rsa2048_Sha512.CreateKeys(out publicKey, out privateKey);

                this.PublicKey = publicKey;
                this.PrivateKey = privateKey;
            }
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
                    else if (id == (byte)SerializeId.PrivateKey)
                    {
                        this.PrivateKey = ItemUtilities.GetByteArray(rangeStream);
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
            // PrivateKey
            if (this.PrivateKey != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.PrivateKey, this.PrivateKey);
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
            if ((object)obj == null || !(obj is DigitalSignature)) return false;

            return this.Equals((DigitalSignature)obj);
        }

        public override bool Equals(DigitalSignature other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Nickname != other.Nickname
                || this.DigitalSignatureAlgorithm != other.DigitalSignatureAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null))
                || ((this.PrivateKey == null) != (other.PrivateKey == null)))
            {
                return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Unsafe.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            if (this.PrivateKey != null && other.PrivateKey != null)
            {
                if (!Unsafe.Equals(this.PrivateKey, other.PrivateKey)) return false;
            }

            return true;
        }

        public DigitalSignature Clone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return DigitalSignature.Import(stream, BufferManager.Instance);
            }
        }

        public override string ToString()
        {
            if (_toString == null)
                _toString = Signature.GetSignature(this);

            return _toString;
        }

        public static Certificate CreateCertificate(DigitalSignature digitalSignature, Stream stream)
        {
            return new Certificate(digitalSignature, stream);
        }

        public static Certificate CreateFileCertificate(DigitalSignature digitalSignature, string name, Stream stream)
        {
            BufferManager bufferManager = BufferManager.Instance;

            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(Path.GetFileName(name));
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)FileSerializeId.Name);

                streams.Add(bufferStream);
            }

            {
                Stream exportStream = new WrapperStream(stream, true);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)FileSerializeId.Stream);

                streams.Add(new UniteStream(bufferStream, exportStream));
            }

            using (var uniteStream = new UniteStream(streams))
            {
                return new Certificate(digitalSignature, uniteStream);
            }
        }

        public static bool VerifyCertificate(Certificate certificate, Stream stream)
        {
            return certificate.Verify(stream);
        }

        public static bool VerifyFileCertificate(Certificate certificate, string name, Stream stream)
        {
            BufferManager bufferManager = BufferManager.Instance;
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Name
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(Path.GetFileName(name));
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)FileSerializeId.Name);

                streams.Add(bufferStream);
            }
            // Stream
            {
                Stream exportStream = new WrapperStream(stream, true);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)FileSerializeId.Stream);

                streams.Add(new UniteStream(bufferStream, exportStream));
            }

            using (var uniteStream = new UniteStream(streams))
            {
                return certificate.Verify(uniteStream);
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
                if (value != null && value.Length > DigitalSignature.MaxPublicKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _publicKey = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtilities.GetHashCode(value);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        [DataMember(Name = "PrivateKey")]
        public byte[] PrivateKey
        {
            get
            {
                return _privateKey;
            }
            private set
            {
                if (value != null && value.Length > DigitalSignature.MaxPrivateKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _privateKey = value;
                }
            }
        }
    }
}
