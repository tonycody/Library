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

        public SectionManager GetSectionManager(Tag tag, string leaderSignature)
        {
            return new SectionManager(tag, leaderSignature, this, _bufferManager);
        }

        internal IEnumerable<Header> GetHeaders(Link link)
        {
            lock (_thisLock)
            {
                return _connectionsManager.GetHeaders(link);
            }
        }

        internal ArraySegment<byte>? Download(Header header)
        {
            lock (this.ThisLock)
            {
                return _downloadManager.Download(header);
            }
        }

        public void Upload(Tag tag,
            string path,
            SectionProfileContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, path, content, digitalSignature);
            }
        }

        public void Upload(Tag tag,
            string path,
            SectionMessageContent content,

            ExchangePublicKey exchangePublicKey,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, path, content, exchangePublicKey, digitalSignature);
            }
        }

        public void Upload(Tag tag,
            string path,
            DocumentPageContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, path, content, digitalSignature);
            }
        }

        public void Upload(Tag tag,
            string path,
            DocumentVoteContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, path, content, digitalSignature);
            }
        }

        public void Upload(Tag tag,
            string path,
            ChatTopicContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, path, content, digitalSignature);
            }
        }

        public void Upload(Tag tag,
            string path,
            ChatMessageContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                _uploadManager.Upload(tag, path, content, digitalSignature);
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
