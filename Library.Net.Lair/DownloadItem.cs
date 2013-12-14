using System.Runtime.Serialization;
using System;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "DownloadState", Namespace = "http://Library/Net/Lair")]
    public enum DownloadState
    {
        [EnumMember(Value = "Downloading")]
        Downloading = 0,

        [EnumMember(Value = "Completed")]
        Completed = 1,

        [EnumMember(Value = "Error")]
        Error = 2,
    }

    [DataContract(Name = "DownloadItem", Namespace = "http://Library/Net/Lair")]
    sealed class DownloadItem
    {
        private DownloadState _state;

        private string _type;
        private Key _key;

        private ExchangePrivateKey _exchangePrivateKey;

        private SectionProfileContent _sectionProfileContent;
        private SectionMessageContent _sectionMessageContent;
        private ArchiveDocumentContent _archiveDocumentContent;
        private ArchiveVoteContent _archiveVoteContent;
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

        [DataMember(Name = "State")]
        public DownloadState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _state = value;
                }
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

        [DataMember(Name = "Key")]
        public Key Key
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _key;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _key = value;
                }
            }
        }

        [DataMember(Name = "ExchangePrivateKey")]
        public ExchangePrivateKey ExchangePrivateKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _exchangePrivateKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _exchangePrivateKey = value;
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

        [DataMember(Name = "ArchiveDocumentContent")]
        public ArchiveDocumentContent ArchiveDocumentContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _archiveDocumentContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _archiveDocumentContent = value;
                }
            }
        }

        [DataMember(Name = "ArchiveVoteContent")]
        public ArchiveVoteContent ArchiveVoteContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _archiveVoteContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _archiveVoteContent = value;
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
