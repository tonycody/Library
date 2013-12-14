﻿using System;
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
                        if (item != null)
                        {
                            ArraySegment<byte> buffer = new ArraySegment<byte>();

                            try
                            {
                                if (item.Type == "SectionProfile")
                                {
                                    buffer = ContentConverter.ToSectionProfileContentBlock(item.SectionProfileContent);
                                }
                                else if (item.Type == "SectionMessage")
                                {
                                    buffer = ContentConverter.ToSectionMessageContentBlock(item.SectionMessageContent, item.ExchangePublicKey);
                                }
                                else if (item.Type == "ArchiveDocument")
                                {
                                    buffer = ContentConverter.ToArchiveDocumentContentBlock(item.ArchiveDocumentContent);
                                }
                                else if (item.Type == "ArchiveVote")
                                {
                                    buffer = ContentConverter.ToArchiveVoteContentBlock(item.ArchiveVoteContent);
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

                                if (item.Type == "SectionProfile")
                                {
                                    var header = new SectionProfileHeader(item.Section, key, item.DigitalSignature);
                                    _connectionsManager.Upload(header);
                                }
                                else if (item.Type == "SectionMessage")
                                {
                                    var header = new SectionMessageHeader(item.Section, key, item.DigitalSignature);
                                    _connectionsManager.Upload(header);
                                }
                                else if (item.Type == "ArchiveDocument")
                                {
                                    var header = new ArchiveDocumentHeader(item.Archive, key, item.DigitalSignature);
                                    _connectionsManager.Upload(header);
                                }
                                else if (item.Type == "ArchiveVote")
                                {
                                    var header = new ArchiveVoteHeader(item.Archive, key, item.DigitalSignature);
                                    _connectionsManager.Upload(header);
                                }
                                else if (item.Type == "ChatTopic")
                                {
                                    var header = new ChatTopicHeader(item.Chat, key, item.DigitalSignature);
                                    _connectionsManager.Upload(header);
                                }
                                else if (item.Type == "ChatMessage")
                                {
                                    var header = new ChatMessageHeader(item.Chat, key, item.DigitalSignature);
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
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e);
                    }
                }
            }
        }

        public void Upload(Section tag,
            SectionProfileContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "SectionProfile";
                uploadItem.Section = tag;
                uploadItem.SectionProfileContent = content;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(Section tag,
            SectionMessageContent content,
            ExchangePublicKey exchangePublicKey,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "SectionMessage";
                uploadItem.Section = tag;
                uploadItem.SectionMessageContent = content;
                uploadItem.ExchangePublicKey = exchangePublicKey;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(Archive tag,
            ArchiveDocumentContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "ArchiveDocument";
                uploadItem.Archive = tag;
                uploadItem.ArchiveDocumentContent = content;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(Archive tag,
            ArchiveVoteContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "ArchiveVote";
                uploadItem.Archive = tag;
                uploadItem.ArchiveVoteContent = content;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(Chat tag,
            ChatTopicContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "ChatTopic";
                uploadItem.Chat = tag;
                uploadItem.ChatTopicContent = content;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);
            }
        }

        public void Upload(Chat tag,
            ChatMessageContent content,
            DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "ChatMessage";
                uploadItem.Chat = tag;
                uploadItem.ChatMessageContent = content;
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