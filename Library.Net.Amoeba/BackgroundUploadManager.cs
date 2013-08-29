using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;
using System.Diagnostics;

namespace Library.Net.Amoeba
{
    class BackgroundUploadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private volatile Thread _uploadManagerThread = null;
        private volatile Thread _watchThread = null;

        private ManagerState _state = ManagerState.Stop;

        private Thread _uploadedThread;

        private WaitQueue<Key> _uploadedKeys = new WaitQueue<Key>();

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public BackgroundUploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
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
                            foreach (var item in _settings.BackgroundUploadItems.ToArray())
                            {
                                if (item.UploadKeys.Remove(key))
                                {
                                    item.UploadedKeys.Add(key);

                                    if (item.State == BackgroundUploadState.Uploading)
                                    {
                                        if (item.UploadKeys.Count == 0)
                                        {
                                            item.State = BackgroundUploadState.Completed;

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
            _uploadedThread.Name = "BackgroundUploadManager_UploadedThread";
            _uploadedThread.Start();
        }

        private void SetKeyCount(BackgroundUploadItem item)
        {
            lock (this.ThisLock)
            {
                foreach (var key in item.UploadKeys.ToArray())
                {
                    if (!_connectionsManager.IsUploadWaiting(key))
                    {
                        item.UploadedKeys.Add(key);
                        item.UploadKeys.Remove(key);
                    }
                }

                if (item.State == BackgroundUploadState.Uploading)
                {
                    if (item.UploadKeys.Count == 0)
                    {
                        item.State = BackgroundUploadState.Completed;

                        _connectionsManager.Upload(item.Seed);
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

                BackgroundUploadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        if (_settings.BackgroundUploadItems.Count > 0)
                        {
                            item = _settings.BackgroundUploadItems
                                .Where(n => n.State == BackgroundUploadState.Encoding)
                                .FirstOrDefault();
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
                            Stream stream = null;

                            try
                            {
                                if (item.Type == BackgroundItemType.Link)
                                {
                                    stream = ((Link)item.Value).Export(_bufferManager);
                                }
                                else if (item.Type == BackgroundItemType.Store)
                                {
                                    stream = ((Store)item.Value).Export(_bufferManager);
                                }
                                else
                                {
                                    throw new FormatException();
                                }

                                if (stream.Length == 0)
                                {
                                    lock (this.ThisLock)
                                    {
                                        item.Seed.Rank = 0;

                                        if (item.DigitalSignature != null)
                                        {
                                            item.Seed.CreateCertificate(item.DigitalSignature);
                                        }

                                        _connectionsManager.Upload(item.Seed);

                                        item.State = BackgroundUploadState.Completed;
                                    }
                                }
                                else
                                {
                                    KeyCollection keys = null;
                                    byte[] cryptoKey = null;

                                    try
                                    {
                                        using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.BackgroundUploadItems.Contains(item));
                                        }, 1024 * 1024, true))
                                        {
                                            item.Seed.Length = stream.Length;

                                            if (item.Seed.Length == 0) throw new InvalidOperationException("Stream Length");

                                            if (item.HashAlgorithm == HashAlgorithm.Sha512)
                                            {
                                                cryptoKey = Sha512.ComputeHash(encodingProgressStream);
                                            }

                                            encodingProgressStream.Seek(0, SeekOrigin.Begin);

                                            item.State = BackgroundUploadState.Encoding;
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
                            }
                            finally
                            {
                                if (stream != null) stream.Dispose();
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

                                item.State = BackgroundUploadState.Uploading;
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
                                    return (this.State == ManagerState.Stop || !_settings.BackgroundUploadItems.Contains(item));
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

                            byte[] cryptoKey = null;
                            KeyCollection keys = null;

                            try
                            {
                                using (var stream = index.Export(_bufferManager))
                                using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.State == ManagerState.Stop || !_settings.BackgroundUploadItems.Contains(item));
                                }, 1024 * 1024, true))
                                {
                                    if (item.HashAlgorithm == HashAlgorithm.Sha512)
                                    {
                                        cryptoKey = Sha512.ComputeHash(encodingProgressStream);
                                    }

                                    encodingProgressStream.Seek(0, SeekOrigin.Begin);

                                    item.State = BackgroundUploadState.Encoding;
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
                    item.State = BackgroundUploadState.Error;

                    Log.Error(e);

                    this.Remove(item);
                }
            }
        }

        private void WatchThread()
        {
            Stopwatch watchStopwatch = new Stopwatch();

            try
            {
                for (; ; )
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;

                    if (!watchStopwatch.IsRunning || watchStopwatch.Elapsed.TotalMinutes >= 10)
                    {
                        lock (this.ThisLock)
                        {
                            var now = DateTime.UtcNow;

                            foreach (var item in _settings.BackgroundUploadItems.ToArray())
                            {
                                if (item.State == BackgroundUploadState.Completed
                                    && (now - item.Seed.CreationTime) > new TimeSpan(3, 0, 0, 0))
                                {
                                    this.Remove(item);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public void Upload(Link link,
            CompressionAlgorithm compressionAlgorithm,
            CryptoAlgorithm cryptoAlgorithm,
            CorrectionAlgorithm correctionAlgorithm,
            HashAlgorithm hashAlgorithm,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                {
                    foreach (var item in _settings.BackgroundUploadItems.ToArray())
                    {
                        if (item.DigitalSignature.ToString() != digitalSignature.ToString()) continue;

                        this.Remove(item);
                    }
                }

                {
                    BackgroundUploadItem item = new BackgroundUploadItem();

                    item.Value = link;
                    item.Type = BackgroundItemType.Link;
                    item.State = BackgroundUploadState.Encoding;
                    item.Rank = 1;
                    item.CompressionAlgorithm = compressionAlgorithm;
                    item.CryptoAlgorithm = cryptoAlgorithm;
                    item.CorrectionAlgorithm = correctionAlgorithm;
                    item.HashAlgorithm = hashAlgorithm;
                    item.DigitalSignature = digitalSignature;
                    item.Seed = new Seed();
                    item.Seed.Keywords.Add(ConnectionsManager.Keyword_Link);
                    item.Seed.CreationTime = DateTime.UtcNow;
                    item.BlockLength = 1024 * 1024 * 1;

                    _settings.BackgroundUploadItems.Add(item);
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
                    foreach (var item in _settings.BackgroundUploadItems.ToArray())
                    {
                        if (item.DigitalSignature.ToString() != digitalSignature.ToString()) continue;

                        this.Remove(item);
                    }
                }

                {
                    BackgroundUploadItem item = new BackgroundUploadItem();

                    item.Value = store;
                    item.Type = BackgroundItemType.Store;
                    item.State = BackgroundUploadState.Encoding;
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

                    _settings.BackgroundUploadItems.Add(item);
                }
            }
        }

        private void Remove(BackgroundUploadItem item)
        {
            lock (this.ThisLock)
            {
                foreach (var key in item.LockedKeys)
                {
                    _cacheManager.Unlock(key);
                }

                _settings.BackgroundUploadItems.Remove(item);
            }
        }

        private void Reset(BackgroundUploadItem item)
        {
            lock (this.ThisLock)
            {
                this.Remove(item);

                if (item.Type == BackgroundItemType.Link)
                {
                    this.Upload((Link)item.Value,
                        item.CompressionAlgorithm,
                        item.CryptoAlgorithm,
                        item.CorrectionAlgorithm,
                        item.HashAlgorithm,
                        item.DigitalSignature);
                }
                else if (item.Type == BackgroundItemType.Store)
                {
                    this.Upload((Store)item.Value,
                        item.CompressionAlgorithm,
                        item.CryptoAlgorithm,
                        item.CorrectionAlgorithm,
                        item.HashAlgorithm,
                        item.DigitalSignature);
                }
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
                _uploadManagerThread.Name = "BackgroundUploadManager_UploadManagerThread";
                _uploadManagerThread.Start();

                _watchThread = new Thread(this.WatchThread);
                _watchThread.Priority = ThreadPriority.Lowest;
                _watchThread.Name = "BackgroundUploadManager_WatchThread";
                _watchThread.Start();
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

            _watchThread.Join();
            _watchThread = null;
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.BackgroundUploadItems.ToArray())
                {
                    try
                    {
                        this.SetKeyCount(item);
                    }
                    catch (Exception)
                    {
                        _settings.BackgroundUploadItems.Remove(item);
                    }
                }

                foreach (var item in _settings.BackgroundUploadItems)
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

        private class Settings : Library.Configuration.SettingsBase
        {
            private object _thisLock = new object();

            public Settings()
                : base(new List<Library.Configuration.ISettingsContext>() { 
                    new Library.Configuration.SettingsContext<LockedList<BackgroundUploadItem>>() { Name = "BackgroundUploadItems", Value = new LockedList<BackgroundUploadItem>() },
                })
            {

            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public LockedList<BackgroundUploadItem> BackgroundUploadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<BackgroundUploadItem>)this["BackgroundUploadItems"];
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
            _disposed = true;

            if (disposing)
            {
                _uploadedKeys.Dispose();

                _uploadedThread.Join();
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
