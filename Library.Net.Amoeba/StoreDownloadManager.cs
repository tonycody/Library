using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    class StoreDownloadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private volatile Thread _downloadManagerThread = null;
        private volatile Thread _decodeManagerThread = null;
        private volatile Thread _watchThread = null;
        private string _workDirectory = Path.GetTempPath();
        private CountCache _countCache = new CountCache();

        private ManagerState _state = ManagerState.Stop;

        private Thread _setThread;
        private Thread _removeThread;

        private WaitQueue<Key> _setKeys = new WaitQueue<Key>();
        private WaitQueue<Key> _removeKeys = new WaitQueue<Key>();

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public StoreDownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings();

            _cacheManager.SetKeyEvent += (object sender, IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _setKeys.Enqueue(key);
                }
            };

            _cacheManager.RemoveKeyEvent += (object sender, IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _removeKeys.Enqueue(key);
                }
            };

            _setThread = new Thread(new ThreadStart(() =>
            {
                try
                {
                    for (; ; )
                    {
                        var key = _setKeys.Dequeue();

                        lock (this.ThisLock)
                        {
                            _countCache.SetState(key, true);
                        }
                    }
                }
                catch (Exception)
                {

                }
            }));
            _setThread.Priority = ThreadPriority.BelowNormal;
            _setThread.Name = "DownloadManager_SetThread";
            _setThread.Start();

            _removeThread = new Thread(new ThreadStart(() =>
            {
                try
                {
                    for (; ; )
                    {
                        var key = _removeKeys.Dequeue();

                        lock (this.ThisLock)
                        {
                            _countCache.SetState(key, false);
                        }
                    }
                }
                catch (Exception)
                {

                }
            }));
            _removeThread.Priority = ThreadPriority.BelowNormal;
            _removeThread.Name = "DownloadManager_RemoveThread";
            _removeThread.Start();
        }

        public SignatureCollection SearchSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Signatures;
                }
            }
        }

        private void SetKeyCount(StoreDownloadItem item)
        {
            lock (this.ThisLock)
            {
                if (item.Index == null) return;

                foreach (var group in item.Index.Groups)
                {
                    _countCache.SetGroup(group);

                    foreach (var key in group.Keys)
                    {
                        _countCache.SetState(key, _cacheManager.Contains(key));
                    }
                }
            }
        }

        private static bool CheckBoxDigitalSignature(ref Box box)
        {
            bool flag = true;
            var seedList = new List<Seed>();
            var boxList = new List<Box>();
            boxList.Add(box);

            for (int i = 0; i < boxList.Count; i++)
            {
                boxList.AddRange(boxList[i].Boxes);
                seedList.AddRange(boxList[i].Seeds);
            }

            foreach (var item in seedList.Reverse<Seed>())
            {
                if (!item.VerifyCertificate())
                {
                    flag = false;

                    item.CreateCertificate(null);
                }
            }

            foreach (var item in boxList.Reverse<Box>())
            {
                if (!item.VerifyCertificate())
                {
                    flag = false;

                    item.CreateCertificate(null);
                }
            }

            return flag;
        }

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    FileStream fs = new FileStream(path, FileMode.CreateNew);
                    return fs;
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {
                    throw;
                }
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    try
                    {
                        FileStream fs = new FileStream(text, FileMode.CreateNew);
                        return fs;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {
                        if (index > 100) throw;
                    }
                }
            }
        }

        private void DownloadManagerThread()
        {
            Random random = new Random();
            int round = 0;

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                StoreDownloadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        lock (_settings.ThisLock)
                        {
                            if (_settings.StoreDownloadItems.Count > 0)
                            {
                                {
                                    var items = _settings.StoreDownloadItems
                                       .Where(n => n.State == StoreDownloadState.Downloading)
                                       .Where(x =>
                                       {
                                           if (x.Rank == 1) return 0 == (!_cacheManager.Contains(x.Seed.Key) ? 1 : 0);
                                           else return 0 == (x.Index.Groups.Sum(n => n.InformationLength) - x.Index.Groups.Sum(n => Math.Min(n.InformationLength, _countCache.GetCount(n))));
                                       })
                                       .ToList();

                                    item = items.FirstOrDefault();
                                }

                                if (item == null)
                                {
                                    var items = _settings.StoreDownloadItems
                                        .Where(n => n.State == StoreDownloadState.Downloading)
                                        .ToList();

                                    if (items.Count > 0)
                                    {
                                        round = (round >= items.Count) ? 0 : round;
                                        item = items[round++];
                                    }
                                }
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
                        if (item.Rank == 1)
                        {
                            if (!_cacheManager.Contains(item.Seed.Key))
                            {
                                item.State = StoreDownloadState.Downloading;

                                _connectionsManager.Download(item.Seed.Key);
                            }
                            else
                            {
                                item.State = StoreDownloadState.Decoding;
                            }
                        }
                        else
                        {
                            if (!item.Index.Groups.All(n => _countCache.GetCount(n) >= n.InformationLength))
                            {
                                item.State = StoreDownloadState.Downloading;

                                //int limitCount = (int)(1024 * (Math.Pow(item.Priority, 3) / Math.Pow(6, 3)));
                                int limitCount = (int)32;
                                int downloadingCount = 0;

                                List<Key> keyList = new List<Key>();

                                foreach (var group in item.Index.Groups.ToArray())
                                {
                                    if (_countCache.GetCount(group) >= group.InformationLength) continue;

                                    List<Key> keys = new List<Key>();

                                    foreach (var key in _countCache.GetKeys(group, false))
                                    {
                                        if (_connectionsManager.DownloadWaiting(key))
                                        {
                                            downloadingCount++;
                                        }
                                        else
                                        {
                                            keys.Add(key);
                                        }
                                    }

                                    if (downloadingCount > limitCount) goto End;

                                    int length = group.InformationLength - (group.Keys.Count - keys.Count);
                                    if (length <= 0) continue;

                                    length = Math.Max(length, 6);

                                    foreach (var key in keys
                                        .OrderBy(n => random.Next())
                                        .Take(length))
                                    {
                                        keyList.Add(key);
                                    }
                                }

                                foreach (var key in keyList
                                    .OrderBy(n => random.Next())
                                    .Take(limitCount - downloadingCount))
                                {
                                    _connectionsManager.Download(key);
                                }

                            End: ;
                            }
                            else
                            {
                                item.State = StoreDownloadState.Decoding;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    item.State = StoreDownloadState.Error;

                    Log.Error(e);

                    this.Remove(item);
                }
            }
        }

        private void DecodeManagerThread()
        {
            Random random = new Random();

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                StoreDownloadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        lock (_settings.ThisLock)
                        {
                            if (_settings.StoreDownloadItems.Count > 0)
                            {
                                item = _settings.StoreDownloadItems
                                    .Where(n => n.State == StoreDownloadState.Decoding)
                                    .OrderBy(n => (n.Rank != n.Seed.Rank) ? 0 : 1)
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
                        if (item.Rank == 1)
                        {
                            if (!_cacheManager.Contains(item.Seed.Key))
                            {
                                item.State = StoreDownloadState.Downloading;
                            }
                            else
                            {
                                item.State = StoreDownloadState.Decoding;

                                if (item.Rank < item.Seed.Rank)
                                {
                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        using (FileStream stream = StoreDownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.StoreDownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Index.Groups.Sum(n => n.Length)))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }
                                        }, 1024 * 1024, true))
                                        {
                                            fileName = stream.Name;

                                            _cacheManager.Decoding(decodingProgressStream, item.Seed.CompressionAlgorithm, item.Seed.CryptoAlgorithm, item.Seed.CryptoKey,
                                                new KeyCollection() { item.Seed.Key });
                                        }
                                    }
                                    catch (StopIOException)
                                    {
                                        if (File.Exists(fileName))
                                            File.Delete(fileName);

                                        if (largeFlag)
                                        {
                                            throw new Exception();
                                        }

                                        continue;
                                    }
                                    catch (Exception)
                                    {
                                        if (File.Exists(fileName))
                                            File.Delete(fileName);

                                        throw;
                                    }

                                    Index index;

                                    using (FileStream stream = new FileStream(fileName, FileMode.Open))
                                    {
                                        index = Index.Import(stream, _bufferManager);
                                    }

                                    File.Delete(fileName);

                                    lock (this.ThisLock)
                                    {
                                        item.Index = index;

                                        foreach (var group in item.Index.Groups)
                                        {
                                            foreach (var key in group.Keys)
                                            {
                                                _cacheManager.Lock(key);
                                            }
                                        }

                                        item.Indexes.Add(index);

                                        item.Rank++;

                                        this.SetKeyCount(item);

                                        item.State = StoreDownloadState.Downloading;
                                    }
                                }
                                else
                                {
                                    bool largeFlag = false;
                                    Store store;

                                    try
                                    {
                                        using (Stream stream = new BufferStream(_bufferManager))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.StoreDownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Seed.Length))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }
                                        }, 1024 * 1024, true))
                                        {
                                            _cacheManager.Decoding(decodingProgressStream, item.Seed.CompressionAlgorithm, item.Seed.CryptoAlgorithm, item.Seed.CryptoKey,
                                                new KeyCollection() { item.Seed.Key });

                                            if (stream.Length != item.Seed.Length) throw new Exception();

                                            stream.Seek(0, SeekOrigin.Begin);
                                            store = Store.Import(stream, _bufferManager);
                                        }
                                    }
                                    catch (StopIOException)
                                    {
                                        if (largeFlag)
                                        {
                                            throw new Exception();
                                        }

                                        continue;
                                    }
                                    catch (Exception)
                                    {
                                        throw;
                                    }

                                    lock (this.ThisLock)
                                    {
                                        for (int i = 0; i < store.Boxes.Count; i++)
                                        {
                                            var box = store.Boxes[i];
                                            StoreDownloadManager.CheckBoxDigitalSignature(ref box);
                                            store.Boxes[i] = box;
                                        }

                                        item.Store = store;

                                        if (item.Seed != null)
                                        {
                                            _cacheManager.Unlock(item.Seed.Key);
                                        }

                                        foreach (var index in item.Indexes)
                                        {
                                            foreach (var group in index.Groups)
                                            {
                                                foreach (var key in group.Keys)
                                                {
                                                    _cacheManager.Unlock(key);
                                                }
                                            }
                                        }

                                        item.Indexes.Clear();

                                        item.State = StoreDownloadState.Completed;
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (!item.Index.Groups.All(n => _countCache.GetCount(n) >= n.InformationLength))
                            {
                                item.State = StoreDownloadState.Downloading;
                            }
                            else
                            {
                                List<Key> headers = new List<Key>();

                                try
                                {
                                    foreach (var group in item.Index.Groups.ToArray())
                                    {
                                        headers.AddRange(_cacheManager.ParityDecoding(group, (object state2) =>
                                        {
                                            return (this.State == ManagerState.Stop || !_settings.StoreDownloadItems.Contains(item));
                                        }));
                                    }
                                }
                                catch (StopException)
                                {
                                    continue;
                                }

                                item.State = StoreDownloadState.Decoding;

                                if (item.Rank < item.Seed.Rank)
                                {
                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        using (FileStream stream = StoreDownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.StoreDownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Index.Groups.Sum(n => n.Length)))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }
                                        }, 1024 * 1024, true))
                                        {
                                            fileName = stream.Name;

                                            _cacheManager.Decoding(decodingProgressStream, item.Index.CompressionAlgorithm, item.Index.CryptoAlgorithm, item.Index.CryptoKey,
                                                new KeyCollection(headers));
                                        }
                                    }
                                    catch (StopIOException)
                                    {
                                        if (File.Exists(fileName))
                                            File.Delete(fileName);

                                        if (largeFlag)
                                        {
                                            throw new Exception();
                                        }

                                        continue;
                                    }
                                    catch (Exception)
                                    {
                                        if (File.Exists(fileName))
                                            File.Delete(fileName);

                                        throw;
                                    }

                                    Index index;

                                    using (FileStream stream = new FileStream(fileName, FileMode.Open))
                                    {
                                        index = Index.Import(stream, _bufferManager);
                                    }

                                    File.Delete(fileName);

                                    lock (this.ThisLock)
                                    {
                                        item.Index = index;

                                        foreach (var group in item.Index.Groups)
                                        {
                                            foreach (var key in group.Keys)
                                            {
                                                _cacheManager.Lock(key);
                                            }
                                        }

                                        item.Indexes.Add(index);

                                        item.Rank++;

                                        this.SetKeyCount(item);

                                        item.State = StoreDownloadState.Downloading;
                                    }
                                }
                                else
                                {
                                    item.State = StoreDownloadState.Decoding;

                                    bool largeFlag = false;
                                    Store store;

                                    try
                                    {
                                        using (Stream stream = new BufferStream(_bufferManager))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.StoreDownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Seed.Length))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }
                                        }, 1024 * 1024, true))
                                        {
                                            _cacheManager.Decoding(decodingProgressStream, item.Index.CompressionAlgorithm, item.Index.CryptoAlgorithm, item.Index.CryptoKey,
                                                new KeyCollection(headers));

                                            if (stream.Length != item.Seed.Length) throw new Exception();

                                            stream.Seek(0, SeekOrigin.Begin);
                                            store = Store.Import(stream, _bufferManager);
                                        }
                                    }
                                    catch (StopIOException)
                                    {
                                        if (largeFlag)
                                        {
                                            throw new Exception();
                                        }

                                        continue;
                                    }
                                    catch (Exception)
                                    {
                                        throw;
                                    }

                                    lock (this.ThisLock)
                                    {
                                        for (int i = 0; i < store.Boxes.Count; i++)
                                        {
                                            var box = store.Boxes[i];
                                            StoreDownloadManager.CheckBoxDigitalSignature(ref box);
                                            store.Boxes[i] = box;
                                        }

                                        item.Store = store;

                                        if (item.Seed != null)
                                        {
                                            _cacheManager.Unlock(item.Seed.Key);
                                        }

                                        foreach (var index in item.Indexes)
                                        {
                                            foreach (var group in index.Groups)
                                            {
                                                foreach (var key in group.Keys)
                                                {
                                                    _cacheManager.Unlock(key);
                                                }
                                            }
                                        }

                                        item.Indexes.Clear();

                                        item.State = StoreDownloadState.Completed;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    if (_cacheManager.Contains(item.Seed.Key))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[item.Seed.Key];
                        }
                        catch (Exception)
                        {

                        }
                        finally
                        {
                            if (buffer.Array != null)
                            {
                                _bufferManager.ReturnBuffer(buffer.Array);
                            }
                        }
                    }

                    foreach (var index in item.Indexes)
                    {
                        foreach (var group in index.Groups)
                        {
                            foreach (var key in group.Keys)
                            {
                                if (!_cacheManager.Contains(key)) continue;

                                ArraySegment<byte> buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = _cacheManager[key];
                                }
                                catch (Exception)
                                {

                                }
                                finally
                                {
                                    if (buffer.Array != null)
                                    {
                                        _bufferManager.ReturnBuffer(buffer.Array);
                                    }
                                }
                            }
                        }
                    }

                    item.State = StoreDownloadState.Error;

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

                    if (!watchStopwatch.IsRunning || watchStopwatch.Elapsed.TotalSeconds >= 60)
                    {
                        lock (this.ThisLock)
                        {
                            foreach (var item in _settings.StoreDownloadItems.ToArray())
                            {
                                if (this.SearchSignatures.Contains(item.Seed.Certificate.ToString())) continue;

                                this.Remove(item);
                            }

                            foreach (var signature in this.SearchSignatures)
                            {
                                var seed = _connectionsManager.GetStoreSeed(signature);
                                if (seed == null || seed.Length > 1024 * 1024 * 32) continue;

                                var item = _settings.StoreDownloadItems.FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);

                                if (item == null)
                                {
                                    this.Download(seed);
                                }
                                else if (seed.CreationTime > item.Seed.CreationTime)
                                {
                                    this.Remove(item);
                                    this.Download(seed);
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

        private void Remove(StoreDownloadItem item)
        {
            lock (this.ThisLock)
            {
                if (item.State != StoreDownloadState.Completed)
                {
                    if (item.Seed != null)
                    {
                        _cacheManager.Unlock(item.Seed.Key);
                    }

                    foreach (var index in item.Indexes)
                    {
                        foreach (var group in index.Groups)
                        {
                            foreach (var key in group.Keys)
                            {
                                _cacheManager.Unlock(key);
                            }
                        }
                    }
                }

                _settings.StoreDownloadItems.Remove(item);
            }
        }

        private void Reset(StoreDownloadItem item)
        {
            lock (this.ThisLock)
            {
                this.Remove(item);
                this.Download(item.Seed);
            }
        }

        private void Download(Seed seed)
        {
            lock (this.ThisLock)
            {
                if (_settings.StoreDownloadItems.Any(n => n.Seed == seed)) return;

                StoreDownloadItem item = new StoreDownloadItem();

                item.Rank = 1;
                item.Seed = seed;
                item.State = StoreDownloadState.Downloading;

                if (item.Seed != null)
                {
                    _cacheManager.Lock(item.Seed.Key);
                }

                _settings.StoreDownloadItems.Add(item);
            }
        }

        public Store GetStore(string signature)
        {
            lock (this.ThisLock)
            {
                var item = _settings.StoreDownloadItems.FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);
                if (item == null) return null;

                return item.Store;
            }
        }

        public void Reset(string signature)
        {
            lock (this.ThisLock)
            {
                var item = _settings.StoreDownloadItems.FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);
                if (item == null) return;

                this.Remove(item);
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
            while (_downloadManagerThread != null) Thread.Sleep(1000);
            while (_decodeManagerThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _downloadManagerThread = new Thread(this.DownloadManagerThread);
                _downloadManagerThread.Priority = ThreadPriority.Lowest;
                _downloadManagerThread.Name = "StoreDownloadManager_DownloadManagerThread";
                _downloadManagerThread.Start();

                _decodeManagerThread = new Thread(this.DecodeManagerThread);
                _decodeManagerThread.Priority = ThreadPriority.Lowest;
                _decodeManagerThread.Name = "StoreDownloadManager_DecodeManagerThread";
                _decodeManagerThread.Start();

                _watchThread = new Thread(this.WatchThread);
                _watchThread.Priority = ThreadPriority.Lowest;
                _watchThread.Name = "StoreDownloadManager_WatchThread";
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

            _downloadManagerThread.Join();
            _downloadManagerThread = null;

            _decodeManagerThread.Join();
            _decodeManagerThread = null;

            _watchThread.Join();
            _watchThread = null;
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.StoreDownloadItems.ToArray())
                {
                    try
                    {
                        this.SetKeyCount(item);
                    }
                    catch (Exception)
                    {
                        _settings.StoreDownloadItems.Remove(item);
                    }
                }

                foreach (var item in _settings.StoreDownloadItems)
                {
                    if (item.State != StoreDownloadState.Completed)
                    {
                        if (item.Seed != null)
                        {
                            _cacheManager.Lock(item.Seed.Key);
                        }

                        foreach (var index in item.Indexes)
                        {
                            foreach (var group in index.Groups)
                            {
                                foreach (var key in group.Keys)
                                {
                                    _cacheManager.Lock(key);
                                }
                            }
                        }
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
                    new Library.Configuration.SettingsContext<LockedList<StoreDownloadItem>>() { Name = "StoreDownloadItems", Value = new LockedList<StoreDownloadItem>() },
                    new Library.Configuration.SettingsContext<SignatureCollection>() { Name = "Signatures", Value = new SignatureCollection() },
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

            public LockedList<StoreDownloadItem> StoreDownloadItems
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedList<StoreDownloadItem>)this["StoreDownloadItems"];
                    }
                }
            }

            public SignatureCollection Signatures
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (SignatureCollection)this["Signatures"];
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
                _setKeys.Dispose();
                _removeKeys.Dispose();

                _setThread.Join();
                _removeThread.Join();
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
