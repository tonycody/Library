using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Amoeba
{
    sealed class MessagesManager : IThisLock
    {
        private LockedDictionary<Node, MessageManager> _messageManagerDictionary = new LockedDictionary<Node, MessageManager>();
        private LockedDictionary<Node, DateTime> _updateTimeDictionary = new LockedDictionary<Node, DateTime>();
        private int _id = 0;
        private DateTime _lastCircularTime = DateTime.MinValue;
        private object _thisLock = new object();

        private void Circular()
        {
            lock (this.ThisLock)
            {
                var now = DateTime.UtcNow;

                if ((now - _lastCircularTime) > new TimeSpan(0, 1, 0))
                {
                    if (_messageManagerDictionary.Count > 128)
                    {
                        var nodes = _messageManagerDictionary.Keys.ToList();

                        nodes.Sort(new Comparison<Node>((Node x, Node y) =>
                        {
                            return _updateTimeDictionary[x].CompareTo(_updateTimeDictionary[y]);
                        }));

                        foreach (var node in nodes.Take(_messageManagerDictionary.Count - 128))
                        {
                            _messageManagerDictionary.Remove(node);
                            _updateTimeDictionary.Remove(node);
                        }
                    }

                    foreach (var node in _messageManagerDictionary.Keys.ToArray())
                    {
                        _messageManagerDictionary[node].PushBlocks.TrimExcess();

                        _messageManagerDictionary[node].PullBlocksLink.TrimExcess();
                        _messageManagerDictionary[node].PullBlocksRequest.TrimExcess();

                        _messageManagerDictionary[node].PushBlocksLink.TrimExcess();
                        _messageManagerDictionary[node].PushBlocksRequest.TrimExcess();
                    }

                    _lastCircularTime = now;
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
        private int _priority = 0;

        private long _receivedByteCount;
        private long _sentByteCount;

        private DateTime _lastPullTime = DateTime.UtcNow;
        private LockedHashSet<Node> _surroundingNodes;
        private CirculationCollection<Key> _pushBlocks;

        private CirculationCollection<Key> _pushBlocksLink;
        private CirculationCollection<Key> _pushBlocksRequest;

        private CirculationCollection<Key> _pullBlocksLink;
        private CirculationCollection<Key> _pullBlocksRequest;

        private object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _surroundingNodes = new LockedHashSet<Node>(128);
            _pushBlocks = new CirculationCollection<Key>(new TimeSpan(1, 0, 0, 0));

            _pushBlocksLink = new CirculationCollection<Key>(new TimeSpan(0, 60, 0), 8192 * 60);
            _pushBlocksRequest = new CirculationCollection<Key>(new TimeSpan(0, 60, 0), 8192 * 60);

            _pullBlocksLink = new CirculationCollection<Key>(new TimeSpan(0, 30, 0), 8192 * 30);
            _pullBlocksRequest = new CirculationCollection<Key>(new TimeSpan(0, 30, 0), 8192 * 30);
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

        public LockedHashSet<Node> SurroundingNodes
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _surroundingNodes;
                }
            }
        }

        public CirculationCollection<Key> PushBlocks
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocks;
                }
            }
        }

        public CirculationCollection<Key> PushBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksLink;
                }
            }
        }

        public CirculationCollection<Key> PushBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksRequest;
                }
            }
        }

        public CirculationCollection<Key> PullBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksLink;
                }
            }
        }

        public CirculationCollection<Key> PullBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksRequest;
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
