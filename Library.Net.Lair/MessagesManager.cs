using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;
using System.Threading;

namespace Library.Net.Lair
{
    delegate IEnumerable<Node> GetLockNodesEventHandler(object sender);

    sealed class MessagesManager : IThisLock
    {
        private LockedDictionary<Node, MessageManager> _messageManagerDictionary = new LockedDictionary<Node, MessageManager>();
        private LockedDictionary<Node, DateTime> _updateTimeDictionary = new LockedDictionary<Node, DateTime>();
        private int _id = 0;
        private DateTime _lastCircularTime = DateTime.MinValue;
        private object _thisLock = new object();

        public GetLockNodesEventHandler GetLockNodesEvent;

        private void Circular()
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
            {
                List<Node> lockedNodes = new List<Node>();

                if (this.GetLockNodesEvent != null)
                {
                    foreach (var node in this.GetLockNodesEvent(this))
                    {
                        lockedNodes.Add(node);
                    }
                }

                lock (this.ThisLock)
                {
                    var now = DateTime.UtcNow;

                    if ((now - _lastCircularTime) > new TimeSpan(0, 1, 0))
                    {
                        if (_messageManagerDictionary.Count > 128)
                        {
                            var nodes = _messageManagerDictionary.Keys.ToList();

                            foreach (var node in lockedNodes)
                            {
                                nodes.Remove(node);
                            }

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
                            _messageManagerDictionary[node].PushMessages.TrimExcess();
                            _messageManagerDictionary[node].PushFilters.TrimExcess();

                            _messageManagerDictionary[node].PushChannelsRequest.TrimExcess();

                            _messageManagerDictionary[node].PullChannelsRequest.TrimExcess();
                        }

                        _lastCircularTime = now;
                    }
                }
            }));
        }

        public MessageManager this[Node node]
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.Circular();

                    if (!_messageManagerDictionary.ContainsKey(node))
                    {
                        while (_messageManagerDictionary.Any(n => n.Value.Id == _id)) _id++;
                        _messageManagerDictionary[node] = new MessageManager(_id);
                    }

                    if (!_updateTimeDictionary.ContainsKey(node))
                    {
                        _updateTimeDictionary[node] = DateTime.UtcNow;
                    }

                    return _messageManagerDictionary[node];
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
        private CirculationCollection<byte[]> _pushMessages;
        private CirculationCollection<byte[]> _pushFilters;

        private CirculationCollection<Channel> _pushChannelsRequest;
        private CirculationCollection<Channel> _pullChannelsRequest;

        private object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _surroundingNodes = new LockedHashSet<Node>(128);
            _pushMessages = new CirculationCollection<byte[]>(new TimeSpan(1, 0, 0, 0), new BytesEqualityComparer());
            _pushFilters = new CirculationCollection<byte[]>(new TimeSpan(1, 0, 0, 0), new BytesEqualityComparer());

            _pushChannelsRequest = new CirculationCollection<Channel>(new TimeSpan(0, 3, 0), 128 * 3 * 2);
            _pullChannelsRequest = new CirculationCollection<Channel>(new TimeSpan(0, 3, 0), 128 * 3 * 2);
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

        public CirculationCollection<byte[]> PushMessages
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMessages;
                }
            }
        }

        public CirculationCollection<byte[]> PushFilters
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushFilters;
                }
            }
        }

        public CirculationCollection<Channel> PushChannelsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushChannelsRequest;
                }
            }
        }

        public CirculationCollection<Channel> PullChannelsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullChannelsRequest;
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
