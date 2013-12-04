using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    class UploadManager : StateManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private volatile Thread _uploadThread;

        private ManagerState _state = ManagerState.Stop;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        private const int _maxRawContentSize = 256;

        public UploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);
        }

        private void UploadThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

            }
        }

        public void UploadSectionProfile(
            byte[] tagId,
            string tagName,
            IEnumerable<string> options,
            SectionProfileContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                Tag tag = new Tag("Section", tagId, tagName);

                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = ContentConverter.ToSectionProfileContentBlock(content);
                }
                catch (Exception)
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }

                    throw;
                }

                if (buffer.Count < _maxRawContentSize)
                {
                    byte[] binaryContent = new byte[buffer.Count];
                    Array.Copy(buffer.Array, buffer.Offset, binaryContent, 0, buffer.Count);

                    var header = new Header(tag, "Profile", options, ContentFormatType.Raw, binaryContent, digitalSignature);
                    _connectionsManager.Upload(header);
                }
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

        public override void Start()
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

        public override void Stop()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;
            }

            _uploadThread.Join();
            _uploadThread = null;
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);
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
                    new Library.Configuration.SettingContent<LockedDictionary<Key, TimeSpan>>() { Name = "LifeSpans", Value = new LockedDictionary<Key, TimeSpan>() },
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

            public LockedDictionary<Key, TimeSpan> LifeSpans
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedDictionary<Key, TimeSpan>)this["LifeSpans"];
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
