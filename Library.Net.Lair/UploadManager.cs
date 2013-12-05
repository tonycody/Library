using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Library.Collections;
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

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

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

                    try
                    {
                        lock (this.ThisLock)
                        {
                            if (_settings.UploadItems.Count > 0)
                            {
                                item = _settings.UploadItems[0];
                                _settings.UploadItems.RemoveAt(0);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    try
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            if (item.LinkType == "Section")
                            {
                                if (item.HeaderType == "Profile")
                                {
                                    buffer = ContentConverter.ToSectionProfileContentBlock(item.SectionProfileContent);
                                }
                                else if (item.HeaderType == "Message")
                                {
                                    buffer = ContentConverter.ToSectionMessageContentBlock(item.SectionMessageContent, item.ExchangePublicKey);
                                }
                            }
                            else if (item.LinkType == "Document")
                            {
                                if (item.HeaderType == "Page")
                                {
                                    buffer = ContentConverter.ToDocumentPageContentBlock(item.DocumentPageContent);
                                }
                                else if (item.HeaderType == "Vote")
                                {
                                    buffer = ContentConverter.ToDocumentVoteContentBlock(item.DocumentVoteContent);
                                }
                            }
                            else if (item.LinkType == "Chat")
                            {
                                if (item.HeaderType == "Topic")
                                {
                                    buffer = ContentConverter.ToChatTopicContentBlock(item.ChatTopicContent);
                                }
                                else if (item.HeaderType == "Message")
                                {
                                    buffer = ContentConverter.ToChatMessageContentBlock(item.ChatMessageContent);
                                }
                            }

                            if (buffer.Count < _maxRawContentSize)
                            {
                                byte[] binaryContent = new byte[1 + buffer.Count];
                                binaryContent[0] = 0; // Content type
                                Array.Copy(buffer.Array, buffer.Offset, binaryContent, 1, buffer.Count - 1);

                                var link = new Link(item.Tag, item.LinkType, item.Path);

                                var header = new Header(link, item.HeaderType, binaryContent, item.DigitalSignature);
                                _connectionsManager.Upload(header);
                            }
                            else
                            {
                                byte[] binaryKey;

                                {
                                    Key key = null;

                                    if (_hashAlgorithm == HashAlgorithm.Sha512)
                                    {
                                        key = new Key(Sha512.ComputeHash(buffer), _hashAlgorithm);
                                    }

                                    _cacheManager.Lock(key);
                                    _settings.LifeSpans[key] = DateTime.UtcNow;

                                    _cacheManager[key] = buffer;
                                    _connectionsManager.Upload(key);

                                    using (var stream = key.Export(_bufferManager))
                                    {
                                        binaryKey = new byte[1 + stream.Length];
                                        binaryKey[0] = 1; // Content type
                                        stream.Read(binaryKey, 1, binaryKey.Length - 1);
                                    }
                                }

                                var link = new Link(item.Tag, item.LinkType, item.Path);

                                var header = new Header(link, item.HeaderType, binaryKey, item.DigitalSignature);
                                _connectionsManager.Upload(header);
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
                    catch (Exception)
                    {

                    }
                }
            }
        }

        public void Upload(Tag tag,
            string path,
            SectionProfileContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Tag = tag;
                uploadItem.Path = path;

                uploadItem.LinkType = "Section";
                uploadItem.HeaderType = "Profile";

                uploadItem.SectionProfileContent = content;

                uploadItem.DigitalSignature = digitalSignature;
            }
        }

        public void Upload(Tag tag,
            string path,
            SectionMessageContent content,

            ExchangePublicKey exchangePublicKey,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Tag = tag;
                uploadItem.Path = path;

                uploadItem.LinkType = "Section";
                uploadItem.HeaderType = "Message";

                uploadItem.SectionMessageContent = content;

                uploadItem.ExchangePublicKey = exchangePublicKey;
                uploadItem.DigitalSignature = digitalSignature;
            }
        }

        public void Upload(Tag tag,
            string path,
            DocumentPageContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Tag = tag;
                uploadItem.Path = path;

                uploadItem.LinkType = "Document";
                uploadItem.HeaderType = "Page";

                uploadItem.DocumentPageContent = content;

                uploadItem.DigitalSignature = digitalSignature;
            }
        }

        public void Upload(Tag tag,
            string path,
            DocumentVoteContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Tag = tag;
                uploadItem.Path = path;

                uploadItem.LinkType = "Document";
                uploadItem.HeaderType = "Vote";

                uploadItem.DocumentVoteContent = content;

                uploadItem.DigitalSignature = digitalSignature;
            }
        }

        public void Upload(Tag tag,
            string path,
            ChatTopicContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Tag = tag;
                uploadItem.Path = path;

                uploadItem.LinkType = "Chat";
                uploadItem.HeaderType = "Topic";

                uploadItem.ChatTopicContent = content;

                uploadItem.DigitalSignature = digitalSignature;
            }
        }

        public void Upload(Tag tag,
            string path,
            ChatMessageContent content,

            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Tag = tag;
                uploadItem.Path = path;

                uploadItem.LinkType = "Chat";
                uploadItem.HeaderType = "Message";

                uploadItem.ChatMessageContent = content;

                uploadItem.DigitalSignature = digitalSignature;
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
                    new Library.Configuration.SettingContent<LockedList<UploadItem>>() { Name = "UploadItems", Value = new LockedList<UploadItem>() },
                    new Library.Configuration.SettingContent<LockedDictionary<Key, DateTime>>() { Name = "LifeSpans", Value = new LockedDictionary<Key, DateTime>() },
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

            public LockedDictionary<Key, DateTime> LifeSpans
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedDictionary<Key, DateTime>)this["LifeSpans"];
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
