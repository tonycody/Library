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
                    messageManager.StockProfileHeaders.TrimExcess();
                    messageManager.StockSignatureMessageHeaders.TrimExcess();
                    messageManager.StockWikiPageHeaders.TrimExcess();
                    messageManager.StockChatTopicHeaders.TrimExcess();
                    messageManager.StockChatMessageHeaders.TrimExcess();

                    messageManager.PushBlocksLink.TrimExcess();
                    messageManager.PullBlocksLink.TrimExcess();

                    messageManager.PushBlocksRequest.TrimExcess();
                    messageManager.PullBlocksRequest.TrimExcess();

                    messageManager.PushBroadcastSignaturesRequest.TrimExcess();
                    messageManager.PullBroadcastSignaturesRequest.TrimExcess();

                    messageManager.PushUnicastSignaturesRequest.TrimExcess();
                    messageManager.PullUnicastSignaturesRequest.TrimExcess();

                    messageManager.PushMulticastWikisRequest.TrimExcess();
                    messageManager.PullMulticastWikisRequest.TrimExcess();

                    messageManager.PushMulticastChatsRequest.TrimExcess();
                    messageManager.PullMulticastChatsRequest.TrimExcess();
                }

                if (_messageManagerDictionary.Count > 128)
                {
                    if (_checkedFlag) return;
                    _checkedFlag = true;

                    ThreadPool.QueueUserWorkItem((object wstate) =>
                    {
                        var lockedNodes = new SortedSet<Node>();

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

        private VolatileSortedSet<Key> _stockBlocks;
        private VolatileSortedSet<byte[]> _stockProfileHeaders;
        private VolatileSortedSet<byte[]> _stockSignatureMessageHeaders;
        private VolatileSortedSet<byte[]> _stockWikiPageHeaders;
        private VolatileSortedSet<byte[]> _stockChatTopicHeaders;
        private VolatileSortedSet<byte[]> _stockChatMessageHeaders;

        private VolatileSortedSet<Key> _pushBlocksLink;
        private VolatileSortedSet<Key> _pullBlocksLink;

        private VolatileSortedSet<Key> _pushBlocksRequest;
        private VolatileSortedSet<Key> _pullBlocksRequest;

        private VolatileSortedSet<string> _pushBroadcastSignaturesRequest;
        private VolatileSortedSet<string> _pullBroadcastSignaturesRequest;

        private VolatileSortedSet<string> _pushUnicastSignaturesRequest;
        private VolatileSortedSet<string> _pullUnicastSignaturesRequest;

        private VolatileSortedSet<Wiki> _pushMulticastWikisRequest;
        private VolatileSortedSet<Wiki> _pullMulticastWikisRequest;

        private VolatileSortedSet<Chat> _pushMulticastChatsRequest;
        private VolatileSortedSet<Chat> _pullMulticastChatsRequest;

        private readonly object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _priority = new SafeInteger();

            _receivedByteCount = new SafeInteger();
            _sentByteCount = new SafeInteger();

            _stockBlocks = new VolatileSortedSet<Key>(new TimeSpan(1, 0, 0, 0), new KeyComparer());
            _stockProfileHeaders = new VolatileSortedSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayComparer());
            _stockSignatureMessageHeaders = new VolatileSortedSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayComparer());
            _stockWikiPageHeaders = new VolatileSortedSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayComparer());
            _stockChatTopicHeaders = new VolatileSortedSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayComparer());
            _stockChatMessageHeaders = new VolatileSortedSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayComparer());

            _pushBlocksLink = new VolatileSortedSet<Key>(new TimeSpan(0, 30, 0), new KeyComparer());
            _pullBlocksLink = new VolatileSortedSet<Key>(new TimeSpan(0, 30, 0), new KeyComparer());

            _pushBlocksRequest = new VolatileSortedSet<Key>(new TimeSpan(0, 30, 0), new KeyComparer());
            _pullBlocksRequest = new VolatileSortedSet<Key>(new TimeSpan(0, 30, 0), new KeyComparer());

            _pushBroadcastSignaturesRequest = new VolatileSortedSet<string>(new TimeSpan(0, 30, 0));
            _pullBroadcastSignaturesRequest = new VolatileSortedSet<string>(new TimeSpan(0, 30, 0));

            _pushUnicastSignaturesRequest = new VolatileSortedSet<string>(new TimeSpan(0, 30, 0));
            _pullUnicastSignaturesRequest = new VolatileSortedSet<string>(new TimeSpan(0, 30, 0));

            _pushMulticastWikisRequest = new VolatileSortedSet<Wiki>(new TimeSpan(0, 30, 0), new WikiComparer());
            _pullMulticastWikisRequest = new VolatileSortedSet<Wiki>(new TimeSpan(0, 30, 0), new WikiComparer());

            _pushMulticastChatsRequest = new VolatileSortedSet<Chat>(new TimeSpan(0, 30, 0), new ChatComparer());
            _pullMulticastChatsRequest = new VolatileSortedSet<Chat>(new TimeSpan(0, 30, 0), new ChatComparer());
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

        public VolatileSortedSet<Key> StockBlocks
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockBlocks;
                }
            }
        }

        public VolatileSortedSet<byte[]> StockProfileHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockProfileHeaders;
                }
            }
        }

        public VolatileSortedSet<byte[]> StockSignatureMessageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockSignatureMessageHeaders;
                }
            }
        }

        public VolatileSortedSet<byte[]> StockWikiPageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockWikiPageHeaders;
                }
            }
        }

        public VolatileSortedSet<byte[]> StockChatTopicHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockChatTopicHeaders;
                }
            }
        }

        public VolatileSortedSet<byte[]> StockChatMessageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockChatMessageHeaders;
                }
            }
        }

        public VolatileSortedSet<Key> PushBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksLink;
                }
            }
        }

        public VolatileSortedSet<Key> PullBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksLink;
                }
            }
        }

        public VolatileSortedSet<Key> PushBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksRequest;
                }
            }
        }

        public VolatileSortedSet<Key> PullBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksRequest;
                }
            }
        }

        public VolatileSortedSet<string> PushBroadcastSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBroadcastSignaturesRequest;
                }
            }
        }

        public VolatileSortedSet<string> PullBroadcastSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBroadcastSignaturesRequest;
                }
            }
        }

        public VolatileSortedSet<string> PushUnicastSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushUnicastSignaturesRequest;
                }
            }
        }

        public VolatileSortedSet<string> PullUnicastSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullUnicastSignaturesRequest;
                }
            }
        }

        public VolatileSortedSet<Wiki> PushMulticastWikisRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMulticastWikisRequest;
                }
            }
        }

        public VolatileSortedSet<Wiki> PullMulticastWikisRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullMulticastWikisRequest;
                }
            }
        }

        public VolatileSortedSet<Chat> PushMulticastChatsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMulticastChatsRequest;
                }
            }
        }

        public VolatileSortedSet<Chat> PullMulticastChatsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullMulticastChatsRequest;
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
