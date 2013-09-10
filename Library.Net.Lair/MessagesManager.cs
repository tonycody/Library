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
        private LockedDictionary<Node, MessageManager> _messageManagerDictionary = new LockedDictionary<Node, MessageManager>();
        private LockedDictionary<Node, DateTime> _updateTimeDictionary = new LockedDictionary<Node, DateTime>();
        private int _id = 0;
        private DateTime _lastCircularTime = DateTime.UtcNow;
        private object _thisLock = new object();

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

                        messageManager.PushProfiles.TrimExcess();
                        messageManager.PushDocumentPages.TrimExcess();
                        messageManager.PushDocumentOpinions.TrimExcess();
                        messageManager.PushTopics.TrimExcess();
                        messageManager.PushMessages.TrimExcess();
                        messageManager.PushMailMessages.TrimExcess();

                        messageManager.PushSectionsRequest.TrimExcess();
                        messageManager.PullSectionsRequest.TrimExcess();

                        messageManager.PushChatsRequest.TrimExcess();
                        messageManager.PullChatsRequest.TrimExcess();

                        messageManager.PushSignaturesRequest.TrimExcess();
                        messageManager.PullSignaturesRequest.TrimExcess();
                    }

                    _lastCircularTime = now;
                }

                if (flag)
                {
                    ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
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
                    }));
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

        private CirculationDictionary<string, DateTime> _pushProfiles;
        private CirculationDictionary<string, DateTime> _pushDocumentPages;
        private CirculationCollection<Key> _pushDocumentOpinions;
        private CirculationDictionary<string, DateTime> _pushTopics;
        private CirculationCollection<Key> _pushMessages;
        private CirculationCollection<Key> _pushMailMessages;

        private CirculationCollection<Section> _pushSectionsRequest;
        private CirculationCollection<Section> _pullSectionsRequest;

        private CirculationCollection<Chat> _pushChatsRequest;
        private CirculationCollection<Chat> _pullChatsRequest;

        private CirculationCollection<string> _pushSignaturesRequest;
        private CirculationCollection<string> _pullSignaturesRequest;

        private object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _surroundingNodes = new LockedHashSet<Node>(128);

            _pushProfiles = new CirculationDictionary<string, DateTime>(new TimeSpan(1, 0, 0, 0));
            _pushDocumentPages = new CirculationDictionary<string, DateTime>(new TimeSpan(1, 0, 0, 0));
            _pushDocumentOpinions = new CirculationCollection<Key>(new TimeSpan(1, 0, 0, 0));
            _pushTopics = new CirculationDictionary<string, DateTime>(new TimeSpan(1, 0, 0, 0));
            _pushMessages = new CirculationCollection<Key>(new TimeSpan(1, 0, 0, 0));
            _pushMailMessages = new CirculationCollection<Key>(new TimeSpan(1, 0, 0, 0));

            _pushSectionsRequest = new CirculationCollection<Section>(new TimeSpan(0, 60, 0));
            _pullSectionsRequest = new CirculationCollection<Section>(new TimeSpan(0, 30, 0));

            _pushChatsRequest = new CirculationCollection<Chat>(new TimeSpan(0, 60, 0));
            _pullChatsRequest = new CirculationCollection<Chat>(new TimeSpan(0, 30, 0));

            _pushSignaturesRequest = new CirculationCollection<string>(new TimeSpan(0, 60, 0));
            _pullSignaturesRequest = new CirculationCollection<string>(new TimeSpan(0, 30, 0));
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

        public CirculationDictionary<string, DateTime> PushProfiles
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushProfiles;
                }
            }
        }

        public CirculationDictionary<string, DateTime> PushDocumentPages
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushDocumentPages;
                }
            }
        }

        public CirculationCollection<Key> PushDocumentOpinions
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushDocumentOpinions;
                }
            }
        }

        public CirculationDictionary<string, DateTime> PushTopics
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushTopics;
                }
            }
        }

        public CirculationCollection<Key> PushMessages
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMessages;
                }
            }
        }

        public CirculationCollection<Key> PushMailMessages
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMailMessages;
                }
            }
        }

        public CirculationCollection<Section> PushSectionsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushSectionsRequest;
                }
            }
        }

        public CirculationCollection<Section> PullSectionsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullSectionsRequest;
                }
            }
        }

        public CirculationCollection<Chat> PushChatsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushChatsRequest;
                }
            }
        }

        public CirculationCollection<Chat> PullChatsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullChatsRequest;
                }
            }
        }

        public CirculationCollection<string> PushSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushSignaturesRequest;
                }
            }
        }

        public CirculationCollection<string> PullSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullSignaturesRequest;
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
