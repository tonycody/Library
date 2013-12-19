using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "WikiVote", Namespace = "http://Library/Net/Lair")]
    public sealed class WikiVote : IMessage<Wiki>, IEquatable<WikiVote>
    {
        private Wiki _tag;
        private string _signature;
        private DateTime _creationTime;
        private AnchorCollection _goods;
        private AnchorCollection _bads;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxGoodCount = WikiVoteContent.MaxGoodCount;
        public static readonly int MaxBadCount = WikiVoteContent.MaxBadCount;

        internal WikiVote(Wiki tag, string signature, DateTime creationTime, IEnumerable<Anchor> goods, IEnumerable<Anchor> bads)
        {
            this.Tag = tag;
            this.Signature = signature;
            this.CreationTime = creationTime;
            if (goods != null) this.ProtectedGoods.AddRange(goods);
            if (bads != null) this.ProtectedBads.AddRange(bads);
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.ProtectedGoods.Count == 0) return 0;
                else return this.ProtectedGoods[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is WikiVote)) return false;

            return this.Equals((WikiVote)obj);
        }

        public bool Equals(WikiVote other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Tag != other.Tag
                || this.Signature != other.Signature
                || this.CreationTime != other.CreationTime
                || (this.Goods == null) != (other.Goods == null)
                || (this.Bads == null) != (other.Bads == null))
            {
                return false;
            }

            if (this.Goods != null && other.Goods != null)
            {
                if (!Collection.Equals(this.Goods, other.Goods)) return false;
            }

            if (this.Bads != null && other.Bads != null)
            {
                if (!Collection.Equals(this.Bads, other.Bads)) return false;
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

        #region IMessage<Wiki>

        [DataMember(Name = "Tag")]
        public Wiki Tag
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

        private volatile ReadOnlyCollection<Anchor> _readOnlyGoods;

        public IEnumerable<Anchor> Goods
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyGoods == null)
                        _readOnlyGoods = new ReadOnlyCollection<Anchor>(this.ProtectedGoods);

                    return _readOnlyGoods;
                }
            }
        }

        [DataMember(Name = "Goods")]
        private AnchorCollection ProtectedGoods
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_goods == null)
                        _goods = new AnchorCollection(WikiVote.MaxGoodCount);

                    return _goods;
                }
            }
        }

        private volatile ReadOnlyCollection<Anchor> _readOnlyBads;

        public IEnumerable<Anchor> Bads
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyBads == null)
                        _readOnlyBads = new ReadOnlyCollection<Anchor>(this.ProtectedBads);

                    return _readOnlyBads;
                }
            }
        }

        [DataMember(Name = "Bads")]
        private AnchorCollection ProtectedBads
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_bads == null)
                        _bads = new AnchorCollection(WikiVote.MaxBadCount);

                    return _bads;
                }
            }
        }
    }
}
