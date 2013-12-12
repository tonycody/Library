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
        private DownloadManager _downloadManager;
        private UploadManager _uploadManager;

        private ManagerState _state = ManagerState.Stop;

        private GetCriteriaEventHandler _getLockCriteriaEvent;

        private CreateCapEventHandler _createCapEvent;
        private AcceptCapEventHandler _acceptCapEvent;

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

        public IEnumerable<SectionProfileHeader> GetSectionProfileHeaders(Section tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetSectionProfileHeaders(tag);
            }
        }

        public IEnumerable<SectionMessageHeader> GetSectionMessageHeaders(Section tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetSectionMessageHeaders(tag);
            }
        }

        public IEnumerable<ArchiveDocumentHeader> GetArchiveDocumentHeaders(Archive tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetArchiveDocumentHeaders(tag);
            }
        }

        public IEnumerable<ArchiveVoteHeader> GetArchiveVoteHeaders(Archive tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetArchiveVoteHeaders(tag);
            }
        }

        public IEnumerable<ChatTopicHeader> GetChatTopicHeaders(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChatTopicHeaders(tag);
            }
        }

        public IEnumerable<ChatMessageHeader> GetChatMessageHeaders(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChatMessageHeaders(tag);
            }
        }

        public SectionProfileContent GetContent(SectionProfileHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                return _downloadManager.Download(header);
            }
        }

        public SectionMessageContent GetContent(SectionMessageHeader header, ExchangePrivateKey exchangePrivateKey)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                return _downloadManager.Download(header, exchangePrivateKey);
            }
        }

        public ArchiveDocumentContent GetContent(ArchiveDocumentHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                return _downloadManager.Download(header);
            }
        }

        public ArchiveVoteContent GetContent(ArchiveVoteHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                return _downloadManager.Download(header);
            }
        }

        public ChatTopicContent GetContent(ChatTopicHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                return _downloadManager.Download(header);
            }
        }

        public ChatMessageContent GetContent(ChatMessageHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                return _downloadManager.Download(header);
            }
        }

        public void Upload(Section tag,
            SectionProfileContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, content, digitalSignature);
            }
        }

        public void Upload(Section tag,
            SectionMessageContent content,
            ExchangePublicKey exchangePublicKey,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, content, exchangePublicKey, digitalSignature);
            }
        }

        public void Upload(Archive tag,
            ArchiveDocumentContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, content, digitalSignature);
            }
        }

        public void Upload(Archive tag,
            ArchiveVoteContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, content, digitalSignature);
            }
        }

        public void Upload(Chat tag,
            ChatTopicContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, content, digitalSignature);
            }
        }

        public void Upload(Chat tag,
            ChatMessageContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, content, digitalSignature);
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
