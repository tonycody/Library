﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "Certificate", Namespace = "http://Library/Security")]
    public sealed class Certificate : ItemBase<Certificate>, IThisLock
    {
        private enum SerializeId : byte
        {
            Nickname = 0,
            DigitalSignatureAlgorithm = 1,
            PublicKey = 2,
            Signature = 3,
        }

        private string _nickname;
        private DigitalSignatureAlgorithm _digitalSignatureAlgorithm = 0;
        private byte[] _publicKey = null;
        private byte[] _signature = null;
        private int _hashCode = 0;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public const int MaxNickNameLength = 256;
        public const int MaxPublickeyLength = 1024 * 8;
        public const int MaxSignatureLength = 1024 * 8;

        internal Certificate(DigitalSignature digitalSignature, Stream stream)
        {
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            byte[] signature;

            if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.ECDsaP521_Sha512)
            {
                signature = ECDsaP521_Sha512.Sign(digitalSignature.PrivateKey, stream);
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
                        else if (id == (byte)SerializeId.Signature)
                        {
                            byte[] buffer = new byte[(int)rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.Signature = buffer;
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

                if (this.Signature != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Signature.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Signature);
                    bufferStream.Write(this.Signature, 0, this.Signature.Length);

                    streams.Add(bufferStream);
                }

                return new AddStream(streams);
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
            if ((object)obj == null || !(obj is Certificate)) return false;

            return this.Equals((Certificate)obj);
        }

        public override bool Equals(Certificate other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Nickname != other.Nickname
                || this.DigitalSignatureAlgorithm != other.DigitalSignatureAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null))
                || ((this.Signature == null) != (other.Signature == null)))
            {
                return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (this.PublicKey.Length != other.PublicKey.Length) return false;

                for (int i = 0; i < this.PublicKey.Length; i++) if (this.PublicKey[i] != other.PublicKey[i]) return false;
            }

            if (this.Signature != null && other.Signature != null)
            {
                if (this.Signature.Length != other.Signature.Length) return false;

                for (int i = 0; i < this.Signature.Length; i++) if (this.Signature[i] != other.Signature[i]) return false;
            }

            return true;
        }

        public override Certificate DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return Certificate.Import(stream, bufferManager);
                }
            }
        }

        internal bool Verify(Stream stream)
        {
            if (this.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.ECDsaP521_Sha512)
            {
                return ECDsaP521_Sha512.Verify(this.PublicKey, this.Signature, stream);
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
                        try
                        {
                            if (value.Length >= 4) _hashCode = Math.Abs(NetworkConverter.ToInt32(value));
                            else if (value.Length >= 2) _hashCode = NetworkConverter.ToUInt16(value);
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

        [DataMember(Name = "Signature")]
        public byte[] Signature
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _signature;
                }
            }

            private set
            {
                lock (this.ThisLock)
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

        #region IThisLock

        public object ThisLock
        {
            get
            {
                lock (_thisStaticLock)
                {
                    if (_thisLock == null) _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }
}
