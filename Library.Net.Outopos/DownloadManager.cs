using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Security;

namespace Library.Net.Outopos
{
    class DownloadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
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

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _watchTimer = new WatchTimer(this.WatchTimer, Timeout.Infinite);
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

        public Profile GetMessage(ProfileMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        this.Lock(metadata.Key);

                        return ContentConverter.FromProfileBlock(buffer);
                    }
                    catch (Exception)
                    {

                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }

                    return null;
                }
            }
        }

        public SignatureMessage GetMessage(SignatureMessageMetadata metadata, ExchangePrivateKey exchangePrivateKey)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        this.Lock(metadata.Key);

                        return ContentConverter.FromSignatureMessageBlock(buffer, exchangePrivateKey);
                    }
                    catch (Exception)
                    {

                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }

                    return null;
                }
            }
        }

        public WikiDocument GetMessage(WikiDocumentMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        this.Lock(metadata.Key);

                        return ContentConverter.FromWikiDocumentBlock(buffer);
                    }
                    catch (Exception)
                    {

                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }

                    return null;
                }
            }
        }

        public ChatTopic GetMessage(ChatTopicMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        this.Lock(metadata.Key);

                        return ContentConverter.FromChatTopicBlock(buffer);
                    }
                    catch (Exception)
                    {

                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }

                    return null;
                }
            }
        }

        public ChatMessage GetMessage(ChatMessageMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        this.Lock(metadata.Key);

                        return ContentConverter.FromChatMessageBlock(buffer);
                    }
                    catch (Exception)
                    {

                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }

                    return null;
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

    [Serializable]
    class DownloadManagerException : ManagerException
    {
        public DownloadManagerException() : base() { }
        public DownloadManagerException(string message) : base(message) { }
        public DownloadManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class DecodeException : DownloadManagerException
    {
        public DecodeException() : base() { }
        public DecodeException(string message) : base(message) { }
        public DecodeException(string message, Exception innerException) : base(message, innerException) { }
    }
}
