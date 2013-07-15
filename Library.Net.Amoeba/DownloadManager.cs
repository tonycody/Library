using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;

namespace Library.Net.Amoeba
{
    // データ構造が複雑で、一時停止や途中からの再開なども考えるとこうなった

    class DownloadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private volatile Thread _downloadManagerThread = null;
        private volatile Thread _decodeManagerThread = null;
        private string _workDirectory = Path.GetTempPath();
        private CountCache _countCache = new CountCache();
        private Dictionary<int, DownloadItem> _ids = new Dictionary<int, DownloadItem>();
        private int _id = 0;

        private ManagerState _state = ManagerState.Stop;
        private ManagerState _decodeState = ManagerState.Stop;

        private Thread _setThread;
        private Thread _removeThread;

        private WaitQueue<Key> _setKeys = new WaitQueue<Key>();
        private WaitQueue<Key> _removeKeys = new WaitQueue<Key>();

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
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

        public string BaseDirectory
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.BaseDirectory;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _settings.BaseDirectory = value;
                }
            }
        }

        public Information Information
        {
            get
            {
                lock (this.ThisLock)
                {
                    List<InformationContext> contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("DownloadingCount", _settings.DownloadItems
                        .Count(n => !(n.State == DownloadState.Completed || n.State == DownloadState.Error))));

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    List<Information> list = new List<Information>();

                    foreach (var item in _ids)
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Id", item.Key));
                        contexts.Add(new InformationContext("Priority", item.Value.Priority));
                        contexts.Add(new InformationContext("Name", DownloadManager.GetNormalizedPath(item.Value.Seed.Name ?? "")));
                        contexts.Add(new InformationContext("Length", item.Value.Seed.Length));
                        contexts.Add(new InformationContext("State", item.Value.State));
                        contexts.Add(new InformationContext("Rank", item.Value.Rank));
                        if (item.Value.Path != null) contexts.Add(new InformationContext("Path", Path.Combine(item.Value.Path, DownloadManager.GetNormalizedPath(item.Value.Seed.Name ?? ""))));
                        else contexts.Add(new InformationContext("Path", DownloadManager.GetNormalizedPath(item.Value.Seed.Name ?? "")));

                        contexts.Add(new InformationContext("Seed", item.Value.Seed));

                        if (item.Value.State == DownloadState.Downloading || item.Value.State == DownloadState.Completed || item.Value.State == DownloadState.Error)
                        {
                            if (item.Value.State == DownloadState.Downloading)
                            {
                                if (item.Value.Rank == 1) contexts.Add(new InformationContext("DownloadBlockCount", _cacheManager.Contains(item.Value.Seed.Key) ? 1 : 0));
                                else contexts.Add(new InformationContext("DownloadBlockCount", item.Value.Index.Groups.Sum(n => Math.Min(n.InformationLength, _countCache.GetCount(n)))));
                            }
                            else if (item.Value.State == DownloadState.Completed || item.Value.State == DownloadState.Error)
                            {
                                if (item.Value.Rank == 1) contexts.Add(new InformationContext("DownloadBlockCount", _cacheManager.Contains(item.Value.Seed.Key) ? 1 : 0));
                                else contexts.Add(new InformationContext("DownloadBlockCount", item.Value.Index.Groups.Sum(n => _countCache.GetCount(n))));
                            }

                            if (item.Value.Rank == 1) contexts.Add(new InformationContext("BlockCount", 1));
                            else contexts.Add(new InformationContext("BlockCount", item.Value.Index.Groups.Sum(n => n.Keys.Count)));

                            if (item.Value.Rank == 1) contexts.Add(new InformationContext("ParityBlockCount", 0));
                            else contexts.Add(new InformationContext("ParityBlockCount", item.Value.Index.Groups.Sum(n => n.Keys.Count - n.InformationLength)));
                        }
                        else if (item.Value.State == DownloadState.Decoding || item.Value.State == DownloadState.ParityDecoding)
                        {
                            contexts.Add(new InformationContext("DecodeBytes", item.Value.DecodeBytes));
                            contexts.Add(new InformationContext("DecodingBytes", item.Value.DecodingBytes));
                        }

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public SeedCollection DownloadedSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.DownloadedSeeds;
                }
            }
        }

        private void SetKeyCount(DownloadItem item)
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

        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
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
                    return text;
                }
            }
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

        private static string GetNormalizedPath(string path)
        {
            string filePath = path;

            foreach (char ic in Path.GetInvalidFileNameChars())
            {
                filePath = filePath.Replace(ic.ToString(), "-");
            }
            foreach (char ic in Path.GetInvalidPathChars())
            {
                filePath = filePath.Replace(ic.ToString(), "-");
            }

            return filePath;
        }

        private void DownloadManagerThread()
        {
            Random random = new Random();
            int round = 0;

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                DownloadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        lock (_settings.ThisLock)
                        {
                            if (_settings.DownloadItems.Count > 0)
                            {
                                {
                                    var items = _settings.DownloadItems
                                       .Where(n => n.State == DownloadState.Downloading)
                                       .Where(n => n.Priority != 0)
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
                                    var items = _settings.DownloadItems
                                        .Where(n => n.State == DownloadState.Downloading)
                                        .Where(n => n.Priority != 0)
                                        .OrderBy(n => -n.Priority)
                                        .Take(128)
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
                                item.State = DownloadState.Downloading;

                                _connectionsManager.Download(item.Seed.Key);
                            }
                            else
                            {
                                item.State = DownloadState.Decoding;
                            }
                        }
                        else
                        {
                            if (!item.Index.Groups.All(n => _countCache.GetCount(n) >= n.InformationLength))
                            {
                                item.State = DownloadState.Downloading;

                                int limitCount = (int)(1024 * (Math.Pow(item.Priority, 3) / Math.Pow(6, 3)));
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

                                    length = Math.Max(length, 32);

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
                                item.State = DownloadState.ParityDecoding;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    item.State = DownloadState.Error;

                    Log.Error(e);
                }
            }
        }

        private void DecodeManagerThread()
        {
            Random random = new Random();

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.DecodeState == ManagerState.Stop) return;

                DownloadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        lock (_settings.ThisLock)
                        {
                            if (_settings.DownloadItems.Count > 0)
                            {
                                item = _settings.DownloadItems
                                    .Where(n => n.State == DownloadState.Decoding || n.State == DownloadState.ParityDecoding)
                                    .Where(n => n.Priority != 0)
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
                                item.State = DownloadState.Downloading;
                            }
                            else
                            {
                                item.State = DownloadState.Decoding;

                                if (item.Rank < item.Seed.Rank)
                                {
                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        item.DecodingBytes = 0;
                                        item.DecodeBytes = _cacheManager.GetLength(item.Seed.Key);

                                        using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.DecodeState == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Index.Groups.Sum(n => n.Length)))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }

                                            item.DecodingBytes = writeSize;
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

                                        item.State = DownloadState.Downloading;
                                    }
                                }
                                else
                                {
                                    item.State = DownloadState.Decoding;

                                    string fileName = "";
                                    bool largeFlag = false;
                                    string downloadDirectory;

                                    if (item.Path == null)
                                    {
                                        downloadDirectory = this.BaseDirectory;
                                    }
                                    else
                                    {
                                        if (System.IO.Path.IsPathRooted(item.Path))
                                        {
                                            downloadDirectory = item.Path;
                                        }
                                        else
                                        {
                                            downloadDirectory = Path.Combine(this.BaseDirectory, item.Path);
                                        }
                                    }

                                    Directory.CreateDirectory(downloadDirectory);

                                    try
                                    {
                                        item.DecodingBytes = 0;
                                        item.DecodeBytes = _cacheManager.GetLength(item.Seed.Key);

                                        using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(downloadDirectory, string.Format("{0}.tmp", DownloadManager.GetNormalizedPath(item.Seed.Name)))))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.DecodeState == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Seed.Length))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }

                                            item.DecodingBytes = writeSize;
                                        }, 1024 * 1024, true))
                                        {
                                            fileName = stream.Name;

                                            _cacheManager.Decoding(decodingProgressStream, item.Seed.CompressionAlgorithm, item.Seed.CryptoAlgorithm, item.Seed.CryptoKey,
                                                new KeyCollection() { item.Seed.Key });

                                            if (stream.Length != item.Seed.Length) throw new Exception();
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

                                    File.Move(fileName, DownloadManager.GetUniqueFilePath(Path.Combine(downloadDirectory, DownloadManager.GetNormalizedPath(item.Seed.Name))));

                                    lock (this.ThisLock)
                                    {
                                        _cacheManager.SetSeed(item.Seed.DeepClone(), item.Indexes);
                                        _settings.DownloadedSeeds.Add(item.Seed.DeepClone());

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

                                        item.State = DownloadState.Completed;
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (!item.Index.Groups.All(n => _countCache.GetCount(n) >= n.InformationLength))
                            {
                                item.State = DownloadState.Downloading;
                            }
                            else
                            {
                                item.State = DownloadState.ParityDecoding;
                                item.DecodingBytes = 0;
                                item.DecodeBytes = item.Index.Groups.Sum(n => n.Length);

                                List<Key> headers = new List<Key>();

                                try
                                {
                                    foreach (var group in item.Index.Groups.ToArray())
                                    {
                                        headers.AddRange(_cacheManager.ParityDecoding(group, (object state2) =>
                                        {
                                            return (this.DecodeState == ManagerState.Stop || !_settings.DownloadItems.Contains(item));
                                        }));

                                        item.DecodingBytes += group.Length;
                                    }
                                }
                                catch (StopException)
                                {
                                    continue;
                                }

                                item.State = DownloadState.Decoding;

                                if (item.Rank < item.Seed.Rank)
                                {
                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        item.DecodingBytes = 0;

                                        using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.DecodeState == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Index.Groups.Sum(n => n.Length)))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }

                                            item.DecodingBytes = writeSize;
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

                                        item.State = DownloadState.Downloading;
                                    }
                                }
                                else
                                {
                                    item.State = DownloadState.Decoding;

                                    string fileName = "";
                                    bool largeFlag = false;
                                    string downloadDirectory;

                                    if (item.Path == null)
                                    {
                                        downloadDirectory = this.BaseDirectory;
                                    }
                                    else
                                    {
                                        if (System.IO.Path.IsPathRooted(item.Path))
                                        {
                                            downloadDirectory = item.Path;
                                        }
                                        else
                                        {
                                            downloadDirectory = Path.Combine(this.BaseDirectory, item.Path);
                                        }
                                    }

                                    Directory.CreateDirectory(downloadDirectory);

                                    try
                                    {
                                        item.DecodingBytes = 0;

                                        using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(downloadDirectory, string.Format("{0}.tmp", DownloadManager.GetNormalizedPath(item.Seed.Name)))))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.DecodeState == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                            if (!isStop && (stream.Length > item.Seed.Length))
                                            {
                                                isStop = true;
                                                largeFlag = true;
                                            }

                                            item.DecodingBytes = writeSize;
                                        }, 1024 * 1024, true))
                                        {
                                            fileName = stream.Name;

                                            _cacheManager.Decoding(decodingProgressStream, item.Index.CompressionAlgorithm, item.Index.CryptoAlgorithm, item.Index.CryptoKey,
                                                new KeyCollection(headers));

                                            if (stream.Length != item.Seed.Length) throw new Exception();
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

                                    File.Move(fileName, DownloadManager.GetUniqueFilePath(Path.Combine(downloadDirectory, DownloadManager.GetNormalizedPath(item.Seed.Name))));

                                    lock (this.ThisLock)
                                    {
                                        _cacheManager.SetSeed(item.Seed.DeepClone(), item.Indexes);
                                        _settings.DownloadedSeeds.Add(item.Seed.DeepClone());

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

                                        item.State = DownloadState.Completed;
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

                    item.State = DownloadState.Error;

                    Log.Error(e);
                }
            }
        }

        public void Download(Seed seed,
            int priority)
        {
            lock (this.ThisLock)
            {
                this.Download(seed, null, priority);
            }
        }

        public void Download(Seed seed,
            string path,
            int priority)
        {
            lock (this.ThisLock)
            {
                if (_settings.DownloadItems.Any(n => n.Seed == seed && n.Path == path)) return;

                DownloadItem item = new DownloadItem();

                item.Rank = 1;
                item.Seed = seed;
                item.Path = path;
                item.State = DownloadState.Downloading;
                item.Priority = priority;

                if (item.Seed != null)
                {
                    _cacheManager.Lock(item.Seed.Key);
                }

                _settings.DownloadItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        public void Remove(int id)
        {
            lock (this.ThisLock)
            {
                var item = _ids[id];

                if (item.State != DownloadState.Completed)
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

                _settings.DownloadItems.Remove(item);
                _ids.Remove(id);
            }
        }

        public void Reset(int id)
        {
            lock (this.ThisLock)
            {
                var item = _ids[id];

                this.Remove(id);
                this.Download(item.Seed, item.Path, item.Priority);
            }
        }

        public void SetPriority(int id, int priority)
        {
            lock (this.ThisLock)
            {
                _ids[id].Priority = priority;
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

        public ManagerState DecodeState
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _decodeState;
                }
            }
        }

        public override void Start()
        {
            while (_downloadManagerThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _downloadManagerThread = new Thread(this.DownloadManagerThread);
                _downloadManagerThread.Priority = ThreadPriority.Lowest;
                _downloadManagerThread.Name = "DownloadManager_DownloadManagerThread";
                _downloadManagerThread.Start();
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
        }

        public void DecodeStart()
        {
            while (_decodeManagerThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.DecodeState == ManagerState.Start) return;
                _decodeState = ManagerState.Start;

                _decodeManagerThread = new Thread(this.DecodeManagerThread);
                _decodeManagerThread.Priority = ThreadPriority.Lowest;
                _decodeManagerThread.Name = "DownloadManager_DecodeManagerThread";
                _decodeManagerThread.Start();
            }
        }

        public void DecodeStop()
        {
            lock (this.ThisLock)
            {
                if (this.DecodeState == ManagerState.Stop) return;
                _decodeState = ManagerState.Stop;
            }

            _decodeManagerThread.Join();
            _decodeManagerThread = null;
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.DownloadItems.ToArray())
                {
                    try
                    {
                        this.SetKeyCount(item);
                    }
                    catch (Exception)
                    {
                        _settings.DownloadItems.Remove(item);
                    }
                }

                foreach (var item in _settings.DownloadItems)
                {
                    if (item.State != DownloadState.Completed)
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

                _id = 0;
                _ids.Clear();

                foreach (var item in _settings.DownloadItems)
                {
                    _ids.Add(_id++, item);
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
                    new Library.Configuration.SettingsContext<string>() { Name = "BaseDirectory", Value = "" },
                    new Library.Configuration.SettingsContext<LockedList<DownloadItem>>() { Name = "DownloadItems", Value = new LockedList<DownloadItem>() },
                    new Library.Configuration.SettingsContext<SeedCollection>() { Name = "DownloadedSeeds", Value = new SeedCollection() },
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

            public string BaseDirectory
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (string)this["BaseDirectory"];
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        this["BaseDirectory"] = value;
                    }
                }
            }

            public LockedList<DownloadItem> DownloadItems
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedList<DownloadItem>)this["DownloadItems"];
                    }
                }
            }

            public SeedCollection DownloadedSeeds
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (SeedCollection)this["DownloadedSeeds"];
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
