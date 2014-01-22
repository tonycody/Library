using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "Exchange", Namespace = "http://Library/Security")]
    public sealed class Exchange : ItemBase<Exchange>, IExchangeEncrypt, IExchangeDecrypt
    {
        private enum SerializeId : byte
        {
            ExchangeAlgorithm = 0,
            PublicKey = 1,
            PrivateKey = 2,
        }

        private volatile ExchangeAlgorithm _exchangeAlgorithm = 0;
        private volatile byte[] _publicKey;
        private volatile byte[] _privateKey;

        private volatile int _hashCode;

        public static readonly int MaxPublickeyLength = 1024 * 8;
        public static readonly int MaxPrivatekeyLength = 1024 * 8;

        public Exchange(ExchangeAlgorithm exchangeAlgorithm)
        {
            this.ExchangeAlgorithm = exchangeAlgorithm;

            if (exchangeAlgorithm == ExchangeAlgorithm.Rsa2048)
            {
                byte[] publicKey, privateKey;

                Rsa2048.CreateKeys(out publicKey, out privateKey);

                this.PublicKey = publicKey;
                this.PrivateKey = privateKey;
            }
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
                    if (id == (byte)SerializeId.ExchangeAlgorithm)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ExchangeAlgorithm = (ExchangeAlgorithm)Enum.Parse(typeof(ExchangeAlgorithm), reader.ReadToEnd());
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

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // ExchangeAlgorithm
            if (this.ExchangeAlgorithm != 0)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.ExchangeAlgorithm.ToString());
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.ExchangeAlgorithm);

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

            return new UniteStream(streams);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Exchange)) return false;

            return this.Equals((Exchange)obj);
        }

        public override bool Equals(Exchange other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.ExchangeAlgorithm != other.ExchangeAlgorithm
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

        public ExchangePublicKey GetPublicKey()
        {
            return new ExchangePublicKey(this);
        }

        public ExchangePrivateKey GetPrivateKey()
        {
            return new ExchangePrivateKey(this);
        }

        public static byte[] Encrypt(IExchangeEncrypt exchangeEncrypt, byte[] value)
        {
            if (exchangeEncrypt.ExchangeAlgorithm == ExchangeAlgorithm.Rsa2048)
            {
                return Rsa2048.Encrypt(exchangeEncrypt.PublicKey, value);
            }

            return null;
        }

        public static byte[] Decrypt(IExchangeDecrypt exchangeDecrypt, byte[] value)
        {
            if (exchangeDecrypt.ExchangeAlgorithm == ExchangeAlgorithm.Rsa2048)
            {
                return Rsa2048.Decrypt(exchangeDecrypt.PrivateKey, value);
            }

            return null;
        }

        [DataMember(Name = "ExchangeAlgorithm")]
        public ExchangeAlgorithm ExchangeAlgorithm
        {
            get
            {
                return _exchangeAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(ExchangeAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _exchangeAlgorithm = value;
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
                if (value != null && (value.Length > Exchange.MaxPublickeyLength))
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
                if (value != null && (value.Length > Exchange.MaxPrivatekeyLength))
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
