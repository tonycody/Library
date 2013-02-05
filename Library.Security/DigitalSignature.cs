using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Library;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "DigitalSignature", Namespace = "http://Library/Security")]
    public sealed class DigitalSignature : ItemBase<DigitalSignature>, IThisLock
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

        private string _nickname;
        private DigitalSignatureAlgorithm _digitalSignatureAlgorithm;
        private byte[] _publicKey;
        private byte[] _privateKey;
        private int _hashCode = 0;

        private string _toString = null;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public const int MaxNickNameLength = 256;
        public const int MaxPublickeyLength = 1024 * 8;
        public const int MaxPrivatekeyLength = 1024 * 8;

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
        }

        public override Stream Export(BufferManager bufferManager)
        {
            lock (this.ThisLock)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                if (this.Nickname != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.Nickname);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Nickname);

                    streams.Add(bufferStream);
                }

                if (this.DigitalSignatureAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.DigitalSignatureAlgorithm.ToString());
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.DigitalSignatureAlgorithm);

                    streams.Add(bufferStream);
                }

                if (this.PublicKey != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.PublicKey.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.PublicKey);
                    bufferStream.Write(this.PublicKey, 0, this.PublicKey.Length);

                    streams.Add(bufferStream);
                }

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
                if (this.PublicKey.Length != other.PublicKey.Length) return false;

                for (int i = 0; i < this.PublicKey.Length; i++) if (this.PublicKey[i] != other.PublicKey[i]) return false;
            }

            if (this.PrivateKey != null && other.PrivateKey != null)
            {
                if (this.PrivateKey.Length != other.PrivateKey.Length) return false;

                for (int i = 0; i < this.PrivateKey.Length; i++) if (this.PrivateKey[i] != other.PrivateKey[i]) return false;
            }

            return true;
        }

        public override DigitalSignature DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return DigitalSignature.Import(stream, bufferManager);
                }
            }
        }

        public override string ToString()
        {
            lock (this.ThisLock)
            {
                if (_toString == null)
                    _toString = DigitalSignatureConverter.GetSignature(this);

                return _toString;
            }
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

                using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                {
                    writer.Write(Path.GetFileName(stream.Name));
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)FileSerializeId.Name);

                streams.Add(bufferStream);
            }

            {
                Stream exportStream = new RangeStream(stream, true);

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

        public static bool VerifyFileCertificate(Certificate certificate, FileStream stream, BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                {
                    writer.Write(Path.GetFileName(stream.Name));
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)FileSerializeId.Name);

                streams.Add(bufferStream);
            }

            {
                Stream exportStream = new RangeStream(stream, true);

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
                lock (this.ThisLock)
                {
                    return _nickname;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
        }

        [DataMember(Name = "DigitalSignatureAlgorithm")]
        public DigitalSignatureAlgorithm DigitalSignatureAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _digitalSignatureAlgorithm;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
        }

        [DataMember(Name = "PublicKey")]
        public byte[] PublicKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _publicKey;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
                        try
                        {
                            if (value.Length >= 4) _hashCode = Math.Abs(BitConverter.ToInt32(value, 0));
                            else if (value.Length >= 2) _hashCode = BitConverter.ToUInt16(value, 0);
                            else _hashCode = value[0];
                        }
                        catch
                        {
                            _hashCode = 0;
                        }
                    }
                    else
                    {
                        _hashCode = 0;
                    }
                }
            }
        }

        [DataMember(Name = "PrivateKey")]
        public byte[] PrivateKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _privateKey;
                }
            }
            private set
            {
                lock (this.ThisLock)
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

        #region IThisLock

        public object ThisLock
        {
            get
            {
                lock (_thisStaticLock)
                {
                    if (_thisLock == null)
                        _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }
}
