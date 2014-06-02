using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    class UploadManager : ManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private WatchTimer _watchTimer;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public UploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _watchTimer = new WatchTimer(this.WatchTimer, new TimeSpan(0, 3, 0));
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

        public Header Upload(Tag tag, Stream stream, DigitalSignature digitalSignature)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            if (digitalSignature == null) throw new ArgumentNullException("digitalSignature");

            lock (this.ThisLock)
            {
                if (stream == null)
                {
                    var header = new Header(tag, DateTime.UtcNow, null, digitalSignature);
                    _connectionsManager.Upload(header);

                    return header;
                }
                else
                {
                    using (var dataStream = new RangeStream(stream))
                    {
                        if (dataStream.Length == 0) throw new UploadManagerException();
                        if (dataStream.Length > 1024 * 1024 * 8) throw new UploadManagerException();

                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)dataStream.Length), 0, (int)dataStream.Length);
                            dataStream.Read(buffer.Array, buffer.Offset, buffer.Count);

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

                            var header = new Header(tag, DateTime.UtcNow, key, digitalSignature);
                            _connectionsManager.Upload(header);

                            return header;
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
                    }
                }
            }

            return null;
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

    [Serializable]
    class UploadManagerException : ManagerException
    {
        public UploadManagerException() : base() { }
        public UploadManagerException(string message) : base(message) { }
        public UploadManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
