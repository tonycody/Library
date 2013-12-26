using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "SectionMessage", Namespace = "http://Library/Net/Lair")]
    public sealed class SectionMessage : IMessage<Section>, IEquatable<SectionMessage>
    {
        private Section _tag;
        private string _signature;
        private DateTime _creationTime;
        private string _comment;
        private Anchor _anchor;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxCommentLength = SectionMessageContent.MaxCommentLength;

        internal SectionMessage(Section tag, string signature, DateTime creationTime, string comment, Anchor anchor)
        {
            this.Tag = tag;
            this.Signature = signature;
            this.CreationTime = creationTime;
            this.Comment = comment;
            this.Anchor = anchor;
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
            if ((object)obj == null || !(obj is SectionMessage)) return false;

            return this.Equals((SectionMessage)obj);
        }

        public bool Equals(SectionMessage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Tag != other.Tag
                || this.Signature != other.Signature
                || this.CreationTime != other.CreationTime
                || this.Comment != other.Comment
                || this.Anchor != other.Anchor)
            {
                return false;
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
                    if (value != null && value.Length > SectionMessage.MaxCommentLength)
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

        [DataMember(Name = "Anchor")]
        public Anchor Anchor
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _anchor;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _anchor = value;

                    if (_anchor == null)
                    {
                        _hashCode = 0;
                    }
                    else
                    {
                        _hashCode = _anchor.GetHashCode();
                    }
                }
            }
        }
    }
}
