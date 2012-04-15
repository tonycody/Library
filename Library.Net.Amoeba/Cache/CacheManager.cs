using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using Library.Collections;
using Library.Correction;
using Library.Io;
using System.Threading;

namespace Library.Net.Amoeba
{
    delegate void GetUsingKeysEventHandler(object sender, ref IList<Key> keys);
    public delegate void CheckBlocksProgressEventHandler(object sender, int badBlockCount, int checkedBlockCount, int blockCount, out bool isStop);
    delegate void SetKeyEventHandler(object sender, Key key);
    delegate void RemoveKeyEventHandler(object sender, Key key);
    delegate bool WatchEventHandler(object sender);

    class CacheManager : ManagerBase, Library.Configuration.ISettings, IEnumerable<Key>, IThisLock
    {
        private Settings _settings;
        private FileStream _fileStream = null;
        private string _workDirectory = Path.GetTempPath();
        private BufferManager _bufferManager;
        private HashSet<long> _spaceClusters;
        private Dictionary<int, string> _ids = new Dictionary<int, string>();
        private int _id = 0;

        internal SetKeyEventHandler SetKeyEvent;
        internal RemoveKeyEventHandler RemoveKeyEvent;
        public GetUsingKeysEventHandler GetUsingKeysEvent;

        private bool _disposed = false;
        private object _thisLock = new object();
        public const int ClusterSize = 1024 * 256;

        public CacheManager(string cachePath, string WorkDirectory, BufferManager bufferManager)
        {
            _fileStream = new FileStream(cachePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _workDirectory = WorkDirectory;

            _settings = new Settings();
            _bufferManager = bufferManager;
            _spaceClusters = new HashSet<long>();
        }

        public IEnumerable<Seed> Seeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Seeds.Keys.ToArray();
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

                    contexts.Add(new InformationContext("CacheSeedCount", _settings.Seeds.Keys.Count));

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

                    foreach (var item in _settings.ShareIndex)
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Id", _ids.First(n => n.Value == item.Key).Key));
                        contexts.Add(new InformationContext("Path", item.Key));
                        contexts.Add(new InformationContext("BlockCount", item.Value.KeyAndCluster.Count));

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

        private void CheckSeeds()
        {
            lock (this.ThisLock)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                Random random = new Random();

                foreach (var item in _settings.Seeds.ToArray())
                {
                    var seed = item.Key;
                    var info = item.Value;

                    bool flag = true;

                    foreach (var index in info.Indexs)
                    {
                        foreach (var group in index.Groups)
                        {
                            int count = 0;

                            foreach (var key in group.Keys)
                            {
                                if (this.Contains(key))
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

                    if (!(flag = this.Contains(seed.Key))) goto Break;
                    if (!(flag = File.Exists(info.Path))) goto Break;

                Break: ;

                    if (flag)
                    {
                        if (this.GetPriority(seed.Key) == 0)
                        {
                            this.SetPriority(seed.Key, 1);

                            foreach (var index in info.Indexs)
                            {
                                foreach (var group in index.Groups)
                                {
                                    foreach (var key in group.Keys
                                        .Reverse<Key>()
                                        .Where(n => this.Contains(n))
                                        .Take(group.InformationLength))
                                    {
                                        this.SetPriority(key, 1);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        _settings.Seeds.Remove(seed);

                        this.SetPriority(seed.Key, 0);

                        foreach (var index in info.Indexs)
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

                sw.Stop();
                Debug.WriteLine("CheckSeeds {0}", sw.ElapsedMilliseconds);
            }
        }

        private void CreatingSpace(long clusterCount)
        {
            IList<Key> tempHeaders = new List<Key>();
            this.OnGetUsingHeaders(ref tempHeaders);

            lock (this.ThisLock)
            {
                this.CheckSpace();
                if (clusterCount <= _spaceClusters.Count)
                    return;

                var usingHeaders = new HashSet<Key>(tempHeaders);

                var removeHeaders = _settings.ClustersIndex.Keys.Where(n => !usingHeaders.Contains(n)).ToList();
                removeHeaders.Sort(new Comparison<Key>((x, y) =>
                {
                    var xc = _settings.ClustersIndex[x];
                    var yc = _settings.ClustersIndex[y];

                    if (xc.Priority != yc.Priority)
                    {
                        xc.Priority.CompareTo(yc.Priority);
                    }

                    return xc.UpdateTime.CompareTo(yc.UpdateTime);
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

        private int GetPriority(Key key)
        {
            lock (this.ThisLock)
            {
                if (_settings.ClustersIndex.ContainsKey(key))
                {
                    return _settings.ClustersIndex[key].Priority;
                }

                return 0;
            }
        }

        private void SetPriority(Key key, int priority)
        {
            lock (this.ThisLock)
            {
                if (_settings.ClustersIndex.ContainsKey(key))
                {
                    _settings.ClustersIndex[key].Priority = priority;
                }
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

                foreach (var item in _settings.ShareIndex)
                {
                    if (item.Value.KeyAndCluster.ContainsKey(key))
                        return true;
                }

                return false;
            }
        }

        public void Remove(Key key)
        {
            lock (this.ThisLock)
            {
                if (!_settings.ClustersIndex.ContainsKey(key))
                    return;

                var clus = _settings.ClustersIndex[key];
                _settings.ClustersIndex.Remove(key);
                _spaceClusters.UnionWith(clus.Indexs);

                this.OnRemoveKeyEvent(key);
            }
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
                if (_settings.Seeds.ContainsKey(seed))
                    return;

                var info = new SeedInformation();
                info.Path = path;
                info.Indexs.AddRange(indexs);

                _settings.Seeds.Add(seed, info);
            }
        }

        public void RemoveSeed(Seed seed)
        {
            lock (this.ThisLock)
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
            if (inStream == null) throw new ArgumentNullException("inStream");

            lock (this.ThisLock)
            {
                if (_settings.ShareIndex.ContainsKey(path))
                {
                    throw new Exception();
                }
            }

            byte[] buffer = _bufferManager.TakeBuffer(blockLength);

            KeyCollection keys = new KeyCollection();
            ShareIndex shareIndex = new ShareIndex();
            shareIndex.BlockLength = blockLength;

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

            lock (this.ThisLock)
            {
                _settings.ShareIndex.Add(path, shareIndex);
                _ids.Add(_id++, path);
            }

            foreach (var key in keys)
            {
                this.OnSetKeyEvent(key);
            }

            return keys;
        }

        public void ShareRemove(int id)
        {
            lock (this.ThisLock)
            {
                foreach (var key in _settings.ShareIndex[_ids[id]].KeyAndCluster.Keys)
                {
                    this.OnRemoveKeyEvent(key);
                }

                _settings.ShareIndex.Remove(_ids[id]);
                _ids.Remove(id);
            }
        }

        public KeyCollection Encoding(Stream inStream,
            CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (inStream == null) throw new ArgumentNullException("inStream");
            if (!Enum.IsDefined(typeof(CompressionAlgorithm), compressionAlgorithm)) throw new ArgumentException("CompressAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(CryptoAlgorithm), cryptoAlgorithm)) throw new ArgumentException("CryptoAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(HashAlgorithm), hashAlgorithm)) throw new ArgumentException("HashAlgorithm に存在しない列挙");

            if (compressionAlgorithm == CompressionAlgorithm.XZ && cryptoAlgorithm == CryptoAlgorithm.Rijndael256)
            {
                IList<Key> keys = new List<Key>();

                using (var outStream = new CacheManagerStreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                {
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
#if !DEBUG
                        var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
#else
                        var currentDirectory = Directory.GetCurrentDirectory();
#endif

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
                    }
                }

                return new KeyCollection(keys);
            }
            else if (compressionAlgorithm == CompressionAlgorithm.None && cryptoAlgorithm == CryptoAlgorithm.None)
            {
                IList<Key> keys = new List<Key>();

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

#if !DEBUG
                        var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
#else
                        var currentDirectory = Directory.GetCurrentDirectory();
#endif

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

        public KeyCollection ParityDecoding(Group group, WatchEventHandler watchEvent)
        {
            if (group.InformationLength > group.Keys.Count) throw new ArgumentOutOfRangeException();
            if (group.BlockLength > 1024 * 1024 * 4) throw new ArgumentOutOfRangeException();
            if (group.BlockLength * group.Keys.Count > group.Length) throw new ArgumentOutOfRangeException();

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
                    if (_settings.ClustersIndex.ContainsKey(key))
                    {
                        var clusters = _settings.ClustersIndex[key];

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
                    else if (_settings.ShareIndex.Any(n => n.Value.KeyAndCluster.ContainsKey(key)))
                    {
                        KeyValuePair<string, ShareIndex> target = new KeyValuePair<string, ShareIndex>();

                        foreach (var item in _settings.ShareIndex)
                        {
                            if (item.Value.KeyAndCluster.ContainsKey(key))
                            {
                                target = item;
                            }
                        }

                        byte[] buffer = _bufferManager.TakeBuffer(target.Value.BlockLength);

                        try
                        {
                            using (Stream stream = new FileStream(target.Key, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                int i = target.Value.KeyAndCluster[key];

                                stream.Seek((long)target.Value.BlockLength * i, SeekOrigin.Begin);

                                int length = (int)Math.Min(stream.Length - stream.Position, target.Value.BlockLength);
                                stream.Read(buffer, 0, length);

                                if (key.HashAlgorithm == HashAlgorithm.Sha512)
                                {
                                    if (!Collection.Equals(Sha512.ComputeHash(buffer, 0, length), key.Hash))
                                    {
                                        _settings.ShareIndex.Remove(target.Key);
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
                    else
                    {
                        throw new BlockNotFoundException();
                    }
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (key.HashAlgorithm == HashAlgorithm.Sha512)
                    {
                        if (!Collection.Equals(Sha512.ComputeHash(value), key.Hash))
                            return;
                    }

                    if (_settings.ClustersIndex.ContainsKey(key)
                        || _settings.ShareIndex.Any(n => n.Value.KeyAndCluster.ContainsKey(key)))
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
                    _settings.ClustersIndex[key] = clusters;

                }

                this.OnSetKeyEvent(key);
            }
        }

        #region ISettings メンバ

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

                this.CheckSeeds();
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

        #region IEnumerable<Header> メンバ

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

        #region IEnumerable メンバ

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
                    new Library.Configuration.SettingsContext<LockedDictionary<string, ShareIndex>>() { Name = "ShareIndex", Value = new LockedDictionary<string,ShareIndex>() },
                    new Library.Configuration.SettingsContext<LockedDictionary<Key, Clusters>>() { Name = "ClustersIndex", Value = new LockedDictionary<Key, Clusters>() },
                    new Library.Configuration.SettingsContext<long>() { Name = "Size", Value = (long)1024 * 1024 * 1024 * 50 },
                    new Library.Configuration.SettingsContext<LockedDictionary<Seed, SeedInformation>>() { Name = "Seeds", Value = new LockedDictionary<Seed,SeedInformation>() },
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

            public LockedDictionary<Seed, SeedInformation> Seeds
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedDictionary<Seed, SeedInformation>)this["Seeds"];
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

        [DataContract(Name = "SeedInformation", Namespace = "http://Library/Net/Amoeba/CacheManager")]
        private class SeedInformation : IThisLock
        {
            private string _path;
            private IndexCollection _indexs;

            private object _thisLock;
            private static object _thisStaticLock = new object();

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

            #region IThisLock メンバ

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
            private int _priority;
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

            [DataMember(Name = "Priority")]
            public int Priority
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _priority;
                    }
                }
                set
                {
                    lock (this.ThisLock)
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

            #region IThisLock メンバ

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
