using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Amoeba
{
    // 色々力技が必要になり個々のクラスが見苦しので、このクラスで覆う

    public class AmoebaManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private string _cachePath;
        private string _WorkDirectory;
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private CacheManager _cacheManager;
        private ConnectionsManager _connectionsManager;
        private DownloadManager _downloadManager;
        private UploadManager _uploadManager;

        public GetFilterSeedEventHandler GetFilterSeedEvent;

        private ManagerState _state = ManagerState.Stop;
        private bool _disposed = false;
        private object _thisLock = new object();

        public AmoebaManager(string cachePath, string WorkDirectory, BufferManager bufferManager)
        {
            _WorkDirectory = WorkDirectory;
            _cachePath = cachePath;

            _bufferManager = bufferManager;
            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _cacheManager = new CacheManager(_cachePath, _WorkDirectory, _bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _cacheManager, _bufferManager);
            _downloadManager = new DownloadManager(_connectionsManager, _cacheManager, _bufferManager);
            _uploadManager = new UploadManager(_connectionsManager, _cacheManager, _bufferManager);

            _connectionsManager.GetFilterSeedEvent = (object sender, Seed key) =>
            {
                return this.OnGetFilterSeedEvent(key);
            };
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _clientManager.Filters;
                }
            }
        }

        public UriCollection ListenUris
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _serverManager.ListenUris;
                }
            }
        }

        public string DownloadDirectory
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _downloadManager.BaseDirectory;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _downloadManager.BaseDirectory = value;
                }
            }
        }

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _downloadManager.DownloadingInformation;
                }
            }
        }

        public SeedCollection DownloadedSeeds
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _downloadManager.DownloadedSeeds;
                }
            }
        }

        public IEnumerable<Information> ShareInformation
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _cacheManager.ShareInformation;
                }
            }
        }

        public IEnumerable<Information> UploadingInformation
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _uploadManager.UploadingInformation;
                }
            }
        }

        public SeedCollection UploadedSeeds
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _uploadManager.UploadedSeeds;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.BaseNode;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
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

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.OtherNodes;
                }
            }
        }

        public IEnumerable<Seed> Seeds
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.Seeds;
                }
            }
        }

        public KeywordCollection SearchKeywords
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.SearchKeywords;
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.ConnectionCountLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _connectionsManager.ConnectionCountLimit = value;
                }
            }
        }

        public int DownloadingConnectionCountLowerLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.DownloadingConnectionCountLowerLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _connectionsManager.DownloadingConnectionCountLowerLimit = value;
                }
            }
        }

        public int UploadingConnectionCountLowerLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.UploadingConnectionCountLowerLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _connectionsManager.UploadingConnectionCountLowerLimit = value;
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.ConnectionInformation;
                }
            }
        }

        public Information Information
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.Information;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
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

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionsManager.SentByteCount;
                }
            }
        }

        public long Size
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _cacheManager.Size;
                }
            }
        }

        protected virtual bool OnGetFilterSeedEvent(Seed seed)
        {
            if (GetFilterSeedEvent != null)
            {
                return GetFilterSeedEvent(this, seed);
            }

            return true;
        }

        public void Share(string filePath,
            string name,
            KeywordCollection keywords,
            string comment,
            DigitalSignature digitalSignature)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _uploadManager.Share(filePath,
                    name,
                    keywords,
                    comment, CompressionAlgorithm.XZ,
                    CryptoAlgorithm.Rijndael256,
                    CorrectionAlgorithm.ReedSolomon8,
                    HashAlgorithm.Sha512,
                    digitalSignature);
            }
        }

        public void Upload(string filePath,
            string name,
            KeywordCollection keywords,
            string comment,
            DigitalSignature digitalSignature)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _uploadManager.Upload(filePath,
                    name,
                    keywords,
                    comment, CompressionAlgorithm.XZ,
                    CryptoAlgorithm.Rijndael256,
                    CorrectionAlgorithm.ReedSolomon8,
                    HashAlgorithm.Sha512,
                    digitalSignature);
            }
        }

        public void ShareRemove(int id)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _cacheManager.ShareRemove(id);
            }
        }

        public void UploadRemove(int id)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _uploadManager.Remove(id);
            }
        }

        public void SetUploadPriority(int id, int priority)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _uploadManager.SetPriority(id, priority);
            }
        }

        public void Download(Seed seed)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _downloadManager.Download(seed);
            }
        }

        public void Download(Seed seed, string path)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _downloadManager.Download(seed, path);
            }
        }

        public void DownloadRemove(int id)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _downloadManager.Remove(id);
            }
        }

        public void SetDownloadPriority(int id, int priority)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _downloadManager.SetPriority(id, priority);
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _connectionsManager.SetOtherNodes(nodes);
            }
        }

        public void Upload(Seed seed)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _connectionsManager.Upload(seed);
            }
        }

        public void Resize(long size)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _cacheManager.Resize(size);
            }
        }

        public void CheckBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            _cacheManager.CheckBlocks(getProgressEvent);
        }

        public override ManagerState State
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _state;
                }
            }
        }

        public override void Start()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _serverManager.Start();
                _connectionsManager.Start();
                _downloadManager.Start();
                _uploadManager.Start();
            }
        }

        public override void Stop()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _uploadManager.Stop();
                _downloadManager.Stop();
                _connectionsManager.Stop();
                _serverManager.Stop();
            }
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _clientManager.Load(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _cacheManager.Load(System.IO.Path.Combine(directoryPath, "CacheManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _downloadManager.Load(System.IO.Path.Combine(directoryPath, "DownloadManager"));
                _uploadManager.Load(System.IO.Path.Combine(directoryPath, "UploadManager"));
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _clientManager.Save(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Save(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _cacheManager.Save(System.IO.Path.Combine(directoryPath, "CacheManager"));
                _connectionsManager.Save(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _downloadManager.Save(System.IO.Path.Combine(directoryPath, "DownloadManager"));
                _uploadManager.Save(System.IO.Path.Combine(directoryPath, "UploadManager"));
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {

                }

                _disposed = true;
            }
        }

        #region IThisLock メンバ

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
