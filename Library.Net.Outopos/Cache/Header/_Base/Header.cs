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
    [DataContract(Name = "Header", Namespace = "http://Library/Net/Outopos")]
    public abstract class Header<THeader, TTag> : ImmutableCertificateItemBase<THeader>, IHeader<TTag>
        where THeader : Header<THeader, TTag>
        where TTag : ItemBase<TTag>, ITag
    {
        private enum SerializeId : byte
        {
            Metadata = 0,

            Certificate = 1,
        }

        private volatile Metadata _protectedMetadata;

        private volatile Certificate _certificate;

        private volatile object _thisLock;

        internal Header(TTag tag, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
        {
            this.ProtectedMetadata = new Metadata()
            {
                Tag = tag,
                CreationTime = creationTime,
                Key = key,
            };
            this.ProtectedMetadata.Signature = digitalSignature.ToString();
            this.ProtectedMetadata.CreateCash(miner);
            this.ProtectedMetadata.Signature = null;

            this.CreateCertificate(digitalSignature);
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            byte[] lengthBuffer = new byte[4];

            for (; ; )
            {
                if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                int length = NetworkConverter.ToInt32(lengthBuffer);
                byte id = (byte)stream.ReadByte();

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.Metadata)
                    {
                        this.ProtectedMetadata = ItemBase<Metadata>.Import(rangeStream, bufferManager);
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

            // Metadata
            if (this.ProtectedMetadata != null)
            {
                using (var stream = this.ProtectedMetadata.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Metadata, stream);
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
            if (this.ProtectedMetadata == null) return 0;
            else return this.ProtectedMetadata.GetHashCode();
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

            if (this.ProtectedMetadata != other.ProtectedMetadata

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            return true;
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

        [DataMember(Name = "Metadata")]
        private Metadata ProtectedMetadata
        {
            get
            {
                return _protectedMetadata;
            }
            set
            {
                _protectedMetadata = value;
            }
        }

        #region IHeader<TTag>

        public TTag Tag
        {
            get
            {
                return this.ProtectedMetadata.Tag;
            }
        }

        public DateTime CreationTime
        {
            get
            {
                lock (_thisLock)
                {
                    return this.ProtectedMetadata.CreationTime;
                }
            }
        }

        public Key Key
        {
            get
            {
                return this.ProtectedMetadata.Key;
            }
        }

        private int? _cash;

        public int Cash
        {
            get
            {
                lock (_thisLock)
                {
                    if (_cash == null)
                    {
                        this.ProtectedMetadata.Signature = this.Certificate.ToString();
                        _cash = this.ProtectedMetadata.VerifyCash();
                        this.ProtectedMetadata.Signature = null;
                    }

                    return _cash.Value;
                }
            }
        }

        #endregion

        #region IComputeHash

        private volatile byte[] _sha512_hash;

        public byte[] CreateHash(HashAlgorithm hashAlgorithm)
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

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            return Unsafe.Equals(this.CreateHash(hashAlgorithm), hash);
        }

        #endregion

        [DataContract(Name = "Metadata", Namespace = "http://Library/Net/Outopos/Header")]
        private class Metadata : MutableCashItemBase<Metadata>
        {
            private enum SerializeId : byte
            {
                Tag = 0,
                CreationTime = 1,
                Key = 2,

                Cash = 3,

                Signature = byte.MaxValue,
            }

            private volatile TTag _tag;
            private DateTime _creationTime;
            private volatile Key _key;

            private volatile Cash _cash;

            private volatile string _signature;

            private volatile object _thisLock;

            protected override void Initialize()
            {
                _thisLock = new object();
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Tag)
                        {
                            this.Tag = ItemBase<TTag>.Import(rangeStream, bufferManager);
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

                        else if (id == (byte)SerializeId.Signature)
                        {
                            this.Signature = ItemUtilities.GetString(rangeStream);
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Tag
                if (this.Tag != null)
                {
                    using (var stream = this.Tag.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Tag, stream);
                    }
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

                // Signature
                if (this.Signature != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Signature, this.Signature);
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
                if ((object)obj == null || !(obj is Metadata)) return false;

                return this.Equals((Metadata)obj);
            }

            public override bool Equals(Metadata other)
            {
                if ((object)other == null) return false;
                if (object.ReferenceEquals(this, other)) return true;

                if (this.Tag != other.Tag
                    || this.CreationTime != other.CreationTime
                    || this.Key != other.Key

                    || this.Cash != other.Cash)
                {
                    return false;
                }

                return true;
            }

            public override void CreateCash(Miner miner)
            {
                base.CreateCash(miner);
            }

            public override int VerifyCash()
            {
                return base.VerifyCash();
            }

            protected override Stream GetCashStream()
            {
                var temp = this.Cash;
                this.Cash = null;

                try
                {
                    return this.Export(BufferManager.Instance);
                }
                finally
                {
                    this.Cash = temp;
                }
            }

            public override Cash Cash
            {
                get
                {
                    return _cash;
                }
                protected set
                {
                    _cash = value;
                }
            }

            #region IMetadata<TTag>

            [DataMember(Name = "Tag")]
            public TTag Tag
            {
                get
                {
                    return _tag;
                }
                set
                {
                    _tag = value;
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
                set
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
                    return _key;
                }
                set
                {
                    _key = value;
                }
            }

            [DataMember(Name = "Signature")]
            public string Signature
            {
                get
                {
                    return _signature;
                }
                set
                {
                    _signature = value;
                }
            }

            #endregion
        }
    }
}
