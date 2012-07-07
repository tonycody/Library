using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        private string _workDirectory = Path.GetTempPath();
        private CountCache _countCache = new CountCache();
        private Dictionary<int, DownloadItem> _ids = new Dictionary<int, DownloadItem>();
        private int _id = 0;
        private ManagerState _state = ManagerState.Stop;

        private bool _disposed = false;
        private object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings();
            
            _cacheManager.GetUsingKeysEvent += (object sender, ref IList<Key> headers) =>
            {
                lock (this.ThisLock)
                {
                    HashSet<Key> list = new HashSet<Key>();

                    foreach (var item in _settings.DownloadItems)
                    {
                        if (item.Seed != null)
                        {
                            list.Add(item.Seed.Key);
                        }

                        if (item.Index != null)
                        {
                            foreach (var group in item.Index.Groups)
                            {
                                if (group != null)
                                {
                                    list.UnionWith(group.Keys);
                                }
                            }
                        }
                    }

                    foreach (var item in list)
                    {
                        headers.Add(item);
                    }
                }
            };

            _cacheManager.SetKeyEvent += (object sender, Key otherKey) =>
            {
                lock (this.ThisLock)
                {
                    _countCache.SetKey(otherKey, true);
                }
            };

            _cacheManager.RemoveKeyEvent += (object sender, Key otherKey) =>
            {
                lock (this.ThisLock)
                {
                    _countCache.SetKey(otherKey, false);
                }
            };
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
                        contexts.Add(new InformationContext("Name", DownloadManager.GetNormalizedPath(item.Value.Seed.Name)));
                        contexts.Add(new InformationContext("Length", item.Value.Seed.Length));
                        contexts.Add(new InformationContext("State", item.Value.State));
                        contexts.Add(new InformationContext("Rank", item.Value.Rank));
                        contexts.Add(new InformationContext("Seed", item.Value.Seed));
                        if (item.Value.Path != null) contexts.Add(new InformationContext("Path", item.Value.Path));

                        if (item.Value.Rank == 1) contexts.Add(new InformationContext("BlockCount", 1));
                        else contexts.Add(new InformationContext("BlockCount", item.Value.Index.Groups.Sum(n => n.Keys.Count)));

                        if (item.Value.Rank == 1) contexts.Add(new InformationContext("DownloadBlockCount", _cacheManager.Contains(item.Value.Seed.Key) ? 1 : 0));
                        else
                        {
                            if (item.Value.State == DownloadState.Downloading)
                            {
                                contexts.Add(new InformationContext("DownloadBlockCount", item.Value.Index.Groups.Sum(n => Math.Min(n.InformationLength, _countCache.GetCount(n)))));
                            }
                            else
                            {
                                contexts.Add(new InformationContext("DownloadBlockCount", item.Value.Index.Groups.Sum(n => _countCache.GetCount(n))));
                            }
                        }

                        if (item.Value.Rank == 1) contexts.Add(new InformationContext("ParityBlockCount", 0));
                        else contexts.Add(new InformationContext("ParityBlockCount", item.Value.Index.Groups.Sum(n => n.Keys.Count - n.InformationLength)));

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
                        _countCache.SetKey(key, _cacheManager.Contains(key));
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
            List<DownloadItem> compList = new List<DownloadItem>();
            int round = 0;
            int compRound = 0;

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
                                var items = _settings.DownloadItems.Where(n => n.State == DownloadState.Downloading || n.State == DownloadState.Decoding)
                                    .ToList();

                                if (compRound == 0 && compList.Count == 0)
                                {
                                    compList.AddRange(items.Where(x =>
                                    {
                                        if (x.Rank == 1) return 0 == (!_cacheManager.Contains(x.Seed.Key) ? 1 : 0);
                                        else return 0 == (x.Index.Groups.Sum(n => n.InformationLength) - x.Index.Groups.Sum(n => Math.Min(n.InformationLength, _countCache.GetCount(n))));
                                    }));
                                }

                                if (compList.Count != 0)
                                {
                                    item = compList[0];
                                    compList.RemoveAt(0);
                                }
                                else
                                {
                                    item = items.ElementAtOrDefault(round);
                                }

                                round++;
                                round = (round >= items.Count) ? 0 : round;
                                compRound++;
                                compRound = (compRound >= 10) ? 0 : compRound;
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
                                if (item.Rank < item.Seed.Rank)
                                {
                                    item.State = DownloadState.Decoding;

                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                            if (!isStop)
                                            {
                                                isStop = (stream.Length > item.Index.Groups.Sum(n => n.Length));
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
                                        item.Indexs.Add(index);

                                        item.Rank++;
                                    }

                                    this.SetKeyCount(item);

                                    item.State = DownloadState.Downloading;
                                }
                                else
                                {
                                    item.State = DownloadState.Decoding;
                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(this.BaseDirectory, string.Format("{0}.tmp", DownloadManager.GetNormalizedPath(item.Seed.Name)))))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                            if (!isStop)
                                            {
                                                isStop = (stream.Length > item.Seed.Length);
                                                largeFlag = true;
                                            }
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
                                    File.Move(fileName, DownloadManager.GetUniqueFilePath(Path.Combine(downloadDirectory, DownloadManager.GetNormalizedPath(item.Seed.Name))));

                                    _cacheManager.SetSeed(item.Seed.DeepClone(), item.Indexs);
                                    item.Indexs.Clear();
                                    _settings.DownloadedSeeds.Add(item.Seed.DeepClone());

                                    item.State = DownloadState.Completed;
                                }
                            }
                        }
                        else
                        {
                            if (!item.Index.Groups.All(n => _countCache.GetCount(n) >= n.InformationLength))
                            {
                                item.State = DownloadState.Downloading;

                                foreach (var group in item.Index.Groups.ToArray())
                                {
                                    if (_countCache.GetCount(group) >= group.InformationLength) continue;

                                    var keys = _countCache.GetKeys(group, false).Where(n => !_connectionsManager.DownloadWaiting(n)).ToList();
                                    int length = group.InformationLength - (group.Keys.Count - keys.Count);

                                    foreach (var key in keys.OrderBy(n => random.Next()).Take(length))
                                    {
                                        _connectionsManager.Download(key);
                                    }
                                }
                            }
                            else
                            {
                                item.State = DownloadState.Decoding;

                                List<Key> headers = new List<Key>();

                                try
                                {
                                    foreach (var group in item.Index.Groups.ToArray())
                                    {
                                        headers.AddRange(_cacheManager.ParityDecoding(group, (object state2) =>
                                        {
                                            return (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));
                                        }));
                                    }
                                }
                                catch (StopException)
                                {
                                    continue;
                                }

                                if (item.Rank < item.Seed.Rank)
                                {
                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                            if (!isStop)
                                            {
                                                isStop = (stream.Length > item.Index.Groups.Sum(n => n.Length));
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
                                        item.Indexs.Add(index);

                                        item.Rank++;
                                    }

                                    this.SetKeyCount(item);

                                    item.State = DownloadState.Downloading;
                                }
                                else
                                {
                                    item.State = DownloadState.Decoding;

                                    string fileName = "";
                                    bool largeFlag = false;

                                    try
                                    {
                                        using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(this.BaseDirectory, string.Format("{0}.tmp", DownloadManager.GetNormalizedPath(item.Seed.Name)))))
                                        using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                        {
                                            isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                            if (!isStop)
                                            {
                                                isStop = (stream.Length > item.Seed.Length);
                                                largeFlag = true;
                                            }
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
                                    File.Move(fileName, DownloadManager.GetUniqueFilePath(Path.Combine(downloadDirectory, DownloadManager.GetNormalizedPath(item.Seed.Name))));

                                    _cacheManager.SetSeed(item.Seed.DeepClone(), item.Indexs);
                                    item.Indexs.Clear();
                                    _settings.DownloadedSeeds.Add(item.Seed.DeepClone());

                                    item.State = DownloadState.Completed;
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    item.State = DownloadState.Error;
                }
            }
        }

        public void Download(Seed seed,
            int priority)
        {
            lock (this.ThisLock)
            {
                DownloadItem item = new DownloadItem();

                item.Rank = 1;
                item.Seed = seed;
                item.State = DownloadState.Downloading;
                item.Priority = priority;

                _settings.DownloadItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        public void Download(Seed seed,
            string path,
            int priority)
        {
            lock (this.ThisLock)
            {
                DownloadItem item = new DownloadItem();

                item.Rank = 1;
                item.Seed = seed;
                item.Path = path;
                item.State = DownloadState.Downloading;
                item.Priority = priority;

                _settings.DownloadItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        public void Remove(int id)
        {
            lock (this.ThisLock)
            {
                _settings.DownloadItems.Remove(_ids[id]);
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

        public override void Start()
        {
            while (_downloadManagerThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _downloadManagerThread = new Thread(this.DownloadManagerThread);
                _downloadManagerThread.Priority = ThreadPriority.Lowest;
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

                _ids.Clear();
                _id = 0;

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
            lock (this.ThisLock)
            {
                if (_disposed) return;

                if (disposing)
                {
                    this.Stop();
                }

                _disposed = true;
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
