using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "ExchangePublicKey", Namespace = "http://Library/Security")]
    public sealed class ExchangePublicKey : ItemBase<ExchangePublicKey>, IExchangeEncrypt, IThisLock
    {
        private enum SerializeId : byte
        {
            ExchangeAlgorithm = 0,
            PublicKey = 1,
        }

        private ExchangeAlgorithm _exchangeAlgorithm;
        private byte[] _publicKey;
        private int _hashCode = 0;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public static readonly int MaxPublicKeyLength = 1024 * 8;

        internal ExchangePublicKey(ExchangeAlgorithm exchangeAlgorithm, byte[] publicKey)
        {
            this.ExchangeAlgorithm = exchangeAlgorithm;
            this.PublicKey = publicKey;
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
            if ((object)obj == null || !(obj is ExchangePublicKey)) return false;

            return this.Equals((ExchangePublicKey)obj);
        }

        public override bool Equals(ExchangePublicKey other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null)))
            {
                return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Collection.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            return true;
        }

        public override ExchangePublicKey DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return ExchangePublicKey.Import(stream, BufferManager.Instance);
                }
            }
        }

        [DataMember(Name = "ExchangeAlgorithm")]
        public ExchangeAlgorithm ExchangeAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _exchangeAlgorithm;
                }
            }
            private set
            {
                lock (this.ThisLock)
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
                    if (value != null && (value.Length > ExchangePublicKey.MaxPublicKeyLength))
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
