using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Lair
{
    public sealed class LairManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private string _cachePath;
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private CacheManager _cacheManager;
        private ConnectionsManager _connectionsManager;

        private ManagerState _state = ManagerState.Stop;

        private TrustSignaturesEventHandler _trustSignaturesEvent;
        private LockSectionsEventHandler _removeSectionsEvent;
        private LockChatsEventHandler _removeChatsEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public LairManager(string cachePath, BufferManager bufferManager)
        {
            _cachePath = cachePath;

            _bufferManager = bufferManager;
            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _cacheManager = new CacheManager(_cachePath, _bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _cacheManager, _bufferManager);

            _connectionsManager.TrustSignaturesEvent = (object sender) =>
            {
                if (_trustSignaturesEvent != null)
                {
                    return _trustSignaturesEvent(this);
                }

                return null;
            };

            _connectionsManager.LockSectionsEvent = (object sender) =>
            {
                if (_removeSectionsEvent != null)
                {
                    return _removeSectionsEvent(this);
                }

                return null;
            };

            _connectionsManager.LockChatsEvent = (object sender) =>
            {
                if (_removeChatsEvent != null)
                {
                    return _removeChatsEvent(this);
                }

                return null;
            };
        }

        public TrustSignaturesEventHandler TrustSignaturesEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _trustSignaturesEvent = value;
                }
            }
        }

        public LockSectionsEventHandler LockSectionsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeSectionsEvent = value;
                }
            }
        }

        public LockChatsEventHandler LockChatsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeChatsEvent = value;
                }
            }
        }

        public Information Information
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    List<InformationContext> contexts = new List<InformationContext>();
                    contexts.AddRange(_connectionsManager.Information);
                    contexts.AddRange(_cacheManager.Information);

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionInformation;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.BaseNode;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.BaseNode = value;
                }
            }
        }

        public IEnumerable<Node> OtherNodes
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.OtherNodes;
                }
            }
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _clientManager.Filters;
                }
            }
        }

        public UriCollection ListenUris
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _serverManager.ListenUris;
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionCountLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.ConnectionCountLimit = value;
                }
            }
        }

        public int BandWidthLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.BandWidthLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.BandWidthLimit = value;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ReceivedByteCount;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.SentByteCount;
                }
            }
        }

        public long Size
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _cacheManager.Size;
                }
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SetOtherNodes(nodes);
            }
        }

        public void Resize(long size)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _cacheManager.Resize(size);
            }
        }

        public void CheckBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _cacheManager.CheckBlocks(getProgressEvent);
        }

        public void SendSectionRequest(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendSectionRequest(section);
            }
        }

        public void SendChatRequest(Chat channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendChatRequest(channel);
            }
        }

        public void SendSignatureRequest(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendSignatureRequest(signature);
            }
        }

        public IEnumerable<Section> GetSections()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetSections();
            }
        }

        public IEnumerable<Chat> GetChats()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChats();
            }
        }

        public IEnumerable<string> GetSignatures()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetSignatures();
            }
        }

        public IEnumerable<SectionProfile> GetProfiles(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetProfiles(section);
            }
        }

        public IEnumerable<DocumentPage> GetDocumentPages(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetDocumentPages(section);
            }
        }

        public IEnumerable<DocumentOpinion> GetDocumentOpinions(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetDocumentOpinions(section);
            }
        }

        public IEnumerable<ChatTopic> GetTopics(Chat channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetTopics(channel);
            }
        }

        public IEnumerable<ChatMessage> GetMessages(Chat channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetMessages(channel);
            }
        }

        public IEnumerable<MailMessage> GetMailMessages(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetMailMessages(signature);
            }
        }

        public SectionProfileContent GetContent(SectionProfile profile)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(profile);
            }
        }

        public DocumentPageContent GetContent(DocumentPage document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(document);
            }
        }

        public DocumentOpinionContent GetContent(DocumentOpinion vote)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(vote);
            }
        }

        public ChatTopicContent GetContent(ChatTopic topic)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(topic);
            }
        }

        public ChatMessageContent GetContent(ChatMessage message)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(message);
            }
        }

        public MailMessageContent GetContent(MailMessage mail, IExchangeDecrypt exchangeDecrypt)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(mail, exchangeDecrypt);
            }
        }

        public SectionProfile UploadProfile(Section section,
            IEnumerable<string> trustSignatures, IEnumerable<Chat> channels, string comment, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.UploadProfile(section, trustSignatures, channels, comment, exchangeEncrypt, digitalSignature);
            }
        }

        public DocumentPage UploadDocumentPage(Section section, string name,
            string comment, HypertextFormatType formatType, string hypertext, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.UploadDocumentPage(section, name, comment, formatType, hypertext, digitalSignature);
            }
        }

        public DocumentOpinion UploadDocumentOpinion(Section section,
             IEnumerable<Key> goods, IEnumerable<Key> bads, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.UploadDocumentOpinion(section, goods, bads, digitalSignature);
            }
        }

        public ChatTopic UploadTopic(Chat channel,
            string comment, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.UploadTopic(channel, comment, digitalSignature);
            }
        }

        public ChatMessage UploadMessage(Chat channel,
            string comment, IEnumerable<Key> anchors, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.UploadMessage(channel, comment, anchors, digitalSignature);
            }
        }

        public MailMessage UploadMailMessage(string recipientSignature,
            string text, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.UploadMailMessage(recipientSignature, text, exchangeEncrypt, digitalSignature);
            }
        }

        public override ManagerState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
            }
        }

        public override void Start()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _connectionsManager.Start();
            }
        }

        public override void Stop()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _connectionsManager.Stop();
            }
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                this.Stop();

                _clientManager.Load(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.Save(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _serverManager.Save(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _clientManager.Save(System.IO.Path.Combine(directoryPath, "ClientManager"));
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _connectionsManager.Dispose();
                _serverManager.Dispose();
                _clientManager.Dispose();
            }
        }

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }
}
