using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Library.Collections;
using Library.Security;

namespace Library.Net.Lair
{
    class DownloadManager : StateManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private VolatileDictionary<byte[], DownloadItem> _downloadItems;

        private volatile Thread _downloadManagerThread;

        private ManagerState _state = ManagerState.Stop;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _downloadItems = new VolatileDictionary<byte[], DownloadItem>(new TimeSpan(0, 12, 0), new ByteArrayEqualityComparer());
        }

        private void DownloadThread()
        {
            Stopwatch refreshStopwatch = new Stopwatch();

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalMinutes >= 1)
                {
                    refreshStopwatch.Restart();

                    foreach (var item in _downloadItems.Values.ToArray())
                    {
                        if (this.State == ManagerState.Stop) return;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                            try
                            {
                                if (!_cacheManager.Contains(item.Key))
                                {
                                    _connectionsManager.Download(item.Key);

                                    continue;
                                }
                                else
                                {
                                    try
                                    {
                                        binaryContent = _cacheManager[item.Key];
                                    }
                                    catch (BlockNotFoundException)
                                    {
                                        continue;
                                    }
                                }

                                if (item.Type == "SectionProfile")
                                {
                                    item.SectionProfileContent = ContentConverter.FromSectionProfileContentBlock(binaryContent);
                                }
                                else if (item.Type == "SectionMessage")
                                {
                                    item.SectionMessageContent = ContentConverter.FromSectionMessageContentBlock(binaryContent, item.ExchangePrivateKey);
                                }
                                else if (item.Type == "ArchiveDocument")
                                {
                                    item.ArchiveDocumentContent = ContentConverter.FromArchiveDocumentContentBlock(binaryContent);
                                }
                                else if (item.Type == "ArchiveVote")
                                {
                                    item.ArchiveVoteContent = ContentConverter.FromArchiveVoteContentBlock(binaryContent);
                                }
                                else if (item.Type == "ChatTopic")
                                {
                                    item.ChatTopicContent = ContentConverter.FromChatTopicContentBlock(binaryContent);
                                }
                                else if (item.Type == "ChatMessage")
                                {
                                    item.ChatMessageContent = ContentConverter.FromChatMessageContentBlock(binaryContent);
                                }

                                item.State = DownloadState.Completed;
                            }
                            finally
                            {
                                if (binaryContent.Array != null)
                                {
                                    _bufferManager.ReturnBuffer(binaryContent.Array);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            item.State = DownloadState.Error;

                            continue;
                        }
                    }
                }
            }
        }

        public SectionProfileContent Download(SectionProfileHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                var hash = header.GetHash(_hashAlgorithm);

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "SectionProfile";
                    item.Key = header.Key;

                    _downloadItems.Add(hash, item);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.SectionProfileContent;
                }
                else if (item.State == DownloadState.Error)
                {
                    throw new DecodeException();
                }

                return null;
            }
        }

        public SectionMessageContent Download(SectionMessageHeader header, ExchangePrivateKey exchangePrivateKey)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                var hash = header.GetHash(_hashAlgorithm);

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "SectionMessage";
                    item.Key = header.Key;

                    _downloadItems.Add(hash, item);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.SectionMessageContent;
                }
                else if (item.State == DownloadState.Error)
                {
                    throw new DecodeException();
                }

                return null;
            }
        }

        public ArchiveDocumentContent Download(ArchiveDocumentHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                var hash = header.GetHash(_hashAlgorithm);

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "ArchiveDocument";
                    item.Key = header.Key;

                    _downloadItems.Add(hash, item);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.ArchiveDocumentContent;
                }
                else if (item.State == DownloadState.Error)
                {
                    throw new DecodeException();
                }

                return null;
            }
        }

        public ArchiveVoteContent Download(ArchiveVoteHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                var hash = header.GetHash(_hashAlgorithm);

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "ArchiveVote";
                    item.Key = header.Key;

                    _downloadItems.Add(hash, item);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.ArchiveVoteContent;
                }
                else if (item.State == DownloadState.Error)
                {
                    throw new DecodeException();
                }

                return null;
            }
        }

        public ChatTopicContent Download(ChatTopicHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                var hash = header.GetHash(_hashAlgorithm);

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "ChatTopic";
                    item.Key = header.Key;

                    _downloadItems.Add(hash, item);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.ChatTopicContent;
                }
                else if (item.State == DownloadState.Error)
                {
                    throw new DecodeException();
                }

                return null;
            }
        }

        public ChatMessageContent Download(ChatMessageHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                var hash = header.GetHash(_hashAlgorithm);

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "ChatMessage";
                    item.Key = header.Key;

                    _downloadItems.Add(hash, item);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.ChatMessageContent;
                }
                else if (item.State == DownloadState.Error)
                {
                    throw new DecodeException();
                }

                return null;
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
            while (_downloadManagerThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _downloadManagerThread = new Thread(this.DownloadThread);
                _downloadManagerThread.Priority = ThreadPriority.Lowest;
                _downloadManagerThread.Name = "DownloadManager_DownloadThread";
                _downloadManagerThread.Start();
            }
        }

        public override void Stop()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;
            }

            _downloadManagerThread.Join();
            _downloadManagerThread = null;
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
