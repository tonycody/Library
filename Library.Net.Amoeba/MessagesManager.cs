using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Library.Collections;

namespace Library.Net.Amoeba
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
                    messageManager.StockLinkSeeds.TrimExcess();
                    messageManager.StockStoreSeeds.TrimExcess();

                    messageManager.PushBlocksLink.TrimExcess();
                    messageManager.PullBlocksLink.TrimExcess();

                    messageManager.PushBlocksRequest.TrimExcess();
                    messageManager.PullBlocksRequest.TrimExcess();

                    messageManager.PushSeedsRequest.TrimExcess();
                    messageManager.PullSeedsRequest.TrimExcess();
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
        private VolatileSortedDictionary<string, DateTime> _stockLinkSeeds;
        private VolatileSortedDictionary<string, DateTime> _stockStoreSeeds;

        private VolatileSortedSet<Key> _pushBlocksLink;
        private VolatileSortedSet<Key> _pullBlocksLink;

        private VolatileSortedSet<Key> _pushBlocksRequest;
        private VolatileSortedSet<Key> _pullBlocksRequest;

        private VolatileSortedSet<string> _pushSeedsRequest;
        private VolatileSortedSet<string> _pullSeedsRequest;

        private readonly object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _priority = new SafeInteger();

            _receivedByteCount = new SafeInteger();
            _sentByteCount = new SafeInteger();

            _stockBlocks = new VolatileSortedSet<Key>(new TimeSpan(1, 0, 0, 0), new KeyComparer());
            _stockLinkSeeds = new VolatileSortedDictionary<string, DateTime>(new TimeSpan(1, 0, 0));
            _stockStoreSeeds = new VolatileSortedDictionary<string, DateTime>(new TimeSpan(1, 0, 0));

            _pushBlocksLink = new VolatileSortedSet<Key>(new TimeSpan(0, 30, 0), new KeyComparer());
            _pullBlocksLink = new VolatileSortedSet<Key>(new TimeSpan(0, 30, 0), new KeyComparer());

            _pushBlocksRequest = new VolatileSortedSet<Key>(new TimeSpan(0, 30, 0), new KeyComparer());
            _pullBlocksRequest = new VolatileSortedSet<Key>(new TimeSpan(0, 30, 0), new KeyComparer());

            _pushSeedsRequest = new VolatileSortedSet<string>(new TimeSpan(0, 30, 0));
            _pullSeedsRequest = new VolatileSortedSet<string>(new TimeSpan(0, 30, 0));
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

        public VolatileSortedDictionary<string, DateTime> StockLinkSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockLinkSeeds;
                }
            }
        }

        public VolatileSortedDictionary<string, DateTime> StockStoreSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockStoreSeeds;
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

        public VolatileSortedSet<string> PushSeedsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushSeedsRequest;
                }
            }
        }

        public VolatileSortedSet<string> PullSeedsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullSeedsRequest;
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
