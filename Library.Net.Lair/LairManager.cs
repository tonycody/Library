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
        private LockChannelsEventHandler _removeChannelsEvent;

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

            _connectionsManager.LockChannelsEvent = (object sender) =>
            {
                if (_removeChannelsEvent != null)
                {
                    return _removeChannelsEvent(this);
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

        public LockChannelsEventHandler LockChannelsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeChannelsEvent = value;
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

        public void SendChannelRequest(Channel channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SendChannelRequest(channel);
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

        public IEnumerable<Channel> GetChannels()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChannels();
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

        public IEnumerable<Profile> GetProfiles(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetProfiles(section);
            }
        }

        public IEnumerable<Document> GetDocuments(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetDocuments(section);
            }
        }

        public IEnumerable<Topic> GetTopics(Channel channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetTopics(channel);
            }
        }

        public IEnumerable<Message> GetMessages(Channel channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetMessages(channel);
            }
        }

        public IEnumerable<Mail> GetMails(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetMails(signature);
            }
        }

        public ProfileContent GetContent(Profile profile)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(profile);
            }
        }

        public DocumentContent GetContent(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(document);
            }
        }

        public TopicContent GetContent(Topic topic)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(topic);
            }
        }

        public MessageContent GetContent(Message message)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(message);
            }
        }

        public MailContent GetContent(Mail mail, IExchangeDecrypt exchangeDecrypt)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetContent(mail, exchangeDecrypt);
            }
        }

        public void UploadProfile(Section section,
            IEnumerable<string> trustSignatures, IEnumerable<Channel> channels, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadProfile(section, trustSignatures, channels, exchangeEncrypt, digitalSignature);
            }
        }

        public void UploadDocument(Section section,
            IEnumerable<Page> pages, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadDocument(section, pages, digitalSignature);
            }
        }

        public void UploadTopic(Channel channel,
            string text, ContentFormatType formatType, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadTopic(channel, text, formatType, digitalSignature);
            }
        }

        public void UploadMessage(Channel channel,
            string text, IEnumerable<Key> anchors, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadMessage(channel, text, anchors, digitalSignature);
            }
        }

        public void UploadMail(string recipientSignature,
            string text, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.UploadMail(recipientSignature, text, exchangeEncrypt, digitalSignature);
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
