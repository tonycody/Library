using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using Library.Collections;
using Library.Compression;
using Library.Correction;
using Library.Security;

namespace Library.Net.Outopos
{
    interface ISetOperators<T>
    {
        IEnumerable<T> IntersectFrom(IEnumerable<T> collection);
        IEnumerable<T> ExceptFrom(IEnumerable<T> collection);
    }

    class CacheManager : ManagerBase, Library.Configuration.ISettings, ISetOperators<Key>, IEnumerable<Key>, IThisLock
    {
        private FileStream _fileStream;
        private BitmapManager _bitmapManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private bool _spaceSectorsInitialized;
        private SortedSet<long> _spaceSectors = new SortedSet<long>();

        private long _lockSpace;
        private long _freeSpace;

        private Dictionary<Key, int> _lockedKeys = new Dictionary<Key, int>();

        private WatchTimer _watchTimer;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public static readonly int SectorSize = 1024 * 8;
        public static readonly int SpaceSectorCount = 128 * 1024; // 1MB * 1024 = 1024MB

        private int _threadCount = 2;

        public CacheManager(string cachePath, BitmapManager bitmapManager, BufferManager bufferManager)
        {
            _fileStream = new FileStream(cachePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.None);
            _bitmapManager = bitmapManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 4);

            _watchTimer = new WatchTimer(this.WatchTimer, Timeout.Infinite);
        }

        private void WatchTimer()
        {
            this.CheckInformation();
        }

        private void CheckInformation()
        {
            lock (this.ThisLock)
            {
                try
                {
                    var usingKeys = new SortedSet<Key>(new KeyComparer());
                    usingKeys.UnionWith(_lockedKeys.Keys);

                    long size = 0;

                    foreach (var key in usingKeys)
                    {
                        ClusterInfo clusterInfo;

                        if (_settings.ClusterIndex.TryGetValue(key, out clusterInfo))
                        {
                            size += clusterInfo.Indexes.Length * CacheManager.SectorSize;
                        }
                    }

                    _lockSpace = size;
                    _freeSpace = this.Size - size;
                }
                catch (Exception)
                {

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
                    return _settings.ClusterIndex.Count;
                }
            }
        }

        private void CheckSpace(int sectorCount)
        {
            lock (this.ThisLock)
            {
                if (!_spaceSectorsInitialized)
                {
                    _bitmapManager.SetLength((this.Size + ((long)CacheManager.SectorSize - 1)) / (long)CacheManager.SectorSize);

                    foreach (var clusterInfo in _settings.ClusterIndex.Values)
                    {
                        foreach (var sector in clusterInfo.Indexes)
                        {
                            _bitmapManager.Set(sector, true);
                        }
                    }

                    _spaceSectorsInitialized = true;
                }

                if (_spaceSectors.Count < sectorCount)
                {
                    for (long i = 0, count = _bitmapManager.Length; i < count; i++)
                    {
                        if (!_bitmapManager.Get(i))
                        {
                            _spaceSectors.Add(i);
                            if (_spaceSectors.Count >= sectorCount) break;
                        }
                    }
                }
            }
        }

        private void CreatingSpace(int sectorCount)
        {
            lock (this.ThisLock)
            {
                this.CheckSpace(sectorCount);
                if (sectorCount <= _spaceSectors.Count) return;

                var usingKeys = new SortedSet<Key>(new KeyComparer());
                usingKeys.UnionWith(_lockedKeys.Keys);

                var removePairs = _settings.ClusterIndex
                    .Where(n => !usingKeys.Contains(n.Key))
                    .ToList();

                removePairs.Sort((x, y) =>
                {
                    return x.Value.UpdateTime.CompareTo(y.Value.UpdateTime);
                });

                foreach (var key in removePairs.Select(n => n.Key))
                {
                    if (sectorCount <= _spaceSectors.Count) break;

                    this.Remove(key);
                }
            }
        }

        public void Lock(Key key)
        {
            lock (this.ThisLock)
            {
                int count;

                if (_lockedKeys.TryGetValue(key, out count))
                {
                    _lockedKeys[key] = ++count;
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
                int count;
                if (!_lockedKeys.TryGetValue(key, out count)) throw new KeyNotFoundException();

                count--;

                if (count == 0)
                {
                    _lockedKeys.Remove(key);
                }
                else
                {
                    _lockedKeys[key] = count;
                }
            }
        }

        public int GetLength(Key key)
        {
            lock (this.ThisLock)
            {
                if (_settings.ClusterIndex.ContainsKey(key))
                {
                    return _settings.ClusterIndex[key].Length;
                }

                throw new KeyNotFoundException();
            }
        }

        public bool Contains(Key key)
        {
            lock (this.ThisLock)
            {
                if (_settings.ClusterIndex.ContainsKey(key))
                {
                    return true;
                }

                return false;
            }
        }

        public IEnumerable<Key> IntersectFrom(IEnumerable<Key> collection)
        {
            lock (this.ThisLock)
            {
                foreach (var key in collection)
                {
                    if (_settings.ClusterIndex.ContainsKey(key))
                    {
                        yield return key;
                    }
                }
            }
        }

        public IEnumerable<Key> ExceptFrom(IEnumerable<Key> collection)
        {
            lock (this.ThisLock)
            {
                foreach (var key in collection)
                {
                    if (!_settings.ClusterIndex.ContainsKey(key))
                    {
                        yield return key;
                    }
                }
            }
        }

        public void Remove(Key key)
        {
            lock (this.ThisLock)
            {
                ClusterInfo clusterInfo = null;

                if (_settings.ClusterIndex.TryGetValue(key, out clusterInfo))
                {
                    _settings.ClusterIndex.Remove(key);

                    foreach (var sector in clusterInfo.Indexes)
                    {
                        _bitmapManager.Set(sector, false);
                        if (_spaceSectors.Count < CacheManager.SpaceSectorCount) _spaceSectors.Add(sector);
                    }
                }
            }
        }

        public void Resize(long size)
        {
            lock (this.ThisLock)
            {
                size = (long)Math.Min(size, NetworkConverter.FromSizeString("256TB"));

                long unit = 256 * 1024 * 1024;
                size = ((size + (unit - 1)) / unit) * unit;

                foreach (var key in _settings.ClusterIndex.Keys.ToArray()
                    .Where(n => _settings.ClusterIndex[n].Indexes.Any(point => size < (point * CacheManager.SectorSize) + CacheManager.SectorSize))
                    .ToArray())
                {
                    this.Remove(key);
                }

                _settings.Size = ((size + ((long)CacheManager.SectorSize - 1)) / (long)CacheManager.SectorSize) * CacheManager.SectorSize;
                _fileStream.SetLength(Math.Min(_settings.Size, _fileStream.Length));

                _spaceSectors.Clear();
                _spaceSectorsInitialized = false;
            }
        }

        public ArraySegment<byte> this[Key key]
        {
            get
            {
                lock (this.ThisLock)
                {
                    {
                        ClusterInfo clusterInfo = null;

                        if (_settings.ClusterIndex.TryGetValue(key, out clusterInfo))
                        {
                            clusterInfo.UpdateTime = DateTime.UtcNow;

                            byte[] buffer = _bufferManager.TakeBuffer(clusterInfo.Length);

                            try
                            {
                                for (int i = 0, remain = clusterInfo.Length; i < clusterInfo.Indexes.Length; i++, remain -= CacheManager.SectorSize)
                                {
                                    try
                                    {
                                        long posision = clusterInfo.Indexes[i] * CacheManager.SectorSize;

                                        if (posision > _fileStream.Length)
                                        {
                                            this.Remove(key);

                                            throw new BlockNotFoundException();
                                        }

                                        if (_fileStream.Position != posision)
                                        {
                                            _fileStream.Seek(posision, SeekOrigin.Begin);
                                        }

                                        int length = Math.Min(remain, CacheManager.SectorSize);
                                        _fileStream.Read(buffer, CacheManager.SectorSize * i, length);
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
                                    if (!Unsafe.Equals(Sha512.ComputeHash(buffer, 0, clusterInfo.Length), key.Hash))
                                    {
                                        this.Remove(key);

                                        throw new BlockNotFoundException();
                                    }
                                }
                                else
                                {
                                    throw new FormatException();
                                }

                                return new ArraySegment<byte>(buffer, 0, clusterInfo.Length);
                            }
                            catch (Exception)
                            {
                                _bufferManager.ReturnBuffer(buffer);

                                throw;
                            }
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
                        if (!Unsafe.Equals(Sha512.ComputeHash(value), key.Hash)) throw new BadBlockException();
                    }
                    else
                    {
                        throw new FormatException();
                    }

                    if (this.Contains(key)) return;

                    List<long> sectorList = null;

                    try
                    {
                        int count = (value.Count + (CacheManager.SectorSize - 1)) / CacheManager.SectorSize;

                        sectorList = new List<long>(count);

                        if (_spaceSectors.Count < count)
                        {
                            this.CreatingSpace(CacheManager.SpaceSectorCount);
                        }

                        if (_spaceSectors.Count < count) throw new SpaceNotFoundException();

                        sectorList.AddRange(_spaceSectors.Take(count));

                        foreach (var sector in sectorList)
                        {
                            _bitmapManager.Set(sector, true);
                            _spaceSectors.Remove(sector);
                        }

                        for (int i = 0, remain = value.Count; i < sectorList.Count && 0 < remain; i++, remain -= CacheManager.SectorSize)
                        {
                            long posision = sectorList[i] * CacheManager.SectorSize;

                            if ((_fileStream.Length < posision + CacheManager.SectorSize))
                            {
                                int unit = 1024 * 1024 * 256;// 256MB
                                long size = (((posision + CacheManager.SectorSize) + (unit - 1)) / unit) * unit;

                                _fileStream.SetLength(Math.Min(size, this.Size));
                            }

                            if (_fileStream.Position != posision)
                            {
                                _fileStream.Seek(posision, SeekOrigin.Begin);
                            }

                            int length = Math.Min(remain, CacheManager.SectorSize);
                            _fileStream.Write(value.Array, value.Offset + (CacheManager.SectorSize * i), length);
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

                    var clusterInfo = new ClusterInfo();
                    clusterInfo.Indexes = sectorList.ToArray();
                    clusterInfo.Length = value.Count;
                    clusterInfo.UpdateTime = DateTime.UtcNow;
                    _settings.ClusterIndex[key] = clusterInfo;
                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                _watchTimer.Change(new TimeSpan(0, 0, 0), new TimeSpan(0, 5, 0));
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
                return _settings.ClusterIndex.Keys.ToArray();
            }
        }

        #region IEnumerable<Key>

        public IEnumerator<Key> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var key in _settings.ClusterIndex.Keys)
                {
                    yield return key;
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

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<LockedHashDictionary<Key, ClusterInfo>>() { Name = "ClusterIndex", Value = new LockedHashDictionary<Key, ClusterInfo>() },
                    new Library.Configuration.SettingContent<long>() { Name = "Size", Value = (long)1024 * 1024 * 1024 * 8 },
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

            public LockedHashDictionary<Key, ClusterInfo> ClusterIndex
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<Key, ClusterInfo>)this["ClusterIndex"];
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

        [DataContract(Name = "ClusterInfo", Namespace = "http://Library/Net/Outopos/CacheManager")]
        private class ClusterInfo
        {
            private long[] _indexes;
            private int _length;
            private DateTime _updateTime;

            [DataMember(Name = "Indexes")]
            public long[] Indexes
            {
                get
                {
                    return _indexes;
                }
                set
                {
                    _indexes = value;
                }
            }

            [DataMember(Name = "Length")]
            public int Length
            {
                get
                {
                    return _length;
                }
                set
                {
                    _length = value;
                }
            }

            [DataMember(Name = "UpdateTime")]
            public DateTime UpdateTime
            {
                get
                {
                    return _updateTime;
                }
                set
                {
                    var utc = value.ToUniversalTime();
                    _updateTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
            }
        }

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

                if (_watchTimer != null)
                {
                    try
                    {
                        _watchTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _watchTimer = null;
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
