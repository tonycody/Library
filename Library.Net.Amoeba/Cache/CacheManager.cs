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
        private BitmapManager _bitmapManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private bool _spaceSectorsInitialized;
        private SortedSet<long> _spaceSectors = new SortedSet<long>();

        private SortedDictionary<int, string> _ids = new SortedDictionary<int, string>();
        private int _id;

        private bool _shareIndexLinkInitialized;
        private Dictionary<Key, string> _shareIndexLink = new Dictionary<Key, string>();

        private long _lockSpace;
        private long _freeSpace;

        private Dictionary<Key, int> _lockedKeys = new Dictionary<Key, int>();

        private SetKeyEventHandler _setKeyEvent;
        private RemoveShareEventHandler _removeShareEvent;
        private RemoveKeyEventHandler _removeKeyEvent;

        private System.Threading.Timer _watchTimer;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public static readonly int SectorSize = 1024 * 32;

        private int _threadCount = 2;

        public CacheManager(string cachePath, BitmapManager bitmapManager, BufferManager bufferManager)
        {
            _fileStream = new FileStream(cachePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _bitmapManager = bitmapManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

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
                    var usingKeys = new SortedSet<Key>(new KeyComparer());
                    usingKeys.UnionWith(_lockedKeys.Keys);

                    foreach (var seedInfo in _settings.SeedsInformation)
                    {
                        usingKeys.Add(seedInfo.Seed.Key);

                        foreach (var index in seedInfo.Indexes)
                        {
                            foreach (var group in index.Groups)
                            {
                                usingKeys.UnionWith(group.Keys
                                    .Where(n => this.Contains(n))
                                    .Reverse()
                                    .Take(group.InformationLength));
                            }
                        }
                    }

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
                    return _settings.SeedsInformation
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

                    contexts.Add(new InformationContext("SeedCount", _settings.SeedsInformation.Count));
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

                        var shareInfo = _settings.ShareIndex[item.Value];
                        contexts.Add(new InformationContext("BlockCount", shareInfo.Indexes.Count));

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
                    return _settings.ClusterIndex.Count + _settings.ShareIndex.Sum(n => n.Value.Indexes.Count);
                }
            }
        }

        public void CheckSeeds()
        {
            lock (this.ThisLock)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                var pathList = new HashSet<string>();

                pathList.UnionWith(_settings.ShareIndex.Keys);

                for (int i = 0; i < _settings.SeedsInformation.Count; i++)
                {
                    var seedInfo = _settings.SeedsInformation[i];
                    bool flag = true;

                    if (seedInfo.Path != null)
                    {
                        if (!(flag = pathList.Contains(seedInfo.Path))) goto Break;
                    }

                    if (!(flag = this.Contains(seedInfo.Seed.Key))) goto Break;

                    foreach (var index in seedInfo.Indexes)
                    {
                        foreach (var group in index.Groups)
                        {
                            int count = 0;

                            foreach (var key in group.Keys)
                            {
                                if (!this.Contains(key)) continue;

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
                        _settings.SeedsInformation.RemoveAt(i);
                        i--;
                    }
                }

                sw.Stop();
                Debug.WriteLine("CheckSeeds {0}", sw.ElapsedMilliseconds);
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

                foreach (var info in _settings.SeedsInformation)
                {
                    usingKeys.Add(info.Seed.Key);

                    foreach (var index in info.Indexes)
                    {
                        foreach (var group in index.Groups)
                        {
                            usingKeys.UnionWith(group.Keys
                                .Where(n => this.Contains(n))
                                .Reverse()
                                .Take(group.InformationLength));
                        }
                    }
                }

                var removeKeys = _settings.ClusterIndex.Keys
                    .Where(n => !usingKeys.Contains(n))
                    .ToList();

                removeKeys.Sort((x, y) =>
                {
                    var xc = _settings.ClusterIndex[x];
                    var yc = _settings.ClusterIndex[y];

                    return xc.UpdateTime.CompareTo(yc.UpdateTime);
                });

                foreach (var key in removeKeys)
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
                if (_settings.ClusterIndex.ContainsKey(key))
                {
                    return _settings.ClusterIndex[key].Length;
                }

                foreach (var item in _settings.ShareIndex)
                {
                    int i = -1;

                    if (item.Value.Indexes.TryGetValue(key, out i))
                    {
                        var fileLength = new FileInfo(item.Key).Length;
                        return (int)Math.Min(fileLength - ((long)item.Value.BlockLength * i), item.Value.BlockLength);
                    }
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
                    if (_settings.ClusterIndex.ContainsKey(key) || _shareIndexLink.ContainsKey(key))
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
                    if (!(_settings.ClusterIndex.ContainsKey(key) || _shareIndexLink.ContainsKey(key)))
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
                        if (_spaceSectors.Count < 8192) _spaceSectors.Add(sector);
                    }

                    this.OnRemoveKeyEvent(new Key[] { key });
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
                if (_settings.SeedsInformation.Any(n => n.Seed == seed))
                    return;

                var info = new SeedInfo();
                info.Seed = seed;
                info.Path = path;
                info.Indexes.AddRange(indexes);

                _settings.SeedsInformation.Add(info);
            }
        }

        public void RemoveCache(Seed seed)
        {
            lock (this.ThisLock)
            {
                for (int i = 0; i < _settings.SeedsInformation.Count; i++)
                {
                    var info = _settings.SeedsInformation[i];
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

                    _settings.SeedsInformation.RemoveAt(i);
                    i--;
                }
            }
        }

        public void CheckInternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            // 重複するセクタを確保したブロックを検出しRemoveする。
            lock (this.ThisLock)
            {
                _bitmapManager.SetLength((this.Size + ((long)CacheManager.SectorSize - 1)) / (long)CacheManager.SectorSize);

                List<Key> keys = new List<Key>();

                foreach (var pair in _settings.ClusterIndex)
                {
                    var key = pair.Key;
                    var clusterInfo = pair.Value;

                    foreach (var sector in clusterInfo.Indexes)
                    {
                        if (!_bitmapManager.Get(sector))
                        {
                            _bitmapManager.Set(sector, true);
                        }
                        else
                        {
                            keys.Add(key);

                            break;
                        }
                    }
                }

                foreach (var key in keys)
                {
                    _settings.ClusterIndex.Remove(key);
                    this.OnRemoveKeyEvent(new Key[] { key });
                }

                _spaceSectors.Clear();
                _spaceSectorsInitialized = false;
            }

            // 読めないブロックを検出しRemoveする。
            {
                List<Key> list = null;

                lock (this.ThisLock)
                {
                    list = new List<Key>(_settings.ClusterIndex.Keys.Randomize());
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
        }

        public void CheckExternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            // 読めないブロックを検出しRemoveする。
            {
                List<Key> list = null;

                lock (this.ThisLock)
                {
                    list = new List<Key>();

                    foreach (var item in _settings.ShareIndex.Randomize())
                    {
                        list.AddRange(item.Value.Indexes.Keys);
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
        }

        private void _shareIndexLinkUpdate()
        {
            lock (this.ThisLock)
            {
                if (!_shareIndexLinkInitialized)
                {
                    _shareIndexLink.Clear();

                    foreach (var pair in _settings.ShareIndex)
                    {
                        var path = pair.Key;
                        var shareInfo = pair.Value;

                        foreach (var key in shareInfo.Indexes.Keys)
                        {
                            _shareIndexLink[key] = path;
                        }
                    }

                    _shareIndexLinkInitialized = true;
                }
            }
        }

        public KeyCollection Share(Stream inStream, string path, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (inStream == null) throw new ArgumentNullException("inStream");

            byte[] buffer = _bufferManager.TakeBuffer(blockLength);

            KeyCollection keys = new KeyCollection();
            ShareInfo shareInfo = new ShareInfo();
            shareInfo.BlockLength = blockLength;

            while (inStream.Position < inStream.Length)
            {
                int length = (int)Math.Min(inStream.Length - inStream.Position, blockLength);
                inStream.Read(buffer, 0, length);

                Key key = null;

                if (hashAlgorithm == HashAlgorithm.Sha512)
                {
                    key = new Key(Sha512.ComputeHash(buffer, 0, length), HashAlgorithm.Sha512);
                }

                if (!shareInfo.Indexes.ContainsKey(key))
                    shareInfo.Indexes.Add(key, keys.Count);

                keys.Add(key);
            }

            lock (this.ThisLock)
            {
                // 既にShareされている場合は、新しいShareInfoで置き換える。
                if (_settings.ShareIndex.ContainsKey(path))
                {
                    _settings.ShareIndex[path] = shareInfo;

                    _shareIndexLinkInitialized = false;
                }
                else
                {
                    _settings.ShareIndex.Add(path, shareInfo);
                    _ids.Add(_id++, path);

                    _shareIndexLinkInitialized = false;
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
                keys.AddRange(_settings.ShareIndex[path].Indexes.Keys);

                _settings.ShareIndex.Remove(path);
                _ids.Remove(id);

                _shareIndexLinkInitialized = false;

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
                    using (var rijndael = Rijndael.Create())
                    {
                        rijndael.KeySize = 256;
                        rijndael.BlockSize = 256;
                        rijndael.Mode = CipherMode.CBC;
                        rijndael.Padding = PaddingMode.PKCS7;

                        using (var outStream = new CacheManagerStreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                        using (CryptoStream cs = new CryptoStream(outStream, rijndael.CreateEncryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Write))
                        {
                            Xz.Compress(inStream, cs, _bufferManager);
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
                    using (var rijndael = Rijndael.Create())
                    {
                        rijndael.KeySize = 256;
                        rijndael.BlockSize = 256;
                        rijndael.Mode = CipherMode.CBC;
                        rijndael.Padding = PaddingMode.PKCS7;

                        using (var outStream = new CacheManagerStreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                        using (CryptoStream cs = new CryptoStream(outStream, rijndael.CreateEncryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Write))
                        {
                            Lzma.Compress(inStream, cs, _bufferManager);
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
                using (var rijndael = Rijndael.Create())
                {
                    rijndael.KeySize = 256;
                    rijndael.BlockSize = 256;
                    rijndael.Mode = CipherMode.CBC;
                    rijndael.Padding = PaddingMode.PKCS7;

                    using (var inStream = new CacheManagerStreamReader(keys, this, _bufferManager))
                    using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
                    {
                        Xz.Decompress(cs, outStream, _bufferManager);
                    }
                }
            }
            else if (compressionAlgorithm == CompressionAlgorithm.Lzma && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
                using (var rijndael = Rijndael.Create())
                {
                    rijndael.KeySize = 256;
                    rijndael.BlockSize = 256;
                    rijndael.Mode = CipherMode.CBC;
                    rijndael.Padding = PaddingMode.PKCS7;

                    using (var inStream = new CacheManagerStreamReader(keys, this, _bufferManager))
                    using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
                    {
                        Lzma.Decompress(cs, outStream, _bufferManager);
                    }
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

                var bufferArray = new ArraySegment<byte>[keys.Count];
                var parityBufferArray = new ArraySegment<byte>[keys.Count];

                int sumLength = 0;

                try
                {
                    KeyCollection parityKeys = new KeyCollection();

                    for (int i = 0; i < bufferArray.Length; i++)
                    {
                        if (watchEvent(this)) throw new StopException();

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

                            bufferArray[i] = buffer;
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

                    for (int i = 0; i < parityBufferArray.Length; i++)
                    {
                        parityBufferArray[i] = new ArraySegment<byte>(_bufferManager.TakeBuffer(blockLength), 0, blockLength);
                    }

                    var intArray = new int[parityBufferArray.Length];

                    for (int i = 0; i < parityBufferArray.Length; i++)
                    {
                        intArray[i] = bufferArray.Length + i;
                    }

                    using (ReedSolomon8 reedSolomon = new ReedSolomon8(bufferArray.Length, bufferArray.Length + parityBufferArray.Length, _threadCount, _bufferManager))
                    {
                        Exception exception = null;

                        Thread thread = new Thread(() =>
                        {
                            try
                            {
                                reedSolomon.Encode(bufferArray, parityBufferArray, intArray, blockLength);
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

                    for (int i = 0; i < parityBufferArray.Length; i++)
                    {
                        if (hashAlgorithm == HashAlgorithm.Sha512)
                        {
                            var key = new Key(Sha512.ComputeHash(parityBufferArray[i]), hashAlgorithm);

                            lock (this.ThisLock)
                            {
                                this.Lock(key);
                                this[key] = parityBufferArray[i];
                            }

                            parityKeys.Add(key);
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }

                    Group group = new Group();
                    group.CorrectionAlgorithm = correctionAlgorithm;
                    group.InformationLength = bufferArray.Length;
                    group.BlockLength = blockLength;
                    group.Length = sumLength;
                    group.Keys.AddRange(keys);
                    group.Keys.AddRange(parityKeys);

#if DEBUG
                    Debug.WriteLine(string.Format("CacheManager_ParityEncoding {0}", sw.Elapsed.ToString()));
#endif

                    return group;
                }
                finally
                {
                    for (int i = 0; i < bufferArray.Length; i++)
                    {
                        _bufferManager.ReturnBuffer(bufferArray[i].Array);
                    }

                    for (int i = 0; i < parityBufferArray.Length; i++)
                    {
                        _bufferManager.ReturnBuffer(parityBufferArray[i].Array);
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
                var bufferArray = new ArraySegment<byte>[group.InformationLength];

                try
                {
                    var intArray = new int[group.InformationLength];

                    int count = 0;

                    for (int i = 0; i < group.Keys.Count && count < group.InformationLength; i++)
                    {
                        if (watchEvent(this)) throw new StopException();

                        if (!this.Contains(group.Keys[i])) continue;

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

                            intArray[count] = i;
                            bufferArray[count] = buffer;

                            count++;
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

                    if (count < group.InformationLength) throw new BlockNotFoundException();

                    using (ReedSolomon8 reedSolomon = new ReedSolomon8(group.InformationLength, group.Keys.Count, _threadCount, _bufferManager))
                    {
                        Exception exception = null;

                        Thread thread = new Thread(() =>
                        {
                            try
                            {
                                reedSolomon.Decode(bufferArray, intArray, group.BlockLength);
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
                        this[group.Keys[i]] = new ArraySegment<byte>(bufferArray[i].Array, bufferArray[i].Offset, (int)Math.Min(bufferArray[i].Count, length));
                    }
                }
                finally
                {
                    for (int i = 0; i < bufferArray.Length; i++)
                    {
                        _bufferManager.ReturnBuffer(bufferArray[i].Array);
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
                                        if ((clusterInfo.Indexes[i] * CacheManager.SectorSize) > _fileStream.Length)
                                        {
                                            this.Remove(key);

                                            throw new BlockNotFoundException();
                                        }

                                        int length = Math.Min(remain, CacheManager.SectorSize);
                                        _fileStream.Seek(clusterInfo.Indexes[i] * CacheManager.SectorSize, SeekOrigin.Begin);
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

                    {
                        string path = null;

                        _shareIndexLinkUpdate();

                        if (_shareIndexLink.TryGetValue(key, out path))
                        {
                            var shareInfo = _settings.ShareIndex[path];

                            byte[] buffer = _bufferManager.TakeBuffer(shareInfo.BlockLength);

                            try
                            {
                                using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                                {
                                    int i = shareInfo.Indexes[key];

                                    stream.Seek((long)shareInfo.BlockLength * i, SeekOrigin.Begin);

                                    int length = (int)Math.Min(stream.Length - stream.Position, shareInfo.BlockLength);
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

                    List<long> sectorList = null;

                    try
                    {
                        int count = (value.Count + (CacheManager.SectorSize - 1)) / CacheManager.SectorSize;

                        sectorList = new List<long>(count);

                        if (_spaceSectors.Count < count)
                        {
                            this.CreatingSpace(8192);// 256MB
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
                            _fileStream.Write(value.Array, CacheManager.SectorSize * i, length);
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

                _shareIndexLinkInitialized = false;
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
                int count = 0;

                {
                    count += _settings.ClusterIndex.Keys.Count;

                    foreach (var shareInfo in _settings.ShareIndex.Values)
                    {
                        count += shareInfo.Indexes.Keys.Count;
                    }
                }

                var list = new List<Key>(count);

                {
                    list.AddRange(_settings.ClusterIndex.Keys);

                    foreach (var shareInfo in _settings.ShareIndex.Values)
                    {
                        list.AddRange(shareInfo.Indexes.Keys);
                    }
                }

                return list.ToArray();
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

                foreach (var shareInfo in _settings.ShareIndex.Values)
                {
                    foreach (var key in shareInfo.Indexes.Keys)
                    {
                        yield return key;
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

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<LockedHashDictionary<Key, ClusterInfo>>() { Name = "ClustersIndex", Value = new LockedHashDictionary<Key, ClusterInfo>() },
                    new Library.Configuration.SettingContent<long>() { Name = "Size", Value = (long)1024 * 1024 * 1024 * 50 },
                    new Library.Configuration.SettingContent<LockedSortedDictionary<string, ShareInfo>>() { Name = "ShareIndex", Value = new LockedSortedDictionary<string, ShareInfo>() },
                    new Library.Configuration.SettingContent<LockedList<SeedInfo>>() { Name = "SeedInformation", Value = new LockedList<SeedInfo>() },
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
                        return (LockedHashDictionary<Key, ClusterInfo>)this["ClustersIndex"];
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

            public LockedSortedDictionary<string, ShareInfo> ShareIndex
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedSortedDictionary<string, ShareInfo>)this["ShareIndex"];
                    }
                }
            }

            public LockedList<SeedInfo> SeedsInformation
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<SeedInfo>)this["SeedInformation"];
                    }
                }
            }
        }

        [DataContract(Name = "Clusters", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class ClusterInfo
        {
            private long[] _indexes;
            private int _length;
            private DateTime _updateTime = DateTime.UtcNow;

            [DataMember(Name = "Indexs")]
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
                    _updateTime = value;
                }
            }
        }

        [DataContract(Name = "ShareIndex", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class ShareInfo
        {
            private SortedDictionary<Key, int> _indexes;
            private int _blockLength;

            [DataMember(Name = "KeyAndCluster")]
            public SortedDictionary<Key, int> Indexes
            {
                get
                {
                    if (_indexes == null)
                        _indexes = new SortedDictionary<Key, int>(new KeyComparer());

                    return _indexes;
                }
            }

            [DataMember(Name = "BlockLength")]
            public int BlockLength
            {
                get
                {
                    return _blockLength;
                }
                set
                {
                    _blockLength = value;
                }
            }
        }

        [DataContract(Name = "SeedInformation", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class SeedInfo
        {
            private Seed _seed;
            private IndexCollection _indexes;
            private string _path;

            [DataMember(Name = "Seed")]
            public Seed Seed
            {
                get
                {
                    return _seed;
                }
                set
                {
                    _seed = value;
                }
            }

            [DataMember(Name = "Indexs")]
            public IndexCollection Indexes
            {
                get
                {
                    if (_indexes == null)
                        _indexes = new IndexCollection();

                    return _indexes;
                }
            }

            [DataMember(Name = "Path")]
            public string Path
            {
                get
                {
                    return _path;
                }
                set
                {
                    _path = value;
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
