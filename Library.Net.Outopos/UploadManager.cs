using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Library.Collections;
using Library.Security;
using System.IO;

namespace Library.Net.Outopos
{
    class UploadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Thread _uploadThread;

        private ManagerState _state = ManagerState.Stop;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public UploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);
        }

        public IEnumerable<Information> UploadingInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    List<Information> list = new List<Information>();

                    foreach (var item in _settings.UploadItems.ToArray())
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Type", item.Type));

                        if (item.Signature != null) contexts.Add(new InformationContext("Signature", item.Signature));
                        if (item.Wiki != null) contexts.Add(new InformationContext("Wiki", item.Wiki));
                        if (item.Chat != null) contexts.Add(new InformationContext("Chat", item.Chat));

                        contexts.Add(new InformationContext("CreationTime", item.CreationTime));
                        contexts.Add(new InformationContext("DigitalSignature", item.DigitalSignature));

                        if (item.Type == "Profile")
                        {
                            contexts.Add(new InformationContext("Content", item.ProfileContent));
                        }
                        else if (item.Type == "SignatureMessage")
                        {
                            contexts.Add(new InformationContext("Content", item.SignatureMessageContent));
                        }
                        else if (item.Type == "WikiPage")
                        {
                            contexts.Add(new InformationContext("Content", item.WikiPageContent));
                        }
                        else if (item.Type == "ChatTopic")
                        {
                            contexts.Add(new InformationContext("Content", item.ChatTopicContent));
                        }
                        else if (item.Type == "ChatMessage")
                        {
                            contexts.Add(new InformationContext("Content", item.ChatMessageContent));
                        }

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        private void UploadThread()
        {
            Stopwatch refreshStopwatch = new Stopwatch();

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalMinutes >= 30)
                {
                    refreshStopwatch.Restart();

                    lock (this.ThisLock)
                    {
                        var now = DateTime.UtcNow;

                        foreach (var item in _settings.LifeSpans.ToArray())
                        {
                            if ((now - item.Value) > new TimeSpan(64, 0, 0, 0))
                            {
                                _cacheManager.Unlock(item.Key);
                                _settings.LifeSpans.Remove(item.Key);
                            }
                        }
                    }
                }

                {
                    UploadItem item = null;

                    lock (this.ThisLock)
                    {
                        if (_settings.UploadItems.Count > 0)
                        {
                            item = _settings.UploadItems[0];
                        }
                    }

                    try
                    {
                        if (item != null)
                        {
                            ArraySegment<byte> buffer = new ArraySegment<byte>();

                            try
                            {
                                if (item.Type == "Profile")
                                {
                                    buffer = ContentConverter.ToProfileContentBlock(item.ProfileContent);
                                }
                                else if (item.Type == "SignatureMessage")
                                {
                                    buffer = ContentConverter.ToSignatureMessageContentBlock(item.SignatureMessageContent, item.ExchangePublicKey);
                                }
                                else if (item.Type == "WikiPage")
                                {
                                    buffer = ContentConverter.ToWikiPageContentBlock(item.WikiPageContent);
                                }
                                else if (item.Type == "ChatTopic")
                                {
                                    buffer = ContentConverter.ToChatTopicContentBlock(item.ChatTopicContent);
                                }
                                else if (item.Type == "ChatMessage")
                                {
                                    buffer = ContentConverter.ToChatMessageContentBlock(item.ChatMessageContent);
                                }

                                Key key = null;

                                {
                                    if (_hashAlgorithm == HashAlgorithm.Sha512)
                                    {
                                        key = new Key(Sha512.ComputeHash(buffer), _hashAlgorithm);
                                    }

                                    _cacheManager.Lock(key);
                                    _settings.LifeSpans[key] = DateTime.UtcNow;
                                }

                                _cacheManager[key] = buffer;
                                _connectionsManager.Upload(key);

                                var miner = new Miner(CashAlgorithm.Version1, item.MiningLimit, item.MiningTime);

                                var task = Task.Factory.StartNew(() =>
                                {
                                    if (item.Type == "Profile")
                                    {
                                        var header = new ProfileHeader(item.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(header);
                                    }
                                    else if (item.Type == "SignatureMessage")
                                    {
                                        var header = new SignatureMessageHeader(item.Signature, item.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(header);
                                    }
                                    else if (item.Type == "WikiPage")
                                    {
                                        var header = new WikiPageHeader(item.Wiki, item.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(header);
                                    }
                                    else if (item.Type == "ChatTopic")
                                    {
                                        var header = new ChatTopicHeader(item.Chat, item.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(header);
                                    }
                                    else if (item.Type == "ChatMessage")
                                    {
                                        var header = new ChatMessageHeader(item.Chat, item.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(header);
                                    }
                                });

                                while (!task.IsCompleted)
                                {
                                    if (this.State == ManagerState.Stop) miner.Cancel();

                                    lock (this.ThisLock)
                                    {
                                        if (!_settings.UploadItems.Contains(item))
                                        {
                                            miner.Cancel();
                                        }
                                    }

                                    Thread.Sleep(1000);
                                }

                                if (task.Exception != null) throw task.Exception;

                                lock (this.ThisLock)
                                {
                                    _settings.UploadItems.Remove(item);
                                }
                            }
                            finally
                            {
                                if (buffer.Array != null)
                                {
                                    _bufferManager.ReturnBuffer(buffer.Array);
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
            }
        }

        public void Upload(
            ProfileContent content,
            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "Profile";
                uploadItem.CreationTime = DateTime.UtcNow;
                uploadItem.ProfileContent = content;
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Type == uploadItem.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(string signature,
            SignatureMessageContent content,
            ExchangePublicKey exchangePublicKey,
            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "SignatureMessage";
                uploadItem.Signature = signature;
                uploadItem.CreationTime = DateTime.UtcNow;
                uploadItem.SignatureMessageContent = content;
                uploadItem.ExchangePublicKey = exchangePublicKey;
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(Wiki tag,
            WikiPageContent content,
            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "WikiPage";
                uploadItem.Wiki = tag;
                uploadItem.CreationTime = DateTime.UtcNow;
                uploadItem.WikiPageContent = content;
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Type == uploadItem.Type
                        && target.Wiki == uploadItem.Wiki
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(Chat tag,
            ChatTopicContent content,
            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "ChatTopic";
                uploadItem.Chat = tag;
                uploadItem.CreationTime = DateTime.UtcNow;
                uploadItem.ChatTopicContent = content;
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Type == uploadItem.Type
                        && target.Chat == uploadItem.Chat
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(Chat tag,
            ChatMessageContent content,
            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "ChatMessage";
                uploadItem.Chat = tag;
                uploadItem.CreationTime = DateTime.UtcNow;
                uploadItem.ChatMessageContent = content;
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public override ManagerState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
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

                    _uploadThread = new Thread(this.UploadThread);
                    _uploadThread.Priority = ThreadPriority.Lowest;
                    _uploadThread.Name = "UploadManager_UploadManagerThread";
                    _uploadThread.Start();
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

                _uploadThread.Join();
                _uploadThread = null;
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                foreach (var key in _settings.LifeSpans.Keys)
                {
                    _cacheManager.Lock(key);
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
                    new Library.Configuration.SettingContent<List<UploadItem>>() { Name = "UploadItems", Value = new List<UploadItem>() },
                    new Library.Configuration.SettingContent<Dictionary<Key, DateTime>>() { Name = "LifeSpans", Value = new Dictionary<Key, DateTime>() },
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

            public List<UploadItem> UploadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (List<UploadItem>)this["UploadItems"];
                    }
                }
            }

            public Dictionary<Key, DateTime> LifeSpans
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Key, DateTime>)this["LifeSpans"];
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
