using System;
using System.Collections.Generic;
using System.Linq;
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
        private DownloadManager _downloadManager;
        private UploadManager _uploadManager;

        private ManagerState _state = ManagerState.Stop;

        private CreateCapEventHandler _createCapEvent;
        private AcceptCapEventHandler _acceptCapEvent;
        private GetCriteriaEventHandler _getLockCriteriaEvent;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public LairManager(string cachePath, BufferManager bufferManager)
        {
            _cachePath = cachePath;

            _bufferManager = bufferManager;
            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _cacheManager = new CacheManager(_cachePath, _bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _cacheManager, _bufferManager);
            _downloadManager = new DownloadManager(_connectionsManager, _cacheManager, _bufferManager);
            _uploadManager = new UploadManager(_connectionsManager, _cacheManager, _bufferManager);

            _clientManager.CreateCapEvent = (object sender, string uri) =>
            {
                if (_createCapEvent != null)
                {
                    return _createCapEvent(this, uri);
                }

                return null;
            };

            _serverManager.AcceptCapEvent = (object sender, out string uri) =>
            {
                uri = null;

                if (_acceptCapEvent != null)
                {
                    return _acceptCapEvent(this, out uri);
                }

                return null;
            };

            _connectionsManager.GetLockCriteriaEvent = (object sender) =>
            {
                if (_getLockCriteriaEvent != null)
                {
                    return _getLockCriteriaEvent(this);
                }

                return null;
            };
        }

        public CreateCapEventHandler CreateCapEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _createCapEvent = value;
                }
            }
        }

        public AcceptCapEventHandler AcceptCapEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _acceptCapEvent = value;
                }
            }
        }

        public GetCriteriaEventHandler GetLockCriteriaEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockCriteriaEvent = value;
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

        public void SetBaseNode(Node baseNode)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start)
                {
                    _connectionsManager.Stop();
                }

                _connectionsManager.SetBaseNode(baseNode);

                if (this.State == ManagerState.Start)
                {
                    _connectionsManager.Start();
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

        public IEnumerable<SectionProfile> GetSectionProfile(Section tag, IEnumerable<string> trustSignatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetSectionProfile(tag, trustSignatures);
            }
        }

        public SectionProfile GetSectionProfile(Section tag, string trustSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetSectionProfile(tag, new string[] { trustSignature }).FirstOrDefault();
            }
        }

        public IEnumerable<SectionMessage> GetSectionMessage(Section tag, IEnumerable<string> trustSignatures, ExchangePrivateKey exchangePrivateKey)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetSectionMessage(tag, trustSignatures, exchangePrivateKey);
            }
        }

        public IEnumerable<SectionMessage> GetSectionMessage(Section tag, string trustSignature, ExchangePrivateKey exchangePrivateKey)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetSectionMessage(tag, new string[] { trustSignature }, exchangePrivateKey);
            }
        }

        public IEnumerable<WikiPage> GetWikiPage(Wiki tag, IEnumerable<string> trustSignatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetWikiPage(tag, trustSignatures);
            }
        }

        public IEnumerable<WikiPage> GetWikiPage(Wiki tag, string trustSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetWikiPage(tag, new string[] { trustSignature });
            }
        }

        public IEnumerable<WikiVote> GetWikiVote(Wiki tag, IEnumerable<string> trustSignatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetWikiVote(tag, trustSignatures);
            }
        }

        public WikiVote GetWikiVote(Wiki tag, string trustSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetWikiVote(tag, new string[] { trustSignature }).FirstOrDefault();
            }
        }

        public IEnumerable<ChatTopic> GetChatTopic(Chat tag, IEnumerable<string> trustSignatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetChatTopic(tag, trustSignatures);
            }
        }

        public ChatTopic GetChatTopic(Chat tag, string trustSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetChatTopic(tag, new string[] { trustSignature }).FirstOrDefault();
            }
        }

        public IEnumerable<ChatMessage> GetChatMessage(Chat tag, IEnumerable<string> trustSignatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetChatMessage(tag, trustSignatures);
            }
        }

        public IEnumerable<ChatMessage> GetChatMessage(Chat tag, string trustSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetChatMessage(tag, new string[] { trustSignature });
            }
        }

        public void UploadSectionProfile(Section tag,
            string comment, ExchangePublicKey exchangePublicKey, IEnumerable<string> trustSignatures, IEnumerable<Wiki> wikis, IEnumerable<Chat> chats,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, new SectionProfileContent(comment, exchangePublicKey, trustSignatures, wikis, chats), digitalSignature);
            }
        }

        public void UploadSectionMessage(Section tag,
            string comment, Anchor anchor,
            ExchangePublicKey exchangePublicKey,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, new SectionMessageContent(comment, anchor), exchangePublicKey, digitalSignature);
            }
        }

        public void UploadWikiPage(Wiki tag,
            HypertextFormatType formatType, string hypertext,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, new WikiPageContent(formatType, hypertext), digitalSignature);
            }
        }

        public void UploadWikiVote(Wiki tag,
            IEnumerable<Anchor> goods, IEnumerable<Anchor> bads,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, new WikiVoteContent(goods, bads), digitalSignature);
            }
        }

        public void UploadChatTopic(Chat tag,
            HypertextFormatType formatType, string hypertext,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, new ChatTopicContent(formatType, hypertext), digitalSignature);
            }
        }

        public void UploadChatMessage(Chat tag,
            string comment, IEnumerable<Anchor> anchors,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, new ChatMessageContent(comment, anchors), digitalSignature);
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
                _downloadManager.Start();
                _uploadManager.Start();
            }
        }

        public override void Stop()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _uploadManager.Stop();
                _downloadManager.Stop();
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
                _cacheManager.Load(System.IO.Path.Combine(directoryPath, "CacheManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _uploadManager.Load(System.IO.Path.Combine(directoryPath, "UploadManager"));
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _uploadManager.Save(System.IO.Path.Combine(directoryPath, "UploadManager"));
                _connectionsManager.Save(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _cacheManager.Save(System.IO.Path.Combine(directoryPath, "CacheManager"));
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
                _downloadManager.Dispose();
                _uploadManager.Dispose();
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
