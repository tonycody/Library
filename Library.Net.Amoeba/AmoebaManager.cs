using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;
using System.IO;

namespace Library.Net.Amoeba
{
    // 色々力技が必要になり個々のクラスが見苦しので、このクラスで覆う

    public class AmoebaManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
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
        }

        public Information Information
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.ConnectionInformation;
                }
            }
        }

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                    
                    return _downloadManager.DownloadingInformation;
                }
            }
        }

        public IEnumerable<Information> UploadingInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _uploadManager.UploadingInformation;
                }
            }
        }

        public IEnumerable<Information> ShareInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                    
                    return _cacheManager.ShareInformation;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.BaseNode;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _connectionsManager.BaseNode = value;
                }
            }
        }

        public IEnumerable<Node> OtherNodes
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.OtherNodes;
                }
            }
        }

        public IEnumerable<Seed> CacheSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _cacheManager.CacheSeeds;
                }
            }
        }

        public IEnumerable<Seed> ShareSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _cacheManager.ShareSeeds;
                }
            }
        }

        public SeedCollection DownloadedSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _downloadManager.DownloadedSeeds;
                }
            }
        }

        public SeedCollection UploadedSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _uploadManager.UploadedSeeds;
                }
            }
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _clientManager.Filters;
                }
            }
        }

        public UriCollection ListenUris
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _serverManager.ListenUris;
                }
            }
        }

        public string DownloadDirectory
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _downloadManager.BaseDirectory;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _downloadManager.BaseDirectory = value;
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.ConnectionCountLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _connectionsManager.ConnectionCountLimit = value;
                }
            }
        }

        public long BandWidthLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.BandWidthLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _connectionsManager.BandWidthLimit = value;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.ReceivedByteCount;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.SentByteCount;
                }
            }
        }

        public long Size
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _cacheManager.Size;
                }
            }
        }

        public void Download(Seed seed, int priority)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _downloadManager.Download(seed, priority);
            }
        }

        public void Download(Seed seed, string path, int priority)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _downloadManager.Remove(id);
            }
        }

        public void RemoveUpload(int id)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _uploadManager.Remove(id);
            }
        }

        public void RemoveShare(int id)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _cacheManager.RemoveShare(id);
            }
        }

        public void RemoveCacheSeed(Seed seed)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _cacheManager.RemoveCacheSeed(seed);
            }
        }
        
        public void RemoveShareSeed(Seed seed)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _cacheManager.RemoveShareSeed(seed);
            }
        }

        public void ResetDownload(int id)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _downloadManager.Reset(id);
            }
        }

        public void ResetUpload(int id)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _uploadManager.Reset(id);
            }
        }

        public void SetDownloadPriority(int id, int priority)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _downloadManager.SetPriority(id, priority);
            }
        }

        public void SetUploadPriority(int id, int priority)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _uploadManager.SetPriority(id, priority);
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.SetOtherNodes(nodes);
            }
        }

        public void Resize(long size)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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

            _cacheManager.CheckSeeds();
        }

        public void CheckBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _cacheManager.CheckBlocks(getProgressEvent);
        }

        public override ManagerState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _state;
                }
            }
        }

        public override void Start()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                this.Stop();

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
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                try
                {
                    this.Stop();
                }
                catch (Exception)
                {

                }

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
