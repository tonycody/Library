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
    class BackgroundDownloadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private volatile Thread _downloadManagerThread;
        private volatile Thread _decodeManagerThread;
        private volatile Thread _watchThread;
        private string _workDirectory = Path.GetTempPath();
        private CountCache _countCache = new CountCache();

        private volatile ManagerState _state = ManagerState.Stop;

        private Thread _setThread;
        private Thread _removeThread;

        private WaitQueue<Key> _setKeys = new WaitQueue<Key>();
        private WaitQueue<Key> _removeKeys = new WaitQueue<Key>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public BackgroundDownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

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

            _setThread = new Thread(() =>
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
            });
            _setThread.Priority = ThreadPriority.BelowNormal;
            _setThread.Name = "BackgroundDownloadManager_SetThread";
            _setThread.Start();

            _removeThread = new Thread(() =>
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
            });
            _removeThread.Priority = ThreadPriority.BelowNormal;
            _removeThread.Name = "BackgroundDownloadManager_RemoveThread";
            _removeThread.Start();

            _connectionsManager.GetLockSignaturesEvent = (object sender) =>
            {
                return this.SearchSignatures;
            };
        }

        public IEnumerable<string> SearchSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Signatures.ToArray();
                }
            }
        }

        private void SetKeyCount(BackgroundDownloadItem item)
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

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    return new FileStream(path, FileMode.CreateNew);
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
                        return new FileStream(text, FileMode.CreateNew);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {
                        if (index > 1024) throw;
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

                BackgroundDownloadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        if (_settings.BackgroundDownloadItems.Count > 0)
                        {
                            {
                                var items = _settings.BackgroundDownloadItems
                                   .Where(n => n.State == BackgroundDownloadState.Downloading)
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
                                var items = _settings.BackgroundDownloadItems
                                    .Where(n => n.State == BackgroundDownloadState.Downloading)
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
                                item.State = BackgroundDownloadState.Downloading;

                                _connectionsManager.Download(item.Seed.Key);
                            }
                            else
                            {
                                item.State = BackgroundDownloadState.Decoding;
                            }
                        }
                        else
                        {
                            if (!item.Index.Groups.All(n => _countCache.GetCount(n) >= n.InformationLength))
                            {
                                item.State = BackgroundDownloadState.Downloading;

                                int limitCount = 256;

                                foreach (var group in item.Index.Groups.ToArray().Randomize())
                                {
                                    if (_countCache.GetCount(group) >= group.InformationLength) continue;

                                    foreach (var key in _countCache.GetKeys(group, false))
                                    {
                                        if (_connectionsManager.IsDownloadWaiting(key))
                                        {
                                            limitCount--;

                                            if (limitCount <= 0) goto End;
                                        }
                                    }
                                }

                                List<Key> keyList = new List<Key>();

                                foreach (var group in item.Index.Groups.ToArray())
                                {
                                    if (_countCache.GetCount(group) >= group.InformationLength) continue;

                                    int downloadCount = 0;
                                    List<Key> tempKeys = new List<Key>();

                                    foreach (var key in _countCache.GetKeys(group, false))
                                    {
                                        if (_connectionsManager.IsDownloadWaiting(key))
                                        {
                                            downloadCount++;
                                        }
                                        else
                                        {
                                            tempKeys.Add(key);
                                        }
                                    }

                                    int length = Math.Max(group.InformationLength / 2, 32) - downloadCount;
                                    if (length <= 0) continue;

                                    foreach (var key in tempKeys
                                        .Randomize()
                                        .Take(length))
                                    {
                                        _connectionsManager.Download(key);

                                        limitCount--;
                                    }

                                    if (limitCount <= 0) goto End;
                                }

                            End: ;
                            }
                            else
                            {
                                item.State = BackgroundDownloadState.Decoding;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    item.State = BackgroundDownloadState.Error;

                    Log.Error(e);

                    this.Remove(item);
                }
            }
        }

        private void DecodeManagerThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                BackgroundDownloadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        if (_settings.BackgroundDownloadItems.Count > 0)
                        {
                            item = _settings.BackgroundDownloadItems
                                .Where(n => n.State == BackgroundDownloadState.Decoding)
                                .OrderBy(n => (n.Rank != n.Seed.Rank) ? 0 : 1)
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
                        if (item.Rank == 1)
                        {
                            if (!_cacheManager.Contains(item.Seed.Key))
                            {
                                item.State = BackgroundDownloadState.Downloading;
                            }
                            else
                            {
                                item.State = BackgroundDownloadState.Decoding;

                                if (item.Rank < item.Seed.Rank)
                                {
                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        using (FileStream stream = BackgroundDownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.BackgroundDownloadItems.Contains(item));

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
                                    catch (StopIoException)
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

                                        item.State = BackgroundDownloadState.Downloading;
                                    }
                                }
                                else
                                {
                                    bool largeFlag = false;
                                    object value = null;

                                    try
                                    {
                                        using (Stream stream = new BufferStream(_bufferManager))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.BackgroundDownloadItems.Contains(item));

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

                                            if (item.Type == BackgroundItemType.Link)
                                            {
                                                value = Link.Import(stream, _bufferManager);
                                            }
                                            else if (item.Type == BackgroundItemType.Store)
                                            {
                                                value = Store.Import(stream, _bufferManager);
                                            }
                                        }
                                    }
                                    catch (StopIoException)
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
                                        item.Value = value;

                                        if (item.Seed.Key != null)
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

                                        item.State = BackgroundDownloadState.Completed;
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (!item.Index.Groups.All(n => _countCache.GetCount(n) >= n.InformationLength))
                            {
                                item.State = BackgroundDownloadState.Downloading;
                            }
                            else
                            {
                                List<Key> keys = new List<Key>();

                                try
                                {
                                    foreach (var group in item.Index.Groups.ToArray())
                                    {
                                        keys.AddRange(_cacheManager.ParityDecoding(group, (object state2) =>
                                        {
                                            return (this.State == ManagerState.Stop || !_settings.BackgroundDownloadItems.Contains(item));
                                        }));
                                    }
                                }
                                catch (StopException)
                                {
                                    continue;
                                }

                                item.State = BackgroundDownloadState.Decoding;

                                if (item.Rank < item.Seed.Rank)
                                {
                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        using (FileStream stream = BackgroundDownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.BackgroundDownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Index.Groups.Sum(n => n.Length)))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }
                                        }, 1024 * 1024, true))
                                        {
                                            fileName = stream.Name;

                                            _cacheManager.Decoding(decodingProgressStream, item.Index.CompressionAlgorithm, item.Index.CryptoAlgorithm, item.Index.CryptoKey,
                                                new KeyCollection(keys));
                                        }
                                    }
                                    catch (StopIoException)
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

                                        item.State = BackgroundDownloadState.Downloading;
                                    }
                                }
                                else
                                {
                                    item.State = BackgroundDownloadState.Decoding;

                                    bool largeFlag = false;
                                    object value = null;

                                    try
                                    {
                                        using (Stream stream = new BufferStream(_bufferManager))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.BackgroundDownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Seed.Length))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }
                                        }, 1024 * 1024, true))
                                        {
                                            _cacheManager.Decoding(decodingProgressStream, item.Index.CompressionAlgorithm, item.Index.CryptoAlgorithm, item.Index.CryptoKey,
                                                new KeyCollection(keys));

                                            if (stream.Length != item.Seed.Length) throw new Exception();

                                            stream.Seek(0, SeekOrigin.Begin);

                                            if (item.Type == BackgroundItemType.Link)
                                            {
                                                value = Link.Import(stream, _bufferManager);
                                            }
                                            else if (item.Type == BackgroundItemType.Store)
                                            {
                                                value = Store.Import(stream, _bufferManager);
                                            }
                                        }
                                    }
                                    catch (StopIoException)
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
                                        item.Value = value;

                                        if (item.Seed.Key != null)
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

                                        item.State = BackgroundDownloadState.Completed;
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
                                if (this.State == ManagerState.Stop) return;

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

                    item.State = BackgroundDownloadState.Error;

                    Log.Error(e);
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
                        watchStopwatch.Restart();

                        lock (this.ThisLock)
                        {
                            foreach (var item in _settings.BackgroundDownloadItems.ToArray())
                            {
                                if (item.State != BackgroundDownloadState.Error) continue;

                                this.Remove(item);
                            }

                            foreach (var item in _settings.BackgroundDownloadItems.ToArray())
                            {
                                if (this.SearchSignatures.Contains(item.Seed.Certificate.ToString())) continue;

                                this.Remove(item);
                            }

                            foreach (var signature in this.SearchSignatures.ToArray())
                            {
                                _connectionsManager.SendSeedRequest(signature);

                                // Link
                                {
                                    Seed linkSeed;

                                    if (null != (linkSeed = _connectionsManager.GetLinkSeed(signature))
                                        && linkSeed.Length < 1024 * 1024 * 32)
                                    {
                                        var item = _settings.BackgroundDownloadItems
                                            .Where(n => n.Type == BackgroundItemType.Link)
                                            .FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);

                                        if (item == null)
                                        {
                                            this.Download(linkSeed, BackgroundItemType.Link);
                                        }
                                        else if (linkSeed.CreationTime > item.Seed.CreationTime)
                                        {
                                            this.Remove(item);
                                            this.Download(linkSeed, BackgroundItemType.Link);
                                        }
                                    }
                                }

                                // Store
                                {
                                    Seed storeSeed;

                                    if (null != (storeSeed = _connectionsManager.GetStoreSeed(signature))
                                        && storeSeed.Length < 1024 * 1024 * 32)
                                    {
                                        var item = _settings.BackgroundDownloadItems
                                            .Where(n => n.Type == BackgroundItemType.Store)
                                            .FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);

                                        if (item == null)
                                        {
                                            this.Download(storeSeed, BackgroundItemType.Store);
                                        }
                                        else if (storeSeed.CreationTime > item.Seed.CreationTime)
                                        {
                                            this.Remove(item);
                                            this.Download(storeSeed, BackgroundItemType.Store);
                                        }
                                    }
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

        private void Remove(BackgroundDownloadItem item)
        {
            lock (this.ThisLock)
            {
                if (item.State != BackgroundDownloadState.Completed)
                {
                    if (item.Seed.Key != null)
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

                _settings.BackgroundDownloadItems.Remove(item);
            }
        }

        private void Download(Seed seed, BackgroundItemType type)
        {
            if (seed == null) return;

            lock (this.ThisLock)
            {
                if (_settings.BackgroundDownloadItems.Any(n => n.Seed == seed)) return;

                if (seed.Rank == 0)
                {
                    BackgroundDownloadItem item = new BackgroundDownloadItem();

                    item.Rank = 0;
                    item.Seed = seed;
                    item.State = BackgroundDownloadState.Completed;
                    item.Type = type;

                    if (item.Type == BackgroundItemType.Link)
                    {
                        item.Value = new Link();
                    }
                    else if (item.Type == BackgroundItemType.Store)
                    {
                        item.Value = new Store();
                    }
                    else
                    {
                        throw new FormatException();
                    }

                    _settings.BackgroundDownloadItems.Add(item);
                }
                else
                {
                    if (seed.Key == null) return;

                    BackgroundDownloadItem item = new BackgroundDownloadItem();

                    item.Rank = 1;
                    item.Seed = seed;
                    item.State = BackgroundDownloadState.Downloading;
                    item.Type = type;

                    if (item.Seed.Key != null)
                    {
                        _cacheManager.Lock(item.Seed.Key);
                    }

                    _settings.BackgroundDownloadItems.Add(item);
                }
            }
        }

        public void SetSearchSignatures(IEnumerable<string> signatures)
        {
            lock (this.ThisLock)
            {
                lock (_settings.Signatures.ThisLock)
                {
                    _settings.Signatures.Clear();
                    _settings.Signatures.AddRange(signatures);
                }
            }
        }

        public Link GetLink(string signature)
        {
            lock (this.ThisLock)
            {
                var item = _settings.BackgroundDownloadItems
                    .Where(n => n.Type == BackgroundItemType.Link)
                    .FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);
                if (item == null) return null;

                var link = item.Value as Link;
                if (link == null) return null;

                return link;
            }
        }

        public Store GetStore(string signature)
        {
            lock (this.ThisLock)
            {
                var item = _settings.BackgroundDownloadItems
                    .Where(n => n.Type == BackgroundItemType.Store)
                    .FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);
                if (item == null) return null;

                var store = item.Value as Store;
                if (store == null) return null;

                return store;
            }
        }

        public override ManagerState State
        {
            get
            {
                return _state;
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    _downloadManagerThread = new Thread(this.DownloadManagerThread);
                    _downloadManagerThread.Priority = ThreadPriority.Lowest;
                    _downloadManagerThread.Name = "BackgroundDownloadManager_DownloadManagerThread";
                    _downloadManagerThread.Start();

                    _decodeManagerThread = new Thread(this.DecodeManagerThread);
                    _decodeManagerThread.Priority = ThreadPriority.Lowest;
                    _decodeManagerThread.Name = "BackgroundDownloadManager_DecodeManagerThread";
                    _decodeManagerThread.Start();

                    _watchThread = new Thread(this.WatchThread);
                    _watchThread.Priority = ThreadPriority.Lowest;
                    _watchThread.Name = "BackgroundDownloadManager_WatchThread";
                    _watchThread.Start();
                }
            }
        }

        public override void Stop()
        {
            lock (_stateLock)
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
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.BackgroundDownloadItems.ToArray())
                {
                    try
                    {
                        this.SetKeyCount(item);
                    }
                    catch (Exception)
                    {
                        _settings.BackgroundDownloadItems.Remove(item);
                    }
                }

                foreach (var item in _settings.BackgroundDownloadItems)
                {
                    if (item.State != BackgroundDownloadState.Completed)
                    {
                        if (item.Seed.Key != null)
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

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<LockedList<BackgroundDownloadItem>>() { Name = "BackgroundDownloadItems", Value = new LockedList<BackgroundDownloadItem>() },
                    new Library.Configuration.SettingContent<SignatureCollection>() { Name = "Signatures", Value = new SignatureCollection() },
                })
            {
                _thisLock = lockObject;
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

            public LockedList<BackgroundDownloadItem> BackgroundDownloadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<BackgroundDownloadItem>)this["BackgroundDownloadItems"];
                    }
                }
            }

            public SignatureCollection Signatures
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (SignatureCollection)this["Signatures"];
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_countCache != null)
                {
                    try
                    {
                        _countCache.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _countCache = null;
                }

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
