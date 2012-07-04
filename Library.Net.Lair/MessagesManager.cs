using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;

namespace Library.Net.Lair
{
    class MessagesManager : IThisLock
    {
        private LockedDictionary<Node, MessageManager> _messageManagerDictionary = new LockedDictionary<Node, MessageManager>();
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
                    foreach (var item in _messageManagerDictionary.Keys.ToArray())
                    {
                        _messageManagerDictionary[item].PushMessages.TrimExcess();
                        _messageManagerDictionary[item].PushFilters.TrimExcess();

                        _messageManagerDictionary[item].PushChannelsRequest.TrimExcess();

                        _messageManagerDictionary[item].PullChannelsRequest.TrimExcess();
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
            lock (this.ThisLock)
            {
                _messageManagerDictionary.Remove(node);
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

        private LockedHashSet<Node> _surroundingNodes;
        private CirculationCollection<Message> _pushMessages;
        private CirculationCollection<Filter> _pushFilters;

        private CirculationCollection<Channel> _pushChannelsRequest;
        private CirculationCollection<Channel> _pullChannelsRequest;

        private object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _surroundingNodes = new LockedHashSet<Node>(128);
            _pushMessages = new CirculationCollection<Message>(new TimeSpan(64, 0, 0, 0));
            _pushFilters = new CirculationCollection<Filter>(new TimeSpan(64, 0, 0, 0));

            _pushChannelsRequest = new CirculationCollection<Channel>(new TimeSpan(0, 60, 0), 8192 * 30);
            _pullChannelsRequest = new CirculationCollection<Channel>(new TimeSpan(0, 30, 0), 8192 * 30);
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

        public CirculationCollection<Message> PushMessages
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMessages;
                }
            }
        }

        public CirculationCollection<Filter> PushFilters
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
