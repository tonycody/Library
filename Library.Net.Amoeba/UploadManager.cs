using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    // 全体的にカオスだけど、進行状況の報告とか考えるとこんな風になってしまった

    class UploadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private volatile Thread _uploadManagerThread = null;
        private LockedDictionary<Key, bool> _keyCount = new LockedDictionary<Key, bool>();
        private Dictionary<int, UploadItem> _ids = new Dictionary<int, UploadItem>();
        private int _id = 0;
        private ManagerState _state = ManagerState.Stop;

        private bool _disposed = false;
        private object _thisLock = new object();

        public UploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
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

                    foreach (var item in _settings.UploadItems)
                    {
                        if (item.Seed != null)
                        {
                            list.Add(item.Seed.Key);
                        }

                        list.UnionWith(item.UploadKeys);
                        list.UnionWith(item.Keys);

                        foreach (var group in item.Groups)
                        {
                            if (group != null)
                            {
                                list.UnionWith(group.Keys);
                            }
                        }
                    }

                    foreach (var item in list)
                    {
                        headers.Add(item);
                    }
                }
            };

            _connectionsManager.UploadedEvent += (object sender, Key otherKey) =>
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    foreach (var item in _settings.UploadItems)
                    {
                        if (item.UploadKeys.Contains(otherKey))
                        {
                            item.UploadedKeys.Add(otherKey);
                            item.UploadKeys.Remove(otherKey);
                        }
                    }
                }
            };

            _cacheManager.RemoveKeyEvent += (object sender, Key otherKey) =>
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    foreach (var item in _settings.UploadItems)
                    {
                        if (item.UploadKeys.Contains(otherKey))
                        {
                            item.UploadedKeys.Add(otherKey);
                            item.UploadKeys.Remove(otherKey);
                        }
                    }
                }
            };
        }

        public IEnumerable<Information> UploadingInformation
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<Information> list = new List<Information>();

                    foreach (var item in _settings.UploadItems)
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Id", _ids.First(n => n.Value == item).Key));
                        contexts.Add(new InformationContext("Priority", item.Priority));
                        contexts.Add(new InformationContext("Name", item.Seed.Name));
                        contexts.Add(new InformationContext("Length", item.Seed.Length));
                        contexts.Add(new InformationContext("State", item.State));
                        contexts.Add(new InformationContext("Rank", item.Rank));
                        if (item.State == UploadState.Completed || item.State == UploadState.Uploading)
                            contexts.Add(new InformationContext("Seed", item.Seed));

                        if (item.State == UploadState.Uploading)
                        {
                            contexts.Add(new InformationContext("BlockCount", item.UploadKeys.Count + item.UploadedKeys.Count));
                            contexts.Add(new InformationContext("UploadBlockCount", item.UploadedKeys.Count));
                        }
                        else if (item.State == UploadState.Encoding || item.State == UploadState.ComputeHash || item.State == UploadState.ComputeCorrection)
                        {
                            contexts.Add(new InformationContext("EncodeBytes", item.EncodeBytes));
                            contexts.Add(new InformationContext("EncodingBytes", item.EncodingBytes));
                        }
                        else if (item.State == UploadState.Completed)
                        {
                            contexts.Add(new InformationContext("BlockCount", item.UploadKeys.Count + item.UploadedKeys.Count));
                            contexts.Add(new InformationContext("UploadBlockCount", item.UploadedKeys.Count));
                        }

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public SeedCollection UploadedSeeds
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.UploadedSeeds;
                }
            }
        }

        private void SetKeyCount(UploadItem item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var key in item.UploadKeys.ToArray())
                {
                    if (!_connectionsManager.UploadWaiting(key))
                    {
                        item.UploadedKeys.Add(key);
                        item.UploadKeys.Remove(key);
                    }
                }
            }
        }

        private void UploadManagerThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                UploadItem item = null;

                try
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        using (DeadlockMonitor.Lock(_settings.ThisLock))
                        {
                            if (_settings.UploadItems.Count > 0)
                            {
                                var items = _settings.UploadItems.Where(n => n.State == UploadState.Encoding || n.State == UploadState.ComputeHash || n.State == UploadState.ComputeCorrection).ToList();

                                items.Sort(delegate(UploadItem x, UploadItem y)
                                {
                                    return x.GetHashCode().CompareTo(y.GetHashCode());
                                });
                                items.Sort(delegate(UploadItem x, UploadItem y)
                                {
                                    return y.Priority.CompareTo(x.Priority);
                                });

                                item = items.FirstOrDefault();
                            }

                            foreach (var item2 in _settings.UploadItems)
                            {
                                if (item2.State == UploadState.Uploading)
                                {
                                    if (item2.UploadKeys.Count == 0)
                                    {
                                        item2.State = UploadState.Completed;

                                        _cacheManager.SetSeed(item2.Seed.DeepClone(), item2.Indexs);
                                        _settings.UploadedSeeds.Add(item2.Seed.DeepClone());
                                    }
                                }
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
                        if (item.Type == UploadType.Upload)
                        {
                            if (item.Groups.Count == 0 && item.Keys.Count == 0)
                            {
                                item.State = UploadState.Encoding;

                                KeyCollection keys;
                                byte[] cryptoKey;

                                try
                                {
                                    using (FileStream stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                    using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                        item.EncodingBytes = Math.Min(readSize, stream.Length);
                                    }, 1024 * 1024, true))
                                    using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                        item.EncodingBytes = Math.Min(readSize, stream.Length);
                                    }, 1024 * 1024, true))
                                    {
                                        item.EncodeBytes = stream.Length;
                                        item.Seed.Length = stream.Length;

                                        item.State = UploadState.ComputeHash;
                                        cryptoKey = Sha512.ComputeHash(hashProgressStream);
                                        //cryptoKey = new byte[64];

                                        stream.Seek(0, SeekOrigin.Begin);
                                        item.EncodingBytes = 0;

                                        item.State = UploadState.Encoding;
                                        keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.HashAlgorithm, item.BlockLength);
                                    }
                                }
                                catch (StopIOException)
                                {
                                    continue;
                                }

                                using (DeadlockMonitor.Lock(this.ThisLock))
                                {
                                    item.EncodingBytes = 0;
                                    item.EncodeBytes = 0;

                                    item.CryptoKey = cryptoKey;
                                    item.Keys.AddRange(keys);
                                }
                            }
                            else if (item.Groups.Count == 0 && item.Keys.Count == 1)
                            {
                                using (DeadlockMonitor.Lock(this.ThisLock))
                                {
                                    item.Seed.Rank = item.Rank;
                                    item.Seed.Key = item.Keys[0];

                                    item.Seed.CompressionAlgorithm = item.CompressionAlgorithm;

                                    item.Seed.CryptoAlgorithm = item.CryptoAlgorithm;
                                    item.Seed.CryptoKey = item.CryptoKey;

                                    if (item.DigitalSignature != null)
                                    {
                                        item.Seed.CreateCertificate(item.DigitalSignature);
                                    }

                                    item.UploadKeys.Add(item.Seed.Key);

                                    foreach (var header in item.UploadKeys)
                                    {
                                        _connectionsManager.Upload(header);
                                    }

                                    this.SetKeyCount(item);
                                }

                                item.State = UploadState.Uploading;
                            }
                            else if (item.Keys.Count > 0)
                            {
                                item.State = UploadState.ComputeCorrection;

                                item.EncodeBytes = item.Groups.Sum(n =>
                                {
                                    long sumLength = 0;

                                    for (int i = 0; i < n.InformationLength; i++)
                                    {
                                        if (_cacheManager.Contains(n.Keys[i]))
                                        {
                                            sumLength += (long)_cacheManager.GetLength(n.Keys[i]);
                                        }
                                    }

                                    return sumLength;
                                }) + item.Keys.Sum(n =>
                                {
                                    if (_cacheManager.Contains(n))
                                    {
                                        return (long)_cacheManager.GetLength(n);
                                    }

                                    return 0;
                                });

                                var length = Math.Min(item.Keys.Count, 128);
                                var keys = new KeyCollection(item.Keys.Take(length));
                                var group = _cacheManager.ParityEncoding(keys, item.HashAlgorithm, item.BlockLength, item.CorrectionAlgorithm);

                                using (DeadlockMonitor.Lock(this.ThisLock))
                                {
                                    foreach (var header in group.Keys)
                                    {
                                        item.UploadKeys.Add(header);
                                    }

                                    item.Groups.Add(group);

                                    item.EncodingBytes = item.Groups.Sum(n =>
                                    {
                                        long sumLength = 0;

                                        for (int i = 0; i < n.InformationLength; i++)
                                        {
                                            if (_cacheManager.Contains(n.Keys[i]))
                                            {
                                                sumLength += (long)_cacheManager.GetLength(n.Keys[i]);
                                            }
                                        }

                                        return sumLength;
                                    });

                                    for (int i = 0; i < length; i++)
                                    {
                                        item.Keys.RemoveAt(0);
                                    }
                                }
                            }
                            else if (item.Groups.Count > 0 && item.Keys.Count == 0)
                            {
                                item.State = UploadState.Encoding;

                                var index = new Index();
                                index.Groups.AddRange(item.Groups);
                                index.CompressionAlgorithm = item.CompressionAlgorithm;
                                index.CryptoAlgorithm = item.CryptoAlgorithm;
                                index.CryptoKey = item.CryptoKey;

                                item.Indexs.Add(index);

                                byte[] cryptoKey;
                                KeyCollection keys;

                                try
                                {
                                    using (var stream = index.Export(_bufferManager))
                                    using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                        item.EncodingBytes = Math.Min(readSize, stream.Length);
                                    }, 1024 * 1024, true))
                                    using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                        item.EncodingBytes = Math.Min(readSize, stream.Length);
                                    }, 1024 * 1024, true))
                                    {
                                        item.EncodeBytes = stream.Length;

                                        item.State = UploadState.ComputeHash;
                                        cryptoKey = Sha512.ComputeHash(hashProgressStream);

                                        stream.Seek(0, SeekOrigin.Begin);
                                        item.EncodingBytes = 0;

                                        item.State = UploadState.Encoding;
                                        keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.HashAlgorithm, item.BlockLength);
                                    }
                                }
                                catch (StopIOException)
                                {
                                    continue;
                                }

                                using (DeadlockMonitor.Lock(this.ThisLock))
                                {
                                    item.EncodingBytes = 0;
                                    item.EncodeBytes = 0;

                                    item.CryptoKey = cryptoKey;
                                    item.Keys.AddRange(keys);
                                    item.Rank++;
                                    item.Groups.Clear();
                                }
                            }
                        }
                        else if (item.Type == UploadType.Share)
                        {
                            if (item.Groups.Count == 0 && item.Keys.Count == 0)
                            {
                                item.State = UploadState.ComputeHash;

                                KeyCollection keys;

                                try
                                {
                                    using (FileStream stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                    using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                        item.EncodingBytes = Math.Min(readSize, stream.Length);
                                    }, 1024 * 1024, true))
                                    {
                                        item.EncodeBytes = stream.Length;
                                        item.Seed.Length = stream.Length;

                                        keys = _cacheManager.Share(hashProgressStream, stream.Name, item.HashAlgorithm, item.BlockLength);
                                    }
                                }
                                catch (StopIOException)
                                {
                                    continue;
                                }

                                using (DeadlockMonitor.Lock(this.ThisLock))
                                {
                                    item.EncodingBytes = 0;
                                    item.EncodeBytes = 0;

                                    if (keys.Count == 1)
                                    {
                                        item.Keys.Add(keys[0]);
                                    }
                                    else
                                    {
                                        Group group = new Group();
                                        group.CorrectionAlgorithm = CorrectionAlgorithm.None;
                                        group.InformationLength = keys.Count;
                                        group.BlockLength = item.BlockLength;
                                        group.Length = item.Seed.Length;
                                        group.Keys.AddRange(keys);

                                        foreach (var header in group.Keys)
                                        {
                                            item.UploadKeys.Add(header);
                                        }

                                        item.Groups.Add(group);
                                    }
                                }

                                item.State = UploadState.Encoding;
                            }
                            else if (item.Groups.Count == 0 && item.Keys.Count == 1)
                            {
                                using (DeadlockMonitor.Lock(this.ThisLock))
                                {
                                    item.Seed.Rank = item.Rank;
                                    item.Seed.Key = item.Keys[0];

                                    if (item.Rank != 1)
                                    {
                                        item.Seed.CompressionAlgorithm = item.CompressionAlgorithm;

                                        item.Seed.CryptoAlgorithm = item.CryptoAlgorithm;
                                        item.Seed.CryptoKey = item.CryptoKey;
                                    }

                                    if (item.DigitalSignature != null)
                                    {
                                        item.Seed.CreateCertificate(item.DigitalSignature);
                                    }

                                    item.UploadKeys.Add(item.Seed.Key);

                                    foreach (var header in item.UploadKeys)
                                    {
                                        _connectionsManager.Upload(header);
                                    }

                                    this.SetKeyCount(item);
                                }

                                item.State = UploadState.Uploading;
                            }
                            else if (item.Keys.Count > 0)
                            {
                                item.State = UploadState.ComputeCorrection;

                                item.EncodeBytes = item.Groups.Sum(n =>
                                {
                                    long sumLength = 0;

                                    for (int i = 0; i < n.InformationLength; i++)
                                    {
                                        if (_cacheManager.Contains(n.Keys[i]))
                                        {
                                            sumLength += (long)_cacheManager.GetLength(n.Keys[i]);
                                        }
                                    }

                                    return sumLength;
                                }) + item.Keys.Sum(n =>
                                {
                                    if (_cacheManager.Contains(n))
                                    {
                                        return (long)_cacheManager.GetLength(n);
                                    }

                                    return 0;
                                });

                                var length = Math.Min(item.Keys.Count, 128);
                                var keys = new KeyCollection(item.Keys.Take(length));
                                var group = _cacheManager.ParityEncoding(keys, item.HashAlgorithm, item.BlockLength, item.CorrectionAlgorithm);

                                using (DeadlockMonitor.Lock(this.ThisLock))
                                {
                                    foreach (var header in group.Keys)
                                    {
                                        item.UploadKeys.Add(header);
                                    }

                                    item.Groups.Add(group);

                                    item.EncodingBytes = item.Groups.Sum(n =>
                                    {
                                        long sumLength = 0;

                                        for (int i = 0; i < n.InformationLength; i++)
                                        {
                                            if (_cacheManager.Contains(n.Keys[i]))
                                            {
                                                sumLength += (long)_cacheManager.GetLength(n.Keys[i]);
                                            }
                                        }

                                        return sumLength;
                                    });

                                    for (int i = 0; i < length; i++)
                                    {
                                        item.Keys.RemoveAt(0);
                                    }
                                }
                            }
                            else if (item.Groups.Count > 0 && item.Keys.Count == 0)
                            {
                                item.State = UploadState.Encoding;

                                var index = new Index();
                                index.Groups.AddRange(item.Groups);

                                if (item.Rank != 1)
                                {
                                    index.CompressionAlgorithm = item.CompressionAlgorithm;

                                    index.CryptoAlgorithm = item.CryptoAlgorithm;
                                    index.CryptoKey = item.CryptoKey;
                                }

                                item.Indexs.Add(index);

                                byte[] cryptoKey;
                                KeyCollection keys;

                                try
                                {
                                    using (var stream = index.Export(_bufferManager))
                                    using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                        item.EncodingBytes = Math.Min(readSize, stream.Length);
                                    }, 1024 * 1024, true))
                                    using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                        item.EncodingBytes = Math.Min(readSize, stream.Length);
                                    }, 1024 * 1024, true))
                                    {
                                        item.EncodeBytes = stream.Length;

                                        item.State = UploadState.ComputeHash;
                                        cryptoKey = Sha512.ComputeHash(hashProgressStream);

                                        stream.Seek(0, SeekOrigin.Begin);
                                        item.EncodingBytes = 0;

                                        item.State = UploadState.Encoding;
                                        keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.HashAlgorithm, item.BlockLength);
                                    }
                                }
                                catch (StopIOException)
                                {
                                    continue;
                                }

                                using (DeadlockMonitor.Lock(this.ThisLock))
                                {
                                    item.EncodingBytes = 0;
                                    item.EncodeBytes = 0;

                                    item.CryptoKey = cryptoKey;
                                    item.Keys.AddRange(keys);
                                    item.Rank++;
                                    item.Groups.Clear();
                                }
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    item.State = UploadState.Error;

                    Log.Error(exception);
                }
            }
        }

        public void Upload(string filePath,
            string name,
            KeywordCollection keywords,
            string comment,
            CompressionAlgorithm compressionAlgorithm,
            CryptoAlgorithm cryptoAlgorithm,
            CorrectionAlgorithm correctionAlgorithm,
            HashAlgorithm hashAlgorithm,
            DigitalSignature digitalSignature)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                UploadItem item = new UploadItem();

                item.Priority = 0;
                item.State = UploadState.Encoding;
                item.Type = UploadType.Upload;
                item.Rank = 1;
                item.FilePath = filePath;
                item.CompressionAlgorithm = compressionAlgorithm;
                item.CryptoAlgorithm = cryptoAlgorithm;
                item.CorrectionAlgorithm = correctionAlgorithm;
                item.HashAlgorithm = hashAlgorithm;
                item.DigitalSignature = digitalSignature;
                item.Seed = new Seed();
                item.Seed.Name = name;
                item.Seed.Keywords.AddRange(keywords);
                item.Seed.CreationTime = DateTime.UtcNow;
                item.Seed.Comment = comment;
                item.BlockLength = 1024 * 256;

                _settings.UploadItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        Dictionary<DownloadItem, int> Ids = new Dictionary<DownloadItem, int>();

        public void Share(string filePath,
            string name,
            KeywordCollection keywords,
            string comment,
            CompressionAlgorithm compressionAlgorithm,
            CryptoAlgorithm cryptoAlgorithm,
            CorrectionAlgorithm correctionAlgorithm,
            HashAlgorithm hashAlgorithm,
            DigitalSignature digitalSignature)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                UploadItem item = new UploadItem();

                item.Priority = 0;
                item.State = UploadState.Encoding;
                item.Type = UploadType.Share;
                item.Rank = 1;
                item.FilePath = filePath;
                item.CompressionAlgorithm = compressionAlgorithm;
                item.CryptoAlgorithm = cryptoAlgorithm;
                item.CorrectionAlgorithm = correctionAlgorithm;
                item.HashAlgorithm = hashAlgorithm;
                item.DigitalSignature = digitalSignature;
                item.Seed = new Seed();
                item.Seed.Name = name;
                item.Seed.Keywords.AddRange(keywords);
                item.Seed.CreationTime = DateTime.UtcNow;
                item.Seed.Comment = comment;
                item.BlockLength = 1024 * 256;

                _settings.UploadItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        public void Remove(int id)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _settings.UploadItems.Remove(_ids[id]);
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
            while (_uploadManagerThread != null) Thread.Sleep(1000);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _uploadManagerThread = new Thread(this.UploadManagerThread);
                _uploadManagerThread.IsBackground = true;
                _uploadManagerThread.Priority = ThreadPriority.Lowest;
                _uploadManagerThread.Start();
            }
        }

        public override void Stop()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;
            }

            _uploadManagerThread.Join();
            _uploadManagerThread = null;
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.UploadItems.ToArray())
                {
                    try
                    {
                        this.SetKeyCount(item);
                    }
                    catch (Exception)
                    {
                        _settings.UploadItems.Remove(item);
                    }
                }

                _ids.Clear();
                _id = 0;

                foreach (var item in _settings.UploadItems)
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
                    new Library.Configuration.SettingsContext<LockedList<UploadItem>>() { Name = "UploadItems", Value = new LockedList<UploadItem>() },
                    new Library.Configuration.SettingsContext<SeedCollection>() { Name = "UploadedSeeds", Value = new SeedCollection() },
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

            public LockedList<UploadItem> UploadItems
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (LockedList<UploadItem>)this["UploadItems"];
                    }
                }
            }

            public SeedCollection UploadedSeeds
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (SeedCollection)this["UploadedSeeds"];
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
                    if (_uploadManagerThread != null)
                    {
                        try
                        {
                            _uploadManagerThread.Abort();
                        }
                        catch (Exception)
                        {

                        }

                        _uploadManagerThread = null;
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
