using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using Library.Collections;
using Library.Compression;
using Library.Correction;

namespace Library.Net.Lair
{
    public delegate void CheckBlocksProgressEventHandler(object sender, int badBlockCount, int checkedBlockCount, int blockCount, out bool isStop);
    delegate void SetKeyEventHandler(object sender, IEnumerable<Key> keys);
    delegate void RemoveKeyEventHandler(object sender, IEnumerable<Key> keys);
    delegate bool WatchEventHandler(object sender);

    class CacheManager : ManagerBase, Library.Configuration.ISettings, IEnumerable<Key>, IThisLock
    {
        private FileStream _fileStream = null;
        private BufferManager _bufferManager;

        private Settings _settings;

        private HashSet<long> _spaceClusters;
        private bool _spaceClustersInitialized = false;

        private volatile AutoResetEvent _resetEvent = new AutoResetEvent(false);
        private long _lockSpace = 0;
        private long _freeSpace = 0;

        private LockedDictionary<Key, int> _lockedKeys = new LockedDictionary<Key, int>();

        private Thread _watchThread = null;

        private SetKeyEventHandler _setKeyEvent;
        private RemoveKeyEventHandler _removeKeyEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public static readonly int ClusterSize = 1024 * 32;

        private int _threadCount = 2;

        public CacheManager(string cachePath, BufferManager bufferManager)
        {
            _fileStream = new FileStream(cachePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _spaceClusters = new HashSet<long>();

            _watchThread = new Thread(new ThreadStart(this.Watch));
            _watchThread.Priority = ThreadPriority.Lowest;
            _watchThread.IsBackground = true;
            _watchThread.Name = "CacheManager_WatchThread";
            _watchThread.Start();

            _threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 2);
        }

        private void Watch()
        {
            try
            {
                for (; ; )
                {
                    _resetEvent.WaitOne(1000 * 60 * 5);

                    var usingHeaders = new HashSet<Key>();
                    usingHeaders.UnionWith(_lockedKeys.Keys);

                    long size = 0;

                    foreach (var item in usingHeaders)
                    {
                        Clusters clusters;

                        if (_settings.ClustersIndex.TryGetValue(item, out clusters))
                        {
                            size += clusters.Indexes.Length * CacheManager.ClusterSize;
                        }
                    }

                    _lockSpace = size;
                    _freeSpace = this.Size - size;
                }
            }
            catch (Exception)
            {

            }
        }

        public event SetKeyEventHandler SetKeyEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _setKeyEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _setKeyEvent -= value;
                }
            }
        }

        public event RemoveKeyEventHandler RemoveKeyEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _removeKeyEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _removeKeyEvent -= value;
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

                    contexts.Add(new InformationContext("UsingSpace", _fileStream.Length));
                    contexts.Add(new InformationContext("LockSpace", _lockSpace));
                    contexts.Add(new InformationContext("FreeSpace", _freeSpace));

                    return new Information(contexts);
                }
            }
        }

        public long Size
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Size;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.ClustersIndex.Count;
                }
            }
        }

        private void CheckSpace(long clusterCount)
        {
            lock (this.ThisLock)
            {
                if (!_spaceClustersInitialized)
                {
                    long i = 0;

                    foreach (var clusters in _settings.ClustersIndex.Values)
                    {
                        foreach (var cluster in clusters.Indexes)
                        {
                            if (i < cluster)
                            {
                                while (i < cluster)
                                {
                                    _spaceClusters.Add(i);
                                    i++;
                                }

                                i++;
                            }
                            else if (cluster < i)
                            {
                                _spaceClusters.Remove(cluster);
                            }
                            else
                            {
                                i++;
                            }
                        }
                    }

                    _spaceClusters.TrimExcess();

                    _spaceClustersInitialized = true;
                }

                {
                    long fileEndCluster = ((this.Size + CacheManager.ClusterSize - 1) / CacheManager.ClusterSize) - 1;
                    long endCluster = this.GetEndCluster();
                    long j = endCluster + 1;
                    long count = fileEndCluster - endCluster;

                    for (int k = _spaceClusters.Count; k < count && k < clusterCount; k++, j++)
                    {
                        _spaceClusters.Add(j);
                    }
                }
            }
        }

        private void CreatingSpace(long clusterCount)
        {
            lock (this.ThisLock)
            {
                this.CheckSpace(clusterCount);
                if (clusterCount <= _spaceClusters.Count) return;

                var usingHeaders = new HashSet<Key>();
                usingHeaders.UnionWith(_lockedKeys.Keys);

                var removeKeys = _settings.ClustersIndex.Keys
                    .Where(n => !usingHeaders.Contains(n))
                    .ToList();

                removeKeys.Sort((x, y) =>
                {
                    var xc = _settings.ClustersIndex[x];
                    var yc = _settings.ClustersIndex[y];

                    return xc.UpdateTime.CompareTo(yc.UpdateTime);
                });

                foreach (var key in removeKeys)
                {
                    if (clusterCount <= _spaceClusters.Count) return;

                    this.Remove(key);
                }
            }
        }

        public void Lock(Key key)
        {
            lock (this.ThisLock)
            {
                if (_lockedKeys.ContainsKey(key))
                {
                    _lockedKeys[key]++;
                }
                else
                {
                    _lockedKeys[key] = 1;
                }
            }
        }

        public void Unlock(Key key)
        {
            lock (this.ThisLock)
            {
                if (_lockedKeys.ContainsKey(key))
                {
                    if (--_lockedKeys[key] == 0)
                    {
                        _lockedKeys.Remove(key);
                    }
                }
            }
        }

        private long GetEndCluster()
        {
            lock (this.ThisLock)
            {
                long endCluster = -1;

                foreach (var clusters in _settings.ClustersIndex.Values)
                {
                    foreach (var cluster in clusters.Indexes)
                    {
                        if (endCluster < cluster)
                        {
                            endCluster = cluster;
                        }
                    }
                }

                return endCluster;
            }
        }

        protected virtual void OnSetKeyEvent(IEnumerable<Key> keys)
        {
            if (_setKeyEvent != null)
            {
                _setKeyEvent(this, keys);
            }
        }

        protected virtual void OnRemoveKeyEvent(IEnumerable<Key> keys)
        {
            if (_removeKeyEvent != null)
            {
                _removeKeyEvent(this, keys);
            }
        }

        public int GetLength(Key key)
        {
            lock (this.ThisLock)
            {
                if (_settings.ClustersIndex.ContainsKey(key))
                {
                    return _settings.ClustersIndex[key].Length;
                }

                throw new KeyNotFoundException();
            }
        }

        public bool Contains(Key key)
        {
            lock (this.ThisLock)
            {
                if (_settings.ClustersIndex.ContainsKey(key))
                {
                    return true;
                }

                return false;
            }
        }

        public void Remove(Key key)
        {
            bool flag = false;

            lock (this.ThisLock)
            {
                Clusters clusters = null;

                if (_settings.ClustersIndex.TryGetValue(key, out clusters))
                {
                    _settings.ClustersIndex.Remove(key);
                    _spaceClusters.UnionWith(clusters.Indexes);

                    flag = true;
                }
            }

            if (flag) this.OnRemoveKeyEvent(new Key[] { key });
        }

        public void Resize(long size)
        {
            lock (this.ThisLock)
            {
                size = (long)Math.Min(size, NetworkConverter.FromSizeString("16TB"));

                long cc = 256 * 1024 * 1024;
                size = (long)((size + (cc - 1)) / cc) * cc;

                foreach (var key in _settings.ClustersIndex.Keys.ToArray()
                    .Where(n => _settings.ClustersIndex[n].Indexes.Any(o => size < ((o + 1) * CacheManager.ClusterSize)))
                    .ToArray())
                {
                    this.Remove(key);
                }

                long count = (size + (long)CacheManager.ClusterSize - 1) / (long)CacheManager.ClusterSize;
                _settings.Size = count * CacheManager.ClusterSize;
                _fileStream.SetLength(Math.Min(_settings.Size, _fileStream.Length));

                _spaceClustersInitialized = false;
                _spaceClusters.Clear();

                _resetEvent.Set();
            }
        }

        public void CheckInternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            List<Key> list = null;

            lock (this.ThisLock)
            {
                list = new List<Key>(_settings.ClustersIndex.Keys.Randomize());
            }

            int badBlockCount = 0;
            int checkedBlockCount = 0;
            int blockCount = list.Count;
            bool isStop = false;

            getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);

            if (isStop) return;

            foreach (var item in list)
            {
                checkedBlockCount++;
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = this[item];
                }
                catch (Exception)
                {
                    badBlockCount++;
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }

                if (checkedBlockCount % 8 == 0)
                    getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);

                if (isStop) return;
            }

            getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);
        }

        public ArraySegment<byte> this[Key key]
        {
            get
            {
                lock (this.ThisLock)
                {
                    Clusters clusters = null;

                    if (_settings.ClustersIndex.TryGetValue(key, out clusters))
                    {
                        if ((clusters.Indexes[0] * CacheManager.ClusterSize) > _fileStream.Length)
                        {
                            this.Remove(key);

                            throw new BlockNotFoundException();
                        }

                        clusters.UpdateTime = DateTime.UtcNow;

                        byte[] buffer = _bufferManager.TakeBuffer(clusters.Length);

                        try
                        {
                            for (int i = 0, remain = clusters.Length; i < clusters.Indexes.Length; i++, remain -= CacheManager.ClusterSize)
                            {
                                try
                                {
                                    if ((clusters.Indexes[i] * CacheManager.ClusterSize) > _fileStream.Length)
                                    {
                                        this.Remove(key);

                                        throw new BlockNotFoundException();
                                    }

                                    int length = Math.Min(remain, CacheManager.ClusterSize);
                                    _fileStream.Seek(clusters.Indexes[i] * CacheManager.ClusterSize, SeekOrigin.Begin);
                                    _fileStream.Read(buffer, CacheManager.ClusterSize * i, length);
                                }
                                catch (EndOfStreamException)
                                {
                                    this.Remove(key);

                                    throw new BlockNotFoundException();
                                }
                                catch (IOException)
                                {
                                    this.Remove(key);

                                    throw new BlockNotFoundException();
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    this.Remove(key);

                                    throw new BlockNotFoundException();
                                }
                            }

                            if (key.HashAlgorithm == HashAlgorithm.Sha512)
                            {
                                if (!Collection.Equals(Sha512.ComputeHash(buffer, 0, clusters.Length), key.Hash))
                                {
                                    this.Remove(key);

                                    throw new BlockNotFoundException();
                                }
                            }

                            return new ArraySegment<byte>(buffer, 0, clusters.Length);
                        }
                        catch (Exception)
                        {
                            _bufferManager.ReturnBuffer(buffer);

                            throw;
                        }
                    }

                    throw new BlockNotFoundException();
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value.Count > 1024 * 1024 * 32) throw new BadBlockException();

                    if (key.HashAlgorithm == HashAlgorithm.Sha512)
                    {
                        if (!Collection.Equals(Sha512.ComputeHash(value), key.Hash)) throw new BadBlockException();
                    }

                    if (this.Contains(key)) return;

                    List<long> clusterList = new List<long>();

                    try
                    {
                        int count = (value.Count + CacheManager.ClusterSize - 1) / CacheManager.ClusterSize;

                        if (_spaceClusters.Count < count)
                        {
                            this.CreatingSpace(8192); // 32KB(cluster size) * 8192(clusters count) = 256MB
                        }

                        if (_spaceClusters.Count < count) throw new SpaceNotFoundException();

                        clusterList.AddRange(_spaceClusters.Take(count));
                        _spaceClusters.ExceptWith(clusterList);

                        for (int i = 0, remain = value.Count; i < clusterList.Count && 0 < remain; i++, remain -= CacheManager.ClusterSize)
                        {
                            if ((_fileStream.Length < ((clusterList[i] + 1) * CacheManager.ClusterSize)))
                            {
                                long endCluster = this.GetEndCluster();
                                long size = (endCluster + 1) * CacheManager.ClusterSize;
                                long space = 1024 * 1024 * 256;

                                size = (((size + space - 1) / space) + 1) * space;
                                _fileStream.SetLength(Math.Min(size, this.Size));
                            }

                            int length = Math.Min(remain, CacheManager.ClusterSize);
                            _fileStream.Seek(clusterList[i] * CacheManager.ClusterSize, SeekOrigin.Begin);
                            _fileStream.Write(value.Array, CacheManager.ClusterSize * i, length);
                        }

                        _fileStream.Flush();
                    }
                    catch (SpaceNotFoundException e)
                    {
                        Log.Error(e);

                        throw e;
                    }
                    catch (IOException e)
                    {
                        Log.Error(e);

                        throw e;
                    }

                    var clusters = new Clusters();
                    clusters.Indexes = clusterList.ToArray();
                    clusters.Length = value.Count;
                    clusters.UpdateTime = DateTime.UtcNow;
                    _settings.ClustersIndex[key] = clusters;
                }

                this.OnSetKeyEvent(new Key[] { key });
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                _resetEvent.Set();
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

        public Key[] ToArray()
        {
            lock (this.ThisLock)
            {
                List<Key> list = new List<Key>();

                list.AddRange(_settings.ClustersIndex.Keys);

                return list.ToArray();
            }
        }

        #region IEnumerable<Key>

        public IEnumerator<Key> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var item in _settings.ClustersIndex.Keys)
                {
                    yield return item;
                }
            }
        }

        #endregion

        #region IEnumerable

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_fileStream != null)
                {
                    try
                    {
                        _fileStream.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _fileStream = null;
                }
            }
        }

        private class Settings : Library.Configuration.SettingsBase
        {
            private object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingsContext>() { 
                    new Library.Configuration.SettingsContext<LockedDictionary<Key, Clusters>>() { Name = "ClustersIndex", Value = new LockedDictionary<Key, Clusters>() },
                    new Library.Configuration.SettingsContext<long>() { Name = "Size", Value = (long)1024 * 1024 * 1024 * 50 },
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

            public LockedDictionary<Key, Clusters> ClustersIndex
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedDictionary<Key, Clusters>)this["ClustersIndex"];
                    }
                }
            }

            public long Size
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (long)this["Size"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["Size"] = value;
                    }
                }
            }
        }

        [DataContract(Name = "Clusters", Namespace = "http://Library/Net/Lair/CacheManager")]
        private class Clusters : IThisLock
        {
            private long[] _indexes;
            private int _length;
            private DateTime _updateTime = DateTime.UtcNow;

            private object _thisLock;
            private static object _thisStaticLock = new object();

            [DataMember(Name = "Indexs")]
            public long[] Indexes
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _indexes;
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        _indexes = value;
                    }
                }
            }

            [DataMember(Name = "Length")]
            public int Length
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _length;
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        _length = value;
                    }
                }
            }

            [DataMember(Name = "UpdateTime")]
            public DateTime UpdateTime
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _updateTime;
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        _updateTime = value;
                    }
                }
            }

            #region IThisLock

            public object ThisLock
            {
                get
                {
                    lock (_thisStaticLock)
                    {
                        if (_thisLock == null)
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
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

    [Serializable]
    class CacheManagerException : ManagerException
    {
        public CacheManagerException() : base() { }
        public CacheManagerException(string message) : base(message) { }
        public CacheManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class SpaceNotFoundException : CacheManagerException
    {
        public SpaceNotFoundException() : base() { }
        public SpaceNotFoundException(string message) : base(message) { }
        public SpaceNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class BlockNotFoundException : CacheManagerException
    {
        public BlockNotFoundException() : base() { }
        public BlockNotFoundException(string message) : base(message) { }
        public BlockNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class StopException : CacheManagerException
    {
        public StopException() : base() { }
        public StopException(string message) : base(message) { }
        public StopException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class BadBlockException : CacheManagerException
    {
        public BadBlockException() : base() { }
        public BadBlockException(string message) : base(message) { }
        public BadBlockException(string message, Exception innerException) : base(message, innerException) { }
    }
}
