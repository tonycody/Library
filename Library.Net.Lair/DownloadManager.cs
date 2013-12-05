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

        private LockedDictionary<Header, DownloadItem> _downloadItems = new LockedDictionary<Header, DownloadItem>();

        private LockedList<ExchangePrivateKey> _exchangePrivateKeys = new LockedList<ExchangePrivateKey>();

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
        }

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    List<Information> list = new List<Information>();

                    foreach (var pair in _downloadItems.ToArray())
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        var header = pair.Key;
                        var item = pair.Value;

                        contexts.Add(new InformationContext("Header", header));
                        contexts.Add(new InformationContext("State", item.State));

                        if (header.Link.Type == "Section")
                        {
                            if (header.Type == "Profile")
                            {
                                contexts.Add(new InformationContext("Content", item.SectionProfileContent));
                            }
                            else if (header.Type == "Message")
                            {
                                contexts.Add(new InformationContext("Content", item.SectionMessageContent));
                            }
                        }
                        else if (header.Link.Type == "Document")
                        {
                            if (header.Type == "Page")
                            {
                                contexts.Add(new InformationContext("Content", item.DocumentPageContent));
                            }
                            else if (header.Type == "Vote")
                            {
                                contexts.Add(new InformationContext("Content", item.DocumentVoteContent));
                            }
                        }
                        else if (header.Link.Type == "Chat")
                        {
                            if (header.Type == "Topic")
                            {
                                contexts.Add(new InformationContext("Content", item.ChatTopicContent));
                            }
                            else if (header.Type == "Message")
                            {
                                contexts.Add(new InformationContext("Content", item.ChatMessageContent));
                            }
                        }

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public IEnumerable<ExchangePrivateKey> ExchangePrivateKeys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _exchangePrivateKeys.ToArray();
                }
            }
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

                    foreach (var pair in _downloadItems.ToArray())
                    {
                        var header = pair.Key;
                        var item = pair.Value;

                        try
                        {
                            if (item.State != DownloadState.Downloading) continue;

                            ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                            try
                            {
                                var type = header.Content[0];

                                if (type == 0)
                                {
                                    binaryContent = new ArraySegment<byte>(_bufferManager.TakeBuffer(header.Content.Length - 1), 0, header.Content.Length - 1);
                                    Array.Copy(header.Content, 0, binaryContent.Array, binaryContent.Offset, binaryContent.Count);
                                }
                                else if (type == 1)
                                {
                                    Key key;

                                    if (item.Key == null)
                                    {
                                        using (var memoryStream = new MemoryStream(header.Content))
                                        {
                                            item.Key = Key.Import(memoryStream, _bufferManager);
                                        }
                                    }

                                    key = item.Key;

                                    if (!_cacheManager.Contains(key))
                                    {
                                        _connectionsManager.Download(key);

                                        continue;
                                    }
                                    else
                                    {
                                        try
                                        {
                                            binaryContent = _cacheManager[key];
                                        }
                                        catch (BlockNotFoundException)
                                        {
                                            continue;
                                        }
                                    }
                                }
                                else
                                {
                                    throw new FormatException();
                                }

                                if (header.Link.Type == "Section")
                                {
                                    if (header.Type == "Profile")
                                    {
                                        item.SectionProfileContent = ContentConverter.FromSectionProfileContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                    else if (header.Type == "Message")
                                    {
                                        SectionMessageContent content = null;

                                        foreach (var exchange in _exchangePrivateKeys)
                                        {
                                            try
                                            {
                                                content = ContentConverter.FromSectionMessageContentBlock(binaryContent, exchange);
                                                break;
                                            }
                                            catch (Exception)
                                            {

                                            }
                                        }

                                        if (content == null)
                                        {
                                            throw new FormatException();
                                        }

                                        item.SectionMessageContent = content;
                                        item.State = DownloadState.Completed;
                                    }
                                }
                                else if (header.Link.Type == "Document")
                                {
                                    if (header.Type == "Page")
                                    {
                                        item.DocumentPageContent = ContentConverter.FromDocumentPageContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                    else if (header.Type == "Vote")
                                    {
                                        item.DocumentVoteContent = ContentConverter.FromDocumentVoteContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                }
                                else if (header.Link.Type == "Chat")
                                {
                                    if (header.Type == "Topic")
                                    {
                                        item.ChatTopicContent = ContentConverter.FromChatTopicContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                    else if (header.Type == "Message")
                                    {
                                        item.ChatMessageContent = ContentConverter.FromChatMessageContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                }
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

        public void Download(Header header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                DownloadItem item;

                if (!_downloadItems.TryGetValue(header, out item))
                {
                    item = new DownloadItem();
                    _downloadItems.Add(header, item);
                }
            }
        }

        public void SetExchangePrivateKeys(IEnumerable<ExchangePrivateKey> exchangePrivateKeys)
        {
            lock (this.ThisLock)
            {
                _exchangePrivateKeys.AddRange(exchangePrivateKeys);
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
}
