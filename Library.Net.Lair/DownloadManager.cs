using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    class DownloadManager : StateManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private VolatileDictionary<Header, DownloadItem> _downloadItems;

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

            _downloadItems = new VolatileDictionary<Header, DownloadItem>(new TimeSpan(0, 0, 10));
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

                        if (item.State != DownloadState.Downloading) continue;

                        ArraySegment<byte> binaryContent = new ArraySegment<byte>();

                        try
                        {
                            if (header.FormatType == ContentFormatType.Raw)
                            {
                                binaryContent = new ArraySegment<byte>(header.Content);
                            }
                            else if (header.FormatType == ContentFormatType.Key)
                            {
                                Key key;

                                try
                                {
                                    if (item.Key == null)
                                    {
                                        using (var memoryStream = new MemoryStream(header.Content))
                                        {
                                            item.Key = Key.Import(memoryStream, _bufferManager);
                                        }
                                    }

                                    key = item.Key;
                                }
                                catch (Exception)
                                {
                                    item.State = DownloadState.Error;

                                    continue;
                                }

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

                            if (header.Tag.Type == "Section")
                            {
                                if (header.Type == "Profile")
                                {
                                    try
                                    {
                                        item.SectionProfileContent = ContentConverter.FromSectionProfileContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                    catch (Exception)
                                    {
                                        item.State = DownloadState.Error;

                                        continue;
                                    }
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
                                        item.State = DownloadState.Error;

                                        continue;
                                    }

                                    item.SectionMessageContent = content;
                                    item.State = DownloadState.Completed;
                                }
                            }
                            else if (header.Tag.Type == "Document")
                            {
                                if (header.Type == "Page")
                                {
                                    try
                                    {
                                        item.DocumentPageContent = ContentConverter.FromDocumentPageContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                    catch (Exception)
                                    {
                                        item.State = DownloadState.Error;

                                        continue;
                                    }
                                }
                                else if (header.Type == "Opinion")
                                {
                                    try
                                    {
                                        item.DocumentOpinionContent = ContentConverter.FromDocumentOpinionContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                    catch (Exception)
                                    {
                                        item.State = DownloadState.Error;

                                        continue;
                                    }
                                }
                            }
                            else if (header.Tag.Type == "Chat")
                            {
                                if (header.Type == "Topic")
                                {
                                    try
                                    {
                                        item.ChatTopicContent = ContentConverter.FromChatTopicContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                    catch (Exception)
                                    {
                                        item.State = DownloadState.Error;

                                        continue;
                                    }
                                }
                                else if (header.Type == "Message")
                                {
                                    try
                                    {
                                        item.ChatMessageContent = ContentConverter.FromChatMessageContentBlock(binaryContent);
                                        item.State = DownloadState.Completed;
                                    }
                                    catch (Exception)
                                    {
                                        item.State = DownloadState.Error;

                                        continue;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            if (binaryContent.Array != null)
                            {
                                if (header.FormatType == ContentFormatType.Key)
                                {
                                    _bufferManager.ReturnBuffer(binaryContent.Array);
                                }
                            }
                        }
                    }
                }
            }
        }

        public Information Download(Header header)
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

                var contexts = new List<InformationContext>();
                contexts.Add(new InformationContext("State", item.State));

                if (item.State == DownloadState.Completed)
                {
                    if (header.Tag.Type == "Section")
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
                    else if (header.Tag.Type == "Document")
                    {
                        if (header.Type == "Page")
                        {
                            contexts.Add(new InformationContext("Content", item.DocumentPageContent));
                        }
                        else if (header.Type == "Opinion")
                        {
                            contexts.Add(new InformationContext("Content", item.DocumentOpinionContent));
                        }
                    }
                    else if (header.Tag.Type == "Chat")
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
                }

                return new Information(contexts);
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
