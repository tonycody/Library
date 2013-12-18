using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Library.Collections;

namespace Library.Net.Lair
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

                        messageManager.PushBlocks.TrimExcess();
                        messageManager.PushSectionProfileHeaders.TrimExcess();
                        messageManager.PushSectionMessageHeaders.TrimExcess();
                        messageManager.PushWikiPageHeaders.TrimExcess();
                        messageManager.PushWikiVoteHeaders.TrimExcess();
                        messageManager.PushChatTopicHeaders.TrimExcess();
                        messageManager.PushChatMessageHeaders.TrimExcess();

                        messageManager.PushBlocksLink.TrimExcess();
                        messageManager.PullBlocksLink.TrimExcess();

                        messageManager.PushBlocksRequest.TrimExcess();
                        messageManager.PullBlocksRequest.TrimExcess();

                        messageManager.PushSectionsRequest.TrimExcess();
                        messageManager.PullSectionsRequest.TrimExcess();

                        messageManager.PushWikisRequest.TrimExcess();
                        messageManager.PullWikisRequest.TrimExcess();

                        messageManager.PushChatsRequest.TrimExcess();
                        messageManager.PullChatsRequest.TrimExcess();
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
        private LockedHashSet<Node> _surroundingNodes;

        private VolatileCollection<Key> _pushBlocks;
        private VolatileCollection<byte[]> _pushSectionProfileHeaders;
        private VolatileCollection<byte[]> _pushSectionMessageHeaders;
        private VolatileCollection<byte[]> _pushWikiPageHeaders;
        private VolatileCollection<byte[]> _pushWikiVoteHeaders;
        private VolatileCollection<byte[]> _pushChatTopicHeaders;
        private VolatileCollection<byte[]> _pushChatMessageHeaders;

        private VolatileCollection<Key> _pushBlocksLink;
        private VolatileCollection<Key> _pullBlocksLink;

        private VolatileCollection<Key> _pushBlocksRequest;
        private VolatileCollection<Key> _pullBlocksRequest;

        private VolatileCollection<Section> _pushSectionsRequest;
        private VolatileCollection<Section> _pullSectionsRequest;

        private VolatileCollection<Wiki> _pushWikisRequest;
        private VolatileCollection<Wiki> _pullWikisRequest;

        private VolatileCollection<Chat> _pushChatsRequest;
        private VolatileCollection<Chat> _pullChatsRequest;

        private readonly object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _surroundingNodes = new LockedHashSet<Node>(128);

            _pushBlocks = new VolatileCollection<Key>(new TimeSpan(1, 0, 0, 0));
            _pushSectionProfileHeaders = new VolatileCollection<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _pushSectionMessageHeaders = new VolatileCollection<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _pushWikiPageHeaders = new VolatileCollection<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _pushWikiVoteHeaders = new VolatileCollection<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _pushChatTopicHeaders = new VolatileCollection<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());
            _pushChatMessageHeaders = new VolatileCollection<byte[]>(new TimeSpan(1, 0, 0, 0), new ByteArrayEqualityComparer());

            _pushBlocksLink = new VolatileCollection<Key>(new TimeSpan(0, 30, 0));
            _pullBlocksLink = new VolatileCollection<Key>(new TimeSpan(0, 30, 0));

            _pushBlocksRequest = new VolatileCollection<Key>(new TimeSpan(0, 30, 0));
            _pullBlocksRequest = new VolatileCollection<Key>(new TimeSpan(0, 30, 0));

            _pushSectionsRequest = new VolatileCollection<Section>(new TimeSpan(0, 30, 0));
            _pullSectionsRequest = new VolatileCollection<Section>(new TimeSpan(0, 30, 0));

            _pushWikisRequest = new VolatileCollection<Wiki>(new TimeSpan(0, 30, 0));
            _pullWikisRequest = new VolatileCollection<Wiki>(new TimeSpan(0, 30, 0));

            _pushChatsRequest = new VolatileCollection<Chat>(new TimeSpan(0, 30, 0));
            _pullChatsRequest = new VolatileCollection<Chat>(new TimeSpan(0, 30, 0));
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

        public VolatileCollection<Key> PushBlocks
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocks;
                }
            }
        }

        public VolatileCollection<byte[]> PushSectionProfileHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushSectionProfileHeaders;
                }
            }
        }

        public VolatileCollection<byte[]> PushSectionMessageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushSectionMessageHeaders;
                }
            }
        }

        public VolatileCollection<byte[]> PushWikiPageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushWikiPageHeaders;
                }
            }
        }

        public VolatileCollection<byte[]> PushWikiVoteHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushWikiVoteHeaders;
                }
            }
        }

        public VolatileCollection<byte[]> PushChatTopicHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushChatTopicHeaders;
                }
            }
        }

        public VolatileCollection<byte[]> PushChatMessageHeaders
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushChatMessageHeaders;
                }
            }
        }

        public VolatileCollection<Key> PushBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksLink;
                }
            }
        }

        public VolatileCollection<Key> PullBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksLink;
                }
            }
        }

        public VolatileCollection<Key> PushBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksRequest;
                }
            }
        }

        public VolatileCollection<Key> PullBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksRequest;
                }
            }
        }

        public VolatileCollection<Section> PushSectionsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushSectionsRequest;
                }
            }
        }

        public VolatileCollection<Section> PullSectionsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullSectionsRequest;
                }
            }
        }

        public VolatileCollection<Wiki> PushWikisRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushWikisRequest;
                }
            }
        }

        public VolatileCollection<Wiki> PullWikisRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullWikisRequest;
                }
            }
        }

        public VolatileCollection<Chat> PushChatsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushChatsRequest;
                }
            }
        }

        public VolatileCollection<Chat> PullChatsRequest
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
