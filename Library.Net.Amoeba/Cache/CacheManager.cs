using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using Library.Collections;
using Library.Correction;
using System.Diagnostics;
using Library.Io;
using System.Reflection;

namespace Library.Net.Amoeba
{
    delegate void GetUsingKeysEventHandler(object sender, ref IList<Key> keys);
    public delegate void CheckBlocksProgressEventHandler(object sender, int badBlockCount, int checkedBlockCount, int blockCount, out bool isStop);
    delegate void SetKeyEventHandler(object sender, Key key);
    delegate void RemoveKeyEventHandler(object sender, Key key);

    class CacheManager : ManagerBase, Library.Configuration.ISettings, IEnumerable<Key>, IThisLock
    {
        private Settings _settings;
        private FileStream _fileStream = null;
        private string _workDirectory = Path.GetTempPath();
        private BufferManager _bufferManager;
        private HashSet<long> _spaceClusters;
        private Dictionary<string, Stream> _streams = new Dictionary<string, Stream>();
        private CountCache _countCache = new CountCache();
        private Dictionary<int, ShareIndex> _ids = new Dictionary<int, ShareIndex>();
        private int _id = 0;

        internal SetKeyEventHandler SetKeyEvent;
        internal RemoveKeyEventHandler RemoveKeyEvent;

        private bool _disposed = false;
        private object _thisLock = new object();
        public const int ClusterSize = 1024 * 32;

        public GetUsingKeysEventHandler GetUsingKeysEvent;

        public CacheManager(string cachePath, string workDirectory, BufferManager bufferManager)
        {
            _fileStream = new FileStream(cachePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            _settings = new Settings();
            _workDirectory = workDirectory;
            _bufferManager = bufferManager;
            _spaceClusters = new HashSet<long>();
        }

        public IEnumerable<Seed> Seeds
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.Seeds.Keys;
                }
            }
        }

        public IEnumerable<Information> ShareInformation
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<Information> list = new List<Information>();

                    foreach (var item in _settings.ShareIndex)
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Id", _ids.First(n => n.Value == item).Key));
                        contexts.Add(new InformationContext("Path", item.FilePath));
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.Size;
                }
            }
        }

        public int Count
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.ClustersDictionary.Count + _settings.ShareIndex.Sum(n => n.KeyAndCluster.Count);
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
                        if (index > 100)
                            throw;
                    }
                }
            }
        }

        internal void ChecksSeed()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                Random random = new Random();

                foreach (var item in _settings.Seeds.ToArray())
                {
                    bool flag = true;

                    foreach (var index in item.Value)
                    {
                        foreach (var group in index.Groups)
                        {
                            if (_countCache.GetCount(group) >= group.InformationLength)
                                continue;

                            flag = false;

                            goto End;
                        }
                    }

                End:
                    ;

                    if (flag)
                    {
                        flag = this.Contains(item.Key.Key);
                    }

                    if (flag)
                    {
                        if (this.GetPriority(item.Key.Key) == 0)
                        {
                            this.SetPriority(item.Key.Key, 1);

                            foreach (var index in item.Value)
                            {
                                foreach (var group in index.Groups)
                                {
                                    foreach (var key in group.Keys.OrderBy(n => random.Next(0, group.Keys.Count)).Take(group.InformationLength))
                                    {
                                        this.SetPriority(key, 1);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        _settings.Seeds.Remove(item.Key);

                        this.SetPriority(item.Key.Key, 0);

                        foreach (var index in item.Value)
                        {
                            foreach (var group in index.Groups)
                            {
                                foreach (var key in group.Keys)
                                {
                                    this.SetPriority(key, 0);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SetStreams()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var item in _streams.ToArray())
                {
                    if (!_settings.ShareIndex.Any(n => n.FilePath == item.Key))
                    {
                        item.Value.Close();
                        _streams.Remove(item.Key);
                    }
                }

                foreach (var item in _settings.ShareIndex.ToArray())
                {
                    if (!File.Exists(item.FilePath))
                    {
                        _settings.ShareIndex.Remove(item);

                        continue;
                    }

                    if (!_streams.Any(n => n.Key == item.FilePath))
                    {
                        _streams.Add(item.FilePath, new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read));
                    }
                }
            }
        }

        private void CheckSpace()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                HashSet<long> spaceList = new HashSet<long>();
                int i = 0;

                foreach (var clusters in _settings.ClustersDictionary.Values)
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

        private void CreatingSpace(long clusterCount)
        {
            IList<Key> tempHeaders = new List<Key>();
            this.OnGetUsingHeaders(ref tempHeaders);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.CheckSpace();
                if (clusterCount <= _spaceClusters.Count)
                    return;

                var usingHeaders = new HashSet<Key>(tempHeaders);
                //usingHeaders.UnionWith(_settings.Keys.Select(n => n.Header));

                var removeHeaders = _settings.ClustersDictionary.Keys.Where(n => !usingHeaders.Contains(n)).ToList();
                removeHeaders.Sort(new Comparison<Key>((x, y) =>
                {
                    if (_settings.ClustersDictionary[x].Priority != _settings.ClustersDictionary[y].Priority)
                    {
                        _settings.ClustersDictionary[x].Priority.CompareTo(_settings.ClustersDictionary[y].Priority);
                    }

                    return _settings.ClustersDictionary[x].UpdateTime.CompareTo(_settings.ClustersDictionary[y].UpdateTime);
                }));

                foreach (var header in removeHeaders)
                {
                    if (clusterCount <= _spaceClusters.Count)
                        return;

                    this.Remove(header);
                }
            }
        }

        private long GetEndCluster()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                long endCluster = -1;

                foreach (var clusters in _settings.ClustersDictionary.Values)
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

        protected virtual void OnGetUsingHeaders(ref IList<Key> keys)
        {
            if (GetUsingKeysEvent != null)
            {
                GetUsingKeysEvent(this, ref keys);
            }
        }

        protected virtual void OnSetKeyEvent(Key key)
        {
            if (SetKeyEvent != null)
            {
                SetKeyEvent(this, key);
            }
        }

        protected virtual void OnRemoveKeyEvent(Key key)
        {
            if (RemoveKeyEvent != null)
            {
                RemoveKeyEvent(this, key);
            }
        }

        public int GetLength(Key key)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_settings.ClustersDictionary.ContainsKey(key))
                {
                    return _settings.ClustersDictionary[key].Length;
                }

                foreach (var item in _settings.ShareIndex)
                {
                    if (item.KeyAndCluster.ContainsKey(key))
                    {
                        var fileLength = _streams[item.FilePath].Length;
                        int i = item.KeyAndCluster[key];

                        return (int)Math.Min(fileLength - ((long)item.BlockLength * i), item.BlockLength);
                    }
                }

                var b = this.Contains(key);

                throw new KeyNotFoundException();
            }
        }

        private int GetPriority(Key key)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_settings.ClustersDictionary.ContainsKey(key))
                {
                    return _settings.ClustersDictionary[key].Priority;
                }

                return 0;
            }
        }

        private void SetPriority(Key key, int priority)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_settings.ClustersDictionary.ContainsKey(key))
                {
                    _settings.ClustersDictionary[key].Priority = priority;
                }
            }
        }

        public bool Contains(Key key)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_settings.ClustersDictionary.ContainsKey(key))
                {
                    return true;
                }

                foreach (var item in _settings.ShareIndex)
                {
                    if (item.KeyAndCluster.ContainsKey(key))
                        return true;
                }

                return false;
            }
        }

        public void Remove(Key key)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (!_settings.ClustersDictionary.ContainsKey(key))
                    return;

                var clus = _settings.ClustersDictionary[key];
                _settings.ClustersDictionary.Remove(key);
                _spaceClusters.UnionWith(clus.Indexs);

                _countCache.SetKey(key, false);
                this.OnRemoveKeyEvent(key);
            }
        }

        public void Resize(long size)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var header in _settings.ClustersDictionary.Keys.ToArray()
                    .Where(n => _settings.ClustersDictionary[n].Indexs.Any(o => size < ((o + 1) * CacheManager.ClusterSize)))
                    .ToArray())
                {
                    this.Remove(header);
                }

                int count = (int)((size + (long)CacheManager.ClusterSize - 1) / (long)CacheManager.ClusterSize);
                _settings.Size = (long)(count + 1) * CacheManager.ClusterSize;
            }
        }

        public void SetSeed(Seed seed, IEnumerable<Index> indexs)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_settings.Seeds.ContainsKey(seed))
                    return;

                _settings.Seeds.Add(seed, new IndexCollection(indexs));

                foreach (var index in indexs)
                {
                    foreach (var group in index.Groups)
                    {
                        _countCache.SetGroup(group);

                        foreach (var key in group.Keys)
                        {
                            _countCache.SetKey(key, this.Contains(key));
                        }
                    }
                }
            }
        }

        public void RemoveSeed(Seed seed)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _settings.Seeds.Remove(seed);
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

        public KeyCollection Share(Stream inStream, string path, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (inStream == null)
                throw new ArgumentNullException("inStream");

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_settings.ShareIndex.Any(n => n.FilePath == path))
                {
                    var list = _settings.ShareIndex.Where(n => n.FilePath != path);
                    _settings.ShareIndex.Clear();
                    _settings.ShareIndex.AddRange(list);

                    this.SetStreams();
                }
            }

            byte[] buffer = _bufferManager.TakeBuffer(blockLength);

            KeyCollection keys = new KeyCollection();
            ShareIndex shareIndex = new ShareIndex();
            shareIndex.BlockLength = blockLength;
            shareIndex.FilePath = path;

            while (inStream.Position < inStream.Length)
            {
                int length = (int)Math.Min(inStream.Length - inStream.Position, blockLength);
                inStream.Read(buffer, 0, length);

                var hash = Sha512.ComputeHash(buffer, 0, length);
                var key = new Key()
                {
                    HashAlgorithm = HashAlgorithm.Sha512,
                    Hash = hash
                };

                if (!shareIndex.KeyAndCluster.ContainsKey(key))
                    shareIndex.KeyAndCluster.Add(key, keys.Count);

                keys.Add(key);
            }

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var item in _settings.ShareIndex.ToArray())
                {
                    if (!File.Exists(item.FilePath))
                    {
                        _settings.ShareIndex.Remove(item);
                    }
                }

                _settings.ShareIndex.Add(shareIndex);
                _ids.Add(_id++, shareIndex);

                this.SetStreams();
            }

            foreach (var key in keys)
            {
                this.OnSetKeyEvent(key);
                _countCache.SetKey(key, true);
            }

            return keys;
        }

        public void ShareRemove(int id)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var key in _ids[id].KeyAndCluster.Keys)
                {
                    _countCache.SetKey(key, false);
                    this.OnRemoveKeyEvent(key);
                }

                _settings.ShareIndex.Remove(_ids[id]);

                this.SetStreams();
            }
        }

        public KeyCollection Encoding(Stream inStream,
            CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (inStream == null)
                throw new ArgumentNullException("inStream");
            if (!Enum.IsDefined(typeof(CompressionAlgorithm), compressionAlgorithm))
                throw new ArgumentException("CompressAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(CryptoAlgorithm), cryptoAlgorithm))
                throw new ArgumentException("CryptoAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(HashAlgorithm), hashAlgorithm))
                throw new ArgumentException("HashAlgorithm に存在しない列挙");

            if (compressionAlgorithm == CompressionAlgorithm.XZ && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
                using (var outStream = new CacheManagerStreamWriter(blockLength, hashAlgorithm, this, _bufferManager))
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
                    var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

                    if (System.Environment.Is64BitProcess)
                    {
                        SevenZip.SevenZipCompressor.SetLibraryPath(Path.Combine(currentDirectory, "7z64.dll"));
                    }
                    else
                    {
                        SevenZip.SevenZipCompressor.SetLibraryPath(Path.Combine(currentDirectory, "7z86.dll"));
                    }

                    var compressor = new SevenZip.SevenZipCompressor();

                    compressor.ArchiveFormat = SevenZip.OutArchiveFormat.XZ;
                    compressor.CompressionMethod = SevenZip.CompressionMethod.Lzma2;
                    compressor.CompressionLevel = SevenZip.CompressionLevel.Low;
                    //compressor.CustomParameters.Add("mt", "off");

                    compressor.CompressStream(inStream, cs);

                    cs.Close();
                    outStream.Close();

                    return new KeyCollection(outStream.GetKeys());
                }
            }
            else if (compressionAlgorithm == CompressionAlgorithm.None && cryptoAlgorithm == CryptoAlgorithm.None)
            {
                using (var outStream = new CacheManagerStreamWriter(blockLength, hashAlgorithm, this, _bufferManager))
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

                    outStream.Close();

                    return new KeyCollection(outStream.GetKeys());
                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public void Decoding(Stream outStream,
            CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey, KeyCollection keys)
        {
            if (outStream == null)
                throw new ArgumentNullException("outStream");
            if (!Enum.IsDefined(typeof(CompressionAlgorithm), compressionAlgorithm))
                throw new ArgumentException("CompressAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(CryptoAlgorithm), cryptoAlgorithm))
                throw new ArgumentException("CryptoAlgorithm に存在しない列挙");

            if (compressionAlgorithm == CompressionAlgorithm.XZ && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
                string tempFilePath = null;

                try
                {
                    using (var tempStream = CacheManager.GetUniqueStream(Path.Combine(_workDirectory, "Decode_Temp.xz")))
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
                            byte[] buffer = _bufferManager.TakeBuffer(1024 * 1024);

                            try
                            {
                                int length = 0;

                                while (0 < (length = cs.Read(buffer, 0, buffer.Length)))
                                {
                                    tempStream.Write(buffer, 0, length);
                                }
                            }
                            finally
                            {
                                _bufferManager.ReturnBuffer(buffer);
                            }
                        }

                        tempFilePath = tempStream.Name;

                        tempStream.Seek(0, SeekOrigin.Begin);

                        var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

                        if (System.Environment.Is64BitProcess)
                        {
                            SevenZip.SevenZipCompressor.SetLibraryPath(Path.Combine(currentDirectory, "7z64.dll"));
                        }
                        else
                        {
                            SevenZip.SevenZipCompressor.SetLibraryPath(Path.Combine(currentDirectory, "7z86.dll"));
                        }

                        var decompressor = new SevenZip.SevenZipExtractor(tempStream);
                        decompressor.ExtractFile(decompressor.ArchiveFileNames[0], outStream);
                    }
                }
                finally
                {
                    if (tempFilePath != null)
                    {
                        File.Delete(tempFilePath);
                    }
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

        public Group ParityEncoding(KeyCollection keys, HashAlgorithm hashAlgorithm, int blockLength, CorrectionAlgorithm correctionAlgorithm)
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
                if (keys.Count > 128)
                    throw new ArgumentOutOfRangeException("headers");

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

                    //ReedSolomon reedSolomon = new ReedSolomon(8, bufferList.Count, bufferList.Count + parityBufferList.Count, 2);
                    ReedSolomon reedSolomon = new ReedSolomon(8, bufferList.Count, bufferList.Count + parityBufferList.Count, 8);
                    List<int> intList = new List<int>();

                    for (int i = 0, length = bufferList.Count + parityBufferList.Count; i < length; i++)
                    {
                        intList.Add(keys.Count + i);
                    }

                    reedSolomon.Encode(bufferList.ToArray(), parityBufferList.ToArray(), intList.ToArray());

                    for (int i = 0; i < parityBufferList.Count; i++)
                    {
                        if (hashAlgorithm == HashAlgorithm.Sha512)
                        {
                            var header = new Key()
                            {
                                Hash = Sha512.ComputeHash(parityBufferList[i]),
                                HashAlgorithm = hashAlgorithm
                            };
                            this[header] = parityBufferList[i];
                            parityHeaders.Add(header);
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

        public KeyCollection ParityDecoding(Group group)
        {
            if (group.CorrectionAlgorithm == CorrectionAlgorithm.None)
            {
                return new KeyCollection(group.Keys.Select(n => n.DeepClone()));
            }
            else if (group.CorrectionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
            {
                List<ArraySegment<byte>> bufferList = new List<ArraySegment<byte>>();

                try
                {
                    //ReedSolomon reedSolomon = new ReedSolomon(8, group.InformationLength, group.Keys.Count, 2);
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

                    reedSolomon.Decode(bufferList.ToArray(), intList.ToArray());

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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (_settings.ClustersDictionary.ContainsKey(key))
                    {
                        var clusters = _settings.ClustersDictionary[key];

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
                    else if (_settings.ShareIndex.Any(n => n.KeyAndCluster.ContainsKey(key)))
                    {
                        var shareIndex = _settings.ShareIndex.First(n => n.KeyAndCluster.ContainsKey(key));
                        byte[] buffer = _bufferManager.TakeBuffer(shareIndex.BlockLength);

                        try
                        {
                            Stream stream = _streams[shareIndex.FilePath];
                            int i = shareIndex.KeyAndCluster[key];

                            stream.Seek((long)shareIndex.BlockLength * i, SeekOrigin.Begin);

                            int length = (int)Math.Min(stream.Length - stream.Position, shareIndex.BlockLength);
                            stream.Read(buffer, 0, length);

                            if (key.HashAlgorithm == HashAlgorithm.Sha512)
                            {
                                if (!Collection.Equals(Sha512.ComputeHash(buffer, 0, length), key.Hash))
                                {
                                    _settings.ShareIndex.Remove(shareIndex);
                                    throw new BlockNotFoundException();
                                }
                            }

                            return new ArraySegment<byte>(buffer, 0, length);
                        }
                        catch (Exception)
                        {
                            _bufferManager.ReturnBuffer(buffer);

                            throw;
                        }
                    }
                    else
                    {
                        throw new BlockNotFoundException();
                    }
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (key.HashAlgorithm == HashAlgorithm.Sha512)
                    {
                        if (!Collection.Equals(Sha512.ComputeHash(value), key.Hash))
                            return;
                    }

                    if (_settings.ClustersDictionary.ContainsKey(key)
                        || _settings.ShareIndex.Any(n => n.KeyAndCluster.ContainsKey(key)))
                    {
                        return;
                    }

                    List<long> clusterList = new List<long>();

                    try
                    {
                        int count = (value.Count + CacheManager.ClusterSize - 1) / CacheManager.ClusterSize;

                        if (_spaceClusters.Count < count)
                        {
                            this.CreatingSpace(Math.Max(count, 8192) - _spaceClusters.Count); // 32KB(cluster size) * 8192(clusters count) = 256MB
                            if (_spaceClusters.Count < count)
                                throw new SpaceNotFoundException();
                        }

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
                    catch (IOException ex)
                    {
                        _spaceClusters.UnionWith(clusterList);

                        Log.Error(ex);
                    }

                    var clusters = new Clusters();
                    clusters.Indexs = clusterList.ToArray();
                    clusters.Length = value.Count;
                    clusters.UpdateTime = DateTime.UtcNow;
                    clusters.Priority = 0;
                    _settings.ClustersDictionary[key] = clusters;

                }

                this.OnSetKeyEvent(key);
                _countCache.SetKey(key, true);
            }
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _settings.Load(directoryPath);

                long size = _settings.Size;
                this.Resize(_fileStream.Length);
                _settings.Size = size;

                this.CheckSpace();
                this.SetStreams();

                _ids.Clear();
                _id = 0;

                foreach (var item in _settings.ShareIndex)
                {
                    _ids.Add(_id++, item);
                }

                foreach (var indexs in _settings.Seeds.Values)
                {
                    foreach (var index in indexs)
                    {
                        foreach (var group in index.Groups)
                        {
                            _countCache.SetGroup(group);

                            foreach (var key in group.Keys)
                            {
                                _countCache.SetKey(key, this.Contains(key));
                            }
                        }
                    }
                }

                this.ChecksSeed();
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

        #region IEnumerable<Header> メンバ

        public IEnumerator<Key> GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var item in _settings.ClustersDictionary.Keys)
                {
                    yield return item;
                }

                foreach (var item in _settings.ShareIndex)
                {
                    foreach (var item2 in item.KeyAndCluster.Keys)
                    {
                        yield return item2;
                    }
                }
            }
        }

        #endregion

        #region IEnumerable メンバ

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.GetEnumerator();
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
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
                    new Library.Configuration.SettingsContext<LockedList<ShareIndex>>() { Name = "ShareIndex", Value = new LockedList<ShareIndex>() },
                    new Library.Configuration.SettingsContext<LockedDictionary<Key, Clusters>>() { Name = "ClustersDictionary", Value = new LockedDictionary<Key, Clusters>() },
                    new Library.Configuration.SettingsContext<long>() { Name = "Size", Value = (long)1024 * 1024 * 1024 * 50 },
                    new Library.Configuration.SettingsContext<LockedDictionary<Seed, IndexCollection>>() { Name = "Seeds", Value = new LockedDictionary<Seed, IndexCollection>() },
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

            public LockedList<ShareIndex> ShareIndex
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (LockedList<ShareIndex>)this["ShareIndex"];
                    }
                }
            }

            public LockedDictionary<Key, Clusters> ClustersDictionary
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (LockedDictionary<Key, Clusters>)this["ClustersDictionary"];
                    }
                }
            }

            public long Size
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (long)this["Size"];
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        this["Size"] = value;
                    }
                }
            }

            public LockedDictionary<Seed, IndexCollection> Seeds
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (LockedDictionary<Seed, IndexCollection>)this["Seeds"];
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

        [DataContract(Name = "Clusters", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class Clusters : IThisLock
        {
            private long[] _indexs;
            private int _length;
            private object _thisLock;
            private DateTime _updateTime = DateTime.UtcNow;
            private int _priority;
            private static object _thisStaticLock = new object();

            [DataMember(Name = "Indexs")]
            public long[] Indexs
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _indexs;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
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
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _length;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
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
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _updateTime;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        _updateTime = value;
                    }
                }
            }

            [DataMember(Name = "Priority")]
            public int Priority
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _priority;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        _priority = value;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
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
            private string _filePath;
            private LockedDictionary<Key, int> _keyAndCluster;
            private int _blockLength;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            [DataMember(Name = "FilePath")]
            public string FilePath
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _filePath;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        _filePath = value;
                    }
                }
            }

            [DataMember(Name = "KeyAndCluster")]
            public LockedDictionary<Key, int> KeyAndCluster
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
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
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _blockLength;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        _blockLength = value;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
                    {
                        if (_thisLock == null)
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
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

    [Serializable]
    class CacheManagerException : ManagerException
    {
        public CacheManagerException()
            : base()
        {
        }
        public CacheManagerException(string message)
            : base(message)
        {
        }
        public CacheManagerException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    [Serializable]
    class SpaceNotFoundException : CacheManagerException
    {
        public SpaceNotFoundException()
            : base()
        {
        }
        public SpaceNotFoundException(string message)
            : base(message)
        {
        }
        public SpaceNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    [Serializable]
    class BlockNotFoundException : CacheManagerException
    {
        public BlockNotFoundException()
            : base()
        {
        }
        public BlockNotFoundException(string message)
            : base(message)
        {
        }
        public BlockNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
