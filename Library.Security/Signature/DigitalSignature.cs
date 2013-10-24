using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
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

        private string _nickname = null;
        private DigitalSignatureAlgorithm _digitalSignatureAlgorithm = 0;
        private byte[] _publicKey = null;
        private byte[] _privateKey = null;

        private int _hashCode = 0;

        private volatile string _toString = null;

        public static readonly int MaxNickNameLength = 256;
        public static readonly int MaxPublickeyLength = 1024 * 8;
        public static readonly int MaxPrivatekeyLength = 1024 * 8;

        public DigitalSignature(string nickname, DigitalSignatureAlgorithm digitalSignatureAlgorithm)
        {
            this.Nickname = nickname;
            this.DigitalSignatureAlgorithm = digitalSignatureAlgorithm;

            if (digitalSignatureAlgorithm == DigitalSignatureAlgorithm.ECDsaP521_Sha512)
            {
                byte[] publicKey, privateKey;

                ECDsaP521_Sha512.CreateKeys(out publicKey, out privateKey);

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
                    else if (id == (byte)SerializeId.PrivateKey)
                    {
                        byte[] buffer = new byte[(int)rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.PrivateKey = buffer;
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Nickname
            if (this.Nickname != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.Nickname);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Nickname);

                streams.Add(bufferStream);
            }
            // DigitalSignatureAlgorithm
            if (this.DigitalSignatureAlgorithm != 0)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.DigitalSignatureAlgorithm.ToString());
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.DigitalSignatureAlgorithm);

                streams.Add(bufferStream);
            }
            // PublicKey
            if (this.PublicKey != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)this.PublicKey.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.PublicKey);
                bufferStream.Write(this.PublicKey, 0, this.PublicKey.Length);

                streams.Add(bufferStream);
            }
            // PrivateKey
            if (this.PrivateKey != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)this.PrivateKey.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.PrivateKey);
                bufferStream.Write(this.PrivateKey, 0, this.PrivateKey.Length);

                streams.Add(bufferStream);
            }

            return new JoinStream(streams);
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
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Nickname != other.Nickname
                || this.DigitalSignatureAlgorithm != other.DigitalSignatureAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null))
                || ((this.PrivateKey == null) != (other.PrivateKey == null)))
            {
                return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Collection.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            if (this.PrivateKey != null && other.PrivateKey != null)
            {
                if (!Collection.Equals(this.PrivateKey, other.PrivateKey)) return false;
            }

            return true;
        }

        public override DigitalSignature DeepClone()
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

        public static Certificate CreateFileCertificate(DigitalSignature digitalSignature, FileStream stream, BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(Path.GetFileName(stream.Name));
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

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            using (var joinStream = new JoinStream(streams))
            {
                return new Certificate(digitalSignature, joinStream);
            }
        }

        public static bool VerifyCertificate(Certificate certificate, Stream stream)
        {
            return certificate.Verify(stream);
        }

        public static bool VerifyFileCertificate(Certificate certificate, FileStream stream)
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
                    writer.Write(Path.GetFileName(stream.Name));
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

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            using (var joinStream = new JoinStream(streams))
            {
                return certificate.Verify(joinStream);
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
                if (value != null && (value.Length > DigitalSignature.MaxPublickeyLength))
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

        [DataMember(Name = "PrivateKey")]
        public byte[] PrivateKey
        {
            get
            {
                return _privateKey;
            }
            private set
            {
                if (value != null && (value.Length > DigitalSignature.MaxPrivatekeyLength))
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
