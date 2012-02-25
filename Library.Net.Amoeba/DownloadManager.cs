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
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _countCache.SetKey(otherKey, true);
                }
            };

            _cacheManager.RemoveKeyEvent += (object sender, Key otherKey) =>
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _countCache.SetKey(otherKey, false);
                }
            };
        }

        public string BaseDirectory
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.BaseDirectory;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _settings.BaseDirectory = value;
                }
            }
        }

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<Information> list = new List<Information>();

                    foreach (var item in _settings.DownloadItems)
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Id", _ids.First(n => n.Value == item).Key));
                        contexts.Add(new InformationContext("Priority", item.Priority));
                        contexts.Add(new InformationContext("Name", item.Seed.Name));
                        contexts.Add(new InformationContext("Length", item.Seed.Length));
                        contexts.Add(new InformationContext("State", item.State));
                        contexts.Add(new InformationContext("Rank", item.Rank));
                        contexts.Add(new InformationContext("Seed", item.Seed));
                        contexts.Add(new InformationContext("Path", item.Path));

                        if (item.Rank == 1) contexts.Add(new InformationContext("BlockCount", 1));
                        else contexts.Add(new InformationContext("BlockCount", item.Index.Groups.Sum(n => n.Keys.Count)));

                        if (item.Rank == 1) contexts.Add(new InformationContext("DownloadBlockCount", _cacheManager.Contains(item.Seed.Key) ? 1 : 0));
                        else
                        {
                            if (item.State == DownloadState.Downloading)
                            {
                                contexts.Add(new InformationContext("DownloadBlockCount", item.Index.Groups.Sum(n => Math.Min(n.InformationLength, _countCache.GetCount(n)))));
                            }
                            else
                            {
                                contexts.Add(new InformationContext("DownloadBlockCount", item.Index.Groups.Sum(n => _countCache.GetCount(n))));
                            }
                        }

                        if (item.Rank == 1) contexts.Add(new InformationContext("ParityBlockCount", 0));
                        else contexts.Add(new InformationContext("ParityBlockCount", item.Index.Groups.Sum(n => n.Keys.Count - n.InformationLength)));

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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.DownloadedSeeds;
                }
            }
        }

        private void SetKeyCount(DownloadItem item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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

        private static string GetUniqueDirectoryPath(string path)
        {
            if (!Directory.Exists(path))
            {
                return path;
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0} ({1})",
                    path,
                    index);

                if (!File.Exists(text))
                {
                    return text;
                }
            }
        }

        private static FileStream GetUniqueStream(string path)
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
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        using (DeadlockMonitor.Lock(_settings.ThisLock))
                        {
                            if (_settings.DownloadItems.Count > 0)
                            {
                                var items = _settings.DownloadItems.Where(n => n.State == DownloadState.Downloading || n.State == DownloadState.Decoding).ToList();

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
                                    items.Sort(delegate(DownloadItem x, DownloadItem y)
                                    {
                                        return x.GetHashCode().CompareTo(y.GetHashCode());
                                    });
                                    items.Sort(delegate(DownloadItem x, DownloadItem y)
                                    {
                                        return y.Priority.CompareTo(x.Priority);
                                    });

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
                catch (Exception ex)
                {
                    Log.Error(ex);

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

                                    using (FileStream stream = DownloadManager.GetUniqueStream(Path.Combine(this._workDirectory, "index")))
                                    using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));
                                    }, 1024 * 1024, true))
                                    {
                                        fileName = stream.Name;

                                        _cacheManager.Decoding(decodingProgressStream, item.Seed.CompressionAlgorithm, item.Seed.CryptoAlgorithm, item.Seed.CryptoKey,
                                            new KeyCollection() { item.Seed.Key });
                                    }

                                    Index index;

                                    using (FileStream stream = new FileStream(fileName, FileMode.Open))
                                    {
                                        index = Index.Import(stream, _bufferManager);
                                    }

                                    File.Delete(fileName);

                                    using (DeadlockMonitor.Lock(this.ThisLock))
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

                                    using (FileStream stream = DownloadManager.GetUniqueStream(Path.Combine(this.BaseDirectory, string.Format("_temp_{0}", item.Seed.Name))))
                                    using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));
                                    }, 1024 * 1024, true))
                                    {
                                        fileName = stream.Name;

                                        _cacheManager.Decoding(decodingProgressStream, item.Seed.CompressionAlgorithm, item.Seed.CryptoAlgorithm, item.Seed.CryptoKey,
                                            new KeyCollection() { item.Seed.Key });
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

                                foreach (var group in item.Index.Groups.ToArray())
                                {
                                    headers.AddRange(_cacheManager.ParityDecoding(group));
                                }

                                if (item.Rank < item.Seed.Rank)
                                {
                                    string fileName = "";

                                    using (FileStream stream = DownloadManager.GetUniqueStream(Path.Combine(this._workDirectory, "index")))
                                    using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));
                                    }, 1024 * 1024, true))
                                    {
                                        fileName = stream.Name;

                                        _cacheManager.Decoding(decodingProgressStream, item.Index.CompressionAlgorithm, item.Index.CryptoAlgorithm, item.Index.CryptoKey,
                                            new KeyCollection(headers));
                                    }

                                    Index index;

                                    using (FileStream stream = new FileStream(fileName, FileMode.Open))
                                    {
                                        index = Index.Import(stream, _bufferManager);
                                    }
                                    File.Delete(fileName);

                                    using (DeadlockMonitor.Lock(this.ThisLock))
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

                                    using (FileStream stream = DownloadManager.GetUniqueStream(Path.Combine(this.BaseDirectory, string.Format("_temp_{0}", item.Seed.Name))))
                                    using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));
                                    }, 1024 * 1024, true))
                                    {
                                        fileName = stream.Name;

                                        _cacheManager.Decoding(decodingProgressStream, item.Index.CompressionAlgorithm, item.Index.CryptoAlgorithm, item.Index.CryptoKey,
                                            new KeyCollection(headers));
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
                                    _settings.DownloadedSeeds.Add(item.Seed.DeepClone());

                                    item.State = DownloadState.Completed;
                                }
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    item.State = DownloadState.Error;

                    Log.Error(exception);
                }
            }
        }

        public void Download(Seed seed)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                DownloadItem item = new DownloadItem();

                item.Rank = 1;
                item.Seed = seed;
                item.State = DownloadState.Downloading;

                _settings.DownloadItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        public void Download(Seed seed, string path)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                DownloadItem item = new DownloadItem();

                item.Rank = 1;
                item.Seed = seed;
                item.Path = path;
                item.State = DownloadState.Downloading;

                _settings.DownloadItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        public void Remove(int id)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _settings.DownloadItems.Remove(_ids[id]);
            }
        }

        public void SetPriority(int id, int priority)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _ids[id].Priority = priority;
            }
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
            while (_downloadManagerThread != null) Thread.Sleep(1000);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _downloadManagerThread = new Thread(this.DownloadManagerThread);
                _downloadManagerThread.IsBackground = true;
                _downloadManagerThread.Priority = ThreadPriority.Lowest;
                _downloadManagerThread.Start();
            }
        }

        public override void Stop()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;
            }

            _downloadManagerThread.Join();
            _downloadManagerThread = null;
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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
            using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    base.Save(directoryPath);
                }
            }

            public string BaseDirectory
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (string)this["BaseDirectory"];
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        this["BaseDirectory"] = value;
                    }
                }
            }

            public LockedList<DownloadItem> DownloadItems
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (LockedList<DownloadItem>)this["DownloadItems"];
                    }
                }
            }

            public SeedCollection DownloadedSeeds
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (SeedCollection)this["DownloadedSeeds"];
                    }
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

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_downloadManagerThread != null)
                    {
                        try
                        {
                            _downloadManagerThread.Abort();
                        }
                        catch (Exception)
                        {

                        }

                        _downloadManagerThread = null;
                    }
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
