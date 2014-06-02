using System;
using System.Collections.Generic;
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

        private volatile ManagerState _state = ManagerState.Stop;

        private CreateCapEventHandler _createCapEvent;
        private AcceptCapEventHandler _acceptCapEvent;
        private CheckUriEventHandler _checkUriEvent;
        private GetTagsEventHandler _getLockTagsEvent;
        private GetSignaturesEventHandler _getLockSignaturesEvent;

        private bool _isLoaded = false;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

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

            _connectionsManager.GetLockTagsEvent = (object sender) =>
            {
                if (_getLockTagsEvent != null)
                {
                    return _getLockTagsEvent(sender);
                }

                return null;
            };

            _connectionsManager.GetLockSignaturesEvent = (object sender, Tag tag) =>
            {
                if (_getLockSignaturesEvent != null)
                {
                    return _getLockSignaturesEvent(sender, tag);
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

        public GetTagsEventHandler GetLockTagsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockTagsEvent = value;
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
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

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

        public void CheckBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            _cacheManager.CheckBlocks(getProgressEvent);
        }

        public IEnumerable<Header> GetHeaders(Tag tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            return _connectionsManager.GetHeaders(tag);
        }

        public Stream GetContent(Header header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            return _downloadManager.GetContent(header);
        }

        public Header Upload(Tag tag, Stream stream, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            return _uploadManager.Upload(tag, stream, digitalSignature);
        }

        public override ManagerState State
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

                return _state;
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

                _clientManager.Load(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _bitmapManager.Load(System.IO.Path.Combine(directoryPath, "BitmapManager"));
                _cacheManager.Load(System.IO.Path.Combine(directoryPath, "CacheManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionsManager"));

                _cacheManager.CheckInformation();
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new OutoposManagerException("OutoposManager is not loaded.");

            lock (this.ThisLock)
            {
                _connectionsManager.Save(System.IO.Path.Combine(directoryPath, "ConnectionsManager"));
                _cacheManager.Save(System.IO.Path.Combine(directoryPath, "CacheManager"));
                _bitmapManager.Save(System.IO.Path.Combine(directoryPath, "BitmapManager"));
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
                _uploadManager.Dispose();
                _downloadManager.Dispose();
                _connectionsManager.Dispose();
                _cacheManager.Dispose();
                _bitmapManager.Dispose();
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
