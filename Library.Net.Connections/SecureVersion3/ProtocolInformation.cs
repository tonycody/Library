using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Security;

namespace Library.Net.Connections.SecureVersion3
{
    [DataContract(Name = "ProtocolInformation", Namespace = "http://Library/Net/Connection/SecureVersion3")]
    class ProtocolInformation : ItemBase<ProtocolInformation>, ICloneable<ProtocolInformation>, IThisLock
    {
        private enum SerializeId : byte
        {
            KeyExchangeAlgorithm = 0,
            KeyDerivationAlgorithm = 1,
            CryptoAlgorithm = 2,
            HashAlgorithm = 3,
            SessionId = 4,
        }

        private KeyExchangeAlgorithm _keyExchangeAlgorithm;
        private KeyDerivationAlgorithm _keyDerivationAlgorithm;
        private CryptoAlgorithm _cryptoAlgorithm;
        private HashAlgorithm _hashAlgorithm;
        private byte[] _sessionId;

        private volatile int _hashCode;

        private volatile object _thisLock;

        public static readonly int MaxSessionIdLength = 32;

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                for (; ; )
                {
                    byte id;
                    {
                        byte[] idBuffer = new byte[1];
                        if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                        id = idBuffer[0];
                    }

                    int length;
                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                        length = NetworkConverter.ToInt32(lengthBuffer);
                    }

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.KeyExchangeAlgorithm)
                        {
                            this.KeyExchangeAlgorithm = EnumEx<KeyExchangeAlgorithm>.Parse(ItemUtilities.GetString(rangeStream));
                        }
                        else if (id == (byte)SerializeId.KeyDerivationAlgorithm)
                        {
                            this.KeyDerivationAlgorithm = EnumEx<KeyDerivationAlgorithm>.Parse(ItemUtilities.GetString(rangeStream));
                        }
                        else if (id == (byte)SerializeId.CryptoAlgorithm)
                        {
                            this.CryptoAlgorithm = EnumEx<CryptoAlgorithm>.Parse(ItemUtilities.GetString(rangeStream));
                        }
                        else if (id == (byte)SerializeId.HashAlgorithm)
                        {
                            this.HashAlgorithm = EnumEx<HashAlgorithm>.Parse(ItemUtilities.GetString(rangeStream));
                        }
                        else if (id == (byte)SerializeId.SessionId)
                        {
                            this.SessionId = ItemUtilities.GetByteArray(rangeStream);
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

                // KeyExchangeAlgorithm
                if (this.KeyExchangeAlgorithm != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.KeyExchangeAlgorithm, this.KeyExchangeAlgorithm.ToString());
                }
                // KeyDerivationAlgorithm
                if (this.KeyDerivationAlgorithm != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.KeyDerivationAlgorithm, this.KeyDerivationAlgorithm.ToString());
                }
                // CryptoAlgorithm
                if (this.CryptoAlgorithm != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CryptoAlgorithm, this.CryptoAlgorithm.ToString());
                }
                // HashAlgorithm
                if (this.HashAlgorithm != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.HashAlgorithm, this.HashAlgorithm.ToString());
                }
                // SessionId
                if (this.SessionId != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.SessionId, this.SessionId);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
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
            if ((object)obj == null || !(obj is ProtocolInformation)) return false;

            return this.Equals((ProtocolInformation)obj);
        }

        public override bool Equals(ProtocolInformation other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.KeyExchangeAlgorithm != other.KeyExchangeAlgorithm
                || this.KeyDerivationAlgorithm != other.KeyDerivationAlgorithm
                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || this.HashAlgorithm != other.HashAlgorithm
                || (this.SessionId == null) != (other.SessionId == null))
            {
                return false;
            }

            if (this.SessionId != null && other.SessionId != null)
            {
                if (!Unsafe.Equals(this.SessionId, other.SessionId)) return false;
            }

            return true;
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

        [DataMember(Name = "KeyDerivationAlgorithm")]
        public KeyDerivationAlgorithm KeyDerivationAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _keyDerivationAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _keyDerivationAlgorithm = value;
                }
            }
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

        [DataMember(Name = "SessionId")]
        public byte[] SessionId
        {
            get
            {
                return _sessionId;
            }
            set
            {
                if (value != null && value.Length > ProtocolInformation.MaxSessionIdLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _sessionId = value;
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
                return _thisLock;
            }
        }

        #endregion
    }
}
