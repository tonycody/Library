using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "UnicastHeader", Namespace = "http://Library/Net/Outopos")]
    public abstract class UnicastHeader<THeader> : ImmutableCertificateItemBase<THeader>, IUnicastHeader
        where THeader : UnicastHeader<THeader>
    {
        private enum SerializeId : byte
        {
            Signature = 0,
            CreationTime = 1,
            Key = 2,
            Cash = 3,

            Certificate = 4,
        }

        private string _signature;
        private DateTime _creationTime;
        private Key _key;
        private Cash _cash;

        private Certificate _certificate;

        private volatile object _thisLock;

        internal UnicastHeader(string signature, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
        {
            this.Signature = signature;
            this.CreationTime = creationTime;
            this.Key = key;
            this.CreateCash(miner, digitalSignature.ToString());

            this.CreateCertificate(digitalSignature);
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (_thisLock)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Signature)
                        {
                            this.Signature = ItemUtilities.GetString(rangeStream);
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                        else if (id == (byte)SerializeId.Key)
                        {
                            this.Key = Key.Import(rangeStream, bufferManager);
                        }
                        else if (id == (byte)SerializeId.Cash)
                        {
                            this.Cash = Cash.Import(rangeStream, bufferManager);
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
            lock (_thisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Signature
                if (this.Signature != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Signature, this.Signature);
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                }
                // Key
                if (this.Key != null)
                {
                    using (var stream = this.Key.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                    }
                }
                // Cash
                if (this.Cash != null)
                {
                    using (var stream = this.Cash.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Cash, stream);
                    }
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
            lock (_thisLock)
            {
                if (this.Key == null) return 0;
                else return this.Key.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is THeader)) return false;

            return this.Equals((THeader)obj);
        }

        public override bool Equals(THeader other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Signature != other.Signature
                || this.CreationTime != other.CreationTime
                || this.Key != other.Key
                || this.Cash != other.Cash

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            return true;
        }

        protected virtual void CreateCash(Miner miner, string signature)
        {
            lock (_thisLock)
            {
                var tempCertificate = this.Certificate;
                this.Certificate = null;

                var tempCash = this.Cash;
                this.Cash = null;

                try
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        ItemUtilities.Write(stream, byte.MaxValue, signature);
                        stream.Seek(0, SeekOrigin.Begin);

                        tempCash = Miner.Create(miner, stream);
                    }
                }
                finally
                {
                    this.Certificate = tempCertificate;
                    this.Cash = tempCash;
                }
            }
        }

        protected virtual int VerifyCash(string signature)
        {
            lock (_thisLock)
            {
                var tempCertificate = this.Certificate;
                this.Certificate = null;

                var tempCash = this.Cash;
                this.Cash = null;

                try
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        ItemUtilities.Write(stream, byte.MaxValue, signature);
                        stream.Seek(0, SeekOrigin.Begin);

                        return Miner.Verify(tempCash, stream);
                    }
                }
                finally
                {
                    this.Certificate = tempCertificate;
                    this.Cash = tempCash;
                }
            }
        }

        [DataMember(Name = "Cash")]
        protected virtual Cash Cash
        {
            get
            {
                lock (_thisLock)
                {
                    return _cash;
                }
            }
            set
            {
                lock (_thisLock)
                {
                    _cash = value;
                }
            }
        }

        protected override void CreateCertificate(DigitalSignature digitalSignature)
        {
            lock (_thisLock)
            {
                base.CreateCertificate(digitalSignature);
            }
        }

        public override bool VerifyCertificate()
        {
            lock (_thisLock)
            {
                return base.VerifyCertificate();
            }
        }

        protected override Stream GetCertificateStream()
        {
            lock (_thisLock)
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
                lock (_thisLock)
                {
                    return _certificate;
                }
            }
            protected set
            {
                lock (_thisLock)
                {
                    _certificate = value;
                }
            }
        }

        #region IUnicastHeader

        [DataMember(Name = "Signature")]
        public string Signature
        {
            get
            {
                lock (_thisLock)
                {
                    return _signature;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    _signature = value;
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (_thisLock)
                {
                    return _creationTime;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    var utc = value.ToUniversalTime();
                    _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
            }
        }

        [DataMember(Name = "Key")]
        public Key Key
        {
            get
            {
                lock (_thisLock)
                {
                    return _key;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    _key = value;
                }
            }
        }

        private int? _coin;

        public int Coin
        {
            get
            {
                lock (_thisLock)
                {
                    if (_coin == null)
                        _coin = this.VerifyCash(this.Certificate.ToString());

                    return (int)_coin;
                }
            }
        }

        #endregion

        #region IComputeHash

        private byte[] _sha512_hash;

        public byte[] CreateHash(HashAlgorithm hashAlgorithm)
        {
            lock (_thisLock)
            {
                if (_sha512_hash == null)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        _sha512_hash = Sha512.ComputeHash(stream);
                    }
                }

                if (hashAlgorithm == HashAlgorithm.Sha512)
                {
                    return _sha512_hash;
                }

                return null;
            }
        }

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            lock (_thisLock)
            {
                return Unsafe.Equals(this.CreateHash(hashAlgorithm), hash);
            }
        }

        #endregion
    }
}
