using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private volatile Thread _uploadManagerThread;
        private SortedDictionary<int, UploadItem> _ids = new SortedDictionary<int, UploadItem>();
        private SortedDictionary<string, List<int>> _shareLink = new SortedDictionary<string, List<int>>();
        private int _id;

        private volatile ManagerState _state = ManagerState.Stop;
        private volatile ManagerState _encodeState = ManagerState.Stop;

        private Thread _uploadedThread;
        private Thread _removeShareThread;

        private WaitQueue<Key> _uploadedKeys = new WaitQueue<Key>();
        private WaitQueue<string> _removeSharePaths = new WaitQueue<string>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public UploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _connectionsManager.UploadedEvent += (object sender, IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _uploadedKeys.Enqueue(key);
                }
            };

            _cacheManager.RemoveKeyEvent += (object sender, IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _uploadedKeys.Enqueue(key);
                }
            };

            _cacheManager.RemoveShareEvent += (object sender, string path) =>
            {
                _removeSharePaths.Enqueue(path);
            };

            _uploadedThread = new Thread(() =>
            {
                try
                {
                    for (; ; )
                    {
                        var key = _uploadedKeys.Dequeue();

                        while (_removeSharePaths.Count > 0) Thread.Sleep(1000);

                        lock (this.ThisLock)
                        {
                            foreach (var item in _settings.UploadItems)
                            {
                                if (item.UploadKeys.Remove(key))
                                {
                                    item.UploadedKeys.Add(key);

                                    if (item.State == UploadState.Uploading)
                                    {
                                        if (item.UploadKeys.Count == 0)
                                        {
                                            item.State = UploadState.Completed;

                                            _settings.UploadedSeeds.Add(item.Seed.Clone());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            });
            _uploadedThread.Priority = ThreadPriority.BelowNormal;
            _uploadedThread.Name = "UploadManager_UploadedThread";
            _uploadedThread.Start();

            _removeShareThread = new Thread(() =>
            {
                try
                {
                    for (; ; )
                    {
                        var path = _removeSharePaths.Dequeue();

                        lock (this.ThisLock)
                        {
                            List<int> ids = null;

                            if (_shareLink.TryGetValue(path, out ids))
                            {
                                foreach (var id in ids)
                                {
                                    this.Remove(id);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            });
            _removeShareThread.Priority = ThreadPriority.BelowNormal;
            _removeShareThread.Name = "UploadManager_RemoveShareThread";
            _removeShareThread.Start();
        }

        public Information Information
        {
            get
            {
                lock (this.ThisLock)
                {
                    List<InformationContext> contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("UploadingCount", _settings.UploadItems
                        .Count(n => !(n.State == UploadState.Completed || n.State == UploadState.Error))));

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> UploadingInformation
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
                        contexts.Add(new InformationContext("Name", item.Value.Seed.Name));
                        contexts.Add(new InformationContext("Length", item.Value.Seed.Length));
                        contexts.Add(new InformationContext("State", item.Value.State));
                        contexts.Add(new InformationContext("Rank", item.Value.Rank));
                        contexts.Add(new InformationContext("Path", item.Value.FilePath));

                        if (item.Value.State == UploadState.Completed || item.Value.State == UploadState.Uploading)
                        {
                            contexts.Add(new InformationContext("Seed", item.Value.Seed));
                        }

                        if (item.Value.State == UploadState.Uploading)
                        {
                            contexts.Add(new InformationContext("BlockCount", item.Value.UploadKeys.Count + item.Value.UploadedKeys.Count));
                            contexts.Add(new InformationContext("UploadBlockCount", item.Value.UploadedKeys.Count));
                        }
                        else if (item.Value.State == UploadState.Encoding || item.Value.State == UploadState.ComputeHash || item.Value.State == UploadState.ParityEncoding)
                        {
                            contexts.Add(new InformationContext("EncodeBytes", item.Value.EncodeBytes));
                            contexts.Add(new InformationContext("EncodingBytes", item.Value.EncodingBytes));
                        }
                        else if (item.Value.State == UploadState.Completed)
                        {
                            contexts.Add(new InformationContext("BlockCount", item.Value.UploadKeys.Count + item.Value.UploadedKeys.Count));
                            contexts.Add(new InformationContext("UploadBlockCount", item.Value.UploadedKeys.Count));
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
                lock (this.ThisLock)
                {
                    return _settings.UploadedSeeds;
                }
            }
        }

        private void CheckState(UploadItem item)
        {
            lock (this.ThisLock)
            {
                foreach (var key in item.UploadKeys.ToArray())
                {
                    if (!_connectionsManager.IsUploadWaiting(key))
                    {
                        item.UploadedKeys.Add(key);
                        item.UploadKeys.Remove(key);
                    }
                }

                if (item.State == UploadState.Uploading)
                {
                    if (item.UploadKeys.Count == 0)
                    {
                        item.State = UploadState.Completed;

                        _settings.UploadedSeeds.Add(item.Seed.Clone());
                    }
                }
            }
        }

        private void UploadManagerThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.EncodeState == ManagerState.Stop) return;

                UploadItem item = null;

                try
                {
                    lock (this.ThisLock)
                    {
                        if (_settings.UploadItems.Count > 0)
                        {
                            item = _settings.UploadItems
                                .Where(n => n.State == UploadState.Encoding || n.State == UploadState.ComputeHash || n.State == UploadState.ParityEncoding)
                                .Where(n => n.Priority != 0)
                                .OrderBy(n => -n.Priority)
                                .FirstOrDefault();
                        }
                    }
                }
                catch (Exception)
                {
                    return;
                }

                if (item == null) continue;

                try
                {
                    if (item.Type == UploadType.Upload)
                    {
                        if (item.Groups.Count == 0 && item.Keys.Count == 0)
                        {
                            item.State = UploadState.Encoding;

                            KeyCollection keys = null;
                            byte[] cryptoKey = null;

                            try
                            {
                                using (FileStream stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodingBytes = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodingBytes = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                {
                                    item.EncodeBytes = stream.Length;
                                    item.Seed.Length = stream.Length;

                                    if (item.Seed.Length == 0) throw new InvalidOperationException("Stream Length");

                                    item.State = UploadState.ComputeHash;

                                    if (item.HashAlgorithm == HashAlgorithm.Sha512)
                                    {
                                        cryptoKey = Sha512.ComputeHash(hashProgressStream);
                                    }

                                    stream.Seek(0, SeekOrigin.Begin);
                                    item.EncodingBytes = 0;

                                    item.State = UploadState.Encoding;
                                    keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.BlockLength, item.HashAlgorithm);
                                }
                            }
                            catch (StopIoException)
                            {
                                continue;
                            }

                            lock (this.ThisLock)
                            {
                                foreach (var key in keys)
                                {
                                    item.UploadKeys.Add(key);
                                    item.LockedKeys.Add(key);
                                }

                                item.EncodingBytes = 0;
                                item.EncodeBytes = 0;

                                item.CryptoKey = cryptoKey;
                                item.Keys.AddRange(keys);
                            }
                        }
                        else if (item.Groups.Count == 0 && item.Keys.Count == 1)
                        {
                            lock (this.ThisLock)
                            {
                                item.Seed.Rank = item.Rank;
                                item.Seed.Key = item.Keys[0];
                                item.Keys.Clear();

                                item.Seed.CompressionAlgorithm = item.CompressionAlgorithm;

                                item.Seed.CryptoAlgorithm = item.CryptoAlgorithm;
                                item.Seed.CryptoKey = item.CryptoKey;

                                if (item.DigitalSignature != null)
                                {
                                    item.Seed.CreateCertificate(item.DigitalSignature);
                                }

                                item.UploadKeys.Add(item.Seed.Key);

                                foreach (var key in item.UploadKeys)
                                {
                                    _connectionsManager.Upload(key);
                                }

                                this.CheckState(item);

                                _cacheManager.SetSeed(item.Seed.Clone(), item.Indexes);
                                item.Indexes.Clear();

                                foreach (var key in item.LockedKeys)
                                {
                                    _cacheManager.Unlock(key);
                                }

                                item.LockedKeys.Clear();

                                item.State = UploadState.Uploading;
                            }
                        }
                        else if (item.Keys.Count > 0)
                        {
                            item.State = UploadState.ParityEncoding;

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
                            Group group = null;

                            try
                            {
                                group = _cacheManager.ParityEncoding(keys, item.HashAlgorithm, item.BlockLength, item.CorrectionAlgorithm, (object state2) =>
                                {
                                    return (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));
                                });
                            }
                            catch (StopException)
                            {
                                continue;
                            }

                            lock (this.ThisLock)
                            {
                                foreach (var key in group.Keys.Skip(group.InformationLength))
                                {
                                    item.UploadKeys.Add(key);
                                    item.LockedKeys.Add(key);
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

                                item.Keys.RemoveRange(0, length);
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

                            item.Indexes.Add(index);

                            byte[] cryptoKey = null;
                            KeyCollection keys = null;

                            try
                            {
                                using (var stream = index.Export(_bufferManager))
                                using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodingBytes = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodingBytes = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                {
                                    item.EncodeBytes = stream.Length;

                                    item.State = UploadState.ComputeHash;

                                    if (item.HashAlgorithm == HashAlgorithm.Sha512)
                                    {
                                        cryptoKey = Sha512.ComputeHash(hashProgressStream);
                                    }

                                    stream.Seek(0, SeekOrigin.Begin);
                                    item.EncodingBytes = 0;

                                    item.State = UploadState.Encoding;
                                    keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.BlockLength, item.HashAlgorithm);
                                }
                            }
                            catch (StopIoException)
                            {
                                continue;
                            }

                            lock (this.ThisLock)
                            {
                                foreach (var key in keys)
                                {
                                    item.UploadKeys.Add(key);
                                    item.LockedKeys.Add(key);
                                }

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

                            KeyCollection keys = null;

                            try
                            {
                                using (FileStream stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodingBytes = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                {
                                    item.EncodeBytes = stream.Length;
                                    item.Seed.Length = stream.Length;

                                    if (item.Seed.Length == 0) throw new InvalidOperationException("Stream Length");

                                    keys = _cacheManager.Share(hashProgressStream, stream.Name, item.HashAlgorithm, item.BlockLength);
                                }
                            }
                            catch (StopIoException)
                            {
                                continue;
                            }

                            var groups = new List<Group>();

                            for (int i = 0, remain = keys.Count; 0 < remain; i++, remain -= 256)
                            {
                                var tempKeys = keys.GetRange(i * 256, Math.Min(remain, 256));

                                Group group = new Group();
                                group.CorrectionAlgorithm = CorrectionAlgorithm.None;
                                group.InformationLength = tempKeys.Count;
                                group.BlockLength = item.BlockLength;
                                group.Length = tempKeys.Sum(n => (long)_cacheManager.GetLength(n));
                                group.Keys.AddRange(tempKeys);

                                groups.Add(group);
                            }

                            lock (this.ThisLock)
                            {
                                item.EncodingBytes = 0;
                                item.EncodeBytes = 0;

                                if (keys.Count == 1)
                                {
                                    item.Keys.Add(keys[0]);
                                }
                                else
                                {
                                    foreach (var key in keys)
                                    {
                                        item.UploadKeys.Add(key);
                                    }

                                    item.Groups.AddRange(groups);
                                }

                                item.State = UploadState.Encoding;
                            }
                        }
                        else if (item.Groups.Count == 0 && item.Keys.Count == 1)
                        {
                            lock (this.ThisLock)
                            {
                                item.Seed.Rank = item.Rank;
                                item.Seed.Key = item.Keys[0];
                                item.Keys.Clear();

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

                                foreach (var key in item.UploadKeys)
                                {
                                    _connectionsManager.Upload(key);
                                }

                                this.CheckState(item);

                                _cacheManager.SetSeed(item.Seed.Clone(), item.FilePath, item.Indexes);
                                item.Indexes.Clear();

                                foreach (var key in item.LockedKeys)
                                {
                                    _cacheManager.Unlock(key);
                                }

                                item.LockedKeys.Clear();

                                item.State = UploadState.Uploading;
                            }
                        }
                        else if (item.Keys.Count > 0)
                        {
                            item.State = UploadState.ParityEncoding;

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
                            Group group = null;

                            try
                            {
                                group = _cacheManager.ParityEncoding(keys, item.HashAlgorithm, item.BlockLength, item.CorrectionAlgorithm, (object state2) =>
                                {
                                    return (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));
                                });
                            }
                            catch (StopException)
                            {
                                continue;
                            }

                            lock (this.ThisLock)
                            {
                                foreach (var key in group.Keys.Skip(group.InformationLength))
                                {
                                    item.UploadKeys.Add(key);
                                    item.LockedKeys.Add(key);
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

                                item.Keys.RemoveRange(0, length);
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

                                item.Indexes.Add(index);
                            }

                            byte[] cryptoKey = null;
                            KeyCollection keys = null;

                            try
                            {
                                using (var stream = index.Export(_bufferManager))
                                using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodingBytes = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodingBytes = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                {
                                    item.EncodeBytes = stream.Length;

                                    item.State = UploadState.ComputeHash;

                                    if (item.HashAlgorithm == HashAlgorithm.Sha512)
                                    {
                                        cryptoKey = Sha512.ComputeHash(hashProgressStream);
                                    }

                                    stream.Seek(0, SeekOrigin.Begin);
                                    item.EncodingBytes = 0;

                                    item.State = UploadState.Encoding;
                                    keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.BlockLength, item.HashAlgorithm);
                                }
                            }
                            catch (StopIoException)
                            {
                                continue;
                            }

                            lock (this.ThisLock)
                            {
                                foreach (var key in keys)
                                {
                                    item.UploadKeys.Add(key);
                                    item.LockedKeys.Add(key);
                                }

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
                catch (Exception e)
                {
                    item.State = UploadState.Error;

                    Log.Error(e);
                }
            }
        }

        public void Upload(string filePath,
            string name,
            IEnumerable<string> keywords,
            string comment,
            DigitalSignature digitalSignature,
            int priority)
        {
            lock (this.ThisLock)
            {
                UploadItem item = new UploadItem();

                item.State = UploadState.Encoding;
                item.Type = UploadType.Upload;
                item.Rank = 1;
                item.FilePath = filePath;
                item.CompressionAlgorithm = CompressionAlgorithm.Xz;
                item.CryptoAlgorithm = CryptoAlgorithm.Rijndael256;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha512;
                item.DigitalSignature = digitalSignature;
                item.Seed = new Seed();
                item.Seed.Name = name;
                item.Seed.Keywords.AddRange(keywords);
                item.Seed.CreationTime = DateTime.UtcNow;
                item.Seed.Comment = comment;
                item.BlockLength = 1024 * 1024 * 1;
                item.Priority = priority;

                _settings.UploadItems.Add(item);
                _ids.Add(_id, item);

                _id++;
            }
        }

        public void Share(string filePath,
            string name,
            IEnumerable<string> keywords,
            string comment,
            DigitalSignature digitalSignature,
            int priority)
        {
            lock (this.ThisLock)
            {
                UploadItem item = new UploadItem();

                item.State = UploadState.Encoding;
                item.Type = UploadType.Share;
                item.Rank = 1;
                item.FilePath = filePath;
                item.CompressionAlgorithm = CompressionAlgorithm.Xz;
                item.CryptoAlgorithm = CryptoAlgorithm.Rijndael256;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha512;
                item.DigitalSignature = digitalSignature;
                item.Seed = new Seed();
                item.Seed.Name = name;
                item.Seed.Keywords.AddRange(keywords);
                item.Seed.CreationTime = DateTime.UtcNow;
                item.Seed.Comment = comment;
                item.BlockLength = 1024 * 1024 * 1;
                item.Priority = priority;

                _settings.UploadItems.Add(item);
                _ids.Add(_id, item);

                List<int> idList = null;

                if (!_shareLink.TryGetValue(filePath, out idList))
                {
                    idList = new List<int>();
                    _shareLink[filePath] = idList;
                }

                idList.Add(_id);

                _id++;
            }
        }

        public void Remove(int id)
        {
            lock (this.ThisLock)
            {
                var item = _ids[id];

                foreach (var key in item.LockedKeys)
                {
                    _cacheManager.Unlock(key);
                }

                _settings.UploadItems.Remove(item);
                _ids.Remove(id);

                if (item.Type == UploadType.Share)
                {
                    List<int> idList = null;

                    if (_shareLink.TryGetValue(item.FilePath, out idList))
                    {
                        idList.Remove(id);
                        if (idList.Count == 0) _shareLink.Remove(item.FilePath);
                    }
                }
            }
        }

        public void Reset(int id)
        {
            lock (this.ThisLock)
            {
                var item = _ids[id];

                this.Remove(id);

                if (item.Type == UploadType.Upload)
                {
                    this.Upload(item.FilePath,
                        item.Seed.Name,
                        item.Seed.Keywords,
                        item.Seed.Comment,
                        item.DigitalSignature,
                        item.Priority);
                }
                else if (item.Type == UploadType.Share)
                {
                    this.Share(item.FilePath,
                        item.Seed.Name,
                        item.Seed.Keywords,
                        item.Seed.Comment,
                        item.DigitalSignature,
                        item.Priority);
                }
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
                return _state;
            }
        }

        public ManagerState EncodeState
        {
            get
            {
                return _encodeState;
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;
                }
            }
        }

        public override void Stop()
        {
            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;
                }
            }
        }

        private readonly object _encodeStateLock = new object();

        public void EncodeStart()
        {
            lock (_encodeStateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.EncodeState == ManagerState.Start) return;
                    _encodeState = ManagerState.Start;

                    _uploadManagerThread = new Thread(this.UploadManagerThread);
                    _uploadManagerThread.Priority = ThreadPriority.BelowNormal;
                    _uploadManagerThread.Name = "UploadManager_UploadManagerThread";
                    _uploadManagerThread.Start();
                }
            }
        }

        public void EncodeStop()
        {
            lock (_encodeStateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.EncodeState == ManagerState.Stop) return;
                    _encodeState = ManagerState.Stop;
                }

                _uploadManagerThread.Join();
                _uploadManagerThread = null;
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.UploadItems)
                {
                    foreach (var key in item.LockedKeys)
                    {
                        _cacheManager.Lock(key);
                    }
                }

                foreach (var item in _settings.UploadItems.ToArray())
                {
                    try
                    {
                        this.CheckState(item);
                    }
                    catch (Exception)
                    {
                        _settings.UploadItems.Remove(item);
                    }
                }

                _id = 0;
                _ids.Clear();
                _shareLink.Clear();

                foreach (var item in _settings.UploadItems)
                {
                    _ids.Add(_id, item);

                    if (item.Type == UploadType.Share)
                    {
                        List<int> idList = null;

                        if (_shareLink.TryGetValue(item.FilePath, out idList))
                        {
                            idList.Add(_id);
                        }
                        else
                        {
                            _shareLink.Add(item.FilePath, new List<int>() { _id });
                        }
                    }

                    _id++;
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

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<LockedList<UploadItem>>() { Name = "UploadItems", Value = new LockedList<UploadItem>() },
                    new Library.Configuration.SettingContent<SeedCollection>() { Name = "UploadedSeeds", Value = new SeedCollection() },
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

            public LockedList<UploadItem> UploadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<UploadItem>)this["UploadItems"];
                    }
                }
            }

            public SeedCollection UploadedSeeds
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (SeedCollection)this["UploadedSeeds"];
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _uploadedKeys.Dispose();
                _removeSharePaths.Dispose();

                _uploadedThread.Join();
                _removeShareThread.Join();
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
