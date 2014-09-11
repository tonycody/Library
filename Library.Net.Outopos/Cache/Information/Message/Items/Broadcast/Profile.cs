using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Profile", Namespace = "http://Library/Net/Outopos")]
    public sealed class Profile : ImmutableCertificateItemBase<Profile>, IBroadcastHeader, IProfileContent
    {
        private enum SerializeId : byte
        {
            CreationTime = 0,

            Cost = 1,
            ExchangePublicKey = 2,
            TrustSignature = 3,
            DeleteSignature = 4,
            Wiki = 5,
            Chat = 6,

            Certificate = 7,
        }

        private DateTime _creationTime;

        private volatile int _cost;
        private volatile ExchangePublicKey _exchangePublicKey;
        private volatile SignatureCollection _trustSignatures;
        private volatile SignatureCollection _deleteSignatures;
        private volatile WikiCollection _wikis;
        private volatile ChatCollection _chats;

        private volatile Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxTrustSignatureCount = 1024;
        public static readonly int MaxDeleteSignatureCount = 1024;
        public static readonly int MaxWikiCount = 256;
        public static readonly int MaxChatCount = 256;

        internal Profile(DateTime creationTime, int cost, ExchangePublicKey exchangePublicKey, IEnumerable<string> trustSignatures, IEnumerable<string> deleteSignatures, IEnumerable<Wiki> wikis, IEnumerable<Chat> chats, DigitalSignature digitalSignature)
        {
            this.CreationTime = creationTime;

            this.Cost = cost;
            this.ExchangePublicKey = exchangePublicKey;
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (deleteSignatures != null) this.ProtectedDeleteSignatures.AddRange(deleteSignatures);
            if (wikis != null) this.ProtectedWikis.AddRange(wikis);
            if (chats != null) this.ProtectedChats.AddRange(chats);

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
                    if (id == (byte)SerializeId.CreationTime)
                    {
                        this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                    }

                    else if (id == (byte)SerializeId.Cost)
                    {
                        this.Cost = ItemUtilities.GetInt(rangeStream);
                    }
                    else if (id == (byte)SerializeId.ExchangePublicKey)
                    {
                        this.ExchangePublicKey = ExchangePublicKey.Import(rangeStream, bufferManager);
                    }
                    else if (id == (byte)SerializeId.TrustSignature)
                    {
                        this.ProtectedTrustSignatures.Add(ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.DeleteSignature)
                    {
                        this.ProtectedDeleteSignatures.Add(ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.Wiki)
                    {
                        this.ProtectedWikis.Add(Wiki.Import(rangeStream, bufferManager));
                    }
                    else if (id == (byte)SerializeId.Chat)
                    {
                        this.ProtectedChats.Add(Chat.Import(rangeStream, bufferManager));
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

            // CreationTime
            if (this.CreationTime != DateTime.MinValue)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
            }

            // Cost
            if (this.Cost != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Cost, this.Cost);
            }
            // ExchangePublicKey
            if (this.ExchangePublicKey != null)
            {
                using (var stream = this.ExchangePublicKey.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.ExchangePublicKey, stream);
                }
            }
            // TrustSignatures
            foreach (var value in this.TrustSignatures)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.TrustSignature, value);
            }
            // DeleteSignatures
            foreach (var value in this.DeleteSignatures)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.DeleteSignature, value);
            }
            // Wikis
            foreach (var value in this.Wikis)
            {
                using (var stream = value.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Wiki, stream);
                }
            }
            // Chats
            foreach (var value in this.Chats)
            {
                using (var stream = value.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Chat, stream);
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
            return this.CreationTime.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Profile)) return false;

            return this.Equals((Profile)obj);
        }

        public override bool Equals(Profile other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CreationTime != other.CreationTime

                || this.Cost != other.Cost
                || this.ExchangePublicKey != other.ExchangePublicKey
                || (this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.DeleteSignatures == null) != (other.DeleteSignatures == null)
                || (this.Wikis == null) != (other.Wikis == null)
                || (this.Chats == null) != (other.Chats == null)

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.TrustSignatures != null && other.TrustSignatures != null)
            {
                if (!CollectionUtilities.Equals(this.TrustSignatures, other.TrustSignatures)) return false;
            }

            if (this.DeleteSignatures != null && other.DeleteSignatures != null)
            {
                if (!CollectionUtilities.Equals(this.DeleteSignatures, other.DeleteSignatures)) return false;
            }

            if (this.Wikis != null && other.Wikis != null)
            {
                if (!CollectionUtilities.Equals(this.Wikis, other.Wikis)) return false;
            }

            if (this.Chats != null && other.Chats != null)
            {
                if (!CollectionUtilities.Equals(this.Chats, other.Chats)) return false;
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

        #region IBroadcastHeader

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

        #region IProfile

        [DataMember(Name = "Cost")]
        public int Cost
        {
            get
            {
                return _cost;
            }
            private set
            {
                _cost = value;
            }
        }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey
        {
            get
            {
                return _exchangePublicKey;
            }
            private set
            {
                _exchangePublicKey = value;
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyTrustSignatures;

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                if (_readOnlyTrustSignatures == null)
                    _readOnlyTrustSignatures = new ReadOnlyCollection<string>(this.ProtectedTrustSignatures.ToArray());

                return _readOnlyTrustSignatures;
            }
        }

        [DataMember(Name = "TrustSignatures")]
        private SignatureCollection ProtectedTrustSignatures
        {
            get
            {
                if (_trustSignatures == null)
                    _trustSignatures = new SignatureCollection(Profile.MaxTrustSignatureCount);

                return _trustSignatures;
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyDeleteSignatures;

        public IEnumerable<string> DeleteSignatures
        {
            get
            {
                if (_readOnlyDeleteSignatures == null)
                    _readOnlyDeleteSignatures = new ReadOnlyCollection<string>(this.ProtectedDeleteSignatures.ToArray());

                return _readOnlyDeleteSignatures;
            }
        }

        [DataMember(Name = "DeleteSignatures")]
        private SignatureCollection ProtectedDeleteSignatures
        {
            get
            {
                if (_deleteSignatures == null)
                    _deleteSignatures = new SignatureCollection(Profile.MaxDeleteSignatureCount);

                return _deleteSignatures;
            }
        }

        private volatile ReadOnlyCollection<Wiki> _readOnlyWikis;

        public IEnumerable<Wiki> Wikis
        {
            get
            {
                if (_readOnlyWikis == null)
                    _readOnlyWikis = new ReadOnlyCollection<Wiki>(this.ProtectedWikis.ToArray());

                return _readOnlyWikis;
            }
        }

        [DataMember(Name = "Wikis")]
        private WikiCollection ProtectedWikis
        {
            get
            {
                if (_wikis == null)
                    _wikis = new WikiCollection(Profile.MaxWikiCount);

                return _wikis;
            }
        }

        private volatile ReadOnlyCollection<Chat> _readOnlyChats;

        public IEnumerable<Chat> Chats
        {
            get
            {
                if (_readOnlyChats == null)
                    _readOnlyChats = new ReadOnlyCollection<Chat>(this.ProtectedChats.ToArray());

                return _readOnlyChats;
            }
        }

        [DataMember(Name = "Chats")]
        private ChatCollection ProtectedChats
        {
            get
            {
                if (_chats == null)
                    _chats = new ChatCollection(Profile.MaxChatCount);

                return _chats;
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
    }
}
