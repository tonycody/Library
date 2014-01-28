using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Connections.SecureVersion1
{
    [DataContract(Name = "ProtocolInformation", Namespace = "http://Library/Net/Connection/SecureVersion1")]
    sealed class ProtocolInformation : ItemBase<ProtocolInformation>, ICloneable<ProtocolInformation>, IThisLock
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

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
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

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                Encoding encoding = new UTF8Encoding(false);

                if (this.KeyExchangeAlgorithm != 0)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.KeyExchangeAlgorithm.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.KeyExchangeAlgorithm);
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

                if (this.CryptoAlgorithm != 0)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.CryptoAlgorithm.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.CryptoAlgorithm);
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

                if (this.HashAlgorithm != 0)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.HashAlgorithm.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.HashAlgorithm);
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

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
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
            if ((object)obj == null || !(obj is ProtocolInformation)) return false;

            return this.Equals((ProtocolInformation)obj);
        }

        public override bool Equals(ProtocolInformation other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.KeyExchangeAlgorithm != other.KeyExchangeAlgorithm
                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || this.HashAlgorithm != other.HashAlgorithm)
            {
                return false;
            }

            return true;
        }

        public static ProtocolInformation operator &(ProtocolInformation x, ProtocolInformation y)
        {
            if ((((object)x) == null) || (((object)y) == null))
                return null;

            var protocol = new SecureVersion1.ProtocolInformation();
            protocol.KeyExchangeAlgorithm = x.KeyExchangeAlgorithm & y.KeyExchangeAlgorithm;
            protocol.CryptoAlgorithm = x.CryptoAlgorithm & y.CryptoAlgorithm;
            protocol.HashAlgorithm = x.HashAlgorithm & y.HashAlgorithm;

            return protocol;
        }

        public static ProtocolInformation operator ^(ProtocolInformation x, ProtocolInformation y)
        {
            if ((((object)x) == null) || (((object)y) == null))
                return null;

            var protocol = new SecureVersion1.ProtocolInformation();
            protocol.KeyExchangeAlgorithm = x.KeyExchangeAlgorithm ^ y.KeyExchangeAlgorithm;
            protocol.CryptoAlgorithm = x.CryptoAlgorithm ^ y.CryptoAlgorithm;
            protocol.HashAlgorithm = x.HashAlgorithm ^ y.HashAlgorithm;

            return protocol;
        }

        public static ProtocolInformation operator |(ProtocolInformation x, ProtocolInformation y)
        {
            if ((((object)x) == null) || (((object)y) == null))
                return null;

            var protocol = new SecureVersion1.ProtocolInformation();
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

        #region ICloneable<ProtocolInformation>

        public ProtocolInformation Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return ProtocolInformation.Import(stream, BufferManager.Instance);
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
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

        #endregion
    }
}
