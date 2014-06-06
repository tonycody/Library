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
    [DataContract(Name = "Metadata", Namespace = "http://Library/Net/Outopos")]
    public abstract class Metadata<TMetadata, TTag> : ImmutableCashItemBase<TMetadata>, IMetadata<TTag>
        where TMetadata : Metadata<TMetadata, TTag>
        where TTag : ItemBase<TTag>, ITag
    {
        private enum SerializeId : byte
        {
            Tag = 0,
            Signature = 1,
            CreationTime = 2,
            Key = 3,

            Cash = 4,
        }

        private volatile TTag _tag;
        private volatile string _signature;
        private DateTime _creationTime;
        private volatile Key _key;

        private volatile Cash _cash;

        private volatile object _thisLock;

        public Metadata(TTag tag, string signature, DateTime creationTime, Key key, Miner miner)
        {
            this.Tag = tag;
            this.Signature = signature;
            this.CreationTime = creationTime;
            this.Key = key;

            this.CreateCash(miner);
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
                    if (id == (byte)SerializeId.Tag)
                    {
                        this.Tag = ItemBase<TTag>.Import(rangeStream, bufferManager);
                    }
                    else if (id == (byte)SerializeId.Signature)
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

            if (this.Tag != other.Tag
                || this.CreationTime != other.CreationTime
                || this.Key != other.Key

                || this.Cash != other.Cash)
            {
                return false;
            }

            return true;
        }

        protected override void CreateCash(Miner miner)
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
            private set
            {
                _tag = value;
            }
        }

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

        #endregion
    }
}
