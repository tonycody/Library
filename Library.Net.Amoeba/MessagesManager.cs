using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Amoeba
{
    class MessagesManager : IThisLock
    {
        private LockedDictionary<Node, MessageManager> _messageManagerDictionary = new LockedDictionary<Node, MessageManager>();
        private int _id = 0;
        private DateTime _lastCircularTime = DateTime.MinValue;
        private object _thisLock = new object();

        private void Circular()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                var now = DateTime.UtcNow;

                if ((now - _lastCircularTime) > new TimeSpan(0, 1, 0))
                {
                    foreach (var item in _messageManagerDictionary.Keys.ToArray())
                    {
                        _messageManagerDictionary[item].PushSeeds.TrimExcess();
                        _messageManagerDictionary[item].PullBlocksLink.TrimExcess();
                        _messageManagerDictionary[item].PullBlocksRequest.TrimExcess();
                        _messageManagerDictionary[item].PullSeedsLink.TrimExcess();
                        _messageManagerDictionary[item].PullSeedsRequest.TrimExcess();
                        _messageManagerDictionary[item].PushBlocksLink.TrimExcess();
                        _messageManagerDictionary[item].PushBlocksRequest.TrimExcess();
                        _messageManagerDictionary[item].PushSeedsLink.TrimExcess();
                        _messageManagerDictionary[item].PushSeedsRequest.TrimExcess();
                    }

                    _lastCircularTime = now;
                }
            }
        }

        public MessageManager this[Node node]
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (!_messageManagerDictionary.ContainsKey(node))
                    {
                        while (_messageManagerDictionary.Any(n => n.Value.Id == _id)) _id++;
                        _messageManagerDictionary[node] = new MessageManager(_id);
                    }

                    this.Circular();

                    return _messageManagerDictionary[node];
                }
            }
        }

        public void Remove(Node node)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _messageManagerDictionary.Remove(node);
            }
        }

        #region IThisLock メンバ

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

        private LockedHashSet<Node> _surroundingNodes;
        private CirculationCollection<Seed> _pushSeeds;
        
        private bool _pushNodesRequest = false;
        private CirculationCollection<Key> _pushBlocksLink;
        private CirculationCollection<Key> _pushBlocksRequest;
        private CirculationCollection<Keyword> _pushSeedsLink;
        private CirculationCollection<Keyword> _pushSeedsRequest;
        
        private bool _pullNodesRequest = false;
        private CirculationCollection<Key> _pullBlocksLink;
        private CirculationCollection<Key> _pullBlocksRequest;
        private CirculationCollection<Keyword> _pullSeedsLink;
        private CirculationCollection<Keyword> _pullSeedsRequest;
        
        private object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _surroundingNodes = new LockedHashSet<Node>();
            _pushSeeds = new CirculationCollection<Seed>(new TimeSpan(1, 0, 0, 0));

            _pushBlocksLink = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));
            _pushBlocksRequest = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));
            _pushSeedsLink = new CirculationCollection<Keyword>(new TimeSpan(0, 30, 0));
            _pushSeedsRequest = new CirculationCollection<Keyword>(new TimeSpan(0, 30, 0));

            _pullBlocksLink = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));
            _pullBlocksRequest = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));
            _pullSeedsLink = new CirculationCollection<Keyword>(new TimeSpan(0, 30, 0));
            _pullSeedsRequest = new CirculationCollection<Keyword>(new TimeSpan(0, 30, 0));
        }

        public int Id
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _id;
                }
            }
        }

        public byte[] SessionId
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _sessionId;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _sessionId = value;
                }
            }
        }

        public int Priority
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _priority;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _priority = value;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _receivedByteCount;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _receivedByteCount = value;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _sentByteCount;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _sentByteCount = value;
                }
            }
        }

        public LockedHashSet<Node> SurroundingNodes
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _surroundingNodes;
                }
            }
        }

        public CirculationCollection<Seed> PushSeeds
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pushSeeds;
                }
            }
        }

        public bool PushNodesRequest
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pushNodesRequest;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _pushNodesRequest = value;
                }
            }
        }

        public CirculationCollection<Key> PushBlocksLink
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pushBlocksLink;
                }
            }
        }

        public CirculationCollection<Key> PushBlocksRequest
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pushBlocksRequest;
                }
            }
        }

        public CirculationCollection<Keyword> PushSeedsLink
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pushSeedsLink;
                }
            }
        }

        public CirculationCollection<Keyword> PushSeedsRequest
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pushSeedsRequest;
                }
            }
        }

        public bool PullNodesRequest
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pullNodesRequest;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _pullNodesRequest = value;
                }
            }
        }

        public CirculationCollection<Key> PullBlocksLink
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pullBlocksLink;
                }
            }
        }

        public CirculationCollection<Key> PullBlocksRequest
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pullBlocksRequest;
                }
            }
        }

        public CirculationCollection<Keyword> PullSeedsLink
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pullSeedsLink;
                }
            }
        }

        public CirculationCollection<Keyword> PullSeedsRequest
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _pullSeedsRequest;
                }
            }
        }

        #region IThisLock メンバ

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
