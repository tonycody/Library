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
    [DataContract(Name = "UnicastMetadata", Namespace = "http://Library/Net/Outopos")]
    public abstract class UnicastMetadata<TMetadata> : ImmutableCertificateItemBase<TMetadata>, IUnicastHeader, IUnicastOptions
        where TMetadata : UnicastMetadata<TMetadata>
    {
        private enum SerializeId : byte
        {
            Signature = 0,
            CreationTime = 1,

            Key = 2,
            Cash = 3,

            Certificate = 4,
        }

        private volatile string _signature;
        private DateTime _creationTime;

        private volatile Key _key;
        private volatile Cash _cash;

        private volatile Certificate _certificate;

        private volatile object _thisLock;

        internal UnicastMetadata(string signature, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
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

        protected override Stream Export(BufferManager bufferManager, int count)
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

        public override int GetHashCode()
        {
            if (this.Key == null) return 0;
            else return this.Key.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is TMetadata)) return false;

            return this.Equals((TMetadata)obj);
        }

        public override bool Equals(TMetadata other)
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
            var tempCertificate = this.Certificate;
            this.Certificate = null;

            var tempCash = this.Cash;
            this.Cash = null;

            try
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    stream.Seek(0, SeekOrigin.End);
                    ItemUtilities.Write(stream, byte.MaxValue, signature);
                    stream.Seek(0, SeekOrigin.Begin);

                    tempCash = miner.Create(stream);
                }
            }
            finally
            {
                this.Certificate = tempCertificate;
                this.Cash = tempCash;
            }
        }

        protected virtual int VerifyCash(string signature)
        {
            var tempCertificate = this.Certificate;
            this.Certificate = null;

            var tempCash = this.Cash;
            this.Cash = null;

            try
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    stream.Seek(0, SeekOrigin.End);
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

        [DataMember(Name = "Cash")]
        protected virtual Cash Cash
        {
            get
            {
                return _cash;
            }
            set
            {
                _cash = value;
            }
        }

        protected override void CreateCertificate(DigitalSignature digitalSignature)
        {
            base.CreateCertificate(digitalSignature);
        }

        public override bool VerifyCertificate()
        {
            return base.VerifyCertificate();
        }

        protected override Stream GetCertificateStream()
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

        public override Certificate Certificate
        {
            get
            {
                return _certificate;
            }
            protected set
            {
                _certificate = value;
            }
        }

        #region IUnicastHeader

        [DataMember(Name = "Signature")]
        public string Signature
        {
            get
            {
                return _signature;
            }
            private set
            {
                _signature = value;
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

        #endregion

        #region IUnicastMetadata

        [DataMember(Name = "Key")]
        public Key Key
        {
            get
            {
                return _key;
            }
            private set
            {
                _key = value;
            }
        }

        private int? _cost;

        public int Cost
        {
            get
            {
                lock (_thisLock)
                {
                    if (_cost == null)
                        _cost = this.VerifyCash(this.Certificate.ToString());

                    return (int)_cost;
                }
            }
        }

        #endregion

        #region IComputeHash

        private volatile byte[] _Sha256_hash;

        public byte[] CreateHash(HashAlgorithm hashAlgorithm)
        {
            if (_Sha256_hash == null)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    _Sha256_hash = Sha256.ComputeHash(stream);
                }
            }

            if (hashAlgorithm == HashAlgorithm.Sha256)
            {
                return _Sha256_hash;
            }

            return null;
        }

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            return Unsafe.Equals(this.CreateHash(hashAlgorithm), hash);
        }

        #endregion
    }
}
