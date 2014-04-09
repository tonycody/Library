using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Library.Collections;

namespace Library.Net.Amoeba
{
    delegate IEnumerable<Node> GetLockNodesEventHandler(object sender);

    sealed class MessagesManager : IThisLock
    {
        private Dictionary<Node, MessageManager> _messageManagerDictionary = new Dictionary<Node, MessageManager>();
        private Dictionary<Node, DateTime> _updateTimeDictionary = new Dictionary<Node, DateTime>();
        private int _id;
        private DateTime _lastCircularTime = DateTime.UtcNow;
        private readonly object _thisLock = new object();

        public GetLockNodesEventHandler GetLockNodesEvent;

        private void Circular()
        {
            lock (this.ThisLock)
            {
                bool flag = false;
                var now = DateTime.UtcNow;

                if ((now - _lastCircularTime) > new TimeSpan(0, 1, 0))
                {
                    if (_messageManagerDictionary.Count > 128)
                    {
                        flag = true;
                    }

                    foreach (var node in _messageManagerDictionary.Keys.ToArray())
                    {
                        var messageManager = _messageManagerDictionary[node];

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

                    _lastCircularTime = now;
                }

                if (flag)
                {
                    ThreadPool.QueueUserWorkItem((object wstate) =>
                    {
                        List<Node> lockedNodes = new List<Node>();

                        if (this.GetLockNodesEvent != null)
                        {
                            lockedNodes.AddRange(this.GetLockNodesEvent(this));
                        }

                        lock (this.ThisLock)
                        {
                            if (_messageManagerDictionary.Count > 128)
                            {
                                var nodes = _messageManagerDictionary.Keys.ToList();

                                foreach (var node in lockedNodes)
                                {
                                    nodes.Remove(node);
                                }

                                nodes.Sort((x, y) =>
                                {
                                    return _updateTimeDictionary[x].CompareTo(_updateTimeDictionary[y]);
                                });

                                foreach (var node in nodes.Take(_messageManagerDictionary.Count - 128))
                                {
                                    _messageManagerDictionary.Remove(node);
                                    _updateTimeDictionary.Remove(node);
                                }
                            }
                        }
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
                    this.Circular();

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
        private int _priority;

        private long _receivedByteCount;
        private long _sentByteCount;

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

            _stockBlocks = new VolatileSortedSet<Key>(new TimeSpan(1, 0, 0, 0), new KeyComparer());
            _stockLinkSeeds = new VolatileSortedDictionary<string, DateTime>(new TimeSpan(1, 0, 0, 0));
            _stockStoreSeeds = new VolatileSortedDictionary<string, DateTime>(new TimeSpan(1, 0, 0, 0));

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

        public int Priority
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _priority;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _priority = value;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _receivedByteCount;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _receivedByteCount = value;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _sentByteCount;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _sentByteCount = value;
                }
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
