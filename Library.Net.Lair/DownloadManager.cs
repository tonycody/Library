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
    public delegate void SectionProfileEventHandler(object sender, Header header, SectionProfileContent content);
    public delegate void SectionMessageEventHandler(object sender, Header header, SectionMessageContent content);
    public delegate void DocumentPageEventHandler(object sender, Header header, DocumentPageContent content);
    public delegate void DocumentOpinionEventHandler(object sender, Header header, DocumentOpinionContent content);
    public delegate void ChatTopicEventHandler(object sender, Header header, ChatTopicContent content);
    public delegate void ChatMessageEventHandler(object sender, Header header, ChatMessageContent content);

    class DownloadManager : StateManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private LockedList<Header> _downloadHeaders = new LockedList<Header>();
        private LockedList<IExchangeDecrypt> _privateKeys = new LockedList<IExchangeDecrypt>();

        private LockedHashSet<Header> _completeHeaders = new LockedHashSet<Header>();
        private LockedDictionary<Header, Key> _keys = new LockedDictionary<Header, Key>();

        private LockedQueue<SectionProfileEventItem> _sectionProfileEventItems = new LockedQueue<SectionProfileEventItem>();
        private LockedQueue<SectionMessageEventItem> _sectionMessageEventItems = new LockedQueue<SectionMessageEventItem>();
        private LockedQueue<DocumentPageEventItem> _documentPageEventItems = new LockedQueue<DocumentPageEventItem>();
        private LockedQueue<DocumentOpinionEventItem> _documentOpinionEventItems = new LockedQueue<DocumentOpinionEventItem>();
        private LockedQueue<ChatTopicEventItem> _chatTopicEventItems = new LockedQueue<ChatTopicEventItem>();
        private LockedQueue<ChatMessageEventItem> _chatMessageEventItems = new LockedQueue<ChatMessageEventItem>();

        private volatile Thread _downloadManagerThread;
        private volatile Thread _eventThread;

        private ManagerState _state = ManagerState.Stop;
        private ManagerState _decodeState = ManagerState.Stop;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private event SectionProfileEventHandler _sectionProfileEvent;
        private event SectionMessageEventHandler _sectionMessageEvent;
        private event DocumentPageEventHandler _documentPageEvent;
        private event DocumentOpinionEventHandler _documentOpinionEvent;
        private event ChatTopicEventHandler _chatTopicEvent;
        private event ChatMessageEventHandler _chatMessageEvent;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;
        }

        public event SectionProfileEventHandler SectionProfileEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _sectionProfileEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _sectionProfileEvent -= value;
                }
            }
        }

        public event SectionMessageEventHandler SectionMessageEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _sectionMessageEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _sectionMessageEvent -= value;
                }
            }
        }

        public event DocumentPageEventHandler DocumentPageEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _documentPageEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _documentPageEvent -= value;
                }
            }
        }

        public event DocumentOpinionEventHandler DocumentOpinionEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _documentOpinionEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _documentOpinionEvent -= value;
                }
            }
        }

        public event ChatTopicEventHandler ChatTopicEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _chatTopicEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _chatTopicEvent -= value;
                }
            }
        }

        public event ChatMessageEventHandler ChatMessageEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _chatMessageEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _chatMessageEvent -= value;
                }
            }
        }

        public IEnumerable<Header> Headers
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _downloadHeaders.ToArray();
                }
            }
        }

        public IEnumerable<IExchangeDecrypt> ExchangeInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _privateKeys.ToArray();
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

                    lock (this.ThisLock)
                    {
                        foreach (var header in _completeHeaders.ToArray())
                        {
                            if (_downloadHeaders.Contains(header)) continue;

                            _completeHeaders.Remove(header);
                        }

                        foreach (var header in _keys.Keys.ToArray())
                        {
                            if (_downloadHeaders.Contains(header)) continue;

                            _keys.Remove(header);
                        }
                    }

                    foreach (var header in _downloadHeaders.ToArray())
                    {
                        if (_completeHeaders.Contains(header)) continue;

                        if (header.FormatType == ContentFormatType.Raw)
                        {
                            try
                            {
                                this.Report(header, new ArraySegment<byte>(header.Content));
                            }
                            catch (Exception)
                            {

                            }

                            _completeHeaders.Add(header);
                        }
                        else if (header.FormatType == ContentFormatType.Key)
                        {
                            Key key;

                            try
                            {
                                if (!_keys.TryGetValue(header, out key))
                                {
                                    using (var memoryStream = new MemoryStream(header.Content))
                                    {
                                        key = Key.Import(memoryStream, _bufferManager);
                                    }

                                    _keys[header] = key;
                                }
                            }
                            catch (Exception)
                            {
                                _completeHeaders.Add(header);

                                continue;
                            }

                            try
                            {
                                if (!_cacheManager.Contains(key))
                                {
                                    _connectionsManager.Download(key);
                                }
                                else
                                {
                                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                                    try
                                    {
                                        buffer = _cacheManager[key];

                                        this.Report(header, buffer);
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

                                    _completeHeaders.Add(header);
                                }
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                }
            }
        }

        private void Report(Header header, ArraySegment<byte> buffer)
        {
            if (header.Tag.Type == "Section")
            {
                if (header.Type == "Profile")
                {
                    try
                    {
                        var content = ContentConverter.FromSectionProfileContentBlock(buffer);
                        _sectionProfileEventItems.Enqueue(new SectionProfileEventItem() { Header = header, Content = content });
                    }
                    catch (Exception)
                    {

                    }
                }
                else if (header.Type == "Message")
                {
                    foreach (var exchange in _privateKeys)
                    {
                        try
                        {
                            var content = ContentConverter.FromSectionMessageContentBlock(buffer, exchange);
                            _sectionMessageEventItems.Enqueue(new SectionMessageEventItem() { Header = header, Content = content });
                        }
                        catch (Exception)
                        {

                        }
                    }
                }
            }
            else if (header.Tag.Type == "Document")
            {
                if (header.Type == "Page")
                {
                    try
                    {
                        var content = ContentConverter.FromDocumentPageContentBlock(buffer);
                        _documentPageEventItems.Enqueue(new DocumentPageEventItem() { Header = header, Content = content });
                    }
                    catch (Exception)
                    {

                    }
                }
                else if (header.Type == "Opinion")
                {
                    try
                    {
                        var content = ContentConverter.FromDocumentOpinionContentBlock(buffer);
                        _documentOpinionEventItems.Enqueue(new DocumentOpinionEventItem() { Header = header, Content = content });
                    }
                    catch (Exception)
                    {

                    }
                }
            }
            else if (header.Tag.Type == "Chat")
            {
                if (header.Type == "Topic")
                {
                    try
                    {
                        var content = ContentConverter.FromChatTopicContentBlock(buffer);
                        _chatTopicEventItems.Enqueue(new ChatTopicEventItem() { Header = header, Content = content });
                    }
                    catch (Exception)
                    {

                    }
                }
                else if (header.Type == "Message")
                {
                    try
                    {
                        var content = ContentConverter.FromChatMessageContentBlock(buffer);
                        _chatMessageEventItems.Enqueue(new ChatMessageEventItem() { Header = header, Content = content });
                    }
                    catch (Exception)
                    {

                    }
                }
            }
        }

        private void EventThread()
        {
            Stopwatch refreshStopwatch = new Stopwatch();

            for (; ; )
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                for (int i = _sectionProfileEventItems.Count - 1; i >= 0; i--)
                {
                    var item = _sectionProfileEventItems.Dequeue();
                    this.OnSectionProfileEvent(item.Header, item.Content);
                }

                for (int i = _sectionMessageEventItems.Count - 1; i >= 0; i--)
                {
                    var item = _sectionMessageEventItems.Dequeue();
                    this.OnSectionMessageEvent(item.Header, item.Content);
                }

                for (int i = _documentPageEventItems.Count - 1; i >= 0; i--)
                {
                    var item = _documentPageEventItems.Dequeue();
                    this.OnDocumentPageEvent(item.Header, item.Content);
                }

                for (int i = _documentOpinionEventItems.Count - 1; i >= 0; i--)
                {
                    var item = _documentOpinionEventItems.Dequeue();
                    this.OnDocumentOpinionEvent(item.Header, item.Content);
                }

                for (int i = _chatTopicEventItems.Count - 1; i >= 0; i--)
                {
                    var item = _chatTopicEventItems.Dequeue();
                    this.OnChatTopicEvent(item.Header, item.Content);
                }

                for (int i = _chatMessageEventItems.Count - 1; i >= 0; i--)
                {
                    var item = _chatMessageEventItems.Dequeue();
                    this.OnChatMessageEvent(item.Header, item.Content);
                }
            }
        }

        protected virtual void OnSectionProfileEvent(Header header, SectionProfileContent content)
        {
            if (_sectionProfileEvent != null)
            {
                _sectionProfileEvent(this, header, content);
            }
        }

        protected virtual void OnSectionMessageEvent(Header header, SectionMessageContent content)
        {
            if (_sectionMessageEvent != null)
            {
                _sectionMessageEvent(this, header, content);
            }
        }

        protected virtual void OnDocumentPageEvent(Header header, DocumentPageContent content)
        {
            if (_documentPageEvent != null)
            {
                _documentPageEvent(this, header, content);
            }
        }

        protected virtual void OnDocumentOpinionEvent(Header header, DocumentOpinionContent content)
        {
            if (_documentOpinionEvent != null)
            {
                _documentOpinionEvent(this, header, content);
            }
        }

        protected virtual void OnChatTopicEvent(Header header, ChatTopicContent content)
        {
            if (_chatTopicEvent != null)
            {
                _chatTopicEvent(this, header, content);
            }
        }

        protected virtual void OnChatMessageEvent(Header header, ChatMessageContent content)
        {
            if (_chatMessageEvent != null)
            {
                _chatMessageEvent(this, header, content);
            }
        }

        public void Download(Header header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                _downloadHeaders.Add(header);
            }
        }

        public void Remove(Header header)
        {
            if (header == null) throw new ArgumentNullException("header");

            lock (this.ThisLock)
            {
                _downloadHeaders.Remove(header);

                _completeHeaders.Remove(header);
                _keys.Remove(header);
            }
        }

        public void SetExchange(IEnumerable<IExchangeDecrypt> exchanges)
        {
            lock (this.ThisLock)
            {
                _privateKeys.AddRange(exchanges);
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

                _eventThread = new Thread(this.EventThread);
                _eventThread.Priority = ThreadPriority.Lowest;
                _eventThread.Name = "DownloadManager_EventThread";
                _eventThread.Start();
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

            _eventThread.Join();
            _eventThread = null;
        }

        private class SectionProfileEventItem
        {
            public Header Header { get; set; }
            public SectionProfileContent Content { get; set; }
        }

        private class SectionMessageEventItem
        {
            public Header Header { get; set; }
            public SectionMessageContent Content { get; set; }
        }

        private class DocumentPageEventItem
        {
            public Header Header { get; set; }
            public DocumentPageContent Content { get; set; }
        }

        private class DocumentOpinionEventItem
        {
            public Header Header { get; set; }
            public DocumentOpinionContent Content { get; set; }
        }

        private class ChatTopicEventItem
        {
            public Header Header { get; set; }
            public ChatTopicContent Content { get; set; }
        }

        private class ChatMessageEventItem
        {
            public Header Header { get; set; }
            public ChatMessageContent Content { get; set; }
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
