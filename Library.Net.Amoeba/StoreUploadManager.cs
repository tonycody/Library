using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    class StoreUploadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private volatile Thread _uploadManagerThread = null;

        private ManagerState _state = ManagerState.Stop;

        private Thread _uploadedThread;

        private WaitQueue<Key> _uploadedKeys = new WaitQueue<Key>();

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public StoreUploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings();

            _connectionsManager.UploadedEvent += (object sender, IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _uploadedKeys.Enqueue(key);
                }
            };

            _cacheManager.RemoveKeyEvent += (object sender, IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _uploadedKeys.Enqueue(key);
                }
            };

            _uploadedThread = new Thread(new ThreadStart(() =>
            {
                try
                {
                    for (; ; )
                    {
                        var key = _uploadedKeys.Dequeue();

                        lock (this.ThisLock)
                        {
                            foreach (var item in _settings.StoreUploadItems.ToArray())
                            {
                                if (item.UploadKeys.Remove(key))
                                {
                                    item.UploadedKeys.Add(key);

                                    if (item.State == StoreUploadState.Uploading)
                                    {
                                        if (item.UploadKeys.Count == 0)
                                        {
                                            item.State = StoreUploadState.Completed;

                                            _connectionsManager.Upload(item.Seed);

                                            this.Remove(item);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            }));
            _uploadedThread.Priority = ThreadPriority.BelowNormal;
            _uploadedThread.Name = "StoreUploadManager_UploadedThread";
            _uploadedThread.Start();
        }

        private void SetKeyCount(StoreUploadItem item)
        {
            lock (this.ThisLock)
            {
                foreach (var key in item.UploadKeys.ToArray())
                {
                    if (!_connectionsManager.UploadWaiting(key))
                    {
                        item.UploadedKeys.Add(key);
                        item.UploadKeys.Remove(key);

                        if (item.State == StoreUploadState.Uploading)
                        {
                            if (item.UploadKeys.Count == 0)
                            {
                                item.State = StoreUploadState.Completed;

                                _connectionsManager.Upload(item.Seed);

                                this.Remove(item);
                            }
                        }
                    }
                }
            }
        }

        private void UploadManagerThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                StoreUploadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        lock (_settings.ThisLock)
                        {
                            if (_settings.StoreUploadItems.Count > 0)
                            {
                                item = _settings.StoreUploadItems
                                    .Where(n => n.State == StoreUploadState.Encoding)
                                    .FirstOrDefault();
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    return;
                }

                try
                {
                    if (item != null)
                    {
                        if (item.Groups.Count == 0 && item.Keys.Count == 0)
                        {
                            KeyCollection keys = null;
                            byte[] cryptoKey;

                            try
                            {
                                using (Stream stream = item.Store.Export(_bufferManager))
                                using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.State == ManagerState.Stop || !_settings.StoreUploadItems.Contains(item));
                                }, 1024 * 1024, true))
                                {
                                    item.Seed.Length = stream.Length;

                                    if (item.Seed.Length == 0) throw new InvalidOperationException("Stream Length");

                                    cryptoKey = Sha512.ComputeHash(encodingProgressStream);
                                    //cryptoKey = new byte[64];

                                    encodingProgressStream.Seek(0, SeekOrigin.Begin);

                                    item.State = StoreUploadState.Encoding;
                                    keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.HashAlgorithm, item.BlockLength);
                                }
                            }
                            catch (StopIOException)
                            {
                                continue;
                            }

                            lock (this.ThisLock)
                            {
                                foreach (var key in keys)
                                {
                                    item.UploadKeys.Add(key);
                                    item.LockedKeys.Add(key);
                                }

                                item.CryptoKey = cryptoKey;
                                item.Keys.AddRange(keys);
                            }
                        }
                        else if (item.Groups.Count == 0 && item.Keys.Count == 1)
                        {
                            lock (this.ThisLock)
                            {
                                item.Seed.Rank = item.Rank;
                                item.Seed.Key = item.Keys[0];
                                item.Keys.Clear();

                                item.Seed.CompressionAlgorithm = item.CompressionAlgorithm;

                                item.Seed.CryptoAlgorithm = item.CryptoAlgorithm;
                                item.Seed.CryptoKey = item.CryptoKey;

                                if (item.DigitalSignature != null)
                                {
                                    item.Seed.CreateCertificate(item.DigitalSignature);
                                }

                                item.UploadKeys.Add(item.Seed.Key);

                                foreach (var header in item.UploadKeys)
                                {
                                    _connectionsManager.Upload(header);
                                }

                                this.SetKeyCount(item);

                                foreach (var key in item.LockedKeys)
                                {
                                    _cacheManager.Unlock(key);
                                }

                                item.LockedKeys.Clear();

                                item.State = StoreUploadState.Uploading;
                            }
                        }
                        else if (item.Keys.Count > 0)
                        {
                            var length = Math.Min(item.Keys.Count, 128);
                            var keys = new KeyCollection(item.Keys.Take(length));
                            Group group = null;

                            try
                            {
                                group = _cacheManager.ParityEncoding(keys, item.HashAlgorithm, item.BlockLength, item.CorrectionAlgorithm, (object state2) =>
                                {
                                    return (this.State == ManagerState.Stop || !_settings.StoreUploadItems.Contains(item));
                                });
                            }
                            catch (StopException)
                            {
                                continue;
                            }

                            lock (this.ThisLock)
                            {
                                foreach (var key in group.Keys.Skip(group.InformationLength))
                                {
                                    item.UploadKeys.Add(key);
                                    item.LockedKeys.Add(key);
                                }

                                item.Groups.Add(group);

                                for (int i = 0; i < length; i++)
                                {
                                    item.Keys.RemoveAt(0);
                                }
                            }
                        }
                        else if (item.Groups.Count > 0 && item.Keys.Count == 0)
                        {
                            var index = new Index();
                            index.Groups.AddRange(item.Groups);
                            index.CompressionAlgorithm = item.CompressionAlgorithm;
                            index.CryptoAlgorithm = item.CryptoAlgorithm;
                            index.CryptoKey = item.CryptoKey;

                            byte[] cryptoKey;
                            KeyCollection keys = null;

                            try
                            {
                                using (var stream = index.Export(_bufferManager))
                                using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.State == ManagerState.Stop || !_settings.StoreUploadItems.Contains(item));
                                }, 1024 * 1024, true))
                                {
                                    cryptoKey = Sha512.ComputeHash(encodingProgressStream);

                                    encodingProgressStream.Seek(0, SeekOrigin.Begin);

                                    item.State = StoreUploadState.Encoding;
                                    keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.HashAlgorithm, item.BlockLength);
                                }
                            }
                            catch (StopIOException)
                            {
                                continue;
                            }

                            lock (this.ThisLock)
                            {
                                foreach (var key in keys)
                                {
                                    item.UploadKeys.Add(key);
                                    item.LockedKeys.Add(key);
                                }

                                item.CryptoKey = cryptoKey;
                                item.Keys.AddRange(keys);
                                item.Rank++;
                                item.Groups.Clear();
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    item.State = StoreUploadState.Error;

                    Log.Error(e);

                    this.Remove(item);
                }
            }
        }

        public void Upload(Store store,
            CompressionAlgorithm compressionAlgorithm,
            CryptoAlgorithm cryptoAlgorithm,
            CorrectionAlgorithm correctionAlgorithm,
            HashAlgorithm hashAlgorithm,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                {
                    foreach (var item in _settings.StoreUploadItems.ToArray())
                    {
                        if (item.DigitalSignature.ToString() != digitalSignature.ToString()) continue;

                        this.Remove(item);
                    }
                }

                {
                    StoreUploadItem item = new StoreUploadItem();

                    item.Store = store;
                    item.State = StoreUploadState.Encoding;
                    item.Rank = 1;
                    item.CompressionAlgorithm = compressionAlgorithm;
                    item.CryptoAlgorithm = cryptoAlgorithm;
                    item.CorrectionAlgorithm = correctionAlgorithm;
                    item.HashAlgorithm = hashAlgorithm;
                    item.DigitalSignature = digitalSignature;
                    item.Seed = new Seed();
                    item.Seed.Keywords.Add(ConnectionsManager.Keyword_Store);
                    item.Seed.CreationTime = DateTime.UtcNow;
                    item.BlockLength = 1024 * 1024 * 1;

                    _settings.StoreUploadItems.Add(item);
                }
            }
        }

        private void Remove(StoreUploadItem item)
        {
            lock (this.ThisLock)
            {
                foreach (var key in item.LockedKeys)
                {
                    _cacheManager.Unlock(key);
                }

                _settings.StoreUploadItems.Remove(item);
            }
        }

        private void Reset(StoreUploadItem item)
        {
            lock (this.ThisLock)
            {
                this.Remove(item);

                this.Upload(item.Store,
                    item.CompressionAlgorithm,
                    item.CryptoAlgorithm,
                    item.CorrectionAlgorithm,
                    item.HashAlgorithm,
                    item.DigitalSignature);
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
            while (_uploadManagerThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _uploadManagerThread = new Thread(this.UploadManagerThread);
                _uploadManagerThread.Priority = ThreadPriority.Lowest;
                _uploadManagerThread.Name = "StoreUploadManager_UploadManagerThread";
                _uploadManagerThread.Start();
            }
        }

        public override void Stop()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;
            }

            _uploadManagerThread.Join();
            _uploadManagerThread = null;
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.StoreUploadItems.ToArray())
                {
                    try
                    {
                        this.SetKeyCount(item);
                    }
                    catch (Exception)
                    {
                        _settings.StoreUploadItems.Remove(item);
                    }
                }

                foreach (var item in _settings.StoreUploadItems)
                {
                    foreach (var key in item.LockedKeys)
                    {
                        _cacheManager.Lock(key);
                    }
                }
            }
        }

        public void Save(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Save(directoryPath);
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase, IThisLock
        {
            private object _thisLock = new object();

            public Settings()
                : base(new List<Library.Configuration.ISettingsContext>() { 
                    new Library.Configuration.SettingsContext<LockedList<StoreUploadItem>>() { Name = "StoreUploadItems", Value = new LockedList<StoreUploadItem>() },
                })
            {

            }

            public override void Load(string directoryPath)
            {
                lock (this.ThisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (this.ThisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public LockedList<StoreUploadItem> StoreUploadItems
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedList<StoreUploadItem>)this["StoreUploadItems"];
                    }
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

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _uploadedKeys.Dispose();

                _uploadedThread.Join();
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
