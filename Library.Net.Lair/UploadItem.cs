using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    [DataContract(Name = "UploadItem", Namespace = "http://Library/Net/Lair")]
    sealed class UploadItem
    {
        private Tag _tag;
        private string _type;
        private OptionCollection _options;
        private DigitalSignature _digitalSignature;

        private IExchangeEncrypt _publicKey;

        private SectionProfileContent _sectionProfileContent;
        private SectionMessageContent _sectionMessageContent;
        private DocumentPageContent _documentPageContent;
        private DocumentOpinionContent _documentOpinionContent;
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

        [DataMember(Name = "Options")]
        public OptionCollection Options
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_options == null)
                        _options = new OptionCollection();

                    return _options;
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

        [DataMember(Name = "PublicKey")]
        public IExchangeEncrypt PublicKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _publicKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _publicKey = value;
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

        [DataMember(Name = "DocumentOpinionContent")]
        public DocumentOpinionContent DocumentOpinionContent
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _documentOpinionContent;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _documentOpinionContent = value;
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
