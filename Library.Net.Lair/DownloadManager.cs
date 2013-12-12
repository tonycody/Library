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

        private VolatileDictionary<SectionProfileHeader, DownloadItem> _sectionProfileDownloadItems;
        private VolatileDictionary<SectionMessageHeader, DownloadItem> _sectionMessageDownloadItems;
        private VolatileDictionary<ArchiveDocumentHeader, DownloadItem> _archiveDocumentDownloadItems;
        private VolatileDictionary<ArchiveVoteHeader, DownloadItem> _archiveVoteDownloadItems;
        private VolatileDictionary<ChatTopicHeader, DownloadItem> _chatTopicDownloadItems;
        private VolatileDictionary<ChatMessageHeader, DownloadItem> _chatMessageDownloadItems;

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

            _sectionProfileDownloadItems = new VolatileDictionary<SectionProfileHeader, DownloadItem>(new TimeSpan(0, 3, 0));
            _sectionMessageDownloadItems = new VolatileDictionary<SectionMessageHeader, DownloadItem>(new TimeSpan(0, 3, 0));
            _archiveDocumentDownloadItems = new VolatileDictionary<ArchiveDocumentHeader, DownloadItem>(new TimeSpan(0, 3, 0));
            _archiveVoteDownloadItems = new VolatileDictionary<ArchiveVoteHeader, DownloadItem>(new TimeSpan(0, 3, 0));
            _chatTopicDownloadItems = new VolatileDictionary<ChatTopicHeader, DownloadItem>(new TimeSpan(0, 3, 0));
            _chatMessageDownloadItems = new VolatileDictionary<ChatMessageHeader, DownloadItem>(new TimeSpan(0, 3, 0));
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

                    foreach (var pair in _sectionProfileDownloadItems.ToArray())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var header = pair.Key;
                        var item = pair.Value;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                            try
                            {
                                if (!_cacheManager.Contains(header.Key))
                                {
                                    _connectionsManager.Download(header.Key);

                                    continue;
                                }
                                else
                                {
                                    try
                                    {
                                        binaryContent = _cacheManager[header.Key];
                                    }
                                    catch (BlockNotFoundException)
                                    {
                                        continue;
                                    }
                                }

                                item.SectionProfileContent = ContentConverter.FromSectionProfileContentBlock(binaryContent);

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

                    foreach (var pair in _sectionMessageDownloadItems.ToArray())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var header = pair.Key;
                        var item = pair.Value;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                            try
                            {
                                if (!_cacheManager.Contains(header.Key))
                                {
                                    _connectionsManager.Download(header.Key);

                                    continue;
                                }
                                else
                                {
                                    try
                                    {
                                        binaryContent = _cacheManager[header.Key];
                                    }
                                    catch (BlockNotFoundException)
                                    {
                                        continue;
                                    }
                                }

                                item.SectionMessageContent = ContentConverter.FromSectionMessageContentBlock(binaryContent, item.ExchangePrivateKey);

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

                    foreach (var pair in _archiveDocumentDownloadItems.ToArray())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var header = pair.Key;
                        var item = pair.Value;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                            try
                            {
                                if (!_cacheManager.Contains(header.Key))
                                {
                                    _connectionsManager.Download(header.Key);

                                    continue;
                                }
                                else
                                {
                                    try
                                    {
                                        binaryContent = _cacheManager[header.Key];
                                    }
                                    catch (BlockNotFoundException)
                                    {
                                        continue;
                                    }
                                }

                                item.ArchiveDocumentContent = ContentConverter.FromArchiveDocumentContentBlock(binaryContent);

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

                    foreach (var pair in _archiveVoteDownloadItems.ToArray())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var header = pair.Key;
                        var item = pair.Value;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                            try
                            {
                                if (!_cacheManager.Contains(header.Key))
                                {
                                    _connectionsManager.Download(header.Key);

                                    continue;
                                }
                                else
                                {
                                    try
                                    {
                                        binaryContent = _cacheManager[header.Key];
                                    }
                                    catch (BlockNotFoundException)
                                    {
                                        continue;
                                    }
                                }

                                item.ArchiveVoteContent = ContentConverter.FromArchiveVoteContentBlock(binaryContent);

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

                    foreach (var pair in _chatTopicDownloadItems.ToArray())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var header = pair.Key;
                        var item = pair.Value;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                            try
                            {
                                if (!_cacheManager.Contains(header.Key))
                                {
                                    _connectionsManager.Download(header.Key);

                                    continue;
                                }
                                else
                                {
                                    try
                                    {
                                        binaryContent = _cacheManager[header.Key];
                                    }
                                    catch (BlockNotFoundException)
                                    {
                                        continue;
                                    }
                                }

                                item.ChatTopicContent = ContentConverter.FromChatTopicContentBlock(binaryContent);

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

                    foreach (var pair in _chatMessageDownloadItems.ToArray())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var header = pair.Key;
                        var item = pair.Value;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                            try
                            {
                                if (!_cacheManager.Contains(header.Key))
                                {
                                    _connectionsManager.Download(header.Key);

                                    continue;
                                }
                                else
                                {
                                    try
                                    {
                                        binaryContent = _cacheManager[header.Key];
                                    }
                                    catch (BlockNotFoundException)
                                    {
                                        continue;
                                    }
                                }

                                item.ChatMessageContent = ContentConverter.FromChatMessageContentBlock(binaryContent);

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
                DownloadItem item;

                if (!_sectionProfileDownloadItems.TryGetValue(header, out item))
                {
                    item = new DownloadItem();
                    _sectionProfileDownloadItems.Add(header, item);
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
                DownloadItem item;

                if (!_sectionMessageDownloadItems.TryGetValue(header, out item))
                {
                    item = new DownloadItem();
                    item.ExchangePrivateKey = exchangePrivateKey;
                    _sectionMessageDownloadItems.Add(header, item);
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
                DownloadItem item;

                if (!_archiveDocumentDownloadItems.TryGetValue(header, out item))
                {
                    item = new DownloadItem();
                    _archiveDocumentDownloadItems.Add(header, item);
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
                DownloadItem item;

                if (!_archiveVoteDownloadItems.TryGetValue(header, out item))
                {
                    item = new DownloadItem();
                    _archiveVoteDownloadItems.Add(header, item);
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
                DownloadItem item;

                if (!_chatTopicDownloadItems.TryGetValue(header, out item))
                {
                    item = new DownloadItem();
                    _chatTopicDownloadItems.Add(header, item);
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
                DownloadItem item;

                if (!_chatMessageDownloadItems.TryGetValue(header, out item))
                {
                    item = new DownloadItem();
                    _chatMessageDownloadItems.Add(header, item);
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
