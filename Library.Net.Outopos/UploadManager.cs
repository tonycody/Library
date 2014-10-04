using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Security;

namespace Library.Net.Outopos
{
    class UploadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Thread _uploadThread;

        private WatchTimer _watchTimer;

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

            _watchTimer = new WatchTimer(this.WatchTimer, Timeout.Infinite);
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

                        if (item.Type == "Profile")
                        {
                            contexts.Add(new InformationContext("Message", item.Profile));
                        }
                        else if (item.Type == "SignatureMessage")
                        {
                            contexts.Add(new InformationContext("Message", item.SignatureMessage));
                        }
                        else if (item.Type == "WikiDocument")
                        {
                            contexts.Add(new InformationContext("Message", item.WikiDocument));
                        }
                        else if (item.Type == "ChatTopic")
                        {
                            contexts.Add(new InformationContext("Message", item.ChatTopic));
                        }
                        else if (item.Type == "ChatMessage")
                        {
                            contexts.Add(new InformationContext("Message", item.ChatMessage));
                        }

                        contexts.Add(new InformationContext("DigitalSignature", item.DigitalSignature));

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        private void WatchTimer()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;

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

        private void UploadThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

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
                                    buffer = ContentConverter.ToProfileBlock(item.Profile);
                                }
                                else if (item.Type == "SignatureMessage")
                                {
                                    buffer = ContentConverter.ToSignatureMessageBlock(item.SignatureMessage, item.ExchangePublicKey);
                                }
                                else if (item.Type == "WikiDocument")
                                {
                                    buffer = ContentConverter.ToWikiDocumentBlock(item.WikiDocument);
                                }
                                else if (item.Type == "ChatTopic")
                                {
                                    buffer = ContentConverter.ToChatTopicBlock(item.ChatTopic);
                                }
                                else if (item.Type == "ChatMessage")
                                {
                                    buffer = ContentConverter.ToChatMessageBlock(item.ChatMessage);
                                }

                                Key key = null;

                                {
                                    if (_hashAlgorithm == HashAlgorithm.Sha512)
                                    {
                                        key = new Key(Sha512.ComputeHash(buffer), _hashAlgorithm);
                                    }

                                    this.Lock(key);
                                }

                                _cacheManager[key] = buffer;
                                _connectionsManager.Upload(key);

                                var miner = new Miner(CashAlgorithm.Version1, item.MiningLimit, item.MiningTime);

                                var task = Task.Factory.StartNew(() =>
                                {
                                    if (item.Type == "Profile")
                                    {
                                        var metadata = new ProfileMetadata(item.Profile.CreationTime, key, item.DigitalSignature);
                                        _connectionsManager.Upload(metadata);
                                    }
                                    else if (item.Type == "SignatureMessage")
                                    {
                                        var metadata = new SignatureMessageMetadata(item.SignatureMessage.Signature, item.SignatureMessage.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(metadata);
                                    }
                                    else if (item.Type == "WikiDocument")
                                    {
                                        var metadata = new WikiDocumentMetadata(item.WikiDocument.Tag, item.WikiDocument.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(metadata);
                                    }
                                    else if (item.Type == "ChatTopic")
                                    {
                                        var metadata = new ChatTopicMetadata(item.ChatTopic.Tag, item.ChatTopic.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(metadata);
                                    }
                                    else if (item.Type == "ChatMessage")
                                    {
                                        var metadata = new ChatMessageMetadata(item.ChatMessage.Tag, item.ChatMessage.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(metadata);
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

        private void Lock(Key key)
        {
            lock (this.ThisLock)
            {
                if (!_settings.LifeSpans.ContainsKey(key))
                {
                    _cacheManager.Lock(key);
                }

                _settings.LifeSpans[key] = DateTime.UtcNow;
            }
        }

        public Profile UploadProfile(
            int cost,
            ExchangePublicKey exchangePublicKey,
            IEnumerable<string> trustSignatures,
            IEnumerable<string> deleteSignatures,
            IEnumerable<Wiki> wikis,
            IEnumerable<Chat> chats,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "Profile";
                uploadItem.Profile = new Profile(DateTime.UtcNow, cost, exchangePublicKey, trustSignatures, deleteSignatures, wikis, chats, digitalSignature);
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Type == uploadItem.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(uploadItem);

                return uploadItem.Profile;
            }
        }

        public SignatureMessage UploadSignatureMessage(string signature,
            string comment,

            ExchangePublicKey exchangePublicKey,
            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "SignatureMessage";
                uploadItem.SignatureMessage = new SignatureMessage(signature, DateTime.UtcNow, comment, digitalSignature);
                uploadItem.ExchangePublicKey = exchangePublicKey;
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);

                return uploadItem.SignatureMessage;
            }
        }

        public WikiDocument UploadWikiDocument(Wiki tag,
            IEnumerable<WikiPage> wikiPages,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "WikiDocument";
                uploadItem.WikiDocument = new WikiDocument(tag, DateTime.UtcNow, wikiPages, digitalSignature);
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Type == uploadItem.Type
                        && target.WikiDocument.Tag == uploadItem.WikiDocument.Tag
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(uploadItem);

                return uploadItem.WikiDocument;
            }
        }

        public ChatTopic UploadChatTopic(Chat tag,
            HypertextFormatType formatType,
            string hypertext,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "ChatTopic";
                uploadItem.ChatTopic = new ChatTopic(tag, DateTime.UtcNow, formatType, hypertext, digitalSignature);
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Type == uploadItem.Type
                        && target.ChatTopic.Tag == uploadItem.ChatTopic.Tag
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(uploadItem);

                return uploadItem.ChatTopic;
            }
        }

        public ChatMessage UploadChatMessage(Chat tag,
            string comment,
            IEnumerable<Anchor> anchors,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "ChatMessage";
                uploadItem.ChatMessage = new ChatMessage(tag, DateTime.UtcNow, comment, anchors, digitalSignature);
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);

                return uploadItem.ChatMessage;
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

                    _watchTimer.Change(0, 1000 * 60 * 10);

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

                _watchTimer.Change(Timeout.Infinite);

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
