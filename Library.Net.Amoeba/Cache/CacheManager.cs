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

namespace Library.Net.Amoeba
{
    public delegate void CheckBlocksProgressEventHandler(object sender, int badBlockCount, int checkedBlockCount, int blockCount, out bool isStop);
    delegate void SetKeyEventHandler(object sender, IEnumerable<Key> keys);
    delegate void RemoveShareEventHandler(object sender, string path);
    delegate void RemoveKeyEventHandler(object sender, IEnumerable<Key> keys);
    delegate bool WatchEventHandler(object sender);

    class CacheManager : ManagerBase, Library.Configuration.ISettings, IEnumerable<Key>, IThisLock
    {
        private FileStream _fileStream;
        private BufferManager _bufferManager;

        private Lzma _lzma;

        private Settings _settings;

        private SortedSet<long> _spaceClusters;
        private bool _spaceClustersInitialized;
        private Dictionary<int, string> _ids = new Dictionary<int, string>();
        private volatile Dictionary<Key, string> _shareIndexLink;
        private int _id;

        private long _lockSpace;
        private long _freeSpace;

        private LockedDictionary<Key, int> _lockedKeys = new LockedDictionary<Key, int>();

        private SetKeyEventHandler _setKeyEvent;
        private RemoveShareEventHandler _removeShareEvent;
        private RemoveKeyEventHandler _removeKeyEvent;

        private System.Threading.Timer _watchTimer;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public static readonly int ClusterSize = 1024 * 32;

        private int _threadCount = 2;

        public CacheManager(string cachePath, BufferManager bufferManager, Lzma lzma)
        {
            _fileStream = new FileStream(cachePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _bufferManager = bufferManager;
            _lzma = lzma;

            _settings = new Settings(this.ThisLock);

            _spaceClusters = new SortedSet<long>();

            _threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 4);

            _watchTimer = new Timer(this.WatchTimer, null, new TimeSpan(0, 0, 0), new TimeSpan(0, 5, 0));
        }

        public void Rewatch()
        {
            lock (this.ThisLock)
            {
                this.WatchTimer(null);
            }
        }

        private void WatchTimer(object state)
        {
            lock (this.ThisLock)
            {
                try
                {
                    var cachedkeys = new HashSet<Key>();

                    {
                        cachedkeys.UnionWith(_settings.ClustersIndex.Keys);

                        foreach (var item in _settings.ShareIndex)
                        {
                            cachedkeys.UnionWith(item.Value.KeyAndCluster.Keys);
                        }
                    }

                    var usingKeys = new HashSet<Key>();
                    usingKeys.UnionWith(_lockedKeys.Keys);

                    foreach (var info in _settings.SeedInformation)
                    {
                        usingKeys.Add(info.Seed.Key);

                        foreach (var index in info.Indexes)
                        {
                            foreach (var group in index.Groups)
                            {
                                usingKeys.UnionWith(group.Keys
                                    .Where(n => cachedkeys.Contains(n))
                                    .Reverse()
                                    .Take(group.InformationLength));
                            }
                        }
                    }

                    long size = 0;

                    foreach (var item in usingKeys)
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
                catch (Exception)
                {

                }
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

        public event RemoveShareEventHandler RemoveShareEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _removeShareEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _removeShareEvent -= value;
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

        public IEnumerable<Seed> CacheSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.SeedInformation
                        .Select(n => n.Seed)
                        .ToArray();
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

                    contexts.Add(new InformationContext("SeedCount", _settings.SeedInformation.Count));
                    contexts.Add(new InformationContext("ShareCount", _settings.ShareIndex.Count));
                    contexts.Add(new InformationContext("UsingSpace", _fileStream.Length));
                    contexts.Add(new InformationContext("LockSpace", _lockSpace));
                    contexts.Add(new InformationContext("FreeSpace", _freeSpace));

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> ShareInformation
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
                        contexts.Add(new InformationContext("Path", item.Value));

                        var shareIndex = _settings.ShareIndex[item.Value];
                        contexts.Add(new InformationContext("BlockCount", shareIndex.KeyAndCluster.Count));

                        list.Add(new Information(contexts));
                    }

                    return list;
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
                    return _settings.ClustersIndex.Count + _settings.ShareIndex.Sum(n => n.Value.KeyAndCluster.Count);
                }
            }
        }

        public void CheckSeeds()
        {
            lock (this.ThisLock)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                Random random = new Random();

                var cachedkeys = new HashSet<Key>();

                {
                    cachedkeys.UnionWith(_settings.ClustersIndex.Keys);

                    foreach (var item in _settings.ShareIndex)
                    {
                        cachedkeys.UnionWith(item.Value.KeyAndCluster.Keys);
                    }
                }

                var pathList = new HashSet<string>();

                pathList.UnionWith(_settings.ShareIndex.Keys);

                for (int i = 0; i < _settings.SeedInformation.Count; i++)
                {
                    var info = _settings.SeedInformation[i];
                    bool flag = true;

                    if (info.Path != null)
                    {
                        if (!(flag = pathList.Contains(info.Path))) goto Break;
                    }

                    if (!(flag = cachedkeys.Contains(info.Seed.Key))) goto Break;

                    foreach (var index in info.Indexes)
                    {
                        foreach (var group in index.Groups)
                        {
                            int count = 0;

                            foreach (var key in group.Keys)
                            {
                                if (!cachedkeys.Contains(key)) continue;

                                count++;
                                if (count >= group.InformationLength) goto End;
                            }

                            flag = false;
                            goto Break;

                        End: ;
                        }
                    }

                Break: ;

                    if (!flag)
                    {
                        _settings.SeedInformation.RemoveAt(i);
                        i--;
                    }
                }

                sw.Stop();
                Debug.WriteLine("CheckSeeds {0}", sw.ElapsedMilliseconds);
            }
        }

        private void CheckSpace(int clusterCount)
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

                    _spaceClustersInitialized = true;
                }

                if (_spaceClusters.Count < clusterCount)
                {
                    long maxEndCluster = ((this.Size + CacheManager.ClusterSize - 1) / CacheManager.ClusterSize) - 1;
                    long cluster = this.GetEndCluster() + 1;

                    for (int i = (clusterCount - _spaceClusters.Count) - 1; i >= 0 && cluster <= maxEndCluster; i--)
                    {
                        _spaceClusters.Add(cluster++);
                    }
                }

                // _spaceClusters.TrimExcess();
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

        private void CreatingSpace(int clusterCount)
        {
            lock (this.ThisLock)
            {
                this.CheckSpace(clusterCount);
                if (clusterCount <= _spaceClusters.Count) return;

                var cachedkeys = new HashSet<Key>();

                {
                    cachedkeys.UnionWith(_settings.ClustersIndex.Keys);

                    foreach (var item in _settings.ShareIndex)
                    {
                        cachedkeys.UnionWith(item.Value.KeyAndCluster.Keys);
                    }
                }

                var usingKeys = new HashSet<Key>();
                usingKeys.UnionWith(_lockedKeys.Keys);

                foreach (var info in _settings.SeedInformation)
                {
                    usingKeys.Add(info.Seed.Key);

                    foreach (var index in info.Indexes)
                    {
                        foreach (var group in index.Groups)
                        {
                            usingKeys.UnionWith(group.Keys
                                .Where(n => cachedkeys.Contains(n))
                                .Reverse()
                                .Take(group.InformationLength));
                        }
                    }
                }

                var removeKeys = _settings.ClustersIndex.Keys
                    .Where(n => !usingKeys.Contains(n))
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

        protected virtual void OnSetKeyEvent(IEnumerable<Key> keys)
        {
            if (_setKeyEvent != null)
            {
                _setKeyEvent(this, keys);
            }
        }

        protected virtual void OnRemoveShareEvent(string path)
        {
            if (_removeShareEvent != null)
            {
                _removeShareEvent(this, path);
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

                foreach (var item in _settings.ShareIndex)
                {
                    int i = -1;

                    if (item.Value.KeyAndCluster.TryGetValue(key, out i))
                    {
                        if (i < item.Value.KeyAndCluster.Count - 1)
                        {
                            return item.Value.BlockLength;
                        }
                        else
                        {
                            var fileLength = new FileInfo(item.Key).Length;
                            return (int)Math.Min(fileLength - ((long)item.Value.BlockLength * i), item.Value.BlockLength);
                        }
                    }
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

                _shareIndexLinkUpdate();

                if (_shareIndexLink.ContainsKey(key))
                {
                    return true;
                }

                return false;
            }
        }

        public IEnumerable<Key> IntersectFrom(IEnumerable<Key> keys)
        {
            lock (this.ThisLock)
            {
                _shareIndexLinkUpdate();

                foreach (var key in keys)
                {
                    if (_settings.ClustersIndex.ContainsKey(key) || _shareIndexLink.ContainsKey(key))
                    {
                        yield return key;
                    }
                }
            }
        }

        public IEnumerable<Key> ExceptFrom(IEnumerable<Key> keys)
        {
            lock (this.ThisLock)
            {
                _shareIndexLinkUpdate();

                foreach (var key in keys)
                {
                    if (!(_settings.ClustersIndex.ContainsKey(key) || _shareIndexLink.ContainsKey(key)))
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
                Clusters clusters = null;

                if (_settings.ClustersIndex.TryGetValue(key, out clusters))
                {
                    _settings.ClustersIndex.Remove(key);
                    _spaceClusters.UnionWith(clusters.Indexes);

                    this.OnRemoveKeyEvent(new Key[] { key });
                }
            }
        }

        public void Resize(long size)
        {
            lock (this.ThisLock)
            {
                size = (long)Math.Min(size, NetworkConverter.FromSizeString("16TB"));

                long unit = 256 * 1024 * 1024;
                size = (long)((size + (unit - 1)) / unit) * unit;

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
            }
        }

        public void SetSeed(Seed seed, IEnumerable<Index> indexes)
        {
            lock (this.ThisLock)
            {
                this.SetSeed(seed, null, indexes);
            }
        }

        public void SetSeed(Seed seed, string path, IEnumerable<Index> indexes)
        {
            lock (this.ThisLock)
            {
                if (_settings.SeedInformation.Any(n => n.Seed == seed))
                    return;

                var info = new SeedInformation();
                info.Seed = seed;
                info.Path = path;
                info.Indexes.AddRange(indexes);

                _settings.SeedInformation.Add(info);
            }
        }

        public void RemoveCacheSeed(Seed seed)
        {
            lock (this.ThisLock)
            {
                for (int i = 0; i < _settings.SeedInformation.Count; i++)
                {
                    var info = _settings.SeedInformation[i];
                    if (seed != info.Seed) continue;

                    if (info.Path != null)
                    {
                        foreach (var item in _ids.ToArray())
                        {
                            if (item.Value == info.Path)
                            {
                                this.RemoveShare(item.Key);

                                break;
                            }
                        }
                    }

                    _settings.SeedInformation.RemoveAt(i);
                    i--;
                }
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

        public void CheckExternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            List<Key> list = null;

            lock (this.ThisLock)
            {
                list = new List<Key>();

                foreach (var item in _settings.ShareIndex.Randomize())
                {
                    list.AddRange(item.Value.KeyAndCluster.Keys);
                }
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

        private void _shareIndexLinkUpdate()
        {
            lock (this.ThisLock)
            {
                if (_shareIndexLink != null) return;

                _shareIndexLink = new Dictionary<Key, string>();

                foreach (var item in _settings.ShareIndex)
                {
                    foreach (var key in item.Value.KeyAndCluster.Keys)
                    {
                        _shareIndexLink[key] = item.Key;
                    }
                }
            }
        }

        public KeyCollection Share(Stream inStream, string path, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (inStream == null) throw new ArgumentNullException("inStream");

            byte[] buffer = _bufferManager.TakeBuffer(blockLength);

            KeyCollection keys = new KeyCollection();
            ShareIndex shareIndex = new ShareIndex();
            shareIndex.BlockLength = blockLength;

            while (inStream.Position < inStream.Length)
            {
                int length = (int)Math.Min(inStream.Length - inStream.Position, blockLength);
                inStream.Read(buffer, 0, length);

                var key = new Key(Sha512.ComputeHash(buffer, 0, length), HashAlgorithm.Sha512);

                if (!shareIndex.KeyAndCluster.ContainsKey(key))
                    shareIndex.KeyAndCluster.Add(key, keys.Count);

                keys.Add(key);
            }

            lock (this.ThisLock)
            {
                if (_settings.ShareIndex.ContainsKey(path))
                {
                    _settings.ShareIndex[path] = shareIndex;

                    _shareIndexLink = null;
                }
                else
                {
                    _settings.ShareIndex.Add(path, shareIndex);
                    _ids.Add(_id++, path);

                    _shareIndexLink = null;
                }
            }

            this.OnSetKeyEvent(keys);

            return keys;
        }

        public void RemoveShare(int id)
        {
            lock (this.ThisLock)
            {
                string path = _ids[id];

                List<Key> keys = new List<Key>();
                keys.AddRange(_settings.ShareIndex[path].KeyAndCluster.Keys);

                _settings.ShareIndex.Remove(path);
                _ids.Remove(id);

                _shareIndexLink = null;

                this.OnRemoveShareEvent(path);
                this.OnRemoveKeyEvent(keys);
            }
        }

        public KeyCollection Encoding(Stream inStream,
            CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (inStream == null) throw new ArgumentNullException("inStream");
            if (!Enum.IsDefined(typeof(CompressionAlgorithm), compressionAlgorithm)) throw new ArgumentException("CompressAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(CryptoAlgorithm), cryptoAlgorithm)) throw new ArgumentException("CryptoAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(HashAlgorithm), hashAlgorithm)) throw new ArgumentException("HashAlgorithm に存在しない列挙");

            if (compressionAlgorithm == CompressionAlgorithm.Xz && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
#if DEBUG
                Stopwatch sw = new Stopwatch();
                sw.Start();
#endif

                IList<Key> keys = new List<Key>();

                try
                {
                    using (var outStream = new CacheManagerStreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                    using (CryptoStream cs = new CryptoStream(outStream, rijndael.CreateEncryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Write))
                    {
                        Xz.Compress(inStream, cs, _bufferManager);
                    }
                }
                catch (Exception)
                {
                    foreach (var key in keys)
                    {
                        this.Unlock(key);
                    }

                    throw;
                }

#if DEBUG
                Debug.WriteLine(string.Format("CacheManager_Encoding {0}", sw.Elapsed.ToString()));
#endif

                return new KeyCollection(keys);
            }
            else if (compressionAlgorithm == CompressionAlgorithm.Lzma && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
#if DEBUG
                Stopwatch sw = new Stopwatch();
                sw.Start();
#endif

                IList<Key> keys = new List<Key>();

                try
                {
                    using (var outStream = new CacheManagerStreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                    using (CryptoStream cs = new CryptoStream(outStream, rijndael.CreateEncryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Write))
                    {
                        _lzma.Compress(inStream, cs, _bufferManager);
                    }
                }
                catch (Exception)
                {
                    foreach (var key in keys)
                    {
                        this.Unlock(key);
                    }

                    throw;
                }

#if DEBUG
                Debug.WriteLine(string.Format("CacheManager_Encoding {0}", sw.Elapsed.ToString()));
#endif

                return new KeyCollection(keys);
            }
            else if (compressionAlgorithm == CompressionAlgorithm.None && cryptoAlgorithm == CryptoAlgorithm.None)
            {
                IList<Key> keys = new List<Key>();

                try
                {
                    using (var outStream = new CacheManagerStreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                    {
                        byte[] buffer = _bufferManager.TakeBuffer(1024 * 32);

                        try
                        {
                            int length = 0;

                            while (0 < (length = inStream.Read(buffer, 0, buffer.Length)))
                            {
                                outStream.Write(buffer, 0, length);
                            }
                        }
                        finally
                        {
                            _bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                catch (Exception)
                {
                    foreach (var key in keys)
                    {
                        this.Unlock(key);
                    }

                    throw;
                }

                return new KeyCollection(keys);
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public void Decoding(Stream outStream,
            CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey, KeyCollection keys)
        {
            if (outStream == null) throw new ArgumentNullException("outStream");
            if (!Enum.IsDefined(typeof(CompressionAlgorithm), compressionAlgorithm)) throw new ArgumentException("CompressAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(CryptoAlgorithm), cryptoAlgorithm)) throw new ArgumentException("CryptoAlgorithm に存在しない列挙");

            if (compressionAlgorithm == CompressionAlgorithm.Xz && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
                using (var inStream = new CacheManagerStreamReader(keys, this, _bufferManager))
                using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
                {
                    Xz.Decompress(cs, outStream, _bufferManager);
                }
            }
            else if (compressionAlgorithm == CompressionAlgorithm.Lzma && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
                using (var inStream = new CacheManagerStreamReader(keys, this, _bufferManager))
                using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
                {
                    _lzma.Decompress(cs, outStream, _bufferManager);
                }
            }
            else if (compressionAlgorithm == CompressionAlgorithm.None && cryptoAlgorithm == CryptoAlgorithm.None)
            {
                using (var inStream = new CacheManagerStreamReader(keys, this, _bufferManager))
                {
                    byte[] buffer = _bufferManager.TakeBuffer(1024 * 32);

                    try
                    {
                        int length = 0;

                        while (0 != (length = inStream.Read(buffer, 0, buffer.Length)))
                        {
                            outStream.Write(buffer, 0, length);
                        }
                    }
                    finally
                    {
                        _bufferManager.ReturnBuffer(buffer);
                    }
                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        /*
        public Group ParityEncoding(KeyCollection keys, HashAlgorithm hashAlgorithm, int blockLength, CorrectionAlgorithm correctionAlgorithm, WatchEventHandler watchEvent)
        {
            if (correctionAlgorithm == CorrectionAlgorithm.None)
            {
                Group group = new Group();
                group.CorrectionAlgorithm = correctionAlgorithm;
                group.InformationLength = keys.Count;
                group.BlockLength = blockLength;
                group.Length = keys.Sum(n => (long)this.GetLength(n));
                group.Keys.AddRange(keys);

                return group;
            }
            else if (correctionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
            {
#if DEBUG
                Stopwatch sw = new Stopwatch();
                sw.Start();
#endif

                if (keys.Count > 128) throw new ArgumentOutOfRangeException("keys");

                List<ArraySegment<byte>> bufferList = new List<ArraySegment<byte>>();
                List<ArraySegment<byte>> parityBufferList = new List<ArraySegment<byte>>();
                int sumLength = 0;

                try
                {
                    KeyCollection parityHeaders = new KeyCollection();

                    for (int i = 0; i < keys.Count; i++)
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = this[keys[i]];
                            int bufferLength = buffer.Count;

                            sumLength += bufferLength;

                            if (bufferLength > blockLength)
                            {
                                throw new ArgumentOutOfRangeException("blockLength");
                            }
                            else if (bufferLength < blockLength)
                            {
                                ArraySegment<byte> tbuffer = new ArraySegment<byte>(_bufferManager.TakeBuffer(blockLength), 0, blockLength);
                                Array.Copy(buffer.Array, buffer.Offset, tbuffer.Array, tbuffer.Offset, buffer.Count);
                                Array.Clear(tbuffer.Array, tbuffer.Offset + buffer.Count, tbuffer.Count - buffer.Count);
                                _bufferManager.ReturnBuffer(buffer.Array);
                                buffer = tbuffer;
                            }

                            bufferList.Add(buffer);
                        }
                        catch (Exception)
                        {
                            if (buffer.Array != null)
                            {
                                _bufferManager.ReturnBuffer(buffer.Array);
                            }

                            throw;
                        }
                    }

                    for (int i = 0; i < keys.Count; i++)
                    {
                        parityBufferList.Add(new ArraySegment<byte>(_bufferManager.TakeBuffer(blockLength), 0, blockLength));
                    }

                    ReedSolomon reedSolomon = new ReedSolomon(8, bufferList.Count, bufferList.Count + parityBufferList.Count, _threadCount, _bufferManager);
                    List<int> intList = new List<int>();

                    for (int i = keys.Count, length = bufferList.Count + parityBufferList.Count; i < length; i++)
                    {
                        intList.Add(i);
                    }

                    Exception exception = null;

                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            reedSolomon.Encode(bufferList, parityBufferList, intList.ToArray());
                        }
                        catch (Exception e)
                        {
                            exception = e;
                        }
                    });
                    thread.Name = "CacheManager_ReedSolomon.Encode";
                    thread.Start();

                    while (thread.IsAlive)
                    {
                        Thread.Sleep(1000);

                        if (watchEvent(this))
                        {
                            thread.Abort();
                            thread.Join();

                            throw new StopException();
                        }
                    }

                    if (exception != null) throw exception;

                    for (int i = 0; i < parityBufferList.Count; i++)
                    {
                        if (hashAlgorithm == HashAlgorithm.Sha512)
                        {
                            var key = new Key(Sha512.ComputeHash(parityBufferList[i]), hashAlgorithm);

                            lock (this.ThisLock)
                            {
                                this.Lock(key);
                                this[key] = parityBufferList[i];
                            }

                            parityHeaders.Add(key);
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }

                    Group group = new Group();
                    group.CorrectionAlgorithm = correctionAlgorithm;
                    group.InformationLength = bufferList.Count;
                    group.BlockLength = blockLength;
                    group.Length = sumLength;
                    group.Keys.AddRange(keys);
                    group.Keys.AddRange(parityHeaders);

#if DEBUG
                    Debug.WriteLine(string.Format("CacheManager_ParityEncoding {0}:", sw.Elapsed.ToString()));
#endif

                    return group;
                }
                finally
                {
                    for (int i = 0; i < bufferList.Count; i++)
                    {
                        _bufferManager.ReturnBuffer(bufferList[i].Array);
                    }

                    for (int i = 0; i < parityBufferList.Count; i++)
                    {
                        _bufferManager.ReturnBuffer(parityBufferList[i].Array);
                    }
                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public KeyCollection ParityDecoding(Group group, WatchEventHandler watchEvent)
        {
            if (group.BlockLength > 1024 * 1024 * 4) throw new ArgumentOutOfRangeException();

            if (group.CorrectionAlgorithm == CorrectionAlgorithm.None)
            {
                return new KeyCollection(group.Keys);
            }
            else if (group.CorrectionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
            {
                IList<ArraySegment<byte>> bufferList = new List<ArraySegment<byte>>();

                try
                {
                    ReedSolomon reedSolomon = new ReedSolomon(8, group.InformationLength, group.Keys.Count, _threadCount, _bufferManager);
                    List<int> intList = new List<int>();

                    for (int i = 0; i < group.Keys.Count; i++)
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = this[group.Keys[i]];
                            int bufferLength = buffer.Count;

                            if (bufferLength > group.BlockLength)
                            {
                                throw new ArgumentOutOfRangeException("group.BlockLength");
                            }
                            else if (bufferLength < group.BlockLength)
                            {
                                ArraySegment<byte> tbuffer = new ArraySegment<byte>(_bufferManager.TakeBuffer(group.BlockLength), 0, group.BlockLength);
                                Array.Copy(buffer.Array, buffer.Offset, tbuffer.Array, tbuffer.Offset, buffer.Count);
                                Array.Clear(tbuffer.Array, tbuffer.Offset + buffer.Count, tbuffer.Count - buffer.Count);
                                _bufferManager.ReturnBuffer(buffer.Array);
                                buffer = tbuffer;
                            }

                            intList.Add(i);
                            bufferList.Add(buffer);
                        }
                        catch (BlockNotFoundException)
                        {

                        }
                        catch (Exception)
                        {
                            if (buffer.Array != null)
                            {
                                _bufferManager.ReturnBuffer(buffer.Array);
                            }

                            throw;
                        }
                    }

                    if (bufferList.Count < group.InformationLength) throw new BlockNotFoundException();

                    Exception exception = null;

                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            reedSolomon.Decode(ref bufferList, intList.ToArray());
                        }
                        catch (Exception e)
                        {
                            exception = e;
                        }
                    });
                    thread.Name = "CacheManager_ReedSolomon.Decode";
                    thread.Start();

                    while (thread.IsAlive)
                    {
                        Thread.Sleep(1000);

                        if (watchEvent(this))
                        {
                            thread.Abort();
                            thread.Join();

                            throw new StopException();
                        }
                    }

                    if (exception != null) throw exception;

                    long length = group.Length;

                    for (int i = 0; i < group.InformationLength; length -= bufferList[i].Count, i++)
                    {
                        this[group.Keys[i]] = new ArraySegment<byte>(bufferList[i].Array, bufferList[i].Offset, (int)Math.Min(bufferList[i].Count, length));
                    }
                }
                finally
                {
                    for (int i = 0; i < bufferList.Count; i++)
                    {
                        _bufferManager.ReturnBuffer(bufferList[i].Array);
                    }
                }

                KeyCollection keys = new KeyCollection();

                for (int i = 0; i < group.InformationLength; i++)
                {
                    keys.Add(group.Keys[i]);
                }

                return new KeyCollection(keys);
            }
            else
            {
                throw new NotSupportedException();
            }
        }
        */

        public Group ParityEncoding(KeyCollection keys, HashAlgorithm hashAlgorithm, int blockLength, CorrectionAlgorithm correctionAlgorithm, WatchEventHandler watchEvent)
        {
            if (correctionAlgorithm == CorrectionAlgorithm.None)
            {
                Group group = new Group();
                group.CorrectionAlgorithm = correctionAlgorithm;
                group.InformationLength = keys.Count;
                group.BlockLength = blockLength;
                group.Length = keys.Sum(n => (long)this.GetLength(n));
                group.Keys.AddRange(keys);

                return group;
            }
            else if (correctionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
            {
#if DEBUG
                Stopwatch sw = new Stopwatch();
                sw.Start();
#endif

                if (keys.Count > 128) throw new ArgumentOutOfRangeException("keys");

                List<byte[]> bufferList = new List<byte[]>();
                List<byte[]> parityBufferList = new List<byte[]>();
                int sumLength = 0;

                try
                {
                    KeyCollection parityHeaders = new KeyCollection();

                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (watchEvent(this)) throw new StopException();

                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = this[keys[i]];
                            int bufferLength = buffer.Count;

                            sumLength += bufferLength;

                            var target = _bufferManager.TakeBuffer(blockLength);
                            Array.Copy(buffer.Array, buffer.Offset, target, 0, buffer.Count);
                            Array.Clear(target, buffer.Count, target.Length - buffer.Count);

                            bufferList.Add(target);
                        }
                        finally
                        {
                            if (buffer.Array != null)
                            {
                                _bufferManager.ReturnBuffer(buffer.Array);
                            }
                        }
                    }

                    for (int i = 0; i < keys.Count; i++)
                    {
                        parityBufferList.Add(_bufferManager.TakeBuffer(blockLength));
                    }

                    List<int> intList = new List<int>();

                    for (int i = keys.Count, length = bufferList.Count + parityBufferList.Count; i < length; i++)
                    {
                        intList.Add(i);
                    }

                    using (ReedSolomon8 reedSolomon = new ReedSolomon8(bufferList.Count, bufferList.Count + parityBufferList.Count))
                    {
                        Exception exception = null;

                        Thread thread = new Thread(() =>
                        {
                            try
                            {
                                var tempBufferList = bufferList.ToArray();
                                var tempParityBufferList = parityBufferList.ToArray();
                                var tempIntList = intList.ToArray();

                                reedSolomon.Encode(tempBufferList, tempParityBufferList, tempIntList, blockLength);

                                bufferList.Clear();
                                parityBufferList.Clear();
                                intList.Clear();

                                foreach (var buffer in tempBufferList)
                                {
                                    bufferList.Add(buffer);
                                }

                                foreach (var buffer in tempParityBufferList)
                                {
                                    parityBufferList.Add(buffer);
                                }

                                foreach (var index in tempIntList)
                                {
                                    intList.Add(index);
                                }
                            }
                            catch (Exception e)
                            {
                                exception = e;
                            }
                        });
                        thread.Priority = ThreadPriority.Lowest;
                        thread.Name = "CacheManager_ReedSolomon.Encode";
                        thread.Start();

                        while (thread.IsAlive)
                        {
                            Thread.Sleep(1000);

                            if (watchEvent(this))
                            {
                                reedSolomon.Cancel();
                                thread.Join();

                                throw new StopException();
                            }
                        }

                        if (exception != null) throw new StopException("Stop", exception);
                    }

                    for (int i = 0; i < parityBufferList.Count; i++)
                    {
                        if (hashAlgorithm == HashAlgorithm.Sha512)
                        {
                            var key = new Key(Sha512.ComputeHash(parityBufferList[i]), hashAlgorithm);

                            lock (this.ThisLock)
                            {
                                this.Lock(key);
                                this[key] = new ArraySegment<byte>(parityBufferList[i], 0, blockLength);
                            }

                            parityHeaders.Add(key);
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }

                    Group group = new Group();
                    group.CorrectionAlgorithm = correctionAlgorithm;
                    group.InformationLength = bufferList.Count;
                    group.BlockLength = blockLength;
                    group.Length = sumLength;
                    group.Keys.AddRange(keys);
                    group.Keys.AddRange(parityHeaders);

#if DEBUG
                    Debug.WriteLine(string.Format("CacheManager_ParityEncoding {0}", sw.Elapsed.ToString()));
#endif

                    return group;
                }
                finally
                {
                    for (int i = 0; i < bufferList.Count; i++)
                    {
                        _bufferManager.ReturnBuffer(bufferList[i]);
                    }

                    for (int i = 0; i < parityBufferList.Count; i++)
                    {
                        _bufferManager.ReturnBuffer(parityBufferList[i]);
                    }
                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public KeyCollection ParityDecoding(Group group, WatchEventHandler watchEvent)
        {
            if (group.BlockLength > 1024 * 1024 * 4) throw new ArgumentOutOfRangeException();

            if (group.CorrectionAlgorithm == CorrectionAlgorithm.None)
            {
                return new KeyCollection(group.Keys);
            }
            else if (group.CorrectionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
            {
                IList<byte[]> bufferList = new List<byte[]>();

                try
                {
                    List<int> intList = new List<int>();

                    for (int i = 0; bufferList.Count < group.InformationLength && i < group.Keys.Count; i++)
                    {
                        if (watchEvent(this)) throw new StopException();

                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = this[group.Keys[i]];
                            int bufferLength = buffer.Count;

                            var target = _bufferManager.TakeBuffer(group.BlockLength);
                            Array.Copy(buffer.Array, buffer.Offset, target, 0, buffer.Count);
                            Array.Clear(target, buffer.Count, target.Length - buffer.Count);

                            intList.Add(i);
                            bufferList.Add(target);
                        }
                        catch (BlockNotFoundException)
                        {

                        }
                        catch (Exception)
                        {
                            if (buffer.Array != null)
                            {
                                _bufferManager.ReturnBuffer(buffer.Array);
                            }

                            throw;
                        }
                    }

                    if (bufferList.Count < group.InformationLength) throw new BlockNotFoundException();

                    using (ReedSolomon8 reedSolomon = new ReedSolomon8(group.InformationLength, group.Keys.Count))
                    {
                        Exception exception = null;

                        Thread thread = new Thread(() =>
                        {
                            try
                            {
                                var tempBufferList = bufferList.ToArray();
                                var tempIntList = intList.ToArray();

                                reedSolomon.Decode(tempBufferList, tempIntList, group.BlockLength);

                                bufferList.Clear();
                                intList.Clear();

                                foreach (var buffer in tempBufferList)
                                {
                                    bufferList.Add(buffer);
                                }

                                foreach (var index in tempIntList)
                                {
                                    intList.Add(index);
                                }
                            }
                            catch (Exception e)
                            {
                                exception = e;
                            }
                        });
                        thread.Priority = ThreadPriority.Lowest;
                        thread.Name = "CacheManager_ReedSolomon.Decode";
                        thread.Start();

                        while (thread.IsAlive)
                        {
                            Thread.Sleep(1000);

                            if (watchEvent(this))
                            {
                                reedSolomon.Cancel();
                                thread.Join();

                                throw new StopException();
                            }
                        }

                        if (exception != null) throw new StopException("Stop", exception);
                    }

                    long length = group.Length;

                    for (int i = 0; i < group.InformationLength; length -= group.BlockLength, i++)
                    {
                        this[group.Keys[i]] = new ArraySegment<byte>(bufferList[i], 0, (int)Math.Min(group.BlockLength, length));
                    }
                }
                finally
                {
                    for (int i = 0; i < bufferList.Count; i++)
                    {
                        _bufferManager.ReturnBuffer(bufferList[i]);
                    }
                }

                KeyCollection keys = new KeyCollection();

                for (int i = 0; i < group.InformationLength; i++)
                {
                    keys.Add(group.Keys[i]);
                }

                return new KeyCollection(keys);
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public ArraySegment<byte> this[Key key]
        {
            get
            {
                lock (this.ThisLock)
                {
                    {
                        Clusters clusters = null;

                        if (_settings.ClustersIndex.TryGetValue(key, out clusters))
                        {
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
                                    if (!Unsafe.Equals(Sha512.ComputeHash(buffer, 0, clusters.Length), key.Hash))
                                    {
                                        this.Remove(key);

                                        throw new BlockNotFoundException();
                                    }
                                }
                                else
                                {
                                    throw new FormatException();
                                }

                                return new ArraySegment<byte>(buffer, 0, clusters.Length);
                            }
                            catch (Exception)
                            {
                                _bufferManager.ReturnBuffer(buffer);

                                throw;
                            }
                        }
                    }

                    {
                        string path = null;

                        _shareIndexLinkUpdate();

                        if (_shareIndexLink.TryGetValue(key, out path))
                        {
                            var shareIndex = _settings.ShareIndex[path];

                            byte[] buffer = _bufferManager.TakeBuffer(shareIndex.BlockLength);

                            try
                            {
                                using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                                {
                                    int i = shareIndex.KeyAndCluster[key];

                                    stream.Seek((long)shareIndex.BlockLength * i, SeekOrigin.Begin);

                                    int length = (int)Math.Min(stream.Length - stream.Position, shareIndex.BlockLength);
                                    stream.Read(buffer, 0, length);

                                    if (key.HashAlgorithm == HashAlgorithm.Sha512)
                                    {
                                        if (!Unsafe.Equals(Sha512.ComputeHash(buffer, 0, length), key.Hash))
                                        {
                                            foreach (var item in _ids.ToArray())
                                            {
                                                if (item.Value == path)
                                                {
                                                    this.RemoveShare(item.Key);

                                                    break;
                                                }
                                            }

                                            throw new BlockNotFoundException();
                                        }
                                    }
                                    else
                                    {
                                        throw new FormatException();
                                    }

                                    return new ArraySegment<byte>(buffer, 0, length);
                                }
                            }
                            catch (Exception)
                            {
                                _bufferManager.ReturnBuffer(buffer);

                                throw new BlockNotFoundException();
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

                    List<long> clusterList = new List<long>();

                    try
                    {
                        int count = (value.Count + CacheManager.ClusterSize - 1) / CacheManager.ClusterSize;

                        if (_spaceClusters.Count < count)
                        {
                            this.CreatingSpace(8192);// 256MB
                        }

                        if (_spaceClusters.Count < count) throw new SpaceNotFoundException();

                        clusterList.AddRange(_spaceClusters.Take(count));
                        _spaceClusters.ExceptWith(clusterList);

                        for (int i = 0, remain = value.Count; i < clusterList.Count && 0 < remain; i++, remain -= CacheManager.ClusterSize)
                        {
                            long posision = clusterList[i] * CacheManager.ClusterSize;

                            if ((_fileStream.Length < posision + CacheManager.ClusterSize))
                            {
                                int unit = 1024 * 1024 * 256;// 256MB
                                long size = (((posision + CacheManager.ClusterSize) + unit - 1) / unit) * unit;

                                _fileStream.SetLength(Math.Min(size, this.Size));
                            }

                            if (_fileStream.Position != posision)
                            {
                                _fileStream.Seek(posision, SeekOrigin.Begin);
                            }

                            int length = Math.Min(remain, CacheManager.ClusterSize);
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

                    this.OnSetKeyEvent(new Key[] { key });
                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                _ids.Clear();
                _id = 0;

                foreach (var item in _settings.ShareIndex)
                {
                    _ids.Add(_id++, item.Key);
                }

                _shareIndexLink = null;
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

                foreach (var item in _settings.ShareIndex)
                {
                    list.AddRange(item.Value.KeyAndCluster.Keys);
                }

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

                foreach (var item in _settings.ShareIndex)
                {
                    foreach (var item2 in item.Value.KeyAndCluster.Keys)
                    {
                        yield return item2;
                    }
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
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<LockedDictionary<string, ShareIndex>>() { Name = "ShareIndex", Value = new LockedDictionary<string, ShareIndex>() },
                    new Library.Configuration.SettingContent<LockedDictionary<Key, Clusters>>() { Name = "ClustersIndex", Value = new LockedDictionary<Key, Clusters>() },
                    new Library.Configuration.SettingContent<long>() { Name = "Size", Value = (long)1024 * 1024 * 1024 * 50 },
                    new Library.Configuration.SettingContent<LockedList<SeedInformation>>() { Name = "SeedInformation", Value = new LockedList<SeedInformation>() },
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

            public LockedDictionary<string, ShareIndex> ShareIndex
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedDictionary<string, ShareIndex>)this["ShareIndex"];
                    }
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

            public LockedList<SeedInformation> SeedInformation
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<SeedInformation>)this["SeedInformation"];
                    }
                }
            }
        }

        [DataContract(Name = "SeedInformation", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class SeedInformation : IThisLock
        {
            private Seed _seed;
            private string _path;
            private IndexCollection _indexes;

            private volatile object _thisLock;
            private static readonly object _initializeLock = new object();

            [DataMember(Name = "Seed")]
            public Seed Seed
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _seed;
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        _seed = value;
                    }
                }
            }

            [DataMember(Name = "Path")]
            public string Path
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _path;
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        _path = value;
                    }
                }
            }

            [DataMember(Name = "Indexs")]
            public IndexCollection Indexes
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_indexes == null)
                            _indexes = new IndexCollection();

                        return _indexes;
                    }
                }
            }

            #region IThisLock

            public object ThisLock
            {
                get
                {
                    if (_thisLock == null)
                    {
                        lock (_initializeLock)
                        {
                            if (_thisLock == null)
                            {
                                _thisLock = new object();
                            }
                        }
                    }

                    return _thisLock;
                }
            }

            #endregion
        }

        [DataContract(Name = "Clusters", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class Clusters : IThisLock
        {
            private long[] _indexes;
            private int _length;
            private DateTime _updateTime = DateTime.UtcNow;

            private volatile object _thisLock;
            private static readonly object _initializeLock = new object();

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
                    if (_thisLock == null)
                    {
                        lock (_initializeLock)
                        {
                            if (_thisLock == null)
                            {
                                _thisLock = new object();
                            }
                        }
                    }

                    return _thisLock;
                }
            }

            #endregion
        }

        [DataContract(Name = "ShareIndex", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class ShareIndex : IThisLock
        {
            private LockedDictionary<Key, int> _keyAndCluster;
            private int _blockLength;

            private volatile object _thisLock;
            private static readonly object _initializeLock = new object();

            [DataMember(Name = "KeyAndCluster")]
            public LockedDictionary<Key, int> KeyAndCluster
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_keyAndCluster == null)
                            _keyAndCluster = new LockedDictionary<Key, int>();

                        return _keyAndCluster;
                    }
                }
            }

            [DataMember(Name = "BlockLength")]
            public int BlockLength
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _blockLength;
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        _blockLength = value;
                    }
                }
            }

            #region IThisLock

            public object ThisLock
            {
                get
                {
                    if (_thisLock == null)
                    {
                        lock (_initializeLock)
                        {
                            if (_thisLock == null)
                            {
                                _thisLock = new object();
                            }
                        }
                    }

                    return _thisLock;
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
