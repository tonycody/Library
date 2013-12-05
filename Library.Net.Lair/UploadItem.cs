using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "UploadItem", Namespace = "http://Library/Net/Lair")]
    sealed class UploadItem
    {
        private Tag _tag;
        private string _path;

        private string _linkType;
        private string _headerType;

        private DigitalSignature _digitalSignature;
        private ExchangePublicKey _exchangePublicKey;

        private SectionProfileContent _sectionProfileContent;
        private SectionMessageContent _sectionMessageContent;
        private DocumentPageContent _documentPageContent;
        private DocumentVoteContent _documentVoteContent;
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

        [DataMember(Name = "Tag")]
        public Tag Tag
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _tag;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _tag = value;
                }
            }
        }

        [DataMember(Name = "Path")]
        public string Path
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _path;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _path = value;
                }
            }
        }

        [DataMember(Name = "LinkType")]
        public string LinkType
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _linkType;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _linkType = value;
                }
            }
        }

        [DataMember(Name = "HeaderType")]
        public string HeaderType
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _headerType;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _headerType = value;
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

        [DataMember(Name = "DocumentPageContent")]
        public DocumentPageContent DocumentPageContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _documentPageContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _documentPageContent = value;
                }
            }
        }

        [DataMember(Name = "DocumentVoteContent")]
        public DocumentVoteContent DocumentVoteContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _documentVoteContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _documentVoteContent = value;
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
