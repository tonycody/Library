using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Security;

namespace Library.Net.Outopos
{
    class UploadManager : StateManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

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

            _watchTimer = new WatchTimer(this.WatchTimer, new TimeSpan(0, 0, 0), new TimeSpan(3, 0, 0));
        }

        private void WatchTimer()
        {
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

        public Task<SectionProfileHeader> Upload(Section tag, SectionProfileContent content, Miner miner, DigitalSignature digitalSignature)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            if (content == null) throw new ArgumentNullException("content");
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            return Task<SectionProfileHeader>.Factory.StartNew(() =>
            {
                Key key;

                lock (this.ThisLock)
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = ContentConverter.ToSectionProfileContentBlock(content);


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
                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }
                }

                var header = new SectionProfileHeader(new SectionProfileMetadata(tag, digitalSignature.ToString(), DateTime.UtcNow, key, miner), digitalSignature);

                lock (this.ThisLock)
                {
                    _connectionsManager.Upload(header);
                }

                return header;
            });
        }

        public Task<SectionMessageHeader> Upload(Section tag, SectionMessageContent content, ExchangePublicKey exchangePublicKey, Miner miner, DigitalSignature digitalSignature)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            if (content == null) throw new ArgumentNullException("content");
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            return Task<SectionMessageHeader>.Factory.StartNew(() =>
            {
                Key key;

                lock (this.ThisLock)
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = ContentConverter.ToSectionMessageContentBlock(content, exchangePublicKey);


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
                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }
                }

                var header = new SectionMessageHeader(new SectionMessageMetadata(tag, digitalSignature.ToString(), DateTime.UtcNow, key, miner), digitalSignature);

                lock (this.ThisLock)
                {
                    _connectionsManager.Upload(header);
                }

                return header;
            });
        }

        public Task<WikiPageHeader> Upload(Wiki tag, WikiPageContent content, Miner miner, DigitalSignature digitalSignature)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            if (content == null) throw new ArgumentNullException("content");
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            return Task<WikiPageHeader>.Factory.StartNew(() =>
            {
                Key key;

                lock (this.ThisLock)
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = ContentConverter.ToWikiPageContentBlock(content);


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
                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }
                }

                var header = new WikiPageHeader(new WikiPageMetadata(tag, digitalSignature.ToString(), DateTime.UtcNow, key, miner), digitalSignature);

                lock (this.ThisLock)
                {
                    _connectionsManager.Upload(header);
                }

                return header;
            });
        }

        public Task<ChatTopicHeader> Upload(Chat tag, ChatTopicContent content, Miner miner, DigitalSignature digitalSignature)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            if (content == null) throw new ArgumentNullException("content");
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            return Task<ChatTopicHeader>.Factory.StartNew(() =>
            {
                Key key;

                lock (this.ThisLock)
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = ContentConverter.ToChatTopicContentBlock(content);


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
                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }
                }

                var header = new ChatTopicHeader(new ChatTopicMetadata(tag, digitalSignature.ToString(), DateTime.UtcNow, key, miner), digitalSignature);

                lock (this.ThisLock)
                {
                    _connectionsManager.Upload(header);
                }

                return header;
            });
        }

        public Task<ChatMessageHeader> Upload(Chat tag, ChatMessageContent content, Miner miner, DigitalSignature digitalSignature)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            if (content == null) throw new ArgumentNullException("content");
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            return Task<ChatMessageHeader>.Factory.StartNew(() =>
            {
                Key key;

                lock (this.ThisLock)
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = ContentConverter.ToChatMessageContentBlock(content);


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
                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }
                }

                var header = new ChatMessageHeader(new ChatMessageMetadata(tag, digitalSignature.ToString(), DateTime.UtcNow, key, miner), digitalSignature);

                lock (this.ThisLock)
                {
                    _connectionsManager.Upload(header);
                }

                return header;
            });
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
                    new Library.Configuration.SettingContent<LockedHashDictionary<Key, DateTime>>() { Name = "LifeSpans", Value = new LockedHashDictionary<Key, DateTime>() },
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

            public LockedHashDictionary<Key, DateTime> LifeSpans
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<Key, DateTime>)this["LifeSpans"];
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
