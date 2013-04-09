using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Amoeba
{
    // 色々力技が必要になり個々のクラスが見苦しので、このクラスで覆う

    public sealed class AmoebaManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private string _cachePath;
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private CacheManager _cacheManager;
        private ConnectionsManager _connectionsManager;
        private DownloadManager _downloadManager;
        private UploadManager _uploadManager;
        private StoreDownloadManager _storeDownloadManager;
        private StoreUploadManager _storeUploadManager;

        private ManagerState _state = ManagerState.Stop;

        private RemoveSeedSignaturesEventHandler _removeSeedSignaturesEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public AmoebaManager(string cachePath, BufferManager bufferManager)
        {
            _cachePath = cachePath;

            _bufferManager = bufferManager;
            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _cacheManager = new CacheManager(_cachePath, _bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _cacheManager, _bufferManager);
            _downloadManager = new DownloadManager(_connectionsManager, _cacheManager, _bufferManager);
            _uploadManager = new UploadManager(_connectionsManager, _cacheManager, _bufferManager);
            _storeDownloadManager = new StoreDownloadManager(_connectionsManager, _cacheManager, _bufferManager);
            _storeUploadManager = new StoreUploadManager(_connectionsManager, _cacheManager, _bufferManager);

            _connectionsManager.RemoveSeedSignaturesEvent = (object sender) =>
            {
                if (_removeSeedSignaturesEvent != null)
                {
                    return _removeSeedSignaturesEvent(this);
                }

                return null;
            };
        }

        public RemoveSeedSignaturesEventHandler RemoveSeedSignaturesEvent
        {
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _removeSeedSignaturesEvent = value;
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
                    contexts.AddRange(_uploadManager.Information);
                    contexts.AddRange(_downloadManager.Information);

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

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _downloadManager.DownloadingInformation;
                }
            }
        }

        public IEnumerable<Information> UploadingInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _uploadManager.UploadingInformation;
                }
            }
        }

        public IEnumerable<Information> ShareInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _cacheManager.ShareInformation;
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

        public SignatureCollection Signatures
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _storeDownloadManager.Signatures;
                }
            }
        }

        public IEnumerable<Seed> CacheSeeds
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _cacheManager.CacheSeeds;
                }
            }
        }

        public IEnumerable<Seed> ShareSeeds
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _cacheManager.ShareSeeds;
                }
            }
        }

        public SeedCollection DownloadedSeeds
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _downloadManager.DownloadedSeeds;
                }
            }
        }

        public SeedCollection UploadedSeeds
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _uploadManager.UploadedSeeds;
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

        public string DownloadDirectory
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _downloadManager.BaseDirectory;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _downloadManager.BaseDirectory = value;
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

        public long BandWidthLimit
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

        public void Upload(Store store,
            DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _storeUploadManager.Upload(store,
                    CompressionAlgorithm.Lzma,
                    CryptoAlgorithm.Rijndael256,
                    CorrectionAlgorithm.ReedSolomon8,
                    HashAlgorithm.Sha512,
                    digitalSignature);
            }
        }

        public Store GetStore(string signature)
        {
            lock (this.ThisLock)
            {
                return _storeDownloadManager.GetStore(signature);
            }
        }

        public void ResetStore(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _storeDownloadManager.Reset(signature);
            }
        }

        public void Download(Seed seed, int priority)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _downloadManager.Download(seed, priority);
            }
        }

        public void Download(Seed seed, string path, int priority)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _downloadManager.Download(seed, path, priority);
            }
        }

        public void Upload(string filePath,
            string name,
            KeywordCollection keywords,
            string comment,
            DigitalSignature digitalSignature,
            int priority)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _uploadManager.Upload(filePath,
                    name,
                    keywords,
                    comment,
                    CompressionAlgorithm.Lzma,
                    CryptoAlgorithm.Rijndael256,
                    CorrectionAlgorithm.ReedSolomon8,
                    HashAlgorithm.Sha512,
                    digitalSignature,
                    priority);
            }
        }

        public void Share(string filePath,
            string name,
            KeywordCollection keywords,
            string comment,
            DigitalSignature digitalSignature,
            int priority)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _uploadManager.Share(filePath,
                    name,
                    keywords,
                    comment,
                    CompressionAlgorithm.Lzma,
                    CryptoAlgorithm.Rijndael256,
                    CorrectionAlgorithm.ReedSolomon8,
                    HashAlgorithm.Sha512,
                    digitalSignature,
                    priority);
            }
        }

        public void RemoveDownload(int id)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _downloadManager.Remove(id);
            }
        }

        public void RemoveUpload(int id)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _uploadManager.Remove(id);
            }
        }

        public void RemoveShare(int id)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _cacheManager.RemoveShare(id);
            }
        }

        public void RemoveCacheSeed(Seed seed)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _cacheManager.RemoveCacheSeed(seed);
            }
        }

        public void RemoveShareSeed(Seed seed)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _cacheManager.RemoveShareSeed(seed);
            }
        }

        public void ResetDownload(int id)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _downloadManager.Reset(id);
            }
        }

        public void ResetUpload(int id)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _uploadManager.Reset(id);
            }
        }

        public void SetDownloadPriority(int id, int priority)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _downloadManager.SetPriority(id, priority);
            }
        }

        public void SetUploadPriority(int id, int priority)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _uploadManager.SetPriority(id, priority);
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
                if (this.State == ManagerState.Start)
                {
                    _uploadManager.Stop();
                    _downloadManager.Stop();
                }

                _cacheManager.Resize(size);

                if (this.State == ManagerState.Start)
                {
                    _uploadManager.Start();
                    _downloadManager.Start();
                }
            }
        }

        public void CheckSeeds()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _cacheManager.CheckSeeds();
            }
        }

        public void CheckInternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _cacheManager.CheckInternalBlocks(getProgressEvent);
        }

        public void CheckExternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _cacheManager.CheckExternalBlocks(getProgressEvent);
        }

        public override ManagerState State
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _state;
                }
            }
        }

        public override void Start()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _connectionsManager.Start();
                _downloadManager.Start();
                _uploadManager.Start();
                _storeDownloadManager.Start();
                _storeUploadManager.Start();
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _storeUploadManager.Stop();
                _storeDownloadManager.Stop();
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
                this.Stop();

                _clientManager.Load(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _cacheManager.Load(System.IO.Path.Combine(directoryPath, "CacheManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _downloadManager.Load(System.IO.Path.Combine(directoryPath, "DownloadManager"));
                _uploadManager.Load(System.IO.Path.Combine(directoryPath, "UploadManager"));
                _storeDownloadManager.Load(System.IO.Path.Combine(directoryPath, "StoreDownloadManager"));
                _storeUploadManager.Load(System.IO.Path.Combine(directoryPath, "StoreUploadManager"));
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _storeUploadManager.Save(System.IO.Path.Combine(directoryPath, "StoreUploadManager"));
                _storeDownloadManager.Save(System.IO.Path.Combine(directoryPath, "StoreDownloadManager"));
                _uploadManager.Save(System.IO.Path.Combine(directoryPath, "UploadManager"));
                _downloadManager.Save(System.IO.Path.Combine(directoryPath, "DownloadManager"));
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

            if (disposing)
            {
                _storeUploadManager.Dispose();
                _storeDownloadManager.Dispose();
                _uploadManager.Dispose();
                _downloadManager.Dispose();
                _connectionsManager.Dispose();
                _cacheManager.Dispose();
                _serverManager.Dispose();
                _clientManager.Dispose();
            }

            _disposed = true;
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
