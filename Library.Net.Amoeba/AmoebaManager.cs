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
        private BackgroundDownloadManager _backgroundDownloadManager;
        private BackgroundUploadManager _backgroundUploadManager;

        private ManagerState _state = ManagerState.Stop;
        private ManagerState _encodeState = ManagerState.Stop;
        private ManagerState _decodeState = ManagerState.Stop;

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
            _backgroundDownloadManager = new BackgroundDownloadManager(_connectionsManager, _cacheManager, _bufferManager);
            _backgroundUploadManager = new BackgroundUploadManager(_connectionsManager, _cacheManager, _bufferManager);
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

        public IEnumerable<string> SearchSignatures
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _backgroundDownloadManager.SearchSignatures;
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

        public void SetBaseNode(Node baseNode)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SetBaseNode(baseNode);
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
                    _downloadManager.Stop();
                    _uploadManager.Stop();
                    _backgroundDownloadManager.Stop();
                    _backgroundUploadManager.Stop();
                }

                if (this.DecodeState == ManagerState.Start)
                {
                    _downloadManager.DecodeStop();
                }

                if (this.EncodeState == ManagerState.Start)
                {
                    _uploadManager.EncodeStop();
                }

                _cacheManager.Resize(size);

                if (this.State == ManagerState.Start)
                {
                    _downloadManager.Start();
                    _uploadManager.Start();
                    _backgroundDownloadManager.Start();
                    _backgroundUploadManager.Start();
                }

                if (this.DecodeState == ManagerState.Start)
                {
                    _downloadManager.DecodeStart();
                }

                if (this.EncodeState == ManagerState.Start)
                {
                    _uploadManager.EncodeStart();
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

        public Link GetLink(string signature)
        {
            lock (this.ThisLock)
            {
                return _backgroundDownloadManager.GetLink(signature);
            }
        }

        public Store GetStore(string signature)
        {
            lock (this.ThisLock)
            {
                return _backgroundDownloadManager.GetStore(signature);
            }
        }

        public void SetSearchSignatures(IEnumerable<string> signatures)
        {
            _backgroundDownloadManager.SetSearchSignatures(signatures);
        }

        public void Upload(Link link,
            DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _backgroundUploadManager.Upload(link,
                    CompressionAlgorithm.Lzma,
                    CryptoAlgorithm.Rijndael256,
                    CorrectionAlgorithm.ReedSolomon8,
                    HashAlgorithm.Sha512,
                    digitalSignature);
            }
        }

        public void Upload(Store store,
            DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _backgroundUploadManager.Upload(store,
                    CompressionAlgorithm.Lzma,
                    CryptoAlgorithm.Rijndael256,
                    CorrectionAlgorithm.ReedSolomon8,
                    HashAlgorithm.Sha512,
                    digitalSignature);
            }
        }

        public void ResetLink(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _backgroundDownloadManager.ResetLink(signature);
            }
        }

        public void ResetStore(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _backgroundDownloadManager.ResetStore(signature);
            }
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

        public ManagerState EncodeState
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _encodeState;
                }
            }
        }

        public ManagerState DecodeState
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _decodeState;
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
                _backgroundDownloadManager.Start();
                _backgroundUploadManager.Start();
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _backgroundUploadManager.Stop();
                _backgroundDownloadManager.Stop();
                _uploadManager.Stop();
                _downloadManager.Stop();
                _connectionsManager.Stop();
            }
        }

        public void EncodeStart()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.EncodeState == ManagerState.Start) return;
                _encodeState = ManagerState.Start;

                _uploadManager.EncodeStart();
            }
        }

        public void EncodeStop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.EncodeState == ManagerState.Stop) return;
                _encodeState = ManagerState.Stop;

                _uploadManager.EncodeStop();
            }
        }

        public void DecodeStart()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.DecodeState == ManagerState.Start) return;
                _decodeState = ManagerState.Start;

                _downloadManager.DecodeStart();
            }
        }

        public void DecodeStop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.DecodeState == ManagerState.Stop) return;
                _decodeState = ManagerState.Stop;

                _downloadManager.DecodeStop();
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
                _backgroundDownloadManager.Load(System.IO.Path.Combine(directoryPath, "BackgroundDownloadManager"));
                _backgroundUploadManager.Load(System.IO.Path.Combine(directoryPath, "BackgroundUploadManager"));
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _backgroundUploadManager.Save(System.IO.Path.Combine(directoryPath, "BackgroundUploadManager"));
                _backgroundDownloadManager.Save(System.IO.Path.Combine(directoryPath, "BackgroundDownloadManager"));
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
            _disposed = true;

            if (disposing)
            {
                _backgroundDownloadManager.Dispose();
                _backgroundUploadManager.Dispose();
                _downloadManager.Dispose();
                _uploadManager.Dispose();
                _connectionsManager.Dispose();
                _cacheManager.Dispose();
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
