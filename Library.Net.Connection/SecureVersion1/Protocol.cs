using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Connection.SecureVersion1
{
    [DataContract(Name = "Protocol", Namespace = "http://Library/Net/Connection/SecureVersion1")]
    class Protocol : ItemBase<Protocol>, IThisLock
    {
        private enum SerializeId : byte
        {
            KeyExchangeAlgorithm = 0,
            CryptoAlgorithm = 1,
            HashAlgorithm = 2,
        }

        private KeyExchangeAlgorithm _keyExchangeAlgorithm;
        private CryptoAlgorithm _cryptoAlgorithm;
        private HashAlgorithm _hashAlgorithm;

        private object _thisLock;
        private static object _thisStaticLock = new object();

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
                        if (id == (byte)SerializeId.KeyExchangeAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.KeyExchangeAlgorithm = (KeyExchangeAlgorithm)Enum.Parse(typeof(KeyExchangeAlgorithm), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.CryptoAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CryptoAlgorithm = (CryptoAlgorithm)Enum.Parse(typeof(CryptoAlgorithm), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.HashAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.HashAlgorithm = (HashAlgorithm)Enum.Parse(typeof(HashAlgorithm), reader.ReadToEnd());
                            }
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

                if (this.KeyExchangeAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.KeyExchangeAlgorithm.ToString());
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.KeyExchangeAlgorithm);

                    streams.Add(bufferStream);
                }

                if (this.CryptoAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.CryptoAlgorithm.ToString());
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CryptoAlgorithm);

                    streams.Add(bufferStream);
                }

                if (this.HashAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.HashAlgorithm.ToString());
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.HashAlgorithm);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return (int)this.KeyExchangeAlgorithm;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Protocol)) return false;

            return this.Equals((Protocol)obj);
        }

        public override bool Equals(Protocol other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.KeyExchangeAlgorithm != other.KeyExchangeAlgorithm
                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || this.HashAlgorithm != other.HashAlgorithm)
            {
                return false;
            }

            return true;
        }

        public override Protocol DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Protocol.Import(stream, BufferManager.Instance);
                }
            }
        }

        public static Protocol operator &(Protocol x, Protocol y)
        {
            if ((((object)x) == null) || (((object)y) == null))
                return null;

            var protocol = new SecureVersion1.Protocol();
            protocol.KeyExchangeAlgorithm = x.KeyExchangeAlgorithm & y.KeyExchangeAlgorithm;
            protocol.CryptoAlgorithm = x.CryptoAlgorithm & y.CryptoAlgorithm;
            protocol.HashAlgorithm = x.HashAlgorithm & y.HashAlgorithm;

            return protocol;
        }

        public static Protocol operator ^(Protocol x, Protocol y)
        {
            if ((((object)x) == null) || (((object)y) == null))
                return null;

            var protocol = new SecureVersion1.Protocol();
            protocol.KeyExchangeAlgorithm = x.KeyExchangeAlgorithm ^ y.KeyExchangeAlgorithm;
            protocol.CryptoAlgorithm = x.CryptoAlgorithm ^ y.CryptoAlgorithm;
            protocol.HashAlgorithm = x.HashAlgorithm ^ y.HashAlgorithm;

            return protocol;
        }

        public static Protocol operator |(Protocol x, Protocol y)
        {
            if ((((object)x) == null) || (((object)y) == null))
                return null;

            var protocol = new SecureVersion1.Protocol();
            protocol.KeyExchangeAlgorithm = x.KeyExchangeAlgorithm | y.KeyExchangeAlgorithm;
            protocol.CryptoAlgorithm = x.CryptoAlgorithm | y.CryptoAlgorithm;
            protocol.HashAlgorithm = x.HashAlgorithm | y.HashAlgorithm;

            return protocol;
        }

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _cryptoAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "KeyExchangeAlgorithm")]
        public KeyExchangeAlgorithm KeyExchangeAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _keyExchangeAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _keyExchangeAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "HashAlgorithm")]
        public HashAlgorithm HashAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hashAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _hashAlgorithm = value;
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
