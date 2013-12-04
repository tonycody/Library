using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "ExchangePrivateKey", Namespace = "http://Library/Security")]
    public sealed class ExchangePrivateKey : ItemBase<ExchangePrivateKey>, IExchangeDecrypt
    {
        private enum SerializeId : byte
        {
            ExchangeAlgorithm = 0,
            PrivateKey = 1,
        }

        private ExchangeAlgorithm _exchangeAlgorithm = 0;
        private byte[] _privateKey;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxPrivatekeyLength = 1024 * 8;

        public ExchangePrivateKey(Exchange exchange)
        {
            this.ExchangeAlgorithm = exchange.ExchangeAlgorithm;
            this.PrivateKey = exchange.PrivateKey;
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
            if ((object)obj == null || !(obj is ExchangePrivateKey)) return false;

            return this.Equals((ExchangePrivateKey)obj);
        }

        public override bool Equals(ExchangePrivateKey other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || ((this.PrivateKey == null) != (other.PrivateKey == null)))
            {
                return false;
            }

            if (this.PrivateKey != null && other.PrivateKey != null)
            {
                if (!Collection.Equals(this.PrivateKey, other.PrivateKey)) return false;
            }

            return true;
        }

        public override ExchangePrivateKey DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return ExchangePrivateKey.Import(stream, BufferManager.Instance);
                }
            }
        }

        private object ThisLock
        {
            get
            {
                if (_thisLock == null)
                {
                    lock (_initializeLock)
                    {
                        if (_thisLock == null)
                        {
                            _thisLock = new object();
                        }
                    }
                }

                return _thisLock;
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
                    if (value != null && (value.Length > Exchange.MaxPublickeyLength))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _privateKey = value;
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
    }
}
