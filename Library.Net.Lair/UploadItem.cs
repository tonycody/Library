using System;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "UploadItem", Namespace = "http://Library/Net/Lair")]
    sealed class UploadItem
    {
        private string _type;

        private Section _section;
        private Wiki _wiki;
        private Chat _chat;

        private DateTime _creationTime;
        private DigitalSignature _digitalSignature;
        private ExchangePublicKey _exchangePublicKey;

        private SectionProfileContent _sectionProfileContent;
        private SectionMessageContent _sectionMessageContent;
        private WikiPageContent _wikiDocumentContent;
        private WikiVoteContent _wikiVoteContent;
        private ChatTopicContent _chatTopicContent;
        private ChatMessageContent _chatMessageContent;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

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

        [DataMember(Name = "Type")]
        public string Type
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _type;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _type = value;
                }
            }
        }

        [DataMember(Name = "Section")]
        public Section Section
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _section;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _section = value;
                }
            }
        }

        [DataMember(Name = "Wiki")]
        public Wiki Wiki
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _wiki;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _wiki = value;
                }
            }
        }

        [DataMember(Name = "Chat")]
        public Chat Chat
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _chat;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _chat = value;
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
                    _creationTime = value;
                }
            }
        }

        [DataMember(Name = "DigitalSignature")]
        public DigitalSignature DigitalSignature
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _digitalSignature;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _digitalSignature = value;
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
            set
            {
                lock (this.ThisLock)
                {
                    _exchangePublicKey = value;
                }
            }
        }

        [DataMember(Name = "SectionProfileContent")]
        public SectionProfileContent SectionProfileContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _sectionProfileContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _sectionProfileContent = value;
                }
            }
        }

        [DataMember(Name = "SectionMessageContent")]
        public SectionMessageContent SectionMessageContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _sectionMessageContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _sectionMessageContent = value;
                }
            }
        }

        [DataMember(Name = "WikiPageContent")]
        public WikiPageContent WikiPageContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _wikiDocumentContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _wikiDocumentContent = value;
                }
            }
        }

        [DataMember(Name = "WikiVoteContent")]
        public WikiVoteContent WikiVoteContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _wikiVoteContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _wikiVoteContent = value;
                }
            }
        }

        [DataMember(Name = "ChatTopicContent")]
        public ChatTopicContent ChatTopicContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _chatTopicContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _chatTopicContent = value;
                }
            }
        }

        [DataMember(Name = "ChatMessageContent")]
        public ChatMessageContent ChatMessageContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _chatMessageContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _chatMessageContent = value;
                }
            }
        }
    }
}
