using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "WikiPage", Namespace = "http://Library/Net/Lair")]
    public sealed class WikiPage : IMessage<Wiki>, IEquatable<WikiPage>
    {
        private Wiki _tag;
        private string _signature;
        private DateTime _creationTime;
        private HypertextFormatType _formatType;
        private string _hypertext;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxHypertextLength = WikiPageContent.MaxHypertextLength;

        internal WikiPage(Wiki tag, string signature, DateTime creationTime, HypertextFormatType formatType, string hypertext)
        {
            this.Tag = tag;
            this.Signature = signature;
            this.CreationTime = creationTime;
            this.FormatType = formatType;
            this.Hypertext = hypertext;
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Hypertext == null) return 0;
                else return this.Hypertext.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is WikiPage)) return false;

            return this.Equals((WikiPage)obj);
        }

        public bool Equals(WikiPage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Tag != other.Tag
                || this.Signature != other.Signature
                || this.CreationTime != other.CreationTime
                || this.FormatType != other.FormatType
                || this.Hypertext != other.Hypertext)
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

        [DataMember(Name = "FormatType")]
        public HypertextFormatType FormatType
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _formatType;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(HypertextFormatType), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _formatType = value;
                    }
                }
            }
        }

        [DataMember(Name = "Hypertext")]
        public string Hypertext
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hypertext;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > WikiPage.MaxHypertextLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _hypertext = value;
                    }
                }
            }
        }
    }
}
