using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Library.Security;

namespace Library.Net.Outopos
{
    public delegate bool CheckUriEventHandler(object sender, string uri);

    public sealed class OutoposManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private string _bitmapPath;
        private string _cachePath;
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private BitmapManager _bitmapManager;
        private CacheManager _cacheManager;
        private ConnectionsManager _connectionsManager;
        private DownloadManager _downloadManager;
        private UploadManager _uploadManager;

        private ManagerState _state = ManagerState.Stop;

        private CreateCapEventHandler _createCapEvent;
        private AcceptCapEventHandler _acceptCapEvent;
        private CheckUriEventHandler _checkUriEvent;
        private GetSignaturesEventHandler _getLockSignaturesEvent;
        private GetWikisEventHandler _getLockWikisEvent;
        private GetChatsEventHandler _getLockChatsEvent;

        private volatile bool _isLoaded;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public OutoposManager(string bitmapPath, string cachePath, BufferManager bufferManager)
        {
            _bitmapPath = bitmapPath;
            _cachePath = cachePath;
            _bufferManager = bufferManager;

            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _bitmapManager = new BitmapManager(_bitmapPath, _bufferManager);
            _cacheManager = new CacheManager(_cachePath, _bitmapManager, _bufferManager);
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

            _clientManager.CheckUriEvent = (object sender, string uri) =>
            {
                if (_checkUriEvent != null)
                {
                    return _checkUriEvent(this, uri);
                }

                return true;
            };

            _serverManager.CheckUriEvent = (object sender, string uri) =>
            {
                if (_checkUriEvent != null)
                {
                    return _checkUriEvent(this, uri);
                }

                return true;
            };

            _connectionsManager.GetLockSignaturesEvent = (object sender) =>
            {
                if (_getLockSignaturesEvent != null)
                {
                    return _getLockSignaturesEvent(this);
                }

                return null;
            };

            _connectionsManager.GetLockWikisEvent = (object sender) =>
            {
                if (_getLockWikisEvent != null)
                {
                    return _getLockWikisEvent(this);
                }

                return null;
            };

            _connectionsManager.GetLockChatsEvent = (object sender) =>
            {
                if (_getLockChatsEvent != null)
                {
                    return _getLockChatsEvent(this);
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

        public CheckUriEventHandler CheckUriEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _checkUriEvent = value;
                }
            }
        }

        public GetSignaturesEventHandler GetLockSignaturesEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockSignaturesEvent = value;
                }
            }
        }

        public GetWikisEventHandler GetLockWikisEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockWikisEvent = value;
                }
            }
        }

        public GetChatsEventHandler GetLockChatsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockChatsEvent = value;
                }
            }
        }

        public Information Information
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

                lock (this.ThisLock)
                {
                    List<InformationContext> contexts = new List<InformationContext>();
                    contexts.AddRange(_serverManager.Information);
                    contexts.AddRange(_cacheManager.Information);
                    contexts.AddRange(_connectionsManager.Information);

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionInformation;
                }
            }
        }

        public IEnumerable<Information> UploadingInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_isLoaded) throw new OutoposManagerException("AmoebaManager is not loaded.");

                lock (this.ThisLock)
                {
                    return _uploadManager.UploadingInformation;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionCountLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

                lock (this.ThisLock)
                {
                    return _connectionsManager.BandWidthLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

                lock (this.ThisLock)
                {
                    return _cacheManager.Size;
                }
            }
        }

        public void SetBaseNode(Node baseNode)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            lock (this.ThisLock)
            {
                _connectionsManager.SetBaseNode(baseNode);
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            lock (this.ThisLock)
            {
                _connectionsManager.SetOtherNodes(nodes);
            }
        }

        public void Resize(long size)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            lock (this.ThisLock)
            {
                _cacheManager.Resize(size);
            }
        }

        public ProfileMetadata GetProfileMetadata(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetProfileMetadata(signature);
            }
        }

        public IEnumerable<SignatureMessageMetadata> GetSignatureMessageMetadatas(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetSignatureMessageMetadatas(signature);
            }
        }

        public IEnumerable<WikiDocumentMetadata> GetWikiDocumentMetadatas(Wiki tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetWikiDocumentMetadatas(tag);
            }
        }

        public IEnumerable<ChatTopicMetadata> GetChatTopicMetadatas(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChatTopicMetadatas(tag);
            }
        }

        public IEnumerable<ChatMessageMetadata> GetChatMessageMetadatas(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChatMessageMetadatas(tag);
            }
        }

        public Profile GetMessage(ProfileMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetMessage(metadata);
            }
        }

        public SignatureMessage GetMessage(SignatureMessageMetadata metadata, ExchangePrivateKey exchangePrivateKey)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetMessage(metadata, exchangePrivateKey);
            }
        }

        public WikiDocument GetMessage(WikiDocumentMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetMessage(metadata);
            }
        }

        public ChatTopic GetMessage(ChatTopicMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetMessage(metadata);
            }
        }

        public ChatMessage GetMessage(ChatMessageMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _downloadManager.GetMessage(metadata);
            }
        }

        public Profile UploadProfile(
            int cost,
            ExchangePublicKey exchangePublicKey,
            IEnumerable<string> trustSignatures,
            IEnumerable<string> deleteSignatures,
            IEnumerable<Wiki> wikis,
            IEnumerable<Chat> chats,

            DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _uploadManager.UploadProfile(cost, exchangePublicKey, trustSignatures, deleteSignatures, wikis, chats, digitalSignature);
            }
        }

        public SignatureMessage UploadSignatureMessage(string signature,
            string comment,

            ExchangePublicKey exchangePublicKey,
            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _uploadManager.UploadSignatureMessage(signature, comment, exchangePublicKey, miningLimit, miningTime, digitalSignature);
            }
        }

        public WikiDocument UploadWikiDocument(Wiki tag,
            IEnumerable<WikiPage> wikiPages,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _uploadManager.UploadWikiDocument(tag, wikiPages, miningLimit, miningTime, digitalSignature);
            }
        }

        public ChatTopic UploadChatTopic(Chat tag,
            HypertextFormatType formatType,
            string hypertext,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _uploadManager.UploadChatTopic(tag, formatType, hypertext, miningLimit, miningTime, digitalSignature);
            }
        }

        public ChatMessage UploadChatMessage(Chat tag,
            string comment,
            IEnumerable<Anchor> anchors,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _uploadManager.UploadChatMessage(tag, comment, anchors, miningLimit, miningTime, digitalSignature);
            }
        }

        public override ManagerState State
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

                lock (this.ThisLock)
                {
                    return _state;
                }
            }
        }

        public override void Start()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _uploadManager.Stop();
                _downloadManager.Stop();
                _connectionsManager.Stop();
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (_isLoaded) throw new OutoposManagerException("OutoposManager was already loaded.");
                _isLoaded = true;

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                _clientManager.Load(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _cacheManager.Load(System.IO.Path.Combine(directoryPath, "CacheManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionManager"));

                List<Task> tasks = new List<Task>();

                tasks.Add(Task.Factory.StartNew(() => _downloadManager.Load(System.IO.Path.Combine(directoryPath, "DownloadManager"))));
                tasks.Add(Task.Factory.StartNew(() => _uploadManager.Load(System.IO.Path.Combine(directoryPath, "UploadManager"))));

                Task.WaitAll(tasks.ToArray());

                stopwatch.Stop();
                Debug.WriteLine("Settings Load {0} {1}", Path.GetFileName(directoryPath), stopwatch.ElapsedMilliseconds);
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            lock (this.ThisLock)
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                List<Task> tasks = new List<Task>();

                tasks.Add(Task.Factory.StartNew(() => _uploadManager.Save(System.IO.Path.Combine(directoryPath, "UploadManager"))));
                tasks.Add(Task.Factory.StartNew(() => _downloadManager.Save(System.IO.Path.Combine(directoryPath, "DownloadManager"))));

                Task.WaitAll(tasks.ToArray());

                _connectionsManager.Save(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _cacheManager.Save(System.IO.Path.Combine(directoryPath, "CacheManager"));
                _serverManager.Save(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _clientManager.Save(System.IO.Path.Combine(directoryPath, "ClientManager"));

                stopwatch.Stop();
                Debug.WriteLine("Settings Save {0} {1}", Path.GetFileName(directoryPath), stopwatch.ElapsedMilliseconds);
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

    [Serializable]
    class OutoposManagerException : StateManagerException
    {
        public OutoposManagerException() : base() { }
        public OutoposManagerException(string message) : base(message) { }
        public OutoposManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
