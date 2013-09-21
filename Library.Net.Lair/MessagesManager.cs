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

                        messageManager.PushSectionProfiles.TrimExcess();
                        messageManager.PushDocumentPages.TrimExcess();
                        messageManager.PushDocumentOpinions.TrimExcess();
                        messageManager.PushChatTopics.TrimExcess();
                        messageManager.PushChatMessages.TrimExcess();
                        messageManager.PushWhisperMessages.TrimExcess();
                        messageManager.PushMailMessages.TrimExcess();

                        messageManager.PushSectionsRequest.TrimExcess();
                        messageManager.PullSectionsRequest.TrimExcess();

                        messageManager.PushDocumentsRequest.TrimExcess();
                        messageManager.PullDocumentsRequest.TrimExcess();

                        messageManager.PushChatsRequest.TrimExcess();
                        messageManager.PullChatsRequest.TrimExcess();

                        messageManager.PushWhispersRequest.TrimExcess();
                        messageManager.PullWhispersRequest.TrimExcess();

                        messageManager.PushMailsRequest.TrimExcess();
                        messageManager.PullMailsRequest.TrimExcess();
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

        private CirculationDictionary<string, DateTime> _pushSectionProfiles;
        private CirculationCollection<Key> _pushDocumentPages;
        private CirculationDictionary<string, DateTime> _pushDocumentOpinions;
        private CirculationDictionary<string, DateTime> _pushChatTopics;
        private CirculationCollection<Key> _pushChatMessages;
        private CirculationCollection<Key> _pushWhisperMessages;
        private CirculationCollection<Key> _pushMailMessages;

        private CirculationCollection<Section> _pushSectionsRequest;
        private CirculationCollection<Section> _pullSectionsRequest;

        private CirculationCollection<Document> _pushDocumentsRequest;
        private CirculationCollection<Document> _pullDocumentsRequest;

        private CirculationCollection<Chat> _pushChatsRequest;
        private CirculationCollection<Chat> _pullChatsRequest;

        private CirculationCollection<Whisper> _pushWhispersRequest;
        private CirculationCollection<Whisper> _pullWhispersRequest;

        private CirculationCollection<Mail> _pushMailsRequest;
        private CirculationCollection<Mail> _pullMailsRequest;

        private object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _surroundingNodes = new LockedHashSet<Node>(128);

            _pushSectionProfiles = new CirculationDictionary<string, DateTime>(new TimeSpan(1, 0, 0, 0));
            _pushDocumentPages = new CirculationCollection<Key>(new TimeSpan(1, 0, 0, 0));
            _pushDocumentOpinions = new CirculationDictionary<string, DateTime>(new TimeSpan(1, 0, 0, 0));
            _pushChatTopics = new CirculationDictionary<string, DateTime>(new TimeSpan(1, 0, 0, 0));
            _pushChatMessages = new CirculationCollection<Key>(new TimeSpan(1, 0, 0, 0));
            _pushWhisperMessages = new CirculationCollection<Key>(new TimeSpan(1, 0, 0, 0));
            _pushMailMessages = new CirculationCollection<Key>(new TimeSpan(1, 0, 0, 0));

            _pushSectionsRequest = new CirculationCollection<Section>(new TimeSpan(0, 60, 0));
            _pullSectionsRequest = new CirculationCollection<Section>(new TimeSpan(0, 30, 0));

            _pushDocumentsRequest = new CirculationCollection<Document>(new TimeSpan(0, 60, 0));
            _pullDocumentsRequest = new CirculationCollection<Document>(new TimeSpan(0, 30, 0));

            _pushChatsRequest = new CirculationCollection<Chat>(new TimeSpan(0, 60, 0));
            _pullChatsRequest = new CirculationCollection<Chat>(new TimeSpan(0, 30, 0));

            _pushWhispersRequest = new CirculationCollection<Whisper>(new TimeSpan(0, 60, 0));
            _pullWhispersRequest = new CirculationCollection<Whisper>(new TimeSpan(0, 30, 0));

            _pushMailsRequest = new CirculationCollection<Mail>(new TimeSpan(0, 60, 0));
            _pullMailsRequest = new CirculationCollection<Mail>(new TimeSpan(0, 30, 0));
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

        public CirculationDictionary<string, DateTime> PushSectionProfiles
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushSectionProfiles;
                }
            }
        }

        public CirculationCollection<Key> PushDocumentPages
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushDocumentPages;
                }
            }
        }

        public CirculationDictionary<string, DateTime> PushDocumentOpinions
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushDocumentOpinions;
                }
            }
        }

        public CirculationDictionary<string, DateTime> PushChatTopics
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushChatTopics;
                }
            }
        }

        public CirculationCollection<Key> PushChatMessages
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushChatMessages;
                }
            }
        }

        public CirculationCollection<Key> PushWhisperMessages
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushWhisperMessages;
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

        public CirculationCollection<Document> PushDocumentsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushDocumentsRequest;
                }
            }
        }

        public CirculationCollection<Document> PullDocumentsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullDocumentsRequest;
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

        public CirculationCollection<Whisper> PushWhispersRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushWhispersRequest;
                }
            }
        }

        public CirculationCollection<Whisper> PullWhispersRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullWhispersRequest;
                }
            }
        }

        public CirculationCollection<Mail> PushMailsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMailsRequest;
                }
            }
        }

        public CirculationCollection<Mail> PullMailsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullMailsRequest;
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
