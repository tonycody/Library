using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Net.Connection;
using Library.Security;

namespace Library.Net.Lair
{
    public delegate IEnumerable<string> TrustSignaturesEventHandler(object sender);
    public delegate IEnumerable<Document> LockDocumentsEventHandler(object sender);
    public delegate IEnumerable<Chat> LockChatsEventHandler(object sender);
    public delegate IEnumerable<Whisper> LockWhispersEventHandler(object sender);
    public delegate IEnumerable<Mail> LockMailsEventHandler(object sender);

    class ConnectionsManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Kademlia<Node> _routeTable;
        private static Random _random = new Random();

        private byte[] _mySessionId;

        private LockedList<ConnectionManager> _connectionManagers;
        private MessagesManager _messagesManager;

        private LockedDictionary<Node, HashSet<string>> _pushSignaturesRequestDictionary = new LockedDictionary<Node, HashSet<string>>();
        private LockedDictionary<Node, HashSet<Document>> _pushDocumentsRequestDictionary = new LockedDictionary<Node, HashSet<Document>>();
        private LockedDictionary<Node, HashSet<Chat>> _pushChatsRequestDictionary = new LockedDictionary<Node, HashSet<Chat>>();
        private LockedDictionary<Node, HashSet<Whisper>> _pushWhispersRequestDictionary = new LockedDictionary<Node, HashSet<Whisper>>();
        private LockedDictionary<Node, HashSet<Mail>> _pushMailsRequestDictionary = new LockedDictionary<Node, HashSet<Mail>>();

        private LockedHashSet<string> _trustSignatures = new LockedHashSet<string>();

        private LockedList<Node> _creatingNodes;
        private CirculationCollection<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private CirculationDictionary<Node, int> _nodesStatus;

        private CirculationCollection<string> _pushSignaturesRequestList;
        private CirculationCollection<Document> _pushDocumentsRequestList;
        private CirculationCollection<Chat> _pushChatsRequestList;
        private CirculationCollection<Whisper> _pushWhispersRequestList;
        private CirculationCollection<Mail> _pushMailsRequestList;

        private LockedDictionary<string, DateTime> _lastUsedSignatureTimes = new LockedDictionary<string, DateTime>();
        private LockedDictionary<Document, DateTime> _lastUsedDocumentTimes = new LockedDictionary<Document, DateTime>();
        private LockedDictionary<Chat, DateTime> _lastUsedChatTimes = new LockedDictionary<Chat, DateTime>();
        private LockedDictionary<Whisper, DateTime> _lastUsedWhisperTimes = new LockedDictionary<Whisper, DateTime>();
        private LockedDictionary<Mail, DateTime> _lastUsedMailTimes = new LockedDictionary<Mail, DateTime>();

        private volatile Thread _connectionsManagerThread = null;
        private volatile Thread _createClientConnection1Thread = null;
        private volatile Thread _createClientConnection2Thread = null;
        private volatile Thread _createClientConnection3Thread = null;
        private volatile Thread _createServerConnection1Thread = null;
        private volatile Thread _createServerConnection2Thread = null;
        private volatile Thread _createServerConnection3Thread = null;

        private ManagerState _state = ManagerState.Stop;

        private Dictionary<Node, string> _nodeToUri = new Dictionary<Node, string>();

        private BandwidthLimit _bandwidthLimit = new BandwidthLimit();

        private long _receivedByteCount = 0;
        private long _sentByteCount = 0;

        private volatile int _pushNodeCount;
        private volatile int _pushSignatureRequestCount;
        private volatile int _pushSignatureProfileCount;
        private volatile int _pushDocumentRequestCount;
        private volatile int _pushDocumentSiteCount;
        private volatile int _pushDocumentOpinionCount;
        private volatile int _pushChatRequestCount;
        private volatile int _pushChatTopicCount;
        private volatile int _pushChatMessageCount;
        private volatile int _pushWhisperRequestCount;
        private volatile int _pushWhisperMessageCount;
        private volatile int _pushMailRequestCount;
        private volatile int _pushMailMessageCount;

        private volatile int _pullNodeCount;
        private volatile int _pullSignatureRequestCount;
        private volatile int _pullSignatureProfileCount;
        private volatile int _pullDocumentRequestCount;
        private volatile int _pullDocumentSiteCount;
        private volatile int _pullDocumentOpinionCount;
        private volatile int _pullChatRequestCount;
        private volatile int _pullChatTopicCount;
        private volatile int _pullChatMessageCount;
        private volatile int _pullWhisperRequestCount;
        private volatile int _pullWhisperMessageCount;
        private volatile int _pullMailRequestCount;
        private volatile int _pullMailMessageCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        private TrustSignaturesEventHandler _trustSignaturesEvent;
        private LockDocumentsEventHandler _lockDocumentsEvent;
        private LockChatsEventHandler _lockChatsEvent;
        private LockWhispersEventHandler _lockWhispersEvent;
        private LockMailsEventHandler _lockMailsEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        private const int _maxNodeCount = 128;
        private const int _maxRequestCount = 32;
        private const int _maxContentCount = 32;

        private const int _routeTableMinCount = 100;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

#if DEBUG
        private const int _downloadingConnectionCountLowerLimit = 0;
        private const int _uploadingConnectionCountLowerLimit = 0;
#else
        private const int _downloadingConnectionCountLowerLimit = 3;
        private const int _uploadingConnectionCountLowerLimit = 3;
#endif

        public ConnectionsManager(ClientManager clientManager, ServerManager serverManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _clientManager = clientManager;
            _serverManager = serverManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings();

            _routeTable = new Kademlia<Node>(512, 20);

            _connectionManagers = new LockedList<ConnectionManager>();
            _messagesManager = new MessagesManager();
            _messagesManager.GetLockNodesEvent = (object sender) =>
            {
                lock (this.ThisLock)
                {
                    return _connectionManagers.Select(n => n.Node).ToArray();
                }
            };

            _creatingNodes = new LockedList<Node>();
            _cuttingNodes = new CirculationCollection<Node>(new TimeSpan(0, 30, 0));
            _removeNodes = new CirculationCollection<Node>(new TimeSpan(0, 10, 0));
            _nodesStatus = new CirculationDictionary<Node, int>(new TimeSpan(0, 30, 0));

            _pushSignaturesRequestList = new CirculationCollection<string>(new TimeSpan(0, 3, 0));
            _pushDocumentsRequestList = new CirculationCollection<Document>(new TimeSpan(0, 3, 0));
            _pushChatsRequestList = new CirculationCollection<Chat>(new TimeSpan(0, 3, 0));
            _pushWhispersRequestList = new CirculationCollection<Whisper>(new TimeSpan(0, 3, 0));
            _pushMailsRequestList = new CirculationCollection<Mail>(new TimeSpan(0, 3, 0));

            this.UpdateSessionId();
        }

        public TrustSignaturesEventHandler TrustSignaturesEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _trustSignaturesEvent = value;
                }
            }
        }

        public LockDocumentsEventHandler LockDocumentsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockDocumentsEvent = value;
                }
            }
        }

        public LockChatsEventHandler LockChatsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockChatsEvent = value;
                }
            }
        }

        public LockWhispersEventHandler LockWhispersEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockWhispersEvent = value;
                }
            }
        }

        public LockMailsEventHandler LockMailsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockMailsEvent = value;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _settings.BaseNode;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _settings.BaseNode = value;
                    _routeTable.BaseNode = value;

                    this.UpdateSessionId();
                }
            }
        }

        public IEnumerable<Node> OtherNodes
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _routeTable.ToArray();
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _settings.ConnectionCountLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _settings.ConnectionCountLimit = value;
                }
            }
        }

        public int BandWidthLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _settings.BandwidthLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _settings.BandwidthLimit = value;
                    _bandwidthLimit.In = value;
                    _bandwidthLimit.Out = value;
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    List<Information> list = new List<Information>();

                    foreach (var item in _connectionManagers.ToArray())
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Id", _messagesManager[item.Node].Id));
                        contexts.Add(new InformationContext("Node", item.Node));
                        contexts.Add(new InformationContext("Uri", _nodeToUri[item.Node]));
                        contexts.Add(new InformationContext("Priority", _messagesManager[item.Node].Priority));
                        contexts.Add(new InformationContext("ReceivedByteCount", item.ReceivedByteCount + _messagesManager[item.Node].ReceivedByteCount));
                        contexts.Add(new InformationContext("SentByteCount", item.SentByteCount + _messagesManager[item.Node].SentByteCount));

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public Information Information
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    List<InformationContext> contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("PushNodeCount", _pushNodeCount));
                    contexts.Add(new InformationContext("PushSignatureRequestCount", _pushSignatureRequestCount));
                    contexts.Add(new InformationContext("PushSignatureProfileCount", _pushSignatureProfileCount));
                    contexts.Add(new InformationContext("PushDocumentRequestCount", _pushDocumentRequestCount));
                    contexts.Add(new InformationContext("PushDocumentSiteCount", _pushDocumentSiteCount));
                    contexts.Add(new InformationContext("PushDocumentOpinionCount", _pushDocumentOpinionCount));
                    contexts.Add(new InformationContext("PushChatRequestCount", _pushChatRequestCount));
                    contexts.Add(new InformationContext("PushChatTopicCount", _pushChatTopicCount));
                    contexts.Add(new InformationContext("PushChatMessageCount", _pushChatMessageCount));
                    contexts.Add(new InformationContext("PushWhisperRequestCount", _pushWhisperRequestCount));
                    contexts.Add(new InformationContext("PushWhisperMessageCount", _pushWhisperMessageCount));
                    contexts.Add(new InformationContext("PushMailRequestCount", _pushMailRequestCount));
                    contexts.Add(new InformationContext("PushMailMessageCount", _pushMailMessageCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PullSignatureRequestCount", _pullSignatureRequestCount));
                    contexts.Add(new InformationContext("PullSignatureProfileCount", _pullSignatureProfileCount));
                    contexts.Add(new InformationContext("PullDocumentRequestCount", _pullDocumentRequestCount));
                    contexts.Add(new InformationContext("PullDocumentSiteCount", _pullDocumentSiteCount));
                    contexts.Add(new InformationContext("PullDocumentOpinionCount", _pullDocumentOpinionCount));
                    contexts.Add(new InformationContext("PullChatRequestCount", _pullChatRequestCount));
                    contexts.Add(new InformationContext("PullChatTopicCount", _pullChatTopicCount));
                    contexts.Add(new InformationContext("PullChatMessageCount", _pullChatMessageCount));
                    contexts.Add(new InformationContext("PullWhisperRequestCount", _pullWhisperRequestCount));
                    contexts.Add(new InformationContext("PullWhisperMessageCount", _pullWhisperMessageCount));
                    contexts.Add(new InformationContext("PullMailRequestCount", _pullMailRequestCount));
                    contexts.Add(new InformationContext("PullMailMessageCount", _pullMailMessageCount));

                    contexts.Add(new InformationContext("AcceptConnectionCount", _acceptConnectionCount));
                    contexts.Add(new InformationContext("CreateConnectionCount", _createConnectionCount));

                    contexts.Add(new InformationContext("OtherNodeCount", _routeTable.Count));

                    {
                        HashSet<Node> nodes = new HashSet<Node>();

                        foreach (var connectionManager in _connectionManagers)
                        {
                            nodes.Add(connectionManager.Node);

                            foreach (var node in _messagesManager[connectionManager.Node].SurroundingNodes)
                            {
                                nodes.Add(node);
                            }
                        }

                        contexts.Add(new InformationContext("SurroundingNodeCount", nodes.Count));
                    }

                    contexts.AddRange(_settings.Information);

                    return new Information(contexts);
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _receivedByteCount + _connectionManagers.Sum(n => n.ReceivedByteCount);
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _sentByteCount + _connectionManagers.Sum(n => n.SentByteCount);
                }
            }
        }

        protected virtual IEnumerable<string> OnTrustSignaturesEvent()
        {
            if (_trustSignaturesEvent != null)
            {
                return _trustSignaturesEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Document> OnLockDocumentsEvent()
        {
            if (_lockDocumentsEvent != null)
            {
                return _lockDocumentsEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Chat> OnLockChatsEvent()
        {
            if (_lockChatsEvent != null)
            {
                return _lockChatsEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Whisper> OnLockWhispersEvent()
        {
            if (_lockWhispersEvent != null)
            {
                return _lockWhispersEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Mail> OnLockMailsEvent()
        {
            if (_lockMailsEvent != null)
            {
                return _lockMailsEvent(this);
            }

            return null;
        }

        private void UpdateSessionId()
        {
            lock (this.ThisLock)
            {
                _mySessionId = new byte[64];
                (new System.Security.Cryptography.RNGCryptoServiceProvider()).GetBytes(_mySessionId);
            }
        }

        private void RemoveNode(Node node)
        {
#if !DEBUG
            int closeCount;

            _nodesStatus.TryGetValue(node, out closeCount);
            _nodesStatus[node] = ++closeCount;

            if (closeCount >= 3)
            {
                _removeNodes.Add(node);

                if (_routeTable.Count > _routeTableMinCount)
                {
                    _routeTable.Remove(node);
                }

                _nodesStatus.Remove(node);
            }
#endif
        }

        private double ResponseTimePriority(Node node)
        {
            lock (this.ThisLock)
            {
                List<KeyValuePair<Node, TimeSpan>> nodes = new List<KeyValuePair<Node, TimeSpan>>();

                foreach (var connectionManager in _connectionManagers)
                {
                    nodes.Add(new KeyValuePair<Node, TimeSpan>(connectionManager.Node, connectionManager.ResponseTime));
                }

                if (nodes.Count <= 1) return 0.5;

                nodes.Sort((x, y) =>
                {
                    return y.Value.CompareTo(x.Value);
                });

                int i = 1;
                while (i < nodes.Count && nodes[i].Key != node) i++;

                return ((double)i / (double)nodes.Count);
            }
        }

        // 汚いけど、こっちのほうがCPU使用率の一時的な跳ね上がりを防げる

        private Stopwatch _searchNodeStopwatch = new Stopwatch();
        private LockedHashSet<Node> _searchNodes = new LockedHashSet<Node>();
        private LockedHashSet<Node> _connectionsNodes = new LockedHashSet<Node>();
        private LockedDictionary<Node, TimeSpan> _responseTimeDic = new LockedDictionary<Node, TimeSpan>();

        private IEnumerable<Node> GetSearchNode(byte[] id, int count)
        {
            lock (this.ThisLock)
            {
                if (!_searchNodeStopwatch.IsRunning || _searchNodeStopwatch.Elapsed.TotalSeconds > 10)
                {
                    lock (_connectionsNodes.ThisLock)
                    {
                        _connectionsNodes.Clear();
                        _connectionsNodes.UnionWith(_connectionManagers.Select(n => n.Node));
                    }

                    lock (_searchNodes.ThisLock)
                    {
                        _searchNodes.Clear();

                        foreach (var node in _connectionsNodes)
                        {
                            var messageManager = _messagesManager[node];

                            _searchNodes.UnionWith(messageManager.SurroundingNodes);
                            _searchNodes.Add(node);
                        }
                    }

                    lock (_responseTimeDic.ThisLock)
                    {
                        _responseTimeDic.Clear();

                        foreach (var connectionManager in _connectionManagers)
                        {
                            _responseTimeDic.Add(connectionManager.Node, connectionManager.ResponseTime);
                        }
                    }

                    _searchNodeStopwatch.Restart();
                }
            }

            lock (this.ThisLock)
            {
                var requestNodes = Kademlia<Node>.Sort(this.BaseNode, id, _searchNodes).ToList();
                var returnNodes = new List<Node>();

                foreach (var item in requestNodes)
                {
                    if (_connectionsNodes.Contains(item))
                    {
                        if (!returnNodes.Contains(item))
                        {
                            returnNodes.Add(item);
                        }
                    }
                    else
                    {
                        var list = _connectionsNodes
                            .Where(n => _messagesManager[n].SurroundingNodes.Contains(item))
                            .ToList();

                        list.Sort((x, y) =>
                        {
                            return _responseTimeDic[x].CompareTo(_responseTimeDic[y]);
                        });

                        foreach (var node in list)
                        {
                            if (!returnNodes.Contains(node))
                            {
                                returnNodes.Add(node);
                            }
                        }
                    }

                    if (returnNodes.Count >= count) break;
                }

                return returnNodes.Take(count);
            }
        }

        //private IEnumerable<Node> GetSearchNode(byte[] id, int count)
        //{
        //    HashSet<Node> searchNodes = new HashSet<Node>();
        //    HashSet<Node> connectionsNodes = new HashSet<Node>();

        //    lock (this.ThisLock)
        //    {
        //        connectionsNodes.UnionWith(_connectionManagers.Select(n => n.Node));

        //        foreach (var node in connectionsNodes)
        //        {
        //            var messageManager = _messagesManager[node];

        //            searchNodes.UnionWith(messageManager.SurroundingNodes);
        //            searchNodes.Add(node);
        //        }
        //    }

        //    var requestNodes = Kademlia<Node>.Sort(this.BaseNode, id, searchNodes).ToList();
        //    var returnNodes = new List<Node>();

        //    foreach (var item in requestNodes)
        //    {
        //        if (connectionsNodes.Contains(item))
        //        {
        //            returnNodes.Add(item);
        //        }
        //        else
        //        {
        //            var list = connectionsNodes.Where(n => _messagesManager[n].SurroundingNodes.Contains(item)).ToList();
        //            var responseTimeDic = new Dictionary<Node, TimeSpan>();

        //            foreach (var connectionManager in _connectionManagers)
        //            {
        //                responseTimeDic.Add(connectionManager.Node, connectionManager.ResponseTime);
        //            }

        //            list.Sort((x, y) =>
        //            {
        //                var tx = _connectionManagers.FirstOrDefault(n => n.Node == x);
        //                var ty = _connectionManagers.FirstOrDefault(n => n.Node == y);

        //                if (tx == null && ty != null) return 1;
        //                else if (tx != null && ty == null) return -1;
        //                else if (tx == null && ty == null) return 0;

        //                return tx.ResponseTime.CompareTo(ty.ResponseTime);
        //            });

        //            returnNodes.AddRange(list.Where(n => !returnNodes.Contains(n)));
        //        }

        //        if (returnNodes.Count >= count) break;
        //    }

        //    return returnNodes.Take(count);
        //}

        private void AddConnectionManager(ConnectionManager connectionManager, string uri)
        {
            lock (this.ThisLock)
            {
                if (Collection.Equals(connectionManager.Node.Id, this.BaseNode.Id)
                    || _connectionManagers.Any(n => Collection.Equals(n.Node.Id, connectionManager.Node.Id)))
                {
                    connectionManager.Dispose();
                    return;
                }

                {
                    bool flag = false;

                    if (connectionManager.Type == ConnectionManagerType.Server)
                    {
                        var connectionCount = 0;

                        lock (this.ThisLock)
                        {
                            connectionCount = _connectionManagers
                                .Where(n => n.Type == ConnectionManagerType.Server)
                                .Count();
                        }

                        if (connectionCount > ((this.ConnectionCountLimit / 3) * 2))
                        {
                            flag = true;
                        }
                    }

                    if (_connectionManagers.Count > this.ConnectionCountLimit)
                    {
                        flag = true;
                    }

                    if (flag)
                    {
                        connectionManager.Dispose();

                        return;
                    }
                }

                Debug.WriteLine("ConnectionManager: Connect");

                connectionManager.PullNodesEvent += new PullNodesEventHandler(connectionManager_NodesEvent);
                connectionManager.PullSignaturesRequestEvent += new PullSignaturesRequestEventHandler(connectionManager_PullSignaturesRequestEvent);
                connectionManager.PullSignatureProfilesEvent += new PullSignatureProfilesEventHandler(connectionManager_PullSignatureProfilesEvent);
                connectionManager.PullDocumentsRequestEvent += new PullDocumentsRequestEventHandler(connectionManager_PullDocumentsRequestEvent);
                connectionManager.PullDocumentSitesEvent += new PullDocumentSitesEventHandler(connectionManager_PullDocumentSitesEvent);
                connectionManager.PullDocumentOpinionsEvent += new PullDocumentOpinionsEventHandler(connectionManager_PullDocumentOpinionsEvent);
                connectionManager.PullChatsRequestEvent += new PullChatsRequestEventHandler(connectionManager_PullChatsRequestEvent);
                connectionManager.PullChatTopicsEvent += new PullChatTopicsEventHandler(connectionManager_PullChatTopicsEvent);
                connectionManager.PullChatMessagesEvent += new PullChatMessagesEventHandler(connectionManager_PullChatMessagesEvent);
                connectionManager.PullWhispersRequestEvent += new PullWhispersRequestEventHandler(connectionManager_PullWhispersRequestEvent);
                connectionManager.PullWhisperMessagesEvent += new PullWhisperMessagesEventHandler(connectionManager_PullWhisperMessagesEvent);
                connectionManager.PullMailsRequestEvent += new PullMailsRequestEventHandler(connectionManager_PullMailsRequestEvent);
                connectionManager.PullMailMessagesEvent += new PullMailMessagesEventHandler(connectionManager_PullMailMessagesEvent);
                connectionManager.PullCancelEvent += new PullCancelEventHandler(connectionManager_PullCancelEvent);
                connectionManager.CloseEvent += new CloseEventHandler(connectionManager_CloseEvent);

                _nodeToUri.Add(connectionManager.Node, uri);
                _connectionManagers.Add(connectionManager);

                if (_messagesManager[connectionManager.Node].SessionId != null
                    && !Collection.Equals(_messagesManager[connectionManager.Node].SessionId, connectionManager.SesstionId))
                {
                    _messagesManager.Remove(connectionManager.Node);
                }

                _messagesManager[connectionManager.Node].SessionId = connectionManager.SesstionId;
                _messagesManager[connectionManager.Node].LastPullTime = DateTime.UtcNow;

                ThreadPool.QueueUserWorkItem(new WaitCallback(this.ConnectionManagerThread), connectionManager);
            }
        }

        private void RemoveConnectionManager(ConnectionManager connectionManager)
        {
            lock (this.ThisLock)
            {
                lock (_connectionManagers.ThisLock)
                {
                    try
                    {
                        if (_connectionManagers.Contains(connectionManager))
                        {
                            Debug.WriteLine("ConnectionManager: Close");

                            _sentByteCount += connectionManager.SentByteCount;
                            _receivedByteCount += connectionManager.ReceivedByteCount;

                            _messagesManager[connectionManager.Node].SentByteCount += connectionManager.SentByteCount;
                            _messagesManager[connectionManager.Node].ReceivedByteCount += connectionManager.ReceivedByteCount;

                            _nodeToUri.Remove(connectionManager.Node);
                            _connectionManagers.Remove(connectionManager);

                            connectionManager.Dispose();
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
            }
        }

        private void CreateClientConnectionThread()
        {
            for (; ; )
            {
                if (this.State == ManagerState.Stop) return;
                Thread.Sleep(1000);

                Node node = null;

                lock (this.ThisLock)
                {
                    node = _cuttingNodes
                        .ToArray()
                        .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id)) && !_creatingNodes.Contains(n))
                        .Randomize()
                        .FirstOrDefault();

                    if (node == null)
                    {
                        node = _routeTable
                            .ToArray()
                            .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id)) && !_creatingNodes.Contains(n))
                            .Randomize()
                            .FirstOrDefault();
                    }

                    if (node == null) continue;

                    _creatingNodes.Add(node);
                }

                try
                {
                    HashSet<string> uris = new HashSet<string>();
                    uris.UnionWith(node.Uris
                        .Take(12)
                        .Where(n => _clientManager.CheckUri(n))
                        .Randomize());

                    if (uris.Count == 0)
                    {
                        _removeNodes.Remove(node);
                        _cuttingNodes.Remove(node);
                        _routeTable.Remove(node);

                        continue;
                    }

                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers
                            .Where(n => n.Type == ConnectionManagerType.Client)
                            .Count();
                    }

                    if (connectionCount > ((this.ConnectionCountLimit / 3) * 1))
                    {
                        continue;
                    }

                    foreach (var uri in uris)
                    {
                        if (this.State == ManagerState.Stop) return;

                        var connection = _clientManager.CreateConnection(uri, _bandwidthLimit);

                        if (connection != null)
                        {
                            var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectionManagerType.Client, _bufferManager);

                            try
                            {
                                connectionManager.Connect();
                                if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();

                                _cuttingNodes.Remove(node);

                                if (node != connectionManager.Node)
                                {
                                    _removeNodes.Add(node);
                                    _routeTable.Remove(node);
                                }

                                _routeTable.Live(connectionManager.Node);

                                _createConnectionCount++;

                                this.AddConnectionManager(connectionManager, uri);

                                goto End;
                            }
                            catch (Exception)
                            {
                                connectionManager.Dispose();
                            }
                        }

                        this.RemoveNode(node);
                    }

                End: ;
                }
                finally
                {
                    _creatingNodes.Remove(node);
                }
            }
        }

        private void CreateServerConnectionThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                {
                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers
                            .Where(n => n.Type == ConnectionManagerType.Server)
                            .Count();
                    }

                    if (connectionCount > ((this.ConnectionCountLimit / 3) * 2))
                    {
                        continue;
                    }
                }

                string uri;
                var connection = _serverManager.AcceptConnection(out uri, _bandwidthLimit);

                if (connection != null)
                {
                    var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectionManagerType.Server, _bufferManager);

                    try
                    {
                        connectionManager.Connect();
                        if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();
                        if (_removeNodes.Contains(connectionManager.Node)) throw new ArgumentException();

                        if (connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                            _routeTable.Add(connectionManager.Node);

                        _cuttingNodes.Remove(connectionManager.Node);

                        _acceptConnectionCount++;

                        this.AddConnectionManager(connectionManager, uri);
                    }
                    catch (Exception)
                    {
                        connectionManager.Dispose();
                    }
                }
            }
        }

        private class NodeSortItem
        {
            public Node Node { get; set; }
            public TimeSpan ResponseTime { get; set; }
        }

        private volatile bool _refreshThreadRunning = false;
        private volatile bool _trustSignaturesRefreshThreadRunning = false;

        private void ConnectionsManagerThread()
        {
            Stopwatch connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();

            Stopwatch checkContentsStopwatch = new Stopwatch();
            Stopwatch trustSignaturesRefreshStopwatch = new Stopwatch();
            Stopwatch refreshStopwatch = new Stopwatch();

            Stopwatch pushUploadStopwatch = new Stopwatch();
            pushUploadStopwatch.Start();
            Stopwatch pushDownloadStopwatch = new Stopwatch();
            pushDownloadStopwatch.Start();

            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                var connectionCount = 0;

                lock (this.ThisLock)
                {
                    connectionCount = _connectionManagers
                        .Where(n => n.Type == ConnectionManagerType.Client)
                        .Count();
                }

                if (connectionCount > ((this.ConnectionCountLimit / 3) * 1)
                    && connectionCheckStopwatch.Elapsed.TotalMinutes > 30)
                {
                    connectionCheckStopwatch.Restart();

                    var nodeSortItems = new List<NodeSortItem>();

                    lock (this.ThisLock)
                    {
                        foreach (var connectionManager in _connectionManagers)
                        {
                            nodeSortItems.Add(new NodeSortItem()
                            {
                                Node = connectionManager.Node,
                                ResponseTime = connectionManager.ResponseTime,
                            });
                        }
                    }

                    nodeSortItems.Sort((x, y) =>
                    {
                        return y.ResponseTime.CompareTo(x.ResponseTime);
                    });

                    if (nodeSortItems.Count != 0)
                    {
                        for (int i = 0; i < nodeSortItems.Count; i++)
                        {
                            ConnectionManager connectionManager = null;

                            lock (this.ThisLock)
                            {
                                connectionManager = _connectionManagers.FirstOrDefault(n => n.Node == nodeSortItems[i].Node);
                            }

                            if (connectionManager != null)
                            {
                                try
                                {
                                    _removeNodes.Add(connectionManager.Node);
                                    _routeTable.Remove(connectionManager.Node);

                                    connectionManager.PushCancel();

                                    Debug.WriteLine("ConnectionManager: Push Cancel");
                                }
                                catch (Exception)
                                {

                                }

                                this.RemoveConnectionManager(connectionManager);

                                break;
                            }
                        }
                    }
                }

                if (!checkContentsStopwatch.IsRunning || checkContentsStopwatch.Elapsed.TotalMinutes >= 60)
                {
                    checkContentsStopwatch.Restart();

                    lock (this.ThisLock)
                    {
                        {
                            var cacheKeys = new HashSet<Key>(_cacheManager.ToArray());

                            foreach (var signature in _settings.GetSignatures())
                            {
                                var item = _settings.GetSignatureProfile(signature);
                                if (!cacheKeys.Contains(item.Content)) _settings.RemoveSignatureProfile(item);
                            }

                            foreach (var document in _settings.GetDocuments())
                            {
                                foreach (var item in _settings.GetDocumentSites(document))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveDocumentSite(item);
                                }

                                foreach (var item in _settings.GetDocumentOpinions(document))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveDocumentOpinion(item);
                                }
                            }

                            foreach (var chat in _settings.GetChats())
                            {
                                foreach (var item in _settings.GetChatTopics(chat))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveChatTopic(item);
                                }

                                foreach (var item in _settings.GetChatMessages(chat))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveChatMessage(item);
                                }
                            }

                            foreach (var whisper in _settings.GetWhispers())
                            {
                                foreach (var item in _settings.GetWhisperMessages(whisper))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveWhisperMessage(item);
                                }
                            }

                            foreach (var mail in _settings.GetMails())
                            {
                                foreach (var item in _settings.GetMailMessages(mail))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveMailMessage(item);
                                }
                            }
                        }

                        {
                            var linkKeys = new HashSet<Key>();

                            foreach (var signature in _settings.GetSignatures())
                            {
                                var item = _settings.GetSignatureProfile(signature);
                                linkKeys.Add(item.Content);
                            }

                            foreach (var document in _settings.GetDocuments())
                            {
                                foreach (var item in _settings.GetDocumentSites(document))
                                {
                                    linkKeys.Add(item.Content);
                                }

                                foreach (var item in _settings.GetDocumentOpinions(document))
                                {
                                    linkKeys.Add(item.Content);
                                }
                            }

                            foreach (var chat in _settings.GetChats())
                            {
                                foreach (var item in _settings.GetChatTopics(chat))
                                {
                                    linkKeys.Add(item.Content);
                                }

                                foreach (var item in _settings.GetChatMessages(chat))
                                {
                                    linkKeys.Add(item.Content);
                                }
                            }

                            foreach (var whisper in _settings.GetWhispers())
                            {
                                foreach (var item in _settings.GetWhisperMessages(whisper))
                                {
                                    linkKeys.Add(item.Content);
                                }
                            }

                            foreach (var mail in _settings.GetMails())
                            {
                                foreach (var item in _settings.GetMailMessages(mail))
                                {
                                    linkKeys.Add(item.Content);
                                }
                            }

                            foreach (var key in _cacheManager.ToArray())
                            {
                                if (linkKeys.Contains(key)) continue;

                                _cacheManager.Remove(key);
                            }
                        }
                    }
                }

                if (!trustSignaturesRefreshStopwatch.IsRunning || trustSignaturesRefreshStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    trustSignaturesRefreshStopwatch.Restart();

                    ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
                    {
                        if (_trustSignaturesRefreshThreadRunning) return;
                        _trustSignaturesRefreshThreadRunning = true;

                        try
                        {
                            var lockSignatures = this.OnTrustSignaturesEvent();

                            if (lockSignatures != null)
                            {
                                lock (_trustSignatures.ThisLock)
                                {
                                    _trustSignatures.Clear();
                                    _trustSignatures.UnionWith(lockSignatures);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                        finally
                        {
                            _refreshThreadRunning = false;
                        }
                    }));
                }

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    refreshStopwatch.Restart();

                    ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            var now = DateTime.UtcNow;

                            {
                                var lockSignatures = this.OnTrustSignaturesEvent();

                                if (lockSignatures != null)
                                {
                                    var removeSignatures = new HashSet<string>();
                                    removeSignatures.UnionWith(_settings.GetSignatures());
                                    removeSignatures.ExceptWith(lockSignatures);

                                    var sortList = removeSignatures.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedSignatureTimes.TryGetValue(x, out tx);
                                        _lastUsedSignatureTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.RemoveSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new HashSet<string>(_settings.GetSignatures());

                                    foreach (var signature in _lastUsedSignatureTimes.Keys.ToArray())
                                    {
                                        if (liveSignatures.Contains(signature)) continue;

                                        _lastUsedSignatureTimes.Remove(signature);
                                    }
                                }
                            }

                            {
                                var lockDocuments = this.OnLockDocumentsEvent();

                                if (lockDocuments != null)
                                {
                                    var removeDocuments = new HashSet<Document>();
                                    removeDocuments.UnionWith(_settings.GetDocuments());
                                    removeDocuments.ExceptWith(lockDocuments);

                                    var sortList = removeDocuments.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedDocumentTimes.TryGetValue(x, out tx);
                                        _lastUsedDocumentTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.RemoveDocuments(sortList.Take(sortList.Count - 1024));

                                    var liveDocuments = new HashSet<Document>(_settings.GetDocuments());

                                    foreach (var signature in _lastUsedDocumentTimes.Keys.ToArray())
                                    {
                                        if (liveDocuments.Contains(signature)) continue;

                                        _lastUsedDocumentTimes.Remove(signature);
                                    }
                                }
                            }

                            {
                                var lockChats = this.OnLockChatsEvent();

                                if (lockChats != null)
                                {
                                    var removeChats = new HashSet<Chat>();
                                    removeChats.UnionWith(_settings.GetChats());
                                    removeChats.ExceptWith(lockChats);

                                    var sortList = removeChats.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedChatTimes.TryGetValue(x, out tx);
                                        _lastUsedChatTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.RemoveChats(sortList.Take(sortList.Count - 1024));

                                    var liveChats = new HashSet<Chat>(_settings.GetChats());

                                    foreach (var signature in _lastUsedChatTimes.Keys.ToArray())
                                    {
                                        if (liveChats.Contains(signature)) continue;

                                        _lastUsedChatTimes.Remove(signature);
                                    }
                                }
                            }

                            {
                                var lockWhispers = this.OnLockWhispersEvent();

                                if (lockWhispers != null)
                                {
                                    var removeWhispers = new HashSet<Whisper>();
                                    removeWhispers.UnionWith(_settings.GetWhispers());
                                    removeWhispers.ExceptWith(lockWhispers);

                                    var sortList = removeWhispers.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedWhisperTimes.TryGetValue(x, out tx);
                                        _lastUsedWhisperTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.RemoveWhispers(sortList.Take(sortList.Count - 1024));

                                    var liveWhispers = new HashSet<Whisper>(_settings.GetWhispers());

                                    foreach (var signature in _lastUsedWhisperTimes.Keys.ToArray())
                                    {
                                        if (liveWhispers.Contains(signature)) continue;

                                        _lastUsedWhisperTimes.Remove(signature);
                                    }
                                }
                            }

                            {
                                var lockMails = this.OnLockMailsEvent();

                                if (lockMails != null)
                                {
                                    var removeMails = new HashSet<Mail>();
                                    removeMails.UnionWith(_settings.GetMails());
                                    removeMails.ExceptWith(lockMails);

                                    var sortList = removeMails.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedMailTimes.TryGetValue(x, out tx);
                                        _lastUsedMailTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.RemoveMails(sortList.Take(sortList.Count - 1024));

                                    var liveMails = new HashSet<Mail>(_settings.GetMails());

                                    foreach (var signature in _lastUsedMailTimes.Keys.ToArray())
                                    {
                                        if (liveMails.Contains(signature)) continue;

                                        _lastUsedMailTimes.Remove(signature);
                                    }
                                }
                            }

                            {
                                var lockSignatures = this.OnTrustSignaturesEvent();

                                if (lockSignatures != null)
                                {
                                    var lockSignatureHashset = new HashSet<string>(lockSignatures);

                                    lock (this.ThisLock)
                                    {
                                        var removeDocumentSites = new List<DocumentSite>();
                                        var removeDocumentOpinions = new List<DocumentOpinion>();
                                        var removeChatTopics = new List<ChatTopic>();
                                        var removeChatMessages = new List<ChatMessage>();
                                        var removeWhisperMessages = new List<WhisperMessage>();
                                        var removeMailMessages = new List<MailMessage>();

                                        foreach (var document in _settings.GetDocuments())
                                        {
                                            // trustのDocumentSiteはすべて許容
                                            // untrustのDocumentSiteはランダムに256まで許容
                                            {
                                                var untrustDocumentSites = new List<DocumentSite>();

                                                foreach (var item in _settings.GetDocumentSites(document))
                                                {
                                                    if (lockSignatureHashset.Contains(item.Certificate.ToString())) continue;

                                                    untrustDocumentSites.Add(item);
                                                }

                                                removeDocumentSites.AddRange(untrustDocumentSites.Randomize().Take(untrustDocumentSites.Count - 256));
                                            }

                                            // trustのDocumentOpinionはすべて許容
                                            // untrustのDocumentOpinionはランダムに256まで許容
                                            {
                                                var untrustDocumentOpinions = new List<DocumentOpinion>();

                                                foreach (var item in _settings.GetDocumentOpinions(document))
                                                {
                                                    if (lockSignatureHashset.Contains(item.Certificate.ToString())) continue;

                                                    untrustDocumentOpinions.Add(item);
                                                }

                                                removeDocumentOpinions.AddRange(untrustDocumentOpinions.Randomize().Take(untrustDocumentOpinions.Count - 256));
                                            }
                                        }

                                        foreach (var chat in _settings.GetChats())
                                        {
                                            // trustのChatTopicは新しい順に4まで許容
                                            // untrustのChatTopicは新しい順に2まで許容
                                            {
                                                var trustTopics = new List<ChatTopic>();
                                                var untrustTopics = new List<ChatTopic>();

                                                foreach (var item in _settings.GetChatTopics(chat))
                                                {
                                                    if (lockSignatureHashset.Contains(item.Certificate.ToString()))
                                                    {
                                                        trustTopics.Add(item);
                                                    }
                                                    else
                                                    {
                                                        untrustTopics.Add(item);
                                                    }
                                                }

                                                if (trustTopics.Count > 4)
                                                {
                                                    trustTopics.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeChatTopics.AddRange(trustTopics.Take(trustTopics.Count - 4));
                                                }

                                                if (untrustTopics.Count > 2)
                                                {
                                                    untrustTopics.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeChatTopics.AddRange(untrustTopics.Take(untrustTopics.Count - 2));
                                                }
                                            }

                                            // 作成後64日を過ぎたChatMessageは破棄
                                            // trustのChatMessageは新しい順に256まで許容
                                            // untrustのChatMessageは新しい順に32まで許容
                                            {
                                                var trustMessages = new List<ChatMessage>();
                                                var untrustMessages = new List<ChatMessage>();

                                                foreach (var item in _settings.GetChatMessages(chat))
                                                {
                                                    if ((now - item.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                                    {
                                                        removeChatMessages.Add(item);
                                                    }
                                                    else
                                                    {
                                                        if (lockSignatureHashset.Contains(item.Certificate.ToString()))
                                                        {
                                                            trustMessages.Add(item);
                                                        }
                                                        else
                                                        {
                                                            untrustMessages.Add(item);
                                                        }
                                                    }
                                                }

                                                if (trustMessages.Count > 256)
                                                {
                                                    trustMessages.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeChatMessages.AddRange(trustMessages.Take(trustMessages.Count - 256));
                                                }

                                                if (untrustMessages.Count > 32)
                                                {
                                                    untrustMessages.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeChatMessages.AddRange(untrustMessages.Take(untrustMessages.Count - 32));
                                                }
                                            }
                                        }

                                        foreach (var whisper in _settings.GetWhispers())
                                        {
                                            // 作成後64日を過ぎたWhisperMessageは破棄
                                            // trustのWhisperMessageは新しい順に256まで許容
                                            // untrustのWhisperMessageは新しい順に32まで許容
                                            {
                                                var trustMessages = new List<WhisperMessage>();
                                                var untrustMessages = new List<WhisperMessage>();

                                                foreach (var item in _settings.GetWhisperMessages(whisper))
                                                {
                                                    if ((now - item.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                                    {
                                                        removeWhisperMessages.Add(item);
                                                    }
                                                    else
                                                    {
                                                        if (lockSignatureHashset.Contains(item.Certificate.ToString()))
                                                        {
                                                            trustMessages.Add(item);
                                                        }
                                                        else
                                                        {
                                                            untrustMessages.Add(item);
                                                        }
                                                    }
                                                }

                                                if (trustMessages.Count > 256)
                                                {
                                                    trustMessages.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeWhisperMessages.AddRange(trustMessages.Take(trustMessages.Count - 256));
                                                }

                                                if (untrustMessages.Count > 32)
                                                {
                                                    untrustMessages.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeWhisperMessages.AddRange(untrustMessages.Take(untrustMessages.Count - 32));
                                                }
                                            }
                                        }

                                        foreach (var mail in _settings.GetMails())
                                        {
                                            // 64日を過ぎたMailMessageは破棄
                                            // trustのMailMessageは、すべての送信者を許容し、一人の送信者につき新しい順に8まで許容
                                            // untrustのMailMessageは、32人の送信者を許容し、一人の送信者につき2まで許容。
                                            {
                                                var trustMailMessageDic = new Dictionary<string, List<MailMessage>>();
                                                var untrustMailMessageDic = new Dictionary<string, List<MailMessage>>();

                                                foreach (var item in _settings.GetMailMessages(mail))
                                                {
                                                    if ((now - item.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                                    {
                                                        removeMailMessages.Add(item);
                                                    }
                                                    else
                                                    {
                                                        var creatorsignature = item.Certificate.ToString();

                                                        if (lockSignatureHashset.Contains(creatorsignature))
                                                        {
                                                            List<MailMessage> list;

                                                            if (trustMailMessageDic.TryGetValue(creatorsignature, out list))
                                                            {
                                                                list = new List<MailMessage>();
                                                                trustMailMessageDic[creatorsignature] = list;
                                                            }

                                                            list.Add(item);
                                                        }
                                                        else
                                                        {
                                                            List<MailMessage> list;

                                                            if (untrustMailMessageDic.TryGetValue(creatorsignature, out list))
                                                            {
                                                                list = new List<MailMessage>();
                                                                untrustMailMessageDic[creatorsignature] = list;
                                                            }

                                                            list.Add(item);
                                                        }
                                                    }
                                                }

                                                foreach (var list in trustMailMessageDic.Values)
                                                {
                                                    list.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeMailMessages.AddRange(list.Take(list.Count - 8));
                                                }

                                                int i = 0;

                                                foreach (var list in untrustMailMessageDic.Values.Randomize())
                                                {
                                                    if (i < 32)
                                                    {
                                                        list.Sort((x, y) =>
                                                        {
                                                            return x.CreationTime.CompareTo(y);
                                                        });

                                                        removeMailMessages.AddRange(list.Take(list.Count - 2));
                                                    }
                                                    else
                                                    {
                                                        removeMailMessages.AddRange(list);
                                                    }

                                                    i++;
                                                }
                                            }
                                        }

                                        foreach (var item in removeDocumentSites)
                                        {
                                            _settings.RemoveDocumentSite(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeDocumentOpinions)
                                        {
                                            _settings.RemoveDocumentOpinion(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeChatTopics)
                                        {
                                            _settings.RemoveChatTopic(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeChatMessages)
                                        {
                                            _settings.RemoveChatMessage(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeWhisperMessages)
                                        {
                                            _settings.RemoveWhisperMessage(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeMailMessages)
                                        {
                                            _settings.RemoveMailMessage(item);
                                            _cacheManager.Remove(item.Content);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                        finally
                        {
                            _refreshThreadRunning = false;
                        }
                    }));
                }

                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushUploadStopwatch.Elapsed.TotalMinutes > 10)
                {
                    pushUploadStopwatch.Restart();

                    foreach (var item in _settings.GetSignatures())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(Signature.GetSignatureHash(item), 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullSignaturesRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var item in _settings.GetDocuments())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullDocumentsRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var item in _settings.GetChats())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullChatsRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var item in _settings.GetWhispers())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullWhispersRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var item in _settings.GetMails())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullMailsRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }
                }

                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushDownloadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushDownloadStopwatch.Restart();

                    HashSet<string> pushSignaturesRequestList = new HashSet<string>();
                    HashSet<Document> pushDocumentsRequestList = new HashSet<Document>();
                    HashSet<Chat> pushChatsRequestList = new HashSet<Chat>();
                    HashSet<Whisper> pushWhispersRequestList = new HashSet<Whisper>();
                    HashSet<Mail> pushMailsRequestList = new HashSet<Mail>();
                    List<Node> nodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        nodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    {
                        {
                            var list = _pushSignaturesRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSignaturesRequest.Contains(list[i])))
                                {
                                    pushSignaturesRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullSignaturesRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSignaturesRequest.Contains(list[i])))
                                {
                                    pushSignaturesRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _pushDocumentsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushDocumentsRequest.Contains(list[i])))
                                {
                                    pushDocumentsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullDocumentsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushDocumentsRequest.Contains(list[i])))
                                {
                                    pushDocumentsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _pushChatsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushChatsRequest.Contains(list[i])))
                                {
                                    pushChatsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullChatsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushChatsRequest.Contains(list[i])))
                                {
                                    pushChatsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _pushWhispersRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushWhispersRequest.Contains(list[i])))
                                {
                                    pushWhispersRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullWhispersRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushWhispersRequest.Contains(list[i])))
                                {
                                    pushWhispersRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _pushMailsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushMailsRequest.Contains(list[i])))
                                {
                                    pushMailsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullMailsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushMailsRequest.Contains(list[i])))
                                {
                                    pushMailsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }
                    }

                    {
                        Dictionary<Node, HashSet<string>> pushSignaturesRequestDictionary = new Dictionary<Node, HashSet<string>>();

                        foreach (var item in pushSignaturesRequestList.Randomize())
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(Signature.GetSignatureHash(item), 1));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<string> hashset;

                                    if (!pushSignaturesRequestDictionary.TryGetValue(requestNodes[i], out hashset))
                                    {
                                        hashset = new HashSet<string>();
                                        pushSignaturesRequestDictionary[requestNodes[i]] = hashset;
                                    }

                                    hashset.Add(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushSignaturesRequestDictionary.ThisLock)
                        {
                            _pushSignaturesRequestDictionary.Clear();

                            foreach (var item in pushSignaturesRequestDictionary)
                            {
                                _pushSignaturesRequestDictionary.Add(item.Key, item.Value);
                            }
                        }
                    }

                    {
                        Dictionary<Node, HashSet<Chat>> pushChatsRequestDictionary = new Dictionary<Node, HashSet<Chat>>();

                        foreach (var item in pushChatsRequestList)
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Chat> hashset;

                                    if (!pushChatsRequestDictionary.TryGetValue(requestNodes[i], out hashset))
                                    {
                                        hashset = new HashSet<Chat>();
                                        pushChatsRequestDictionary[requestNodes[i]] = hashset;
                                    }

                                    hashset.Add(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushChatsRequestDictionary.ThisLock)
                        {
                            _pushChatsRequestDictionary.Clear();

                            foreach (var item in pushChatsRequestDictionary)
                            {
                                _pushChatsRequestDictionary.Add(item.Key, item.Value);
                            }
                        }
                    }
                }
            }
        }

        private void ConnectionManagerThread(object state)
        {
            Thread.CurrentThread.Name = "ConnectionsManager_ConnectionManagerThread";
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            var connectionManager = state as ConnectionManager;
            if (connectionManager == null) return;

            try
            {
                var messageManager = _messagesManager[connectionManager.Node];

                Stopwatch checkTime = new Stopwatch();
                checkTime.Start();
                Stopwatch nodeUpdateTime = new Stopwatch();
                Stopwatch updateTime = new Stopwatch();
                updateTime.Start();

                for (; ; )
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;
                    if (!_connectionManagers.Contains(connectionManager)) return;

                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers
                            .Where(n => n.Type == ConnectionManagerType.Client)
                            .Count();
                    }

                    // Check
                    if (checkTime.Elapsed.TotalSeconds > 60)
                    {
                        checkTime.Restart();

                        if ((DateTime.UtcNow - messageManager.LastPullTime) > new TimeSpan(0, 30, 0))
                        {
                            _removeNodes.Add(connectionManager.Node);
                            _routeTable.Remove(connectionManager.Node);

                            connectionManager.PushCancel();

                            Debug.WriteLine("ConnectionManager: Push Cancel");
                            return;
                        }
                    }

                    // PushNodes
                    if (!nodeUpdateTime.IsRunning || nodeUpdateTime.Elapsed.TotalSeconds > 60)
                    {
                        nodeUpdateTime.Restart();

                        List<Node> nodes = new List<Node>();

                        lock (this.ThisLock)
                        {
                            var clist = _connectionManagers.ToList();
                            clist.Remove(connectionManager);

                            clist.Sort((x, y) =>
                            {
                                return x.ResponseTime.CompareTo(y.ResponseTime);
                            });

                            nodes.AddRange(clist
                                .Select(n => n.Node)
                                .Where(n => n.Uris.Count > 0)
                                .Take(12));
                        }

                        if (nodes.Count > 0)
                        {
                            connectionManager.PushNodes(nodes);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodeCount += nodes.Count;
                        }
                    }

                    if (updateTime.Elapsed.TotalSeconds > 60)
                    {
                        updateTime.Restart();

                        if (_connectionManagers.Count >= _downloadingConnectionCountLowerLimit)
                        {
                            // PushSignaturesRequest
                            {
                                SignatureCollection tempList = null;
                                int count = (int)(_maxRequestCount * this.ResponseTimePriority(connectionManager.Node));

                                lock (_pushSignaturesRequestDictionary.ThisLock)
                                {
                                    HashSet<string> hashset;

                                    if (_pushSignaturesRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                    {
                                        tempList = new SignatureCollection(hashset.Randomize().Take(count));

                                        hashset.ExceptWith(tempList);
                                        messageManager.PushSignaturesRequest.AddRange(tempList);
                                    }
                                }

                                if (tempList != null && tempList.Count > 0)
                                {
                                    try
                                    {
                                        connectionManager.PushSignaturesRequest(tempList);

                                        foreach (var item in tempList)
                                        {
                                            _pushSignaturesRequestList.Remove(item);
                                        }

                                        Debug.WriteLine(string.Format("ConnectionManager: Push SignaturesRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                        _pushSignatureRequestCount += tempList.Count;
                                    }
                                    catch (Exception e)
                                    {
                                        foreach (var item in tempList)
                                        {
                                            messageManager.PushSignaturesRequest.Remove(item);
                                        }

                                        throw e;
                                    }
                                }
                            }

                            // PushDocumentsRequest
                            {
                                DocumentCollection tempList = null;
                                int count = (int)(_maxRequestCount * this.ResponseTimePriority(connectionManager.Node));

                                lock (_pushDocumentsRequestDictionary.ThisLock)
                                {
                                    HashSet<Document> hashset;

                                    if (_pushDocumentsRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                    {
                                        tempList = new DocumentCollection(hashset.Randomize().Take(count));

                                        hashset.ExceptWith(tempList);
                                        messageManager.PushDocumentsRequest.AddRange(tempList);
                                    }
                                }

                                if (tempList != null && tempList.Count > 0)
                                {
                                    try
                                    {
                                        connectionManager.PushDocumentsRequest(tempList);

                                        foreach (var item in tempList)
                                        {
                                            _pushDocumentsRequestList.Remove(item);
                                        }

                                        Debug.WriteLine(string.Format("ConnectionManager: Push DocumentsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                        _pushDocumentRequestCount += tempList.Count;
                                    }
                                    catch (Exception e)
                                    {
                                        foreach (var item in tempList)
                                        {
                                            messageManager.PushDocumentsRequest.Remove(item);
                                        }

                                        throw e;
                                    }
                                }
                            }

                            // PushChatsRequest
                            {
                                ChatCollection tempList = null;
                                int count = (int)(_maxRequestCount * this.ResponseTimePriority(connectionManager.Node));

                                lock (_pushChatsRequestDictionary.ThisLock)
                                {
                                    HashSet<Chat> hashset;

                                    if (_pushChatsRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                    {
                                        tempList = new ChatCollection(hashset.Randomize().Take(count));

                                        hashset.ExceptWith(tempList);
                                        messageManager.PushChatsRequest.AddRange(tempList);
                                    }
                                }

                                if (tempList != null && tempList.Count > 0)
                                {
                                    try
                                    {
                                        connectionManager.PushChatsRequest(tempList);

                                        foreach (var item in tempList)
                                        {
                                            _pushChatsRequestList.Remove(item);
                                        }

                                        Debug.WriteLine(string.Format("ConnectionManager: Push ChatsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                        _pushChatRequestCount += tempList.Count;
                                    }
                                    catch (Exception e)
                                    {
                                        foreach (var item in tempList)
                                        {
                                            messageManager.PushChatsRequest.Remove(item);
                                        }

                                        throw e;
                                    }
                                }
                            }

                            // PushWhispersRequest
                            {
                                WhisperCollection tempList = null;
                                int count = (int)(_maxRequestCount * this.ResponseTimePriority(connectionManager.Node));

                                lock (_pushWhispersRequestDictionary.ThisLock)
                                {
                                    HashSet<Whisper> hashset;

                                    if (_pushWhispersRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                    {
                                        tempList = new WhisperCollection(hashset.Randomize().Take(count));

                                        hashset.ExceptWith(tempList);
                                        messageManager.PushWhispersRequest.AddRange(tempList);
                                    }
                                }

                                if (tempList != null && tempList.Count > 0)
                                {
                                    try
                                    {
                                        connectionManager.PushWhispersRequest(tempList);

                                        foreach (var item in tempList)
                                        {
                                            _pushWhispersRequestList.Remove(item);
                                        }

                                        Debug.WriteLine(string.Format("ConnectionManager: Push WhispersRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                        _pushWhisperRequestCount += tempList.Count;
                                    }
                                    catch (Exception e)
                                    {
                                        foreach (var item in tempList)
                                        {
                                            messageManager.PushWhispersRequest.Remove(item);
                                        }

                                        throw e;
                                    }
                                }
                            }

                            // PushMailsRequest
                            {
                                MailCollection tempList = null;
                                int count = (int)(_maxRequestCount * this.ResponseTimePriority(connectionManager.Node));

                                lock (_pushMailsRequestDictionary.ThisLock)
                                {
                                    HashSet<Mail> hashset;

                                    if (_pushMailsRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                    {
                                        tempList = new MailCollection(hashset.Randomize().Take(count));

                                        hashset.ExceptWith(tempList);
                                        messageManager.PushMailsRequest.AddRange(tempList);
                                    }
                                }

                                if (tempList != null && tempList.Count > 0)
                                {
                                    try
                                    {
                                        connectionManager.PushMailsRequest(tempList);

                                        foreach (var item in tempList)
                                        {
                                            _pushMailsRequestList.Remove(item);
                                        }

                                        Debug.WriteLine(string.Format("ConnectionManager: Push MailsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                        _pushMailRequestCount += tempList.Count;
                                    }
                                    catch (Exception e)
                                    {
                                        foreach (var item in tempList)
                                        {
                                            messageManager.PushMailsRequest.Remove(item);
                                        }

                                        throw e;
                                    }
                                }
                            }
                        }
                    }

                    if (connectionCount >= _uploadingConnectionCountLowerLimit)
                    {
                        {
                            List<string> signatures = new List<string>(messageManager.PullSignaturesRequest.Randomize());

                            if (signatures.Count > 0)
                            {
                                // PushSignatureProfiles
                                {
                                    var items = new List<SignatureProfile>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var signature in signatures)
                                        {
                                            var item = _settings.GetSignatureProfile(signature);

                                            {
                                                DateTime creationTime;

                                                if (!messageManager.PushSignatureProfiles.TryGetValue(item.Certificate.ToString(), out creationTime)
                                                    || item.CreationTime > creationTime)
                                                {
                                                    items.Add(item);

                                                    if (items.Count >= 256) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in items.ToArray())
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                items.Remove(item);
                                                _settings.RemoveSignatureProfile(item);
                                            }
                                        }

                                        connectionManager.PushSignatureProfiles(items, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push SignatureProfiles ({0})", items.Count));
                                        _pushSignatureProfileCount += items.Count;

                                        foreach (var item in items)
                                        {
                                            messageManager.PushSignatureProfiles.Add(item.Certificate.ToString(), item.CreationTime);
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var content in contents)
                                        {
                                            _bufferManager.ReturnBuffer(content.Array);
                                        }
                                    }
                                }
                            }
                        }

                        {
                            List<Document> documents = new List<Document>(messageManager.PullDocumentsRequest.Randomize());

                            if (documents.Count > 0)
                            {
                                // PushDocumentSites
                                {
                                    var items = new List<DocumentSite>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var signature in documents)
                                        {
                                            foreach (var item in _settings.GetDocumentSites(signature).Randomize())
                                            {
                                                var key = new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm);

                                                if (!messageManager.PushDocumentSites.Contains(key))
                                                {
                                                    items.Add(item);

                                                    if (items.Count >= 1) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in items.ToArray())
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                items.Remove(item);
                                                _settings.RemoveDocumentSite(item);
                                            }
                                        }

                                        connectionManager.PushDocumentSites(items, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push DocumentSites ({0})", items.Count));
                                        _pushDocumentSiteCount += items.Count;

                                        foreach (var item in items)
                                        {
                                            messageManager.PushDocumentSites.Add(new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm));
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var content in contents)
                                        {
                                            _bufferManager.ReturnBuffer(content.Array);
                                        }
                                    }
                                }

                                // PushDocumentOpinions
                                {
                                    var items = new List<DocumentOpinion>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var signature in documents)
                                        {
                                            foreach (var item in _settings.GetDocumentOpinions(signature).Randomize())
                                            {
                                                DateTime creationTime;

                                                if (!messageManager.PushDocumentOpinions.TryGetValue(item.Certificate.ToString(), out creationTime)
                                                    || item.CreationTime > creationTime)
                                                {
                                                    items.Add(item);

                                                    if (items.Count >= 256) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in items.ToArray())
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                items.Remove(item);
                                                _settings.RemoveDocumentOpinion(item);
                                            }
                                        }

                                        connectionManager.PushDocumentOpinions(items, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push DocumentOpinions ({0})", items.Count));
                                        _pushDocumentOpinionCount += items.Count;

                                        foreach (var item in items)
                                        {
                                            messageManager.PushDocumentOpinions.Add(item.Certificate.ToString(), item.CreationTime);
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var content in contents)
                                        {
                                            _bufferManager.ReturnBuffer(content.Array);
                                        }
                                    }
                                }
                            }
                        }

                        {
                            var chats = new List<Chat>(messageManager.PullChatsRequest.Randomize());

                            if (chats.Count > 0)
                            {
                                // PushChatTopics
                                {
                                    var items = new List<ChatTopic>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var chat in chats)
                                        {
                                            foreach (var item in _settings.GetChatTopics(chat).Randomize())
                                            {
                                                DateTime creationTime;

                                                if (!messageManager.PushChatTopics.TryGetValue(item.Certificate.ToString(), out creationTime)
                                                    || item.CreationTime > creationTime)
                                                {
                                                    items.Add(item);

                                                    if (items.Count >= 8) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in items.ToArray())
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                items.Remove(item);
                                                _settings.RemoveChatTopic(item);
                                            }
                                        }

                                        connectionManager.PushChatTopics(items, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push ChatTopics ({0})", items.Count));
                                        _pushChatTopicCount += items.Count;

                                        foreach (var item in items)
                                        {
                                            messageManager.PushChatTopics.Add(item.Certificate.ToString(), item.CreationTime);
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var content in contents)
                                        {
                                            _bufferManager.ReturnBuffer(content.Array);
                                        }
                                    }
                                }

                                // PushChatMessages
                                {
                                    var items = new List<ChatMessage>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var chat in chats)
                                        {
                                            foreach (var item in _settings.GetChatMessages(chat).Randomize())
                                            {
                                                var key = new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm);

                                                if (!messageManager.PushChatMessages.Contains(key))
                                                {
                                                    items.Add(item);

                                                    if (items.Count >= 256) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in items.ToArray())
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                items.Remove(item);
                                                _settings.RemoveChatMessage(item);
                                            }
                                        }

                                        connectionManager.PushChatMessages(items, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push ChatMessages ({0})", items.Count));
                                        _pushChatMessageCount += items.Count;

                                        foreach (var item in items)
                                        {
                                            messageManager.PushChatMessages.Add(new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm));
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var content in contents)
                                        {
                                            _bufferManager.ReturnBuffer(content.Array);
                                        }
                                    }
                                }
                            }
                        }

                        {
                            var whispers = new List<Whisper>(messageManager.PullWhispersRequest.Randomize());

                            if (whispers.Count > 0)
                            {
                                // PushWhisperMessages
                                {
                                    var items = new List<WhisperMessage>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var whisper in whispers)
                                        {
                                            foreach (var item in _settings.GetWhisperMessages(whisper).Randomize())
                                            {
                                                var key = new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm);

                                                if (!messageManager.PushWhisperMessages.Contains(key))
                                                {
                                                    items.Add(item);

                                                    if (items.Count >= 256) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in items.ToArray())
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                items.Remove(item);
                                                _settings.RemoveWhisperMessage(item);
                                            }
                                        }

                                        connectionManager.PushWhisperMessages(items, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push WhisperMessages ({0})", items.Count));
                                        _pushWhisperMessageCount += items.Count;

                                        foreach (var item in items)
                                        {
                                            messageManager.PushWhisperMessages.Add(new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm));
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var content in contents)
                                        {
                                            _bufferManager.ReturnBuffer(content.Array);
                                        }
                                    }
                                }
                            }
                        }

                        {
                            var mails = new List<Mail>(messageManager.PullMailsRequest.Randomize());

                            if (mails.Count > 0)
                            {
                                // PushMailMessages
                                {
                                    var items = new List<MailMessage>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var mail in mails)
                                        {
                                            foreach (var item in _settings.GetMailMessages(mail).Randomize())
                                            {
                                                var key = new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm);

                                                if (!messageManager.PushMailMessages.Contains(key))
                                                {
                                                    items.Add(item);

                                                    if (items.Count >= 256) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in items.ToArray())
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                items.Remove(item);
                                                _settings.RemoveMailMessage(item);
                                            }
                                        }

                                        connectionManager.PushMailMessages(items, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push MailMessages ({0})", items.Count));
                                        _pushMailMessageCount += items.Count;

                                        foreach (var item in items)
                                        {
                                            messageManager.PushMailMessages.Add(new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm));
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var content in contents)
                                        {
                                            _bufferManager.ReturnBuffer(content.Array);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
            finally
            {
                this.RemoveConnectionManager(connectionManager);
            }
        }

        #region connectionManager_Event

        private void connectionManager_NodesEvent(object sender, PullNodesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Nodes == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Nodes ({0})", e.Nodes.Count()));

            foreach (var node in e.Nodes.Take(_maxNodeCount))
            {
                if (node == null || node.Id == null || !node.Uris.Any(n => _clientManager.CheckUri(n)) || _removeNodes.Contains(node)) continue;

                _routeTable.Add(node);
                _pullNodeCount++;
            }

            lock (this.ThisLock)
            {
                lock (_messagesManager.ThisLock)
                {
                    var messageManager = _messagesManager[connectionManager.Node];

                    lock (messageManager.ThisLock)
                    {
                        lock (messageManager.SurroundingNodes.ThisLock)
                        {
                            messageManager.SurroundingNodes.Clear();
                            messageManager.SurroundingNodes.UnionWith(e.Nodes
                                .Where(n => n != null && n.Id != null)
                                .Randomize()
                                .Take(12));
                        }
                    }
                }
            }
        }

        private void connectionManager_PullSignaturesRequestEvent(object sender, PullSignaturesRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Signatures == null
                || messageManager.PullSignaturesRequest.Count > _maxRequestCount * 60) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SignaturesRequest ({0})", e.Signatures.Count()));

            foreach (var s in e.Signatures.Take(_maxRequestCount))
            {
                if (s == null || Signature.HasSignature(s)) continue;

                messageManager.PullSignaturesRequest.Add(s);
                _pullSignatureRequestCount++;
            }
        }

        private void connectionManager_PullSignatureProfilesEvent(object sender, PullSignatureProfilesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.SignatureProfiles == null || e.Contents == null) return;

                var list = e.SignatureProfiles.ToList();
                var contentList = e.Contents.ToList();

                if (list.Count != contentList.Count) return;

                int priority = 0;

                Debug.WriteLine(string.Format("ConnectionManager: Pull SignatureProfiles ({0})", list.Count));

                for (int i = 0; i < list.Count && i < _maxContentCount; i++)
                {
                    var signatureProfile = list[i];
                    var content = contentList[i];

                    if (_settings.SetSignatureProfile(signatureProfile))
                    {
                        try
                        {
                            _cacheManager[signatureProfile.Content] = content;

                            if (_trustSignatures.Contains(signatureProfile.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveSignatureProfile(signatureProfile);
                        }
                    }

                    messageManager.PushSignatureProfiles[signatureProfile.Certificate.ToString()] = signatureProfile.CreationTime;
                    _pullSignatureProfileCount++;
                }

                messageManager.Priority += (priority + (priority - list.Count));
                messageManager.LastPullTime = DateTime.UtcNow;
            }
            finally
            {
                foreach (var content in e.Contents)
                {
                    if (content.Array != null)
                    {
                        _bufferManager.ReturnBuffer(content.Array);
                    }
                }
            }
        }

        private void connectionManager_PullDocumentsRequestEvent(object sender, PullDocumentsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Documents == null
                || messageManager.PullDocumentsRequest.Count > _maxRequestCount * 60) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull DocumentsRequest ({0})", e.Documents.Count()));

            foreach (var d in e.Documents.Take(_maxRequestCount))
            {
                if (d == null || d.Id == null || string.IsNullOrWhiteSpace(d.Name)) continue;

                messageManager.PullDocumentsRequest.Add(d);
                _pullDocumentRequestCount++;
            }
        }

        private void connectionManager_PullDocumentSitesEvent(object sender, PullDocumentSitesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.DocumentSites == null || e.Contents == null) return;

                var list = e.DocumentSites.ToList();
                var contentList = e.Contents.ToList();

                if (list.Count != contentList.Count) return;

                int priority = 0;

                Debug.WriteLine(string.Format("ConnectionManager: Pull DocumentSites ({0})", list.Count));

                for (int i = 0; i < list.Count && i < _maxContentCount; i++)
                {
                    var documentSite = list[i];
                    var content = contentList[i];

                    if (_settings.SetDocumentSite(documentSite))
                    {
                        try
                        {
                            _cacheManager[documentSite.Content] = content;

                            if (_trustSignatures.Contains(documentSite.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveDocumentSite(documentSite);
                        }
                    }

                    var key = new Key(documentSite.GetHash(_hashAlgorithm), _hashAlgorithm);

                    messageManager.PushDocumentSites.Add(key);
                    _pullDocumentSiteCount++;
                }

                messageManager.Priority += (priority + (priority - list.Count));
                messageManager.LastPullTime = DateTime.UtcNow;
            }
            finally
            {
                foreach (var content in e.Contents)
                {
                    if (content.Array != null)
                    {
                        _bufferManager.ReturnBuffer(content.Array);
                    }
                }
            }
        }

        private void connectionManager_PullDocumentOpinionsEvent(object sender, PullDocumentOpinionsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.DocumentOpinions == null || e.Contents == null) return;

                var list = e.DocumentOpinions.ToList();
                var contentList = e.Contents.ToList();

                if (list.Count != contentList.Count) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull DocumentOpinions ({0})", list.Count));

                int priority = 0;

                for (int i = 0; i < list.Count && i < _maxContentCount; i++)
                {
                    var documentOpinion = list[i];
                    var content = contentList[i];

                    if (_settings.SetDocumentOpinion(documentOpinion))
                    {
                        try
                        {
                            _cacheManager[documentOpinion.Content] = content;

                            if (_trustSignatures.Contains(documentOpinion.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveDocumentOpinion(documentOpinion);
                        }
                    }

                    messageManager.PushDocumentOpinions[documentOpinion.Certificate.ToString()] = documentOpinion.CreationTime;
                    _pullDocumentOpinionCount++;
                }

                messageManager.Priority += (priority + (priority - list.Count));
                messageManager.LastPullTime = DateTime.UtcNow;
            }
            finally
            {
                foreach (var content in e.Contents)
                {
                    if (content.Array != null)
                    {
                        _bufferManager.ReturnBuffer(content.Array);
                    }
                }
            }
        }

        private void connectionManager_PullChatsRequestEvent(object sender, PullChatsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Chats == null
                || messageManager.PullChatsRequest.Count > _maxRequestCount * 60) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull ChatsRequest {0} ({1})", String.Join(", ", e.Chats), e.Chats.Count()));

            foreach (var c in e.Chats.Take(_maxRequestCount))
            {
                if (c == null || c.Id == null || string.IsNullOrWhiteSpace(c.Name)) continue;

                messageManager.PullChatsRequest.Add(c);
                _pullChatRequestCount++;
            }
        }

        private void connectionManager_PullChatTopicsEvent(object sender, PullChatTopicsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.ChatTopics == null || e.Contents == null) return;

                var list = e.ChatTopics.ToList();
                var contentList = e.Contents.ToList();

                if (list.Count != contentList.Count) return;

                int priority = 0;

                Debug.WriteLine(string.Format("ConnectionManager: Pull ChatTopics ({0})", list.Count));

                for (int i = 0; i < list.Count && i < _maxContentCount; i++)
                {
                    var chatTopic = list[i];
                    var content = contentList[i];

                    if (_settings.SetChatTopic(chatTopic))
                    {
                        try
                        {
                            _cacheManager[chatTopic.Content] = content;

                            if (_trustSignatures.Contains(chatTopic.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveChatTopic(chatTopic);
                        }
                    }

                    messageManager.PushChatTopics[chatTopic.Certificate.ToString()] = chatTopic.CreationTime;
                    _pullChatTopicCount++;
                }

                messageManager.Priority += (priority + (priority - list.Count));
                messageManager.LastPullTime = DateTime.UtcNow;
            }
            finally
            {
                foreach (var content in e.Contents)
                {
                    if (content.Array != null)
                    {
                        _bufferManager.ReturnBuffer(content.Array);
                    }
                }
            }
        }

        private void connectionManager_PullChatMessagesEvent(object sender, PullChatMessagesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.ChatMessages == null || e.Contents == null) return;

                var list = e.ChatMessages.ToList();
                var contentList = e.Contents.ToList();

                if (list.Count != contentList.Count) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull ChatMessages ({0})", list.Count));

                int priority = 0;

                for (int i = 0; i < list.Count && i < _maxContentCount; i++)
                {
                    var chatMessage = list[i];
                    var content = contentList[i];

                    if (_settings.SetChatMessage(chatMessage))
                    {
                        try
                        {
                            _cacheManager[chatMessage.Content] = content;

                            if (_trustSignatures.Contains(chatMessage.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveChatMessage(chatMessage);
                        }
                    }

                    var key = new Key(chatMessage.GetHash(_hashAlgorithm), _hashAlgorithm);

                    messageManager.PushChatMessages.Add(key);
                    _pullChatMessageCount++;
                }

                messageManager.Priority += (priority + (priority - list.Count));
                messageManager.LastPullTime = DateTime.UtcNow;
            }
            finally
            {
                foreach (var content in e.Contents)
                {
                    if (content.Array != null)
                    {
                        _bufferManager.ReturnBuffer(content.Array);
                    }
                }
            }
        }

        private void connectionManager_PullWhispersRequestEvent(object sender, PullWhispersRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Whispers == null
                || messageManager.PullWhispersRequest.Count > _maxRequestCount * 60) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull WhispersRequest {0} ({1})", String.Join(", ", e.Whispers), e.Whispers.Count()));

            foreach (var w in e.Whispers.Take(_maxRequestCount))
            {
                if (w == null || w.Id == null || string.IsNullOrWhiteSpace(w.Name)) continue;

                messageManager.PullWhispersRequest.Add(w);
                _pullWhisperRequestCount++;
            }
        }

        private void connectionManager_PullWhisperMessagesEvent(object sender, PullWhisperMessagesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.WhisperMessages == null || e.Contents == null) return;

                var list = e.WhisperMessages.ToList();
                var contentList = e.Contents.ToList();

                if (list.Count != contentList.Count) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull WhisperMessages ({0})", list.Count));

                int priority = 0;

                for (int i = 0; i < list.Count && i < _maxContentCount; i++)
                {
                    var whisperMessage = list[i];
                    var content = contentList[i];

                    if (_settings.SetWhisperMessage(whisperMessage))
                    {
                        try
                        {
                            _cacheManager[whisperMessage.Content] = content;

                            if (_trustSignatures.Contains(whisperMessage.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveWhisperMessage(whisperMessage);
                        }
                    }

                    var key = new Key(whisperMessage.GetHash(_hashAlgorithm), _hashAlgorithm);

                    messageManager.PushWhisperMessages.Add(key);
                    _pullWhisperMessageCount++;
                }

                messageManager.Priority += (priority + (priority - list.Count));
                messageManager.LastPullTime = DateTime.UtcNow;
            }
            finally
            {
                foreach (var content in e.Contents)
                {
                    if (content.Array != null)
                    {
                        _bufferManager.ReturnBuffer(content.Array);
                    }
                }
            }
        }

        private void connectionManager_PullMailsRequestEvent(object sender, PullMailsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Mails == null
                || messageManager.PullMailsRequest.Count > _maxRequestCount * 60) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull MailsRequest {0} ({1})", String.Join(", ", e.Mails), e.Mails.Count()));

            foreach (var m in e.Mails.Take(_maxRequestCount))
            {
                if (m == null || m.Id == null || string.IsNullOrWhiteSpace(m.Name)) continue;

                messageManager.PullMailsRequest.Add(m);
                _pullMailRequestCount++;
            }
        }

        private void connectionManager_PullMailMessagesEvent(object sender, PullMailMessagesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.MailMessages == null || e.Contents == null) return;

                var list = e.MailMessages.ToList();
                var contentList = e.Contents.ToList();

                if (list.Count != contentList.Count) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull MailMessages ({0})", list.Count));

                int priority = 0;

                for (int i = 0; i < list.Count && i < _maxContentCount; i++)
                {
                    var mailMessage = list[i];
                    var content = contentList[i];

                    if (_settings.SetMailMessage(mailMessage))
                    {
                        try
                        {
                            _cacheManager[mailMessage.Content] = content;

                            if (_trustSignatures.Contains(mailMessage.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveMailMessage(mailMessage);
                        }
                    }

                    var key = new Key(mailMessage.GetHash(_hashAlgorithm), _hashAlgorithm);

                    messageManager.PushMailMessages.Add(key);
                    _pullMailMessageCount++;
                }

                messageManager.Priority += (priority + (priority - list.Count));
                messageManager.LastPullTime = DateTime.UtcNow;
            }
            finally
            {
                foreach (var content in e.Contents)
                {
                    if (content.Array != null)
                    {
                        _bufferManager.ReturnBuffer(content.Array);
                    }
                }
            }
        }

        private void connectionManager_PullCancelEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            Debug.WriteLine("ConnectionManager: Pull Cancel");

            try
            {
                _removeNodes.Add(connectionManager.Node);

                if (_routeTable.Count > _routeTableMinCount)
                {
                    _routeTable.Remove(connectionManager.Node);
                }

                this.RemoveConnectionManager(connectionManager);
            }
            catch (Exception)
            {

            }
        }

        private void connectionManager_CloseEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            try
            {
                this.RemoveNode(connectionManager.Node);

                if (!_removeNodes.Contains(connectionManager.Node)
                    && connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                {
                    _cuttingNodes.Add(connectionManager.Node);
                }

                this.RemoveConnectionManager(connectionManager);
            }
            catch (Exception)
            {

            }
        }

        #endregion

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                foreach (var node in nodes)
                {
                    if (node == null || node.Id == null || !node.Uris.Any(n => _clientManager.CheckUri(n)) || _removeNodes.Contains(node)) continue;

                    _routeTable.Live(node);
                }
            }
        }

        public void SendSignatureRequest(string signature)
        {
            lock (this.ThisLock)
            {
                _pushSignaturesRequestList.Add(signature);
            }
        }

        public void SendDocumentRequest(Document document)
        {
            lock (this.ThisLock)
            {
                _pushDocumentsRequestList.Add(document);
            }
        }

        public void SendChatRequest(Chat chat)
        {
            lock (this.ThisLock)
            {
                _pushChatsRequestList.Add(chat);
            }
        }

        public void SendWhisperRequest(Whisper whisper)
        {
            lock (this.ThisLock)
            {
                _pushWhispersRequestList.Add(whisper);
            }
        }

        public void SendMailRequest(Mail mail)
        {
            lock (this.ThisLock)
            {
                _pushMailsRequestList.Add(mail);
            }
        }

        public IEnumerable<string> GetSignatures()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetSignatures();
            }
        }

        public IEnumerable<Document> GetDocuments()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetDocuments();
            }
        }

        public IEnumerable<Chat> GetChats()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetChats();
            }
        }

        public IEnumerable<Whisper> GetWhispers()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetWhispers();
            }
        }

        public IEnumerable<Mail> GetMails()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetMails();
            }
        }

        public SignatureProfile GetSignatureProfile(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetSignatureProfile(signature);
            }
        }

        public IEnumerable<DocumentSite> GetDocumentSites(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetDocumentSites(document);
            }
        }

        public IEnumerable<DocumentOpinion> GetDocumentOpinions(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetDocumentOpinions(document);
            }
        }

        public IEnumerable<ChatTopic> GetChatTopics(Chat chat)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetChatTopics(chat);
            }
        }

        public IEnumerable<ChatMessage> GetChatMessages(Chat chat)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetChatMessages(chat);
            }
        }

        public IEnumerable<WhisperMessage> GetWhisperMessages(Whisper whisper)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetWhisperMessages(whisper);
            }
        }

        public IEnumerable<MailMessage> GetMailMessages(Mail mail)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetMailMessages(mail);
            }
        }

        public SignatureProfileContent GetContent(SignatureProfile profile)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[profile.Content];

                    return ContentConverter.FromSignatureProfileContentBlock(buffer);
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public DocumentSiteContent GetContent(DocumentSite document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[document.Content];

                    return ContentConverter.FromDocumentSiteContentBlock(buffer);
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public DocumentOpinionContent GetContent(DocumentOpinion vote)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[vote.Content];

                    return ContentConverter.FromDocumentOpinionContentBlock(buffer);
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public ChatTopicContent GetContent(ChatTopic chatTopic)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[chatTopic.Content];

                    return ContentConverter.FromChatTopicContentBlock(buffer);
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public ChatMessageContent GetContent(ChatMessage chatMessage)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[chatMessage.Content];

                    return ContentConverter.FromChatMessageContentBlock(buffer);
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public WhisperMessageContent GetContent(WhisperMessage whisperMessage, WhisperCryptoInformation cryptoInformation)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[whisperMessage.Content];

                    return ContentConverter.FromWhisperMessageContentBlock(buffer, cryptoInformation);
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public MailMessageContent GetContent(MailMessage mailMessage, IExchangeDecrypt exchangeDecrypt)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[mailMessage.Content];

                    return ContentConverter.FromMailMessageContentBlock(buffer, exchangeDecrypt);
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public void UploadSignatureProfile(string comment, IExchangeEncrypt exchangeEncrypt, IEnumerable<string> trustSignatures, IEnumerable<Document> documents, IEnumerable<Chat> chats, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new SignatureProfileContent(comment, trustSignatures, documents, chats, exchangeEncrypt);
                    buffer = ContentConverter.ToSignatureProfileContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var signatureProfile = new SignatureProfile(key, digitalSignature);

                    if (_settings.SetSignatureProfile(signatureProfile))
                    {
                        _cacheManager[key] = buffer;
                    }
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public void UploadDocumentSite(Document document, 
            IEnumerable<DocumentPage> documentPages, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new DocumentSiteContent(documentPages);
                    buffer = ContentConverter.ToDocumentSiteContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var documentSite = new DocumentSite(document, key, digitalSignature);

                    if (_settings.SetDocumentSite(documentSite))
                    {
                        _cacheManager[key] = buffer;
                    }
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public void UploadDocumentOpinion(Document document,
             IEnumerable<Key> goods, IEnumerable<Key> bads, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new DocumentOpinionContent(goods, bads);
                    buffer = ContentConverter.ToDocumentOpinionContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var documentOpinion = new DocumentOpinion(document, key, digitalSignature);

                    if (_settings.SetDocumentOpinion(documentOpinion))
                    {
                        _cacheManager[key] = buffer;
                    }
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public void UploadChatTopic(Chat chat,
            string comment, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new ChatTopicContent(comment);
                    buffer = ContentConverter.ToChatTopicContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var chatTopic = new ChatTopic(chat, key, digitalSignature);

                    if (_settings.SetChatTopic(chatTopic))
                    {
                        _cacheManager[key] = buffer;
                    }
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public void UploadChatMessage(Chat chat,
            string comment, IEnumerable<Key> anchors, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new ChatMessageContent(comment, anchors);
                    buffer = ContentConverter.ToChatMessageContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var chatMessage = new ChatMessage(chat, key, digitalSignature);

                    if (_settings.SetChatMessage(chatMessage))
                    {
                        _cacheManager[key] = buffer;
                    }
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public void UploadWhisperMessage(Whisper whisper,
            string comment, IEnumerable<Key> anchors, WhisperCryptoInformation cryptoInformation, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new WhisperMessageContent(comment, anchors);
                    buffer = ContentConverter.ToWhisperMessageContentBlock(content, cryptoInformation);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var whisperMessage = new WhisperMessage(whisper, key, digitalSignature);

                    if (_settings.SetWhisperMessage(whisperMessage))
                    {
                        _cacheManager[key] = buffer;
                    }
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public void UploadMailMessage(Mail mail,
            string text, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new MailMessageContent(text);
                    buffer = ContentConverter.ToMailMessageContentBlock(content, exchangeEncrypt);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var mailMessage = new MailMessage(mail, key, digitalSignature);

                    if (_settings.SetMailMessage(mailMessage))
                    {
                        _cacheManager[key] = buffer;
                    }
                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }
        }

        public override ManagerState State
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _state;
                }
            }
        }

        public override void Start()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            while (_createClientConnection1Thread != null) Thread.Sleep(1000);
            while (_createClientConnection2Thread != null) Thread.Sleep(1000);
            while (_createClientConnection3Thread != null) Thread.Sleep(1000);
            while (_createServerConnection1Thread != null) Thread.Sleep(1000);
            while (_createServerConnection2Thread != null) Thread.Sleep(1000);
            while (_createServerConnection3Thread != null) Thread.Sleep(1000);
            while (_connectionsManagerThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _serverManager.Start();

                _createClientConnection1Thread = new Thread(this.CreateClientConnectionThread);
                _createClientConnection1Thread.Name = "ConnectionsManager_CreateClientConnection1Thread";
                _createClientConnection1Thread.Start();
                _createClientConnection2Thread = new Thread(this.CreateClientConnectionThread);
                _createClientConnection2Thread.Name = "ConnectionsManager_CreateClientConnection2Thread";
                _createClientConnection2Thread.Start();
                _createClientConnection3Thread = new Thread(this.CreateClientConnectionThread);
                _createClientConnection3Thread.Name = "ConnectionsManager_CreateClientConnection3Thread";
                _createClientConnection3Thread.Start();
                _createServerConnection1Thread = new Thread(this.CreateServerConnectionThread);
                _createServerConnection1Thread.Name = "ConnectionsManager_CreateServerConnection1Thread";
                _createServerConnection1Thread.Start();
                _createServerConnection2Thread = new Thread(this.CreateServerConnectionThread);
                _createServerConnection2Thread.Name = "ConnectionsManager_CreateServerConnection2Thread";
                _createServerConnection2Thread.Start();
                _createServerConnection3Thread = new Thread(this.CreateServerConnectionThread);
                _createServerConnection3Thread.Name = "ConnectionsManager_CreateServerConnection3Thread";
                _createServerConnection3Thread.Start();
                _connectionsManagerThread = new Thread(this.ConnectionsManagerThread);
                _connectionsManagerThread.Name = "ConnectionsManager_ConnectionsManagerThread";
                _connectionsManagerThread.Start();
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _serverManager.Stop();
            }

            _createClientConnection1Thread.Join();
            _createClientConnection1Thread = null;
            _createClientConnection2Thread.Join();
            _createClientConnection2Thread = null;
            _createClientConnection3Thread.Join();
            _createClientConnection3Thread = null;
            _createServerConnection1Thread.Join();
            _createServerConnection1Thread = null;
            _createServerConnection2Thread.Join();
            _createServerConnection2Thread = null;
            _createServerConnection3Thread.Join();
            _createServerConnection3Thread = null;
            _connectionsManagerThread.Join();
            _connectionsManagerThread = null;

            lock (this.ThisLock)
            {
                foreach (var item in _connectionManagers.ToArray())
                {
                    this.RemoveConnectionManager(item);
                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                _routeTable.BaseNode = _settings.BaseNode;

                foreach (var node in _settings.OtherNodes)
                {
                    if (node == null || node.Id == null || !node.Uris.Any(n => _clientManager.CheckUri(n))) continue;

                    _routeTable.Add(node);
                }

                _bandwidthLimit.In = _settings.BandwidthLimit;
                _bandwidthLimit.Out = _settings.BandwidthLimit;
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.OtherNodes.Clear();
                _settings.OtherNodes.AddRange(_routeTable.ToArray());

                _settings.Save(directoryPath);
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private object _thisLock = new object();

            public Settings()
                : base(new List<Library.Configuration.ISettingsContext>() { 
                    new Library.Configuration.SettingsContext<Node>() { Name = "BaseNode", Value = new Node() },
                    new Library.Configuration.SettingsContext<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingsContext<int>() { Name = "ConnectionCountLimit", Value = 12 },
                    new Library.Configuration.SettingsContext<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingsContext<Dictionary<string, SignatureProfile>>() { Name = "SignatureProfiles", Value = new Dictionary<string, SignatureProfile>() },
                    new Library.Configuration.SettingsContext<Dictionary<Document, Dictionary<string, DocumentSite>>>() { Name = "DocumentSites", Value = new Dictionary<Document, Dictionary<string, DocumentSite>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Document, Dictionary<string, DocumentOpinion>>>() { Name = "DocumentOpinions", Value = new Dictionary<Document, Dictionary<string, DocumentOpinion>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Chat, Dictionary<string, ChatTopic>>>() { Name = "ChatTopics", Value = new Dictionary<Chat, Dictionary<string, ChatTopic>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Chat, HashSet<ChatMessage>>>() { Name = "ChatMessages", Value = new Dictionary<Chat, HashSet<ChatMessage>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Whisper, HashSet<WhisperMessage>>>() { Name = "WhisperMessages", Value = new Dictionary<Whisper, HashSet<WhisperMessage>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Mail, HashSet<MailMessage>>>() { Name = "MailMessages", Value = new Dictionary<Mail, HashSet<MailMessage>>() },
                })
            {

            }

            public Information Information
            {
                get
                {
                    lock (_thisLock)
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("SignatureCount", this.GetSignatures().Count()));
                        contexts.Add(new InformationContext("SignatureProfileCount", this.SignatureProfiles.Count));

                        contexts.Add(new InformationContext("DocumentCount", this.GetDocuments().Count()));
                        contexts.Add(new InformationContext("DocumentSiteCount", this.DocumentSites.Values.Sum(n => n.Count)));
                        contexts.Add(new InformationContext("DocumentOpinionCount", this.DocumentOpinions.Values.Sum(n => n.Count)));

                        contexts.Add(new InformationContext("ChatCount", this.GetChats().Count()));
                        contexts.Add(new InformationContext("ChatTopicCount", this.ChatTopics.Values.Sum(n => n.Count)));
                        contexts.Add(new InformationContext("ChatMessageCount", this.ChatMessages.Sum(n => n.Value.Count)));

                        contexts.Add(new InformationContext("WhisperCount", this.GetWhispers().Count()));
                        contexts.Add(new InformationContext("WhisperMessageCount", this.WhisperMessages.Sum(n => n.Value.Count)));

                        contexts.Add(new InformationContext("MailCount", this.GetMails().Count()));
                        contexts.Add(new InformationContext("MailMessageCount", this.MailMessages.Sum(n => n.Value.Count)));

                        return new Information(contexts);
                    }
                }
            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public IEnumerable<string> GetSignatures()
            {
                lock (_thisLock)
                {
                    HashSet<string> hashset = new HashSet<string>();

                    hashset.UnionWith(this.SignatureProfiles.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Document> GetDocuments()
            {
                lock (_thisLock)
                {
                    HashSet<Document> hashset = new HashSet<Document>();

                    hashset.UnionWith(this.DocumentSites.Keys);
                    hashset.UnionWith(this.DocumentOpinions.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Chat> GetChats()
            {
                lock (_thisLock)
                {
                    HashSet<Chat> hashset = new HashSet<Chat>();

                    hashset.UnionWith(this.ChatTopics.Keys);
                    hashset.UnionWith(this.ChatMessages.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Whisper> GetWhispers()
            {
                lock (_thisLock)
                {
                    HashSet<Whisper> hashset = new HashSet<Whisper>();

                    hashset.UnionWith(this.WhisperMessages.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Mail> GetMails()
            {
                lock (_thisLock)
                {
                    HashSet<Mail> hashset = new HashSet<Mail>();

                    hashset.UnionWith(this.MailMessages.Keys);

                    return hashset;
                }
            }

            public void RemoveSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        this.SignatureProfiles.Remove(signature);
                    }
                }
            }

            public void RemoveDocuments(IEnumerable<Document> documents)
            {
                lock (_thisLock)
                {
                    foreach (var document in documents)
                    {
                        this.DocumentSites.Remove(document);
                        this.DocumentOpinions.Remove(document);
                    }
                }
            }

            public void RemoveChats(IEnumerable<Chat> chats)
            {
                lock (_thisLock)
                {
                    foreach (var chat in chats)
                    {
                        this.ChatTopics.Remove(chat);
                        this.ChatMessages.Remove(chat);
                    }
                }
            }

            public void RemoveWhispers(IEnumerable<Whisper> whispers)
            {
                lock (_thisLock)
                {
                    foreach (var whisper in whispers)
                    {
                        this.WhisperMessages.Remove(whisper);
                    }
                }
            }

            public void RemoveMails(IEnumerable<Mail> mails)
            {
                lock (_thisLock)
                {
                    foreach (var mail in mails)
                    {
                        this.MailMessages.Remove(mail);
                    }
                }
            }

            public SignatureProfile GetSignatureProfile(string signature)
            {
                lock (_thisLock)
                {
                    SignatureProfile signatureProfile = null;
                    this.SignatureProfiles.TryGetValue(signature, out signatureProfile);

                    return signatureProfile;
                }
            }

            public IEnumerable<DocumentSite> GetDocumentSites(Document document)
            {
                lock (_thisLock)
                {
                    Dictionary<string, DocumentSite> dic = null;

                    if (this.DocumentSites.TryGetValue(document, out dic))
                    {
                        return dic.Values;
                    }

                    return new DocumentSite[0];
                }
            }

            public IEnumerable<DocumentOpinion> GetDocumentOpinions(Document document)
            {
                lock (_thisLock)
                {
                    Dictionary<string, DocumentOpinion> dic = null;

                    if (this.DocumentOpinions.TryGetValue(document, out dic))
                    {
                        return dic.Values;
                    }

                    return new DocumentOpinion[0];
                }
            }

            public IEnumerable<ChatTopic> GetChatTopics(Chat chat)
            {
                lock (_thisLock)
                {
                    Dictionary<string, ChatTopic> dic = null;

                    if (this.ChatTopics.TryGetValue(chat, out dic))
                    {
                        return dic.Values;
                    }

                    return new ChatTopic[0];
                }
            }

            public IEnumerable<ChatMessage> GetChatMessages(Chat chat)
            {
                lock (_thisLock)
                {
                    HashSet<ChatMessage> hashset = null;

                    if (this.ChatMessages.TryGetValue(chat, out hashset))
                    {
                        return hashset;
                    }

                    return new ChatMessage[0];
                }
            }

            public IEnumerable<WhisperMessage> GetWhisperMessages(Whisper whisper)
            {
                lock (_thisLock)
                {
                    HashSet<WhisperMessage> hashset = null;

                    if (this.WhisperMessages.TryGetValue(whisper, out hashset))
                    {
                        return hashset;
                    }

                    return new WhisperMessage[0];
                }
            }

            public IEnumerable<MailMessage> GetMailMessages(Mail mail)
            {
                lock (_thisLock)
                {
                    HashSet<MailMessage> hashset = null;

                    if (this.MailMessages.TryGetValue(mail, out hashset))
                    {
                        return hashset;
                    }

                    return new MailMessage[0];
                }
            }

            public bool SetSignatureProfile(SignatureProfile signatureProfile)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (signatureProfile == null
                        || (signatureProfile.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || signatureProfile.Certificate == null || !signatureProfile.VerifyCertificate()) return false;

                    var signature = signatureProfile.Certificate.ToString();

                    SignatureProfile tempSignatureProfile = null;

                    if (!this.SignatureProfiles.TryGetValue(signature, out tempSignatureProfile)
                        || signatureProfile.CreationTime > tempSignatureProfile.CreationTime)
                    {
                        this.SignatureProfiles[signature] = signatureProfile;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetDocumentSite(DocumentSite documentSite)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (documentSite == null || documentSite.Document == null || documentSite.Document.Id == null || documentSite.Document.Id.Length == 0 || string.IsNullOrWhiteSpace(documentSite.Document.Name)
                        || (documentSite.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || documentSite.Certificate == null || !documentSite.VerifyCertificate()) return false;

                    var signature = documentSite.Certificate.ToString();

                    Dictionary<string, DocumentSite> dic = null;

                    if (!this.DocumentSites.TryGetValue(documentSite.Document, out dic))
                    {
                        dic = new Dictionary<string, DocumentSite>();
                        this.DocumentSites[documentSite.Document] = dic;

                        dic[signature] = documentSite;

                        return true;
                    }

                    DocumentSite tempDocumentSite = null;

                    if (!dic.TryGetValue(signature, out tempDocumentSite)
                        || documentSite.CreationTime > tempDocumentSite.CreationTime)
                    {
                        dic[signature] = documentSite;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetDocumentOpinion(DocumentOpinion documentOpinion)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (documentOpinion == null || documentOpinion.Document == null || documentOpinion.Document.Id == null || documentOpinion.Document.Id.Length == 0 || string.IsNullOrWhiteSpace(documentOpinion.Document.Name)
                        || (documentOpinion.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || documentOpinion.Certificate == null || !documentOpinion.VerifyCertificate()) return false;

                    var signature = documentOpinion.Certificate.ToString();

                    Dictionary<string, DocumentOpinion> dic = null;

                    if (!this.DocumentOpinions.TryGetValue(documentOpinion.Document, out dic))
                    {
                        dic = new Dictionary<string, DocumentOpinion>();
                        this.DocumentOpinions[documentOpinion.Document] = dic;

                        dic[signature] = documentOpinion;

                        return true;
                    }

                    DocumentOpinion tempDocumentOpinion = null;

                    if (!dic.TryGetValue(signature, out tempDocumentOpinion)
                        || documentOpinion.CreationTime > tempDocumentOpinion.CreationTime)
                    {
                        dic[signature] = documentOpinion;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetChatTopic(ChatTopic chatTopic)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (chatTopic == null || chatTopic.Chat == null || chatTopic.Chat.Id == null || chatTopic.Chat.Id.Length == 0 || string.IsNullOrWhiteSpace(chatTopic.Chat.Name)
                        || (chatTopic.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || chatTopic.Certificate == null || !chatTopic.VerifyCertificate()) return false;

                    var signature = chatTopic.Certificate.ToString();

                    Dictionary<string, ChatTopic> dic = null;

                    if (!this.ChatTopics.TryGetValue(chatTopic.Chat, out dic))
                    {
                        dic = new Dictionary<string, ChatTopic>();
                        this.ChatTopics[chatTopic.Chat] = dic;

                        dic[signature] = chatTopic;

                        return true;
                    }

                    ChatTopic tempChatTopic = null;

                    if (!dic.TryGetValue(signature, out tempChatTopic)
                        || chatTopic.CreationTime > tempChatTopic.CreationTime)
                    {
                        dic[signature] = chatTopic;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetChatMessage(ChatMessage chatMessage)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (chatMessage == null || chatMessage.Chat == null || chatMessage.Chat.Id == null || chatMessage.Chat.Id.Length == 0 || string.IsNullOrWhiteSpace(chatMessage.Chat.Name)
                        || (now - chatMessage.CreationTime) > new TimeSpan(64, 0, 0, 0)
                        || (chatMessage.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || chatMessage.Certificate == null || !chatMessage.VerifyCertificate()) return false;

                    HashSet<ChatMessage> hashset = null;

                    if (!this.ChatMessages.TryGetValue(chatMessage.Chat, out hashset))
                    {
                        hashset = new HashSet<ChatMessage>();
                        this.ChatMessages[chatMessage.Chat] = hashset;
                    }

                    return hashset.Add(chatMessage);
                }
            }

            public bool SetWhisperMessage(WhisperMessage whisperMessage)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (whisperMessage == null || whisperMessage.Whisper == null || whisperMessage.Whisper.Id == null || whisperMessage.Whisper.Id.Length == 0 || string.IsNullOrWhiteSpace(whisperMessage.Whisper.Name)
                        || (now - whisperMessage.CreationTime) > new TimeSpan(64, 0, 0, 0)
                        || (whisperMessage.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || whisperMessage.Certificate == null || !whisperMessage.VerifyCertificate()) return false;

                    HashSet<WhisperMessage> hashset = null;

                    if (!this.WhisperMessages.TryGetValue(whisperMessage.Whisper, out hashset))
                    {
                        hashset = new HashSet<WhisperMessage>();
                        this.WhisperMessages[whisperMessage.Whisper] = hashset;
                    }

                    return hashset.Add(whisperMessage);
                }
            }

            public bool SetMailMessage(MailMessage mailMessage)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (mailMessage == null || mailMessage.Mail == null || mailMessage.Mail.Id == null || mailMessage.Mail.Id.Length == 0 || string.IsNullOrWhiteSpace(mailMessage.Mail.Name)
                        || (now - mailMessage.CreationTime) > new TimeSpan(64, 0, 0, 0)
                        || (mailMessage.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || mailMessage.Certificate == null || !mailMessage.VerifyCertificate()) return false;

                    HashSet<MailMessage> hashset = null;

                    if (!this.MailMessages.TryGetValue(mailMessage.Mail, out hashset))
                    {
                        hashset = new HashSet<MailMessage>();
                        this.MailMessages[mailMessage.Mail] = hashset;
                    }

                    return hashset.Add(mailMessage);
                }
            }

            public void RemoveSignatureProfile(SignatureProfile signatureProfile)
            {
                lock (_thisLock)
                {
                    this.SignatureProfiles.Remove(signatureProfile.Certificate.ToString());
                }
            }

            public void RemoveDocumentSite(DocumentSite documentSite)
            {
                lock (_thisLock)
                {
                    var signature = documentSite.Certificate.ToString();

                    Dictionary<string, DocumentSite> dic = null;

                    if (this.DocumentSites.TryGetValue(documentSite.Document, out dic))
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            this.DocumentSites.Remove(documentSite.Document);
                        }
                    }
                }
            }

            public void RemoveDocumentOpinion(DocumentOpinion documentOpinion)
            {
                lock (_thisLock)
                {
                    var signature = documentOpinion.Certificate.ToString();

                    Dictionary<string, DocumentOpinion> dic = null;

                    if (this.DocumentOpinions.TryGetValue(documentOpinion.Document, out dic))
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            this.DocumentOpinions.Remove(documentOpinion.Document);
                        }
                    }
                }
            }

            public void RemoveChatTopic(ChatTopic chatTopic)
            {
                lock (_thisLock)
                {
                    var signature = chatTopic.Certificate.ToString();

                    Dictionary<string, ChatTopic> dic = null;

                    if (this.ChatTopics.TryGetValue(chatTopic.Chat, out dic))
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            this.ChatTopics.Remove(chatTopic.Chat);
                        }
                    }
                }
            }

            public void RemoveChatMessage(ChatMessage chatMessage)
            {
                lock (_thisLock)
                {
                    HashSet<ChatMessage> hashset = null;

                    if (this.ChatMessages.TryGetValue(chatMessage.Chat, out hashset))
                    {
                        hashset.Remove(chatMessage);

                        if (hashset.Count == 0)
                        {
                            this.ChatMessages.Remove(chatMessage.Chat);
                        }
                    }
                }
            }

            public void RemoveWhisperMessage(WhisperMessage whisperMessage)
            {
                lock (_thisLock)
                {
                    HashSet<WhisperMessage> hashset = null;

                    if (this.WhisperMessages.TryGetValue(whisperMessage.Whisper, out hashset))
                    {
                        hashset.Remove(whisperMessage);

                        if (hashset.Count == 0)
                        {
                            this.WhisperMessages.Remove(whisperMessage.Whisper);
                        }
                    }
                }
            }

            public void RemoveMailMessage(MailMessage mailMessage)
            {
                lock (_thisLock)
                {
                    HashSet<MailMessage> hashset = null;

                    if (this.MailMessages.TryGetValue(mailMessage.Mail, out hashset))
                    {
                        hashset.Remove(mailMessage);

                        if (hashset.Count == 0)
                        {
                            this.MailMessages.Remove(mailMessage.Mail);
                        }
                    }
                }
            }

            public Node BaseNode
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Node)this["BaseNode"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["BaseNode"] = value;
                    }
                }
            }

            public NodeCollection OtherNodes
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (NodeCollection)this["OtherNodes"];
                    }
                }
            }

            public int ConnectionCountLimit
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (int)this["ConnectionCountLimit"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["ConnectionCountLimit"] = value;
                    }
                }
            }

            public int BandwidthLimit
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (int)this["BandwidthLimit"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["BandwidthLimit"] = value;
                    }
                }
            }

            private Dictionary<string, SignatureProfile> SignatureProfiles
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<string, SignatureProfile>)this["SignatureProfiles"];
                    }
                }
            }

            private Dictionary<Document, Dictionary<string, DocumentSite>> DocumentSites
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Document, Dictionary<string, DocumentSite>>)this["DocumentSites"];
                    }
                }
            }

            private Dictionary<Document, Dictionary<string, DocumentOpinion>> DocumentOpinions
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Document, Dictionary<string, DocumentOpinion>>)this["DocumentOpinions"];
                    }
                }
            }

            private Dictionary<Chat, Dictionary<string, ChatTopic>> ChatTopics
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Chat, Dictionary<string, ChatTopic>>)this["ChatTopics"];
                    }
                }
            }

            private Dictionary<Chat, HashSet<ChatMessage>> ChatMessages
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Chat, HashSet<ChatMessage>>)this["ChatMessages"];
                    }
                }
            }

            private Dictionary<Whisper, HashSet<WhisperMessage>> WhisperMessages
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Whisper, HashSet<WhisperMessage>>)this["WhisperMessages"];
                    }
                }
            }

            private Dictionary<Mail, HashSet<MailMessage>> MailMessages
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Mail, HashSet<MailMessage>>)this["MailMessages"];
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

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
