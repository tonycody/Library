using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using Library.Collections;
using Library.Compression;
using Library.Correction;
using Library.Io;

namespace Library.Net.Amoeba
{
    public delegate void CheckBlocksProgressEventHandler(object sender, int badBlockCount, int checkedBlockCount, int blockCount, out bool isStop);
    delegate void SetKeyEventHandler(object sender, IEnumerable<Key> keys);
    delegate void RemoveShareEventHandler(object sender, string path);
    delegate void RemoveKeyEventHandler(object sender, IEnumerable<Key> keys);
    delegate bool WatchEventHandler(object sender);

    class CacheManager : ManagerBase, Library.Configuration.ISettings, IEnumerable<Key>, IThisLock
    {
        private Settings _settings;
        private FileStream _fileStream = null;
        private BufferManager _bufferManager;
        private HashSet<long> _spaceClusters;
        private Dictionary<int, string> _ids = new Dictionary<int, string>();
        private volatile Dictionary<Key, string> _shareIndexLink = null;
        private volatile HashSet<Key> _shareIndexHashSet = null;
        private int _id = 0;

        private LockedDictionary<Key, int> _lockedKeys = new LockedDictionary<Key, int>();

        internal SetKeyEventHandler SetKeyEvent;
        internal RemoveShareEventHandler RemoveShareEvent;
        internal RemoveKeyEventHandler RemoveKeyEvent;

        private bool _disposed = false;
        private object _thisLock = new object();
        public const int ClusterSize = 1024 * 32;

        public CacheManager(string cachePath, BufferManager bufferManager)
        {
            _fileStream = new FileStream(cachePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            _settings = new Settings();
            _bufferManager = bufferManager;
            _spaceClusters = new HashSet<long>();
        }

        public IEnumerable<Seed> CacheSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.SeedInformation
                        .Where(n => n.Path == null)
                        .Select(n => n.Seed)
                        .ToArray();
                }
            }
        }

        public IEnumerable<Seed> ShareSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.SeedInformation
                        .Where(n => n.Path != null)
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

                    foreach (var id in _ids)
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        var item = _settings.ShareIndex[id.Value];
                        contexts.Add(new InformationContext("Id", id.Key));
                        contexts.Add(new InformationContext("Path", id.Value));
                        contexts.Add(new InformationContext("BlockCount", item.KeyAndCluster.Count));

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
                        if (index > 100)
                            throw;
                    }
                }
            }
        }

        private void CheckSpace()
        {
            lock (this.ThisLock)
            {
                HashSet<long> spaceList = new HashSet<long>();
                long i = 0;

                foreach (var clusters in _settings.ClustersIndex.Values)
                {
                    foreach (var cluster in clusters.Indexs)
                    {
                        while (i < cluster)
                        {
                            spaceList.Add(i);
                            i++;
                        }

                        spaceList.Remove(cluster);
                        i++;
                    }
                }

                {
                    long fileEndCluster = ((this.Size + CacheManager.ClusterSize - 1) / CacheManager.ClusterSize) - 1;
                    long endCluster = this.GetEndCluster();
                    long j = endCluster + 1;
                    long count = fileEndCluster - endCluster;

                    for (int k = 0; k < count && k < 8192; k++, j++)
                    {
                        spaceList.Add(j);
                    }
                }

                _spaceClusters.Clear();
                _spaceClusters.UnionWith(spaceList.Take(8192));
            }
        }

        internal void CheckSeeds()
        {
            lock (this.ThisLock)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                Random random = new Random();

                var keys = new HashSet<Key>();

                keys.UnionWith(_settings.ClustersIndex.Keys);

                foreach (var item in _settings.ShareIndex)
                {
                    keys.UnionWith(item.Value.KeyAndCluster.Keys);
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

                    if (!(flag = keys.Contains(info.Seed.Key))) goto Break;

                    foreach (var index in info.Indexs)
                    {
                        foreach (var group in index.Groups)
                        {
                            int count = 0;

                            foreach (var key in group.Keys)
                            {
                                if (keys.Contains(key))
                                {
                                    count++;
                                    if (count >= group.InformationLength) goto End;
                                }
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

        private void CreatingSpace(long clusterCount)
        {
            lock (this.ThisLock)
            {
                this.CheckSpace();
                if (clusterCount <= _spaceClusters.Count) return;

                var usingHeaders = new HashSet<Key>();
                usingHeaders.UnionWith(_lockedKeys.Keys);

                foreach (var info in _settings.SeedInformation)
                {
                    usingHeaders.Add(info.Seed.Key);

                    foreach (var index in info.Indexs)
                    {
                        foreach (var group in index.Groups)
                        {
                            usingHeaders.UnionWith(group.Keys.Take(group.InformationLength));
                        }
                    }
                }

                var removeHeaders = _settings.ClustersIndex.Keys
                    .Where(n => !usingHeaders.Contains(n))
                    .ToList();

                removeHeaders.Sort(new Comparison<Key>((x, y) =>
                {
                    var xc = _settings.ClustersIndex[x];
                    var yc = _settings.ClustersIndex[y];

                    return xc.UpdateTime.CompareTo(yc.UpdateTime);
                }));

                foreach (var header in removeHeaders)
                {
                    if (clusterCount <= _spaceClusters.Count) return;

                    this.Remove(header);
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
                    foreach (var cluster in clusters.Indexs)
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
            if (this.SetKeyEvent != null)
            {
                this.SetKeyEvent(this, keys);
            }
        }

        protected virtual void OnRemoveShareEvent(string path)
        {
            if (this.RemoveShareEvent != null)
            {
                this.RemoveShareEvent(this, path);
            }
        }

        protected virtual void OnRemoveKeyEvent(IEnumerable<Key> keys)
        {
            if (this.RemoveKeyEvent != null)
            {
                this.RemoveKeyEvent(this, keys);
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
                    if (item.Value.KeyAndCluster.ContainsKey(key))
                    {
                        int i = item.Value.KeyAndCluster[key];

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

                _shareIndexHashSetUpdate();

                if (_shareIndexHashSet.Contains(key))
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
                    _spaceClusters.UnionWith(clusters.Indexs);

                    flag = true;
                }
            }

            if (flag) this.OnRemoveKeyEvent(new Key[] { key });
        }

        public void Resize(long size)
        {
            lock (this.ThisLock)
            {
                long cc = 256 * 1024 * 1024;
                size = ((size + (cc - 1)) / cc) * cc;

                foreach (var header in _settings.ClustersIndex.Keys.ToArray()
                    .Where(n => _settings.ClustersIndex[n].Indexs.Any(o => size < ((o + 1) * CacheManager.ClusterSize)))
                    .ToArray())
                {
                    this.Remove(header);
                }

                long count = (size + (long)CacheManager.ClusterSize - 1) / (long)CacheManager.ClusterSize;
                _settings.Size = count * CacheManager.ClusterSize;
                _fileStream.SetLength(Math.Min(_settings.Size, _fileStream.Length));
            }
        }

        public void SetSeed(Seed seed, IEnumerable<Index> indexs)
        {
            lock (this.ThisLock)
            {
                this.SetSeed(seed, null, indexs);
            }
        }

        public void SetSeed(Seed seed, string path, IEnumerable<Index> indexs)
        {
            lock (this.ThisLock)
            {
                if (path != null) this.RemoveCacheSeed(seed);

                if (_settings.SeedInformation.Any(n => n.Seed == seed))
                    return;

                var info = new SeedInformation();
                info.Seed = seed;
                info.Path = path;
                info.Indexs.AddRange(indexs);

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
                    if (info.Path != null || seed != info.Seed) continue;

                    _settings.SeedInformation.RemoveAt(i);
                    i--;
                }
            }
        }

        public void RemoveShareSeed(Seed seed)
        {
            lock (this.ThisLock)
            {
                for (int i = 0; i < _settings.SeedInformation.Count; i++)
                {
                    var info = _settings.SeedInformation[i];
                    if (info.Path == null || seed != info.Seed) continue;

                    foreach (var item in _ids.ToArray())
                    {
                        if (item.Value == info.Path)
                        {
                            this.RemoveShare(item.Key);

                            break;
                        }
                    }

                    _settings.SeedInformation.RemoveAt(i);
                    i--;
                }
            }
        }

        public void CheckBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            var list = this.ToArray();

            int badBlockCount = 0;
            int checkedBlockCount = 0;
            int blockCount = list.Length;
            bool isStop = false;

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

                if (isStop)
                    return;
            }

            getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);

            this.CheckSpace();
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

        private void _shareIndexHashSetUpdate()
        {
            lock (this.ThisLock)
            {
                if (_shareIndexHashSet != null) return;

                _shareIndexHashSet = new HashSet<Key>();

                foreach (var item in _settings.ShareIndex)
                {
                    _shareIndexHashSet.UnionWith(item.Value.KeyAndCluster.Keys);
                }
            }
        }

        public KeyCollection Share(Stream inStream, string path, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (inStream == null) throw new ArgumentNullException("inStream");

            lock (this.ThisLock)
            {
                if (_settings.ShareIndex.ContainsKey(path)) throw new ArgumentException();
            }

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
                _settings.ShareIndex.Add(path, shareIndex);
                _ids.Add(_id++, path);

                _shareIndexLink = null;
                _shareIndexHashSet = null;
            }

            this.OnSetKeyEvent(keys);

            return keys;
        }

        public void RemoveShare(int id)
        {
            List<Key> keys = new List<Key>();
            string path = null;

            lock (this.ThisLock)
            {
                path = _ids[id];
                keys.AddRange(_settings.ShareIndex[path].KeyAndCluster.Keys);

                _settings.ShareIndex.Remove(path);
                _ids.Remove(id);

                _shareIndexLink = null;
                _shareIndexHashSet = null;
            }

            this.OnRemoveShareEvent(path);
            this.OnRemoveKeyEvent(keys);
        }

        public KeyCollection Encoding(Stream inStream,
            CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (inStream == null) throw new ArgumentNullException("inStream");
            if (!Enum.IsDefined(typeof(CompressionAlgorithm), compressionAlgorithm)) throw new ArgumentException("CompressAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(CryptoAlgorithm), cryptoAlgorithm)) throw new ArgumentException("CryptoAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(HashAlgorithm), hashAlgorithm)) throw new ArgumentException("HashAlgorithm に存在しない列挙");

            if (compressionAlgorithm == CompressionAlgorithm.Lzma && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
                IList<Key> keys = new List<Key>();

                try
                {
                    using (var outStream = new CacheManagerStreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                    using (var rijndael = new RijndaelManaged()
                    {
                        KeySize = 256,
                        BlockSize = 256,
                        Mode = CipherMode.CBC,
                        Padding = PaddingMode.PKCS7
                    })
                    using (CryptoStream cs = new CryptoStream(outStream,
                        rijndael.CreateEncryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Write))
                    {
                        Lzma.Compress(inStream, cs, _bufferManager);
                    }
                }
                catch (Exception)
                {
                    foreach (var key in keys)
                    {
                        this.Unlock(key);
                    }
                }

                return new KeyCollection(keys);
            }

            else if (compressionAlgorithm == CompressionAlgorithm.None && cryptoAlgorithm == CryptoAlgorithm.None)
            {
                IList<Key> keys = new List<Key>();

                try
                {
                    using (var outStream = new CacheManagerStreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                    {
                        byte[] buffer = _bufferManager.TakeBuffer(1024 * 1024);

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

            if (compressionAlgorithm == CompressionAlgorithm.Lzma && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
                using (var inStream = new CacheManagerStreamReader(keys, this, _bufferManager))
                using (var rijndael = new RijndaelManaged()
                {
                    KeySize = 256,
                    BlockSize = 256,
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.PKCS7
                })
                using (CryptoStream cs = new CryptoStream(inStream,
                    rijndael.CreateDecryptor(cryptoKey.Take(32).ToArray(), cryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
                {
                    Lzma.Decompress(cs, outStream, _bufferManager);
                }
            }
            else if (compressionAlgorithm == CompressionAlgorithm.None && cryptoAlgorithm == CryptoAlgorithm.None)
            {
                using (var inStream = new CacheManagerStreamReader(keys, this, _bufferManager))
                {
                    byte[] buffer = _bufferManager.TakeBuffer(1024 * 1024);

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
                group.Keys.AddRange(keys.Select(n => n.DeepClone()));

                return group;
            }
            else if (correctionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
            {
                if (keys.Count > 128) throw new ArgumentOutOfRangeException("headers");

                List<ArraySegment<byte>> bufferList = new List<ArraySegment<byte>>();
                List<ArraySegment<byte>> parityBufferList = new List<ArraySegment<byte>>();
                int sumLength = 0;

                try
                {
                    KeyCollection parityHeaders = new KeyCollection();

                    for (int i = 0; i < keys.Count; i++)
                    {
                        ArraySegment<byte> buffer = this[keys[i]];
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

                    for (int i = 0; i < keys.Count; i++)
                    {
                        parityBufferList.Add(new ArraySegment<byte>(_bufferManager.TakeBuffer(blockLength), 0, blockLength));
                    }

                    ReedSolomon reedSolomon = new ReedSolomon(8, bufferList.Count, bufferList.Count + parityBufferList.Count, 8);
                    List<int> intList = new List<int>();

                    for (int i = 0, length = bufferList.Count + parityBufferList.Count; i < length; i++)
                    {
                        intList.Add(keys.Count + i);
                    }

                    Thread thread = new Thread(new ThreadStart(() =>
                    {
                        reedSolomon.Encode(bufferList.ToArray(), parityBufferList.ToArray(), intList.ToArray());
                    }));
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
                    group.Keys.AddRange(keys.Select(n => n.DeepClone()));
                    group.Keys.AddRange(parityHeaders);

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
            if (group.BlockLength > 1024 * 1024 * 16) throw new ArgumentOutOfRangeException();

            if (group.CorrectionAlgorithm == CorrectionAlgorithm.None)
            {
                return new KeyCollection(group.Keys.Select(n => n.DeepClone()));
            }
            else if (group.CorrectionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
            {
                List<ArraySegment<byte>> bufferList = new List<ArraySegment<byte>>();

                try
                {
                    ReedSolomon reedSolomon = new ReedSolomon(8, group.InformationLength, group.Keys.Count, 8);
                    List<int> intList = new List<int>();

                    for (int i = 0; i < group.Keys.Count; i++)
                    {
                        try
                        {
                            ArraySegment<byte> buffer = this[group.Keys[i]];
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
                    }

                    if (bufferList.Count < group.InformationLength)
                        throw new BlockNotFoundException();

                    Thread thread = new Thread(new ThreadStart(() =>
                    {
                        reedSolomon.Decode(bufferList.ToArray(), intList.ToArray());
                    }));
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

                KeyCollection headers = new KeyCollection();

                for (int i = 0; i < group.InformationLength; i++)
                {
                    headers.Add(group.Keys[i]);
                }

                return new KeyCollection(headers.Select(n => n.DeepClone()));
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
                            if ((clusters.Indexs[0] * CacheManager.ClusterSize) > _fileStream.Length)
                            {
                                this.Remove(key);

                                throw new BlockNotFoundException();
                            }

                            clusters.UpdateTime = DateTime.UtcNow;

                            byte[] buffer = _bufferManager.TakeBuffer(clusters.Length);

                            try
                            {
                                for (int i = 0, remain = clusters.Length; i < clusters.Indexs.Length; i++, remain -= CacheManager.ClusterSize)
                                {
                                    try
                                    {
                                        if ((clusters.Indexs[i] * CacheManager.ClusterSize) > _fileStream.Length)
                                        {
                                            this.Remove(key);

                                            throw new BlockNotFoundException();
                                        }

                                        int length = Math.Min(remain, CacheManager.ClusterSize);
                                        _fileStream.Seek(clusters.Indexs[i] * CacheManager.ClusterSize, SeekOrigin.Begin);
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
                                        if (!Collection.Equals(Sha512.ComputeHash(buffer, 0, length), key.Hash))
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
                    if (key.HashAlgorithm == HashAlgorithm.Sha512)
                    {
                        if (!Collection.Equals(Sha512.ComputeHash(value), key.Hash)) return;
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
                    clusters.Indexs = clusterList.ToArray();
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

                this.CheckSpace();

                _ids.Clear();
                _id = 0;

                foreach (var item in _settings.ShareIndex)
                {
                    _ids.Add(_id++, item.Key);
                }

                _shareIndexLink = null;
                _shareIndexHashSet = null;
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

        #region IEnumerable<Header>

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
            lock (this.ThisLock)
            {
                if (_disposed) return;

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

                _disposed = true;
            }
        }

        private class Settings : Library.Configuration.SettingsBase, IThisLock
        {
            private object _thisLock = new object();

            public Settings()
                : base(new List<Library.Configuration.ISettingsContext>() { 
                    new Library.Configuration.SettingsContext<LockedDictionary<string, ShareIndex>>() { Name = "ShareIndex", Value = new LockedDictionary<string, ShareIndex>() },
                    new Library.Configuration.SettingsContext<LockedDictionary<Key, Clusters>>() { Name = "ClustersIndex", Value = new LockedDictionary<Key, Clusters>() },
                    new Library.Configuration.SettingsContext<long>() { Name = "Size", Value = (long)1024 * 1024 * 1024 * 50 },
                    new Library.Configuration.SettingsContext<LockedList<SeedInformation>>() { Name = "SeedInformation", Value = new LockedList<SeedInformation>() },
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

            public LockedDictionary<string, ShareIndex> ShareIndex
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedDictionary<string, ShareIndex>)this["ShareIndex"];
                    }
                }
            }

            public LockedDictionary<Key, Clusters> ClustersIndex
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedDictionary<Key, Clusters>)this["ClustersIndex"];
                    }
                }
            }

            public long Size
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (long)this["Size"];
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        this["Size"] = value;
                    }
                }
            }

            public LockedList<SeedInformation> SeedInformation
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedList<SeedInformation>)this["SeedInformation"];
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

        [DataContract(Name = "SeedInformation", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class SeedInformation : IThisLock
        {
            private Seed _seed;
            private string _path;
            private IndexCollection _indexs;

            private object _thisLock;
            private static object _thisStaticLock = new object();

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
            public IndexCollection Indexs
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_indexs == null)
                            _indexs = new IndexCollection();

                        return _indexs;
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

        [DataContract(Name = "Clusters", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class Clusters : IThisLock
        {
            private long[] _indexs;
            private int _length;
            private object _thisLock;
            private DateTime _updateTime = DateTime.UtcNow;
            private static object _thisStaticLock = new object();

            [DataMember(Name = "Indexs")]
            public long[] Indexs
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _indexs;
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        _indexs = value;
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

        [DataContract(Name = "ShareIndex", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class ShareIndex : IThisLock
        {
            private LockedDictionary<Key, int> _keyAndCluster;
            private int _blockLength;
            private object _thisLock;
            private static object _thisStaticLock = new object();

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
}
