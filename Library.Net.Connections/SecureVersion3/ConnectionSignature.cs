using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Connections.SecureVersion3
{
    [DataContract(Name = "ConnectionSignature", Namespace = "http://Library/Net/Connection/SecureVersion3")]
    sealed class ConnectionSignature : MutableCertificateItemBase<ConnectionSignature>, ICloneable<ConnectionSignature>, IThisLock
    {
        private enum SerializeId : byte
        {
            CreationTime = 0,
            ExchangeKey = 1,
            ProtocolHash = 2,

            Certificate = 3,
        }

        private DateTime _creationTime;
        private byte[] _exchangeKey;
        private byte[] _protocolHash;

        private Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxExchangeKeyLength = 8192;
        public static readonly int MaxProtocolHashLength = 64;

        public ConnectionSignature()
        {

        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.CreationTime)
                        {
                            this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                        if (id == (byte)SerializeId.ExchangeKey)
                        {
                            this.ExchangeKey = ItemUtilities.GetByteArray(rangeStream);
                        }
                        if (id == (byte)SerializeId.ProtocolHash)
                        {
                            this.ProtocolHash = ItemUtilities.GetByteArray(rangeStream);
                        }

                        else if (id == (byte)SerializeId.Certificate)
                        {
                            this.Certificate = Certificate.Import(rangeStream, bufferManager);
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

                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                }
                // ExchangeKey
                if (this.ExchangeKey != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.ExchangeKey, this.ExchangeKey);
                }
                // ProtocolHash
                if (this.ProtocolHash != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.ProtocolHash, this.ProtocolHash);
                }

                // Certificate
                if (this.Certificate != null)
                {
                    using (var stream = this.Certificate.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Certificate, stream);
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
                return this.CreationTime.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ConnectionSignature)) return false;

            return this.Equals((ConnectionSignature)obj);
        }

        public override bool Equals(ConnectionSignature other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CreationTime != other.CreationTime
                || (this.ExchangeKey == null) != (other.ExchangeKey == null)
                || (this.ProtocolHash == null) != (other.ProtocolHash == null)

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.ExchangeKey != null && other.ExchangeKey != null)
            {
                if (!Native.Equals(this.ExchangeKey, other.ExchangeKey)) return false;
            }

            if (this.ProtocolHash != null && other.ProtocolHash != null)
            {
                if (!Native.Equals(this.ProtocolHash, other.ProtocolHash)) return false;
            }

            return true;
        }

        public override void CreateCertificate(DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                base.CreateCertificate(digitalSignature);
            }
        }

        public override bool VerifyCertificate()
        {
            lock (this.ThisLock)
            {
                return base.VerifyCertificate();
            }
        }

        protected override Stream GetCertificateStream()
        {
            lock (this.ThisLock)
            {
                var temp = this.Certificate;
                this.Certificate = null;

                try
                {
                    return this.Export(BufferManager.Instance);
                }
                finally
                {
                    this.Certificate = temp;
                }
            }
        }

        public override Certificate Certificate
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _certificate;
                }
            }
            protected set
            {
                lock (this.ThisLock)
                {
                    _certificate = value;
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _creationTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

        [DataMember(Name = "ExchangeKey")]
        public byte[] ExchangeKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _exchangeKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > ConnectionSignature.MaxExchangeKeyLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _exchangeKey = value;
                    }
                }
            }
        }

        [DataMember(Name = "ProtocolHash")]
        public byte[] ProtocolHash
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _protocolHash;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > ConnectionSignature.MaxProtocolHashLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _protocolHash = value;
                    }
                }
            }
        }

        #region ICloneable<ConnectionSignature>

        public ConnectionSignature Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return ConnectionSignature.Import(stream, BufferManager.Instance);
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
