using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "SectionProfile", Namespace = "http://Library/Net/Lair")]
    public sealed class SectionProfile : IMessage<Section>, IEquatable<SectionProfile>
    {
        private Section _tag;
        private string _signature;
        private DateTime _creationTime;
        private string _comment;
        private ExchangePublicKey _exchangePublicKey;
        private SignatureCollection _trustSignatures;
        private WikiCollection _wikis;
        private ChatCollection _chats;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxCommentLength = SectionProfileContent.MaxCommentLength;
        public static readonly int MaxTrustSignatureCount = SectionProfileContent.MaxTrustSignatureCount;
        public static readonly int MaxWikiCount = SectionProfileContent.MaxWikiCount;
        public static readonly int MaxChatCount = SectionProfileContent.MaxChatCount;

        public static readonly int MaxPublickeyLength = 1024 * 8;

        internal SectionProfile(Section tag, string signature, DateTime creationTime, string comment, ExchangePublicKey exchangePublicKey, IEnumerable<string> trustSignatures,
            IEnumerable<Wiki> wikis, IEnumerable<Chat> chats)
        {
            this.Tag = tag;
            this.Signature = signature;
            this.CreationTime = creationTime;
            this.Comment = comment;
            this.ExchangePublicKey = exchangePublicKey;
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (wikis != null) this.ProtectedWikis.AddRange(wikis);
            if (chats != null) this.ProtectedChats.AddRange(chats);
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Comment == null) return 0;
                else return this.Comment.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is SectionProfile)) return false;

            return this.Equals((SectionProfile)obj);
        }

        public bool Equals(SectionProfile other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Tag != other.Tag
                || this.Signature != other.Signature
                || this.CreationTime != other.CreationTime
                || this.Comment != other.Comment
                || this.ExchangePublicKey != other.ExchangePublicKey
                || (this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.Wikis == null) != (other.Wikis == null)
                || (this.Chats == null) != (other.Chats == null))
            {
                return false;
            }

            if (this.TrustSignatures != null && other.TrustSignatures != null)
            {
                if (!Collection.Equals(this.TrustSignatures, other.TrustSignatures)) return false;
            }

            if (this.Wikis != null && other.Wikis != null)
            {
                if (!Collection.Equals(this.Wikis, other.Wikis)) return false;
            }

            if (this.Chats != null && other.Chats != null)
            {
                if (!Collection.Equals(this.Chats, other.Chats)) return false;
            }

            return true;
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

        #region IMessage<Section>

        [DataMember(Name = "Tag")]
        public Section Tag
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _tag;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _tag = value;
                }
            }
        }

        [DataMember(Name = "Signature")]
        public string Signature
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _signature;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    if (value != null && !Library.Security.Signature.HasSignature(value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _signature = value;
                    }
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
            private set
            {
                lock (this.ThisLock)
                {
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

        #endregion

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _comment;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {

                    if (value != null && value.Length > SectionProfile.MaxCommentLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _comment = value;
                    }
                }
            }
        }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _exchangePublicKey;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _exchangePublicKey = value;
                }
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyTrustSignatures;

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyTrustSignatures == null)
                        _readOnlyTrustSignatures = new ReadOnlyCollection<string>(this.ProtectedTrustSignatures);

                    return _readOnlyTrustSignatures;
                }
            }
        }

        [DataMember(Name = "TrustSignatures")]
        private SignatureCollection ProtectedTrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_trustSignatures == null)
                        _trustSignatures = new SignatureCollection(SectionProfile.MaxTrustSignatureCount);

                    return _trustSignatures;
                }
            }
        }

        private volatile ReadOnlyCollection<Wiki> _readOnlyWikis;

        public IEnumerable<Wiki> Wikis
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyWikis == null)
                        _readOnlyWikis = new ReadOnlyCollection<Wiki>(this.ProtectedWikis);

                    return _readOnlyWikis;
                }
            }
        }

        [DataMember(Name = "Wikis")]
        private WikiCollection ProtectedWikis
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_wikis == null)
                        _wikis = new WikiCollection(SectionProfile.MaxWikiCount);

                    return _wikis;
                }
            }
        }

        private volatile ReadOnlyCollection<Chat> _readOnlyChats;

        public IEnumerable<Chat> Chats
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyChats == null)
                        _readOnlyChats = new ReadOnlyCollection<Chat>(this.ProtectedChats);

                    return _readOnlyChats;
                }
            }
        }

        [DataMember(Name = "Chats")]
        private ChatCollection ProtectedChats
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_chats == null)
                        _chats = new ChatCollection(SectionProfile.MaxChatCount);

                    return _chats;
                }
            }
        }
    }
}
