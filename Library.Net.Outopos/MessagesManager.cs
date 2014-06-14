using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Library.Collections;

namespace Library.Net.Outopos
{
    delegate IEnumerable<Node> GetLockNodesEventHandler(object sender);

    sealed class MessagesManager : ManagerBase, IThisLock
    {
        private Dictionary<Node, MessageManager> _messageManagerDictionary = new Dictionary<Node, MessageManager>();
        private Dictionary<Node, DateTime> _updateTimeDictionary = new Dictionary<Node, DateTime>();
        private int _id;

        private WatchTimer _refreshTimer;
        private volatile bool _checkedFlag = false;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public GetLockNodesEventHandler GetLockNodesEvent;

        public MessagesManager()
        {
            _refreshTimer = new WatchTimer(this.RefreshTimer, new TimeSpan(0, 0, 30));
        }

        private void RefreshTimer()
        {
            lock (this.ThisLock)
            {
                foreach (var messageManager in _messageManagerDictionary.Values.ToArray())
                {
                    messageManager.StockBlocks.TrimExcess();
                    messageManager.StockBroadcastProfileHeaders.TrimExcess();
                    messageManager.StockUnicastMessageHeaders.TrimExcess();
                    messageManager.StockWikiPageHeaders.TrimExcess();
                    messageManager.StockChatTopicHeaders.TrimExcess();
                    messageManager.StockChatMessageHeaders.TrimExcess();

                    messageManager.PushBlocksLink.TrimExcess();
                    messageManager.PullBlocksLink.TrimExcess();

                    messageManager.PushBlocksRequest.TrimExcess();
                    messageManager.PullBlocksRequest.TrimExcess();

                    messageManager.PushBroadcastsRequest.TrimExcess();
                    messageManager.PullBroadcastsRequest.TrimExcess();

                    messageManager.PushUnicastsRequest.TrimExcess();
                    messageManager.PullUnicastsRequest.TrimExcess();

                    messageManager.PushWikisRequest.TrimExcess();
                    messageManager.PullWikisRequest.TrimExcess();

                    messageManager.PushChatsRequest.TrimExcess();
                    messageManager.PullChatsRequest.TrimExcess();
                }

                if (_messageManagerDictionary.Count > 128)
                {
                    if (_checkedFlag) return;
                    _checkedFlag = true;

                    ThreadPool.QueueUserWorkItem((object wstate) =>
                    {
                        var lockedNodes = new HashSet<Node>();

                        if (this.GetLockNodesEvent != null)
                        {
                            lockedNodes.UnionWith(this.GetLockNodesEvent(this));
                        }

                        lock (this.ThisLock)
                        {
                            if (_messageManagerDictionary.Count > 128)
                            {
                                var pairs = _updateTimeDictionary.Where(n => !lockedNodes.Contains(n.Key)).ToList();

                                pairs.Sort((x, y) =>
                                {
                                    return x.Value.CompareTo(y.Value);
                                });

                                foreach (var node in pairs.Select(n => n.Key).Take(_messageManagerDictionary.Count - 128))
                                {
                                    _messageManagerDictionary.Remove(node);
                                    _updateTimeDictionary.Remove(node);
                                }
                            }
                        }

                        _checkedFlag = false;
                    });
                }
            }
        }

        public MessageManager this[Node node]
        {
            get
            {
                lock (this.ThisLock)
                {
                    MessageManager messageManager = null;

                    if (!_messageManagerDictionary.TryGetValue(node, out messageManager))
                    {
                        while (_messageManagerDictionary.Any(n => n.Value.Id == _id)) _id++;

                        messageManager = new MessageManager(_id);
                        _messageManagerDictionary[node] = messageManager;
                    }

                    _updateTimeDictionary[node] = DateTime.UtcNow;

                    return messageManager;
                }
            }
        }

        public void Remove(Node node)
        {
            lock (this.ThisLock)
            {
                _messageManagerDictionary.Remove(node);
                _updateTimeDictionary.Remove(node);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _messageManagerDictionary.Clear();
                _updateTimeDictionary.Clear();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_refreshTimer != null)
                {
                    try
                    {
                        _refreshTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _refreshTimer = null;
                }
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

    class MessageManager : IThisLock
    {
        private int _id;
        private byte[] _sessionId;
        private readonly SafeInteger _priority;

        private readonly SafeInteger _receivedByteCount;
        private readonly SafeInteger _sentByteCount;

        private DateTime _lastPullTime = DateTime.UtcNow;

        private VolatileHashSet<Key> _stockBlocks;
        private VolatileHashSet<byte[]> _stockBroadcastProfileHeaders;
        private VolatileHashSet<byte[]> _stockUnicastMessageHeaders;
        private VolatileHashSet<byte[]> _stockWikiPageHeaders;
        private VolatileHashSet<byte[]> _stockChatTopicHeaders;
        private VolatileHashSet<byte[]> _stockChatMessageHeaders;

        private VolatileHashSet<Key> _pushBlocksLink;
        private VolatileHashSet<Key> _pullBlocksLink;

        private VolatileHashSet<Key> _pushBlocksRequest;
        private VolatileHashSet<Key> _pullBlocksRequest;

        private VolatileHashSet<string> _pushBroadcastsRequest;
        private VolatileHashSet<string> _pullBroadcastsRequest;

        private VolatileHashSet<string> _pushUnicastsRequest;
        private VolatileHashSet<string> _pullUnicastsRequest;

        private VolatileHashSet<Wiki> _pushWikisRequest;
        private VolatileHashSet<Wiki> _pullWikisRequest;

        private VolatileHashSet<Chat> _pushChatsRequest;
        private VolatileHashSet<Chat> _pullChatsRequest;

        private readonly object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _priority = new SafeInteger();

            _receivedByteCount = new SafeInteger();
            _sentByteCount = new SafeInteger();

            _stockBlocks = new VolatileHashSet<Key>(new TimeSpan(1, 0, 0, 0));
            _stockBroadcastProfileHeaders = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _stockUnicastMessageHeaders = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _stockWikiPageHeaders = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _stockChatTopicHeaders = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _stockChatMessageHeaders = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());

            _pushBlocksLink = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pullBlocksLink = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));

            _pushBlocksRequest = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pullBlocksRequest = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));

            _pushBroadcastsRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));
            _pullBroadcastsRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));

            _pushUnicastsRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));
            _pullUnicastsRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));

            _pushWikisRequest = new VolatileHashSet<Wiki>(new TimeSpan(0, 30, 0));
            _pullWikisRequest = new VolatileHashSet<Wiki>(new TimeSpan(0, 30, 0));

            _pushChatsRequest = new VolatileHashSet<Chat>(new TimeSpan(0, 30, 0));
            _pullChatsRequest = new VolatileHashSet<Chat>(new TimeSpan(0, 30, 0));
        }

        public int Id
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _id;
                }
            }
        }

        public byte[] SessionId
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _sessionId;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _sessionId = value;
                }
            }
        }

        public SafeInteger Priority
        {
            get
            {
                return _priority;
            }
        }

        public SafeInteger ReceivedByteCount
        {
            get
            {
                return _receivedByteCount;
            }
        }

        public SafeInteger SentByteCount
        {
            get
            {
                return _sentByteCount;
            }
        }

        public DateTime LastPullTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _lastPullTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _lastPullTime = value;
                }
            }
        }

        public VolatileHashSet<Key> StockBlocks
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockBlocks;
                }
            }
        }

        public VolatileHashSet<byte[]> StockBroadcastProfileHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockBroadcastProfileHeaders;
                }
            }
        }

        public VolatileHashSet<byte[]> StockUnicastMessageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockUnicastMessageHeaders;
                }
            }
        }

        public VolatileHashSet<byte[]> StockWikiPageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockWikiPageHeaders;
                }
            }
        }

        public VolatileHashSet<byte[]> StockChatTopicHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockChatTopicHeaders;
                }
            }
        }

        public VolatileHashSet<byte[]> StockChatMessageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockChatMessageHeaders;
                }
            }
        }

        public VolatileHashSet<Key> PushBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksLink;
                }
            }
        }

        public VolatileHashSet<Key> PullBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksLink;
                }
            }
        }

        public VolatileHashSet<Key> PushBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksRequest;
                }
            }
        }

        public VolatileHashSet<Key> PullBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksRequest;
                }
            }
        }

        public VolatileHashSet<string> PushBroadcastsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBroadcastsRequest;
                }
            }
        }

        public VolatileHashSet<string> PullBroadcastsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBroadcastsRequest;
                }
            }
        }

        public VolatileHashSet<string> PushUnicastsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushUnicastsRequest;
                }
            }
        }

        public VolatileHashSet<string> PullUnicastsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullUnicastsRequest;
                }
            }
        }

        public VolatileHashSet<Wiki> PushWikisRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushWikisRequest;
                }
            }
        }

        public VolatileHashSet<Wiki> PullWikisRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullWikisRequest;
                }
            }
        }

        public VolatileHashSet<Chat> PushChatsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushChatsRequest;
                }
            }
        }

        public VolatileHashSet<Chat> PullChatsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullChatsRequest;
                }
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
