using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Library.Collections;
using Library.Security;

namespace Library.Net.Outopos
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

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalSeconds >= 20)
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
                                else if (item.Type == "WikiDocument")
                                {
                                    item.WikiPageContent = ContentConverter.FromWikiPageContentBlock(binaryContent);
                                }
                                else if (item.Type == "WikiVote")
                                {
                                    item.WikiVoteContent = ContentConverter.FromWikiVoteContentBlock(binaryContent);
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

        public IEnumerable<SectionProfile> GetSectionProfiles(Section tag, IEnumerable<string> trustSignatures)
        {
            var hashset = new HashSet<string>(trustSignatures);
            var items = new List<SectionProfile>();

            foreach (var header in _connectionsManager.GetSectionProfileHeaders(tag))
            {
                if (!hashset.Contains(header.Certificate.ToString())) continue;

                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new SectionProfile(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Comment,
                    content.ExchangePublicKey,
                    content.TrustSignatures,
                    content.Wikis,
                    content.Chats));
            }

            return items;
        }

        public IEnumerable<SectionProfile> GetSectionProfiles(Section tag)
        {
            var items = new List<SectionProfile>();

            foreach (var header in _connectionsManager.GetSectionProfileHeaders(tag))
            {
                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new SectionProfile(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Comment,
                    content.ExchangePublicKey,
                    content.TrustSignatures,
                    content.Wikis,
                    content.Chats));
            }

            return items;
        }

        public IEnumerable<SectionMessage> GetSectionMessages(Section tag, IEnumerable<string> trustSignatures, ExchangePrivateKey exchangePrivateKey)
        {
            var hashset = new HashSet<string>(trustSignatures);
            var items = new List<SectionMessage>();

            foreach (var header in _connectionsManager.GetSectionMessageHeaders(tag))
            {
                if (!hashset.Contains(header.Certificate.ToString())) continue;

                var content = this.Download(header, exchangePrivateKey);
                if (content == null) continue;

                items.Add(new SectionMessage(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Comment,
                    content.Anchor));
            }

            return items;
        }

        public IEnumerable<SectionMessage> GetSectionMessages(Section tag, ExchangePrivateKey exchangePrivateKey)
        {
            var items = new List<SectionMessage>();

            foreach (var header in _connectionsManager.GetSectionMessageHeaders(tag))
            {
                var content = this.Download(header, exchangePrivateKey);
                if (content == null) continue;

                items.Add(new SectionMessage(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Comment,
                    content.Anchor));
            }

            return items;
        }

        public IEnumerable<WikiPage> GetWikiPages(Wiki tag, IEnumerable<string> trustSignatures)
        {
            var hashset = new HashSet<string>(trustSignatures);
            var items = new List<WikiPage>();

            foreach (var header in _connectionsManager.GetWikiPageHeaders(tag))
            {
                if (!hashset.Contains(header.Certificate.ToString())) continue;

                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new WikiPage(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.FormatType,
                    content.Hypertext));
            }

            return items;
        }

        public IEnumerable<WikiPage> GetWikiPages(Wiki tag)
        {
            var items = new List<WikiPage>();

            foreach (var header in _connectionsManager.GetWikiPageHeaders(tag))
            {
                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new WikiPage(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.FormatType,
                    content.Hypertext));
            }

            return items;
        }

        public IEnumerable<WikiVote> GetWikiVotes(Wiki tag, IEnumerable<string> trustSignatures)
        {
            var hashset = new HashSet<string>(trustSignatures);
            var items = new List<WikiVote>();

            foreach (var header in _connectionsManager.GetWikiVoteHeaders(tag))
            {
                if (!hashset.Contains(header.Certificate.ToString())) continue;

                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new WikiVote(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Goods,
                    content.Bads));
            }

            return items;
        }

        public IEnumerable<WikiVote> GetWikiVotes(Wiki tag)
        {
            var items = new List<WikiVote>();

            foreach (var header in _connectionsManager.GetWikiVoteHeaders(tag))
            {
                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new WikiVote(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Goods,
                    content.Bads));
            }

            return items;
        }

        public IEnumerable<ChatTopic> GetChatTopics(Chat tag, IEnumerable<string> trustSignatures)
        {
            var hashset = new HashSet<string>(trustSignatures);
            var items = new List<ChatTopic>();

            foreach (var header in _connectionsManager.GetChatTopicHeaders(tag))
            {
                if (!hashset.Contains(header.Certificate.ToString())) continue;

                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new ChatTopic(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.FormatType,
                    content.Hypertext));
            }

            return items;
        }

        public IEnumerable<ChatTopic> GetChatTopics(Chat tag)
        {
            var items = new List<ChatTopic>();

            foreach (var header in _connectionsManager.GetChatTopicHeaders(tag))
            {
                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new ChatTopic(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.FormatType,
                    content.Hypertext));
            }

            return items;
        }

        public IEnumerable<ChatMessage> GetChatMessages(Chat tag, IEnumerable<string> trustSignatures)
        {
            var hashset = new HashSet<string>(trustSignatures);
            var items = new List<ChatMessage>();

            foreach (var header in _connectionsManager.GetChatMessageHeaders(tag))
            {
                if (!hashset.Contains(header.Certificate.ToString())) continue;

                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new ChatMessage(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Comment,
                    content.AnchorSignatures));
            }

            return items;
        }

        public IEnumerable<ChatMessage> GetChatMessages(Chat tag)
        {
            var items = new List<ChatMessage>();

            foreach (var header in _connectionsManager.GetChatMessageHeaders(tag))
            {
                var content = this.Download(header);
                if (content == null) continue;

                items.Add(new ChatMessage(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Comment,
                    content.AnchorSignatures));
            }

            return items;
        }

        public SectionProfile GetSectionProfile(Section tag, string targetSignatures)
        {
            foreach (var header in _connectionsManager.GetSectionProfileHeaders(tag))
            {
                if (header.Certificate.ToString() != targetSignatures) continue;

                var content = this.Download(header);
                if (content == null) continue;

                return new SectionProfile(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Comment,
                    content.ExchangePublicKey,
                    content.TrustSignatures,
                    content.Wikis,
                    content.Chats);
            }

            return null;
        }

        public WikiVote GetWikiVote(Wiki tag, string targetSignatures)
        {
            foreach (var header in _connectionsManager.GetWikiVoteHeaders(tag))
            {
                if (header.Certificate.ToString() != targetSignatures) continue;

                var content = this.Download(header);
                if (content == null) continue;

                return new WikiVote(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.Goods,
                    content.Bads);
            }

            return null;
        }

        public ChatTopic GetChatTopic(Chat tag, string targetSignatures)
        {
            foreach (var header in _connectionsManager.GetChatTopicHeaders(tag))
            {
                if (header.Certificate.ToString() != targetSignatures) continue;

                var content = this.Download(header);
                if (content == null) continue;

                return new ChatTopic(header.Tag,
                    header.Certificate.ToString(),
                    header.CreationTime,
                    content.FormatType,
                    content.Hypertext);
            }

            return null;
        }

        private SectionProfileContent Download(SectionProfileHeader header)
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
                else
                {
                    _downloadItems.Refresh(hash);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.SectionProfileContent;
                }

                return null;
            }
        }

        private SectionMessageContent Download(SectionMessageHeader header, ExchangePrivateKey exchangePrivateKey)
        {
            if (header == null) throw new ArgumentNullException("header");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                byte[] hash;

                using (MemoryStream memoryStream = new MemoryStream())
                {
                    var buffer = header.GetHash(_hashAlgorithm);
                    memoryStream.Write(buffer, 0, buffer.Length);

                    using (var stream = exchangePrivateKey.Export(_bufferManager))
                    {
                        var buffer2 = Sha512.ComputeHash(stream);
                        memoryStream.Write(buffer2, 0, buffer2.Length);
                    }

                    hash = memoryStream.ToArray();
                }

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "SectionMessage";
                    item.Key = header.Key;
                    item.ExchangePrivateKey = exchangePrivateKey;

                    _downloadItems.Add(hash, item);
                }
                else
                {
                    _downloadItems.Refresh(hash);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.SectionMessageContent;
                }

                return null;
            }
        }

        private WikiPageContent Download(WikiPageHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                var hash = header.GetHash(_hashAlgorithm);

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "WikiDocument";
                    item.Key = header.Key;

                    _downloadItems.Add(hash, item);
                }
                else
                {
                    _downloadItems.Refresh(hash);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.WikiPageContent;
                }

                return null;
            }
        }

        private WikiVoteContent Download(WikiVoteHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                var hash = header.GetHash(_hashAlgorithm);

                DownloadItem item;

                if (!_downloadItems.TryGetValue(hash, out item))
                {
                    item = new DownloadItem();
                    item.Type = "WikiVote";
                    item.Key = header.Key;

                    _downloadItems.Add(hash, item);
                }
                else
                {
                    _downloadItems.Refresh(hash);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.WikiVoteContent;
                }

                return null;
            }
        }

        private ChatTopicContent Download(ChatTopicHeader header)
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
                else
                {
                    _downloadItems.Refresh(hash);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.ChatTopicContent;
                }

                return null;
            }
        }

        private ChatMessageContent Download(ChatMessageHeader header)
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
                else
                {
                    _downloadItems.Refresh(hash);
                }

                if (item.State == DownloadState.Completed)
                {
                    return item.ChatMessageContent;
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

        private readonly object _stateLock = new object();

        public override void Start()
        {
            lock (_stateLock)
            {
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

                _downloadManagerThread.Join();
                _downloadManagerThread = null;
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
