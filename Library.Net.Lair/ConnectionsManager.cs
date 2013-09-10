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
    public delegate IEnumerable<Section> LockSectionsEventHandler(object sender);
    public delegate IEnumerable<Chat> LockChatsEventHandler(object sender);

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

        private LockedDictionary<Node, HashSet<Section>> _pushSectionsRequestDictionary = new LockedDictionary<Node, HashSet<Section>>();
        private LockedDictionary<Node, HashSet<Chat>> _pushChatsRequestDictionary = new LockedDictionary<Node, HashSet<Chat>>();
        private LockedDictionary<Node, HashSet<string>> _pushSignaturesRequestDictionary = new LockedDictionary<Node, HashSet<string>>();

        private LockedHashSet<string> _trustSignatures = new LockedHashSet<string>();

        private LockedList<Node> _creatingNodes;
        private CirculationCollection<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private CirculationDictionary<Node, int> _nodesStatus;

        private CirculationCollection<Section> _pushSectionsRequestList;
        private CirculationCollection<Chat> _pushChatsRequestList;
        private CirculationCollection<string> _pushSignaturesRequestList;

        private LockedDictionary<Section, DateTime> _lastUsedSectionTimes = new LockedDictionary<Section, DateTime>();
        private LockedDictionary<Chat, DateTime> _lastUsedChatTimes = new LockedDictionary<Chat, DateTime>();
        private LockedDictionary<string, DateTime> _lastUsedSignatureTimes = new LockedDictionary<string, DateTime>();

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
        private volatile int _pushSectionRequestCount;
        private volatile int _pushProfileCount;
        private volatile int _pushDocumentPageCount;
        private volatile int _pushDocumentOpinionCount;
        private volatile int _pushChatRequestCount;
        private volatile int _pushTopicCount;
        private volatile int _pushMessageCount;
        private volatile int _pushSignatureRequestCount;
        private volatile int _pushMailMessageCount;

        private volatile int _pullNodeCount;
        private volatile int _pullSectionRequestCount;
        private volatile int _pullProfileCount;
        private volatile int _pullDocumentPageCount;
        private volatile int _pullDocumentOpinionCount;
        private volatile int _pullChatRequestCount;
        private volatile int _pullTopicCount;
        private volatile int _pullMessageCount;
        private volatile int _pullSignatureRequestCount;
        private volatile int _pullMailMessageCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        private TrustSignaturesEventHandler _trustSignaturesEvent;
        private LockSectionsEventHandler _lockSectionsEvent;
        private LockChatsEventHandler _lockChatsEvent;

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

            _pushSectionsRequestList = new CirculationCollection<Section>(new TimeSpan(0, 3, 0));
            _pushChatsRequestList = new CirculationCollection<Chat>(new TimeSpan(0, 3, 0));
            _pushSignaturesRequestList = new CirculationCollection<string>(new TimeSpan(0, 3, 0));

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

        public LockSectionsEventHandler LockSectionsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockSectionsEvent = value;
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
                    contexts.Add(new InformationContext("PushSectionRequestCount", _pushSectionRequestCount));
                    contexts.Add(new InformationContext("PushProfileCount", _pushProfileCount));
                    contexts.Add(new InformationContext("PushDocumentPageCount", _pushDocumentPageCount));
                    contexts.Add(new InformationContext("PushChatRequestCount", _pushChatRequestCount));
                    contexts.Add(new InformationContext("PushTopicCount", _pushTopicCount));
                    contexts.Add(new InformationContext("PushMessageCount", _pushMessageCount));
                    contexts.Add(new InformationContext("PushMailMessageCount", _pushMailMessageCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PullSectionRequestCount", _pullSectionRequestCount));
                    contexts.Add(new InformationContext("PullProfileCount", _pullProfileCount));
                    contexts.Add(new InformationContext("PullDocumentPageCount", _pullDocumentPageCount));
                    contexts.Add(new InformationContext("PullChatRequestCount", _pullChatRequestCount));
                    contexts.Add(new InformationContext("PullTopicCount", _pullTopicCount));
                    contexts.Add(new InformationContext("PullMessageCount", _pullMessageCount));
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

        protected virtual IEnumerable<Section> OnLockSectionsEvent()
        {
            if (_lockSectionsEvent != null)
            {
                return _lockSectionsEvent(this);
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
                connectionManager.PullSectionsRequestEvent += new PullSectionsRequestEventHandler(connectionManager_PullSectionsRequestEvent);
                connectionManager.PullProfilesEvent += new PullProfilesEventHandler(connectionManager_PullProfilesEvent);
                connectionManager.PullDocumentPagesEvent += new PullDocumentPagesEventHandler(connectionManager_PullDocumentPagesEvent);
                connectionManager.PullDocumentOpinionsEvent += new PullDocumentOpinionsEventHandler(connectionManager_PullDocumentOpinionsEvent);
                connectionManager.PullChatsRequestEvent += new PullChatsRequestEventHandler(connectionManager_PullChatsRequestEvent);
                connectionManager.PullTopicsEvent += new PullTopicsEventHandler(connectionManager_PullTopicsEvent);
                connectionManager.PullMessagesEvent += new PullMessagesEventHandler(connectionManager_PullMessagesEvent);
                connectionManager.PullSignaturesRequestEvent += new PullSignaturesRequestEventHandler(connectionManager_PullSignaturesRequestEvent);
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

                            foreach (var section in _settings.GetSections())
                            {
                                foreach (var item in _settings.GetProfiles(section))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveProfile(item);
                                }

                                foreach (var item in _settings.GetDocumentPages(section))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveDocumentPage(item);
                                }

                                foreach (var item in _settings.GetDocumentOpinions(section))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveDocumentOpinion(item);
                                }
                            }

                            foreach (var channel in _settings.GetChats())
                            {
                                foreach (var item in _settings.GetTopics(channel))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveTopic(item);
                                }

                                foreach (var item in _settings.GetMessages(channel))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveMessage(item);
                                }
                            }

                            foreach (var signature in _settings.GetSignatures())
                            {
                                foreach (var item in _settings.GetMailMessages(signature))
                                {
                                    if (!cacheKeys.Contains(item.Content)) _settings.RemoveMailMessage(item);
                                }
                            }
                        }

                        {
                            var linkKeys = new HashSet<Key>();

                            foreach (var section in _settings.GetSections())
                            {
                                foreach (var item in _settings.GetProfiles(section))
                                {
                                    linkKeys.Add(item.Content);
                                }

                                foreach (var item in _settings.GetDocumentPages(section))
                                {
                                    linkKeys.Add(item.Content);
                                }

                                foreach (var item in _settings.GetDocumentOpinions(section))
                                {
                                    linkKeys.Add(item.Content);
                                }
                            }

                            foreach (var channel in _settings.GetChats())
                            {
                                foreach (var item in _settings.GetTopics(channel))
                                {
                                    linkKeys.Add(item.Content);
                                }

                                foreach (var item in _settings.GetMessages(channel))
                                {
                                    linkKeys.Add(item.Content);
                                }
                            }

                            foreach (var signature in _settings.GetSignatures())
                            {
                                foreach (var item in _settings.GetMailMessages(signature))
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
                                var lockSections = this.OnLockSectionsEvent();

                                if (lockSections != null)
                                {
                                    var removeSections = new HashSet<Section>();
                                    removeSections.UnionWith(_settings.GetSections());
                                    removeSections.ExceptWith(lockSections);

                                    var sortList = removeSections.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedSectionTimes.TryGetValue(x, out tx);
                                        _lastUsedSectionTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.RemoveSections(sortList.Take(sortList.Count - 1024));

                                    var liveSections = new HashSet<Section>(_settings.GetSections());

                                    foreach (var signature in _lastUsedSectionTimes.Keys.ToArray())
                                    {
                                        if (liveSections.Contains(signature)) continue;

                                        _lastUsedSectionTimes.Remove(signature);
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
                                var lockSignatures = this.OnTrustSignaturesEvent();

                                if (lockSignatures != null)
                                {
                                    var lockSignatureHashset = new HashSet<string>(lockSignatures);

                                    lock (this.ThisLock)
                                    {
                                        var removeProfiles = new List<SectionProfile>();
                                        var removeDocumentPages = new List<DocumentPage>();
                                        var removeDocumentOpinions = new List<DocumentOpinion>();
                                        var removeTopics = new List<ChatTopic>();
                                        var removeMessages = new List<ChatMessage>();
                                        var removeMailMessages = new List<MailMessage>();

                                        foreach (var section in _settings.GetSections())
                                        {
                                            {
                                                var untrustProfiles = new List<SectionProfile>();

                                                foreach (var item in _settings.GetProfiles(section))
                                                {
                                                    if (lockSignatureHashset.Contains(item.Certificate.ToString())) continue;

                                                    untrustProfiles.Add(item);
                                                }

                                                removeProfiles.AddRange(untrustProfiles.Randomize().Take(untrustProfiles.Count - 256));
                                            }

                                            // トラストな作成者のドキュメントは新しい順に1024まで許容
                                            // 非トラストな作成者のドキュメントは新しい順に32まで許容
                                            {
                                                var trustDocumentPages = new List<DocumentPage>();
                                                var untrustDocumentPages = new List<DocumentPage>();

                                                foreach (var item in _settings.GetDocumentPages(section))
                                                {
                                                    if (lockSignatureHashset.Contains(item.Certificate.ToString()))
                                                    {
                                                        trustDocumentPages.Add(item);
                                                    }
                                                    else
                                                    {
                                                        untrustDocumentPages.Add(item);
                                                    }
                                                }

                                                if (trustDocumentPages.Count > 1024)
                                                {
                                                    trustDocumentPages.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeDocumentPages.AddRange(trustDocumentPages.Take(trustDocumentPages.Count - 1024));
                                                }

                                                if (untrustDocumentPages.Count > 32)
                                                {
                                                    untrustDocumentPages.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeDocumentPages.AddRange(untrustDocumentPages.Take(untrustDocumentPages.Count - 32));
                                                }
                                            }

                                            {
                                                var untrustDocumentOpinions = new List<DocumentOpinion>();

                                                foreach (var item in _settings.GetDocumentOpinions(section))
                                                {
                                                    if (lockSignatureHashset.Contains(item.Certificate.ToString())) continue;

                                                    untrustDocumentOpinions.Add(item);
                                                }

                                                removeDocumentOpinions.AddRange(untrustDocumentOpinions.Randomize().Take(untrustDocumentOpinions.Count - 256));
                                            }
                                        }

                                        foreach (var channel in _settings.GetChats())
                                        {
                                            {
                                                var trustTopics = new List<ChatTopic>();
                                                var untrustTopics = new List<ChatTopic>();

                                                foreach (var item in _settings.GetTopics(channel))
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

                                                    removeTopics.AddRange(trustTopics.Take(trustTopics.Count - 4));
                                                }

                                                if (untrustTopics.Count > 2)
                                                {
                                                    untrustTopics.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeTopics.AddRange(untrustTopics.Take(untrustTopics.Count - 2));
                                                }
                                            }

                                            // 64日を過ぎたメッセージは破棄
                                            // トラストな作成者のメッセージは新しい順に256まで許容
                                            // 非トラストな作成者のメッセージは新しい順に32まで許容
                                            {
                                                var trustMessages = new List<ChatMessage>();
                                                var untrustMessages = new List<ChatMessage>();

                                                foreach (var item in _settings.GetMessages(channel))
                                                {
                                                    if ((now - item.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                                    {
                                                        removeMessages.Add(item);
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

                                                    removeMessages.AddRange(trustMessages.Take(trustMessages.Count - 256));
                                                }

                                                if (untrustMessages.Count > 32)
                                                {
                                                    untrustMessages.Sort((x, y) =>
                                                    {
                                                        return x.CreationTime.CompareTo(y);
                                                    });

                                                    removeMessages.AddRange(untrustMessages.Take(untrustMessages.Count - 32));
                                                }
                                            }
                                        }

                                        foreach (var signature in _settings.GetSignatures())
                                        {
                                            // 32日を過ぎたメールは破棄
                                            // トラストな作成者のメールはすべて許容、一つのあて先につき新しい順に8まで許容
                                            // 非トラストな作成者のメールは32人分まで許容、一つのあて先につき新しい順に2まで許容
                                            {
                                                var trustMailMessageDic = new Dictionary<string, List<MailMessage>>();
                                                var untrustMailMessageDic = new Dictionary<string, List<MailMessage>>();

                                                foreach (var item in _settings.GetMailMessages(signature))
                                                {
                                                    if ((now - item.CreationTime) > new TimeSpan(32, 0, 0, 0))
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

                                        foreach (var item in removeProfiles)
                                        {
                                            _settings.RemoveProfile(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeDocumentPages)
                                        {
                                            _settings.RemoveDocumentPage(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeDocumentOpinions)
                                        {
                                            _settings.RemoveDocumentOpinion(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeTopics)
                                        {
                                            _settings.RemoveTopic(item);
                                            _cacheManager.Remove(item.Content);
                                        }

                                        foreach (var item in removeMessages)
                                        {
                                            _settings.RemoveMessage(item);
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

                    foreach (var item in _settings.GetSections())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullSectionsRequest.Add(item);
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
                }

                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushDownloadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushDownloadStopwatch.Restart();

                    HashSet<Section> pushSectionsRequestList = new HashSet<Section>();
                    HashSet<Chat> pushChatsRequestList = new HashSet<Chat>();
                    HashSet<string> pushSignaturesRequestList = new HashSet<string>();
                    List<Node> nodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        nodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    {
                        {
                            var list = _pushSectionsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSectionsRequest.Contains(list[i])))
                                {
                                    pushSectionsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullSectionsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSectionsRequest.Contains(list[i])))
                                {
                                    pushSectionsRequestList.Add(list[i]);
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
                    }

                    {
                        Dictionary<Node, HashSet<Section>> pushSectionsRequestDictionary = new Dictionary<Node, HashSet<Section>>();

                        foreach (var item in pushSectionsRequestList.Randomize())
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Section> hashset;

                                    if (!pushSectionsRequestDictionary.TryGetValue(requestNodes[i], out hashset))
                                    {
                                        hashset = new HashSet<Section>();
                                        pushSectionsRequestDictionary[requestNodes[i]] = hashset;
                                    }

                                    hashset.Add(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushSectionsRequestDictionary.ThisLock)
                        {
                            _pushSectionsRequestDictionary.Clear();

                            foreach (var item in pushSectionsRequestDictionary)
                            {
                                _pushSectionsRequestDictionary.Add(item.Key, item.Value);
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

                    {
                        Dictionary<Node, HashSet<string>> pushSignaturesRequestDictionary = new Dictionary<Node, HashSet<string>>();

                        foreach (var item in pushSignaturesRequestList)
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
                            // PushSectionsRequest
                            {
                                SectionCollection tempList = null;
                                int count = (int)(_maxRequestCount * this.ResponseTimePriority(connectionManager.Node));

                                lock (_pushSectionsRequestDictionary.ThisLock)
                                {
                                    HashSet<Section> hashset;

                                    if (_pushSectionsRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                    {
                                        tempList = new SectionCollection(hashset.Randomize().Take(count));

                                        hashset.ExceptWith(tempList);
                                        messageManager.PushSectionsRequest.AddRange(tempList);
                                    }
                                }

                                if (tempList != null && tempList.Count > 0)
                                {
                                    try
                                    {
                                        connectionManager.PushSectionsRequest(tempList);

                                        foreach (var item in tempList)
                                        {
                                            _pushSectionsRequestList.Remove(item);
                                        }

                                        Debug.WriteLine(string.Format("ConnectionManager: Push SectionsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                        _pushSectionRequestCount += tempList.Count;
                                    }
                                    catch (Exception e)
                                    {
                                        foreach (var item in tempList)
                                        {
                                            messageManager.PushSectionsRequest.Remove(item);
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

                        }
                    }

                    if (connectionCount >= _uploadingConnectionCountLowerLimit)
                    {
                        {
                            List<Section> sections = new List<Section>(messageManager.PullSectionsRequest.Randomize());

                            if (sections.Count > 0)
                            {
                                // PushProfiles
                                {
                                    var profiles = new List<SectionProfile>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var section in sections)
                                        {
                                            foreach (var item in _settings.GetProfiles(section).Randomize())
                                            {
                                                DateTime creationTime;

                                                if (!messageManager.PushProfiles.TryGetValue(item.Certificate.ToString(), out creationTime)
                                                    || item.CreationTime > creationTime)
                                                {
                                                    profiles.Add(item);

                                                    if (profiles.Count >= 256) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in profiles)
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                _settings.RemoveProfile(item);
                                            }
                                        }

                                        connectionManager.PushProfiles(profiles, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push Profiles ({0})", profiles.Count));
                                        _pushProfileCount += profiles.Count;

                                        foreach (var item in profiles)
                                        {
                                            messageManager.PushProfiles.Add(item.Certificate.ToString(), item.CreationTime);
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

                                // PushDocumentPages
                                {
                                    var documents = new List<DocumentPage>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var section in sections)
                                        {
                                            foreach (var item in _settings.GetDocumentPages(section).Randomize())
                                            {
                                                DateTime creationTime;

                                                if (!messageManager.PushDocumentPages.TryGetValue(item.Certificate.ToString(), out creationTime)
                                                    || item.CreationTime > creationTime)
                                                {
                                                    documents.Add(item);

                                                    if (documents.Count >= 32) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in documents)
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                _settings.RemoveDocumentPage(item);
                                            }
                                        }

                                        connectionManager.PushDocumentPages(documents, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push DocumentPages ({0})", documents.Count));
                                        _pushDocumentPageCount += documents.Count;

                                        foreach (var item in documents)
                                        {
                                            messageManager.PushDocumentPages.Add(item.Certificate.ToString(), item.CreationTime);
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
                                    var votes = new List<DocumentOpinion>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var section in sections)
                                        {
                                            foreach (var item in _settings.GetDocumentOpinions(section).Randomize())
                                            {
                                                var key = new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm);

                                                if (!messageManager.PushDocumentOpinions.Contains(key))
                                                {
                                                    votes.Add(item);

                                                    if (votes.Count >= 32) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in votes)
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                _settings.RemoveDocumentOpinion(item);
                                            }
                                        }

                                        connectionManager.PushDocumentOpinions(votes, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push DocumentOpinions ({0})", votes.Count));
                                        _pushDocumentOpinionCount += votes.Count;

                                        foreach (var item in votes)
                                        {
                                            messageManager.PushDocumentOpinions.Add(new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm));
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
                            var channels = new List<Chat>(messageManager.PullChatsRequest.Randomize());

                            if (channels.Count > 0)
                            {
                                // PushTopics
                                {
                                    var topics = new List<ChatTopic>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var channel in channels)
                                        {
                                            foreach (var item in _settings.GetTopics(channel).Randomize())
                                            {
                                                DateTime creationTime;

                                                if (!messageManager.PushTopics.TryGetValue(item.Certificate.ToString(), out creationTime)
                                                    || item.CreationTime > creationTime)
                                                {
                                                    topics.Add(item);

                                                    if (topics.Count >= 8) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in topics)
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                _settings.RemoveTopic(item);
                                            }
                                        }

                                        connectionManager.PushTopics(topics, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push Topics ({0})", topics.Count));
                                        _pushTopicCount += topics.Count;

                                        foreach (var item in topics)
                                        {
                                            messageManager.PushTopics.Add(item.Certificate.ToString(), item.CreationTime);
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

                                // PushMessages
                                {
                                    var messages = new List<ChatMessage>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var channel in channels)
                                        {
                                            foreach (var item in _settings.GetMessages(channel).Randomize())
                                            {
                                                var key = new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm);

                                                if (!messageManager.PushMessages.Contains(key))
                                                {
                                                    messages.Add(item);

                                                    if (messages.Count >= 256) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in messages)
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                _settings.RemoveMessage(item);
                                            }
                                        }

                                        connectionManager.PushMessages(messages, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push Messages ({0})", messages.Count));
                                        _pushMessageCount += messages.Count;

                                        foreach (var item in messages)
                                        {
                                            messageManager.PushMessages.Add(new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm));
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
                            var signatures = new List<string>(messageManager.PullSignaturesRequest.Randomize());

                            if (signatures.Count > 0)
                            {
                                // PushMailMessages
                                {
                                    var mails = new List<MailMessage>();

                                    lock (this.ThisLock)
                                    {
                                        foreach (var signature in signatures)
                                        {
                                            foreach (var item in _settings.GetMailMessages(signature).Randomize())
                                            {
                                                var key = new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm);

                                                if (!messageManager.PushMailMessages.Contains(key))
                                                {
                                                    mails.Add(item);

                                                    if (mails.Count >= 256) goto End;
                                                }
                                            }
                                        }
                                    }

                                End: ;

                                    var contents = new List<ArraySegment<byte>>();

                                    try
                                    {
                                        foreach (var item in mails)
                                        {
                                            try
                                            {
                                                contents.Add(_cacheManager[item.Content]);
                                            }
                                            catch (Exception)
                                            {
                                                _settings.RemoveMailMessage(item);
                                            }
                                        }

                                        connectionManager.PushMailMessages(mails, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push MailMessages ({0})", mails.Count));
                                        _pushMailMessageCount += mails.Count;

                                        foreach (var item in mails)
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

        private void connectionManager_PullSectionsRequestEvent(object sender, PullSectionsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Sections == null
                || messageManager.PullSectionsRequest.Count > _maxRequestCount * 60) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SectionsRequest ({0})", e.Sections.Count()));

            foreach (var c in e.Sections.Take(_maxRequestCount))
            {
                if (c == null || c.Id == null || string.IsNullOrWhiteSpace(c.Name)) continue;

                messageManager.PullSectionsRequest.Add(c);
                _pullSectionRequestCount++;
            }
        }

        void connectionManager_PullProfilesEvent(object sender, PullProfilesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.Profiles == null || e.Contents == null) return;

                var profileList = e.Profiles.ToList();
                var contentList = e.Contents.ToList();

                if (profileList.Count != contentList.Count) return;

                int priority = 0;

                Debug.WriteLine(string.Format("ConnectionManager: Pull Profiles ({0})", profileList.Count));

                for (int i = 0; i < profileList.Count && i < _maxContentCount; i++)
                {
                    var profile = profileList[i];
                    var content = contentList[i];

                    if (_settings.SetProfile(profile))
                    {
                        try
                        {
                            _cacheManager[profile.Content] = content;

                            if (_trustSignatures.Contains(profile.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveProfile(profile);
                        }
                    }

                    messageManager.PushProfiles[profile.Certificate.ToString()] = profile.CreationTime;
                    _pullProfileCount++;
                }

                messageManager.Priority += (priority + (priority - profileList.Count));
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

        void connectionManager_PullDocumentPagesEvent(object sender, PullDocumentPagesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.DocumentPages == null || e.Contents == null) return;

                var documentList = e.DocumentPages.ToList();
                var contentList = e.Contents.ToList();

                if (documentList.Count != contentList.Count) return;

                int priority = 0;

                Debug.WriteLine(string.Format("ConnectionManager: Pull DocumentPages ({0})", documentList.Count));

                for (int i = 0; i < documentList.Count && i < _maxContentCount; i++)
                {
                    var profile = documentList[i];
                    var content = contentList[i];

                    if (_settings.SetDocumentPage(profile))
                    {
                        try
                        {
                            _cacheManager[profile.Content] = content;

                            if (_trustSignatures.Contains(profile.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveDocumentPage(profile);
                        }
                    }

                    messageManager.PushDocumentPages[profile.Certificate.ToString()] = profile.CreationTime;
                    _pullDocumentPageCount++;
                }

                messageManager.Priority += (priority + (priority - documentList.Count));
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

        void connectionManager_PullDocumentOpinionsEvent(object sender, PullDocumentOpinionsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.DocumentOpinions == null || e.Contents == null) return;

                var voteList = e.DocumentOpinions.ToList();
                var contentList = e.Contents.ToList();

                if (voteList.Count != contentList.Count) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull DocumentOpinions ({0})", voteList.Count));

                int priority = 0;

                for (int i = 0; i < voteList.Count && i < _maxContentCount; i++)
                {
                    var mail = voteList[i];
                    var content = contentList[i];

                    if (_settings.SetDocumentOpinion(mail))
                    {
                        try
                        {
                            _cacheManager[mail.Content] = content;

                            if (_trustSignatures.Contains(mail.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveDocumentOpinion(mail);
                        }
                    }

                    var key = new Key(mail.GetHash(_hashAlgorithm), _hashAlgorithm);

                    messageManager.PushDocumentOpinions.Add(key);
                    _pullDocumentOpinionCount++;
                }

                messageManager.Priority += (priority + (priority - voteList.Count));
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

        void connectionManager_PullTopicsEvent(object sender, PullTopicsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.Topics == null || e.Contents == null) return;

                var topicList = e.Topics.ToList();
                var contentList = e.Contents.ToList();

                if (topicList.Count != contentList.Count) return;

                int priority = 0;

                Debug.WriteLine(string.Format("ConnectionManager: Pull Topics ({0})", topicList.Count));

                for (int i = 0; i < topicList.Count && i < _maxContentCount; i++)
                {
                    var topic = topicList[i];
                    var content = contentList[i];

                    if (_settings.SetTopic(topic))
                    {
                        try
                        {
                            _cacheManager[topic.Content] = content;

                            if (_trustSignatures.Contains(topic.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveTopic(topic);
                        }
                    }

                    messageManager.PushTopics[topic.Certificate.ToString()] = topic.CreationTime;
                    _pullTopicCount++;
                }

                messageManager.Priority += (priority + (priority - topicList.Count));
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

        void connectionManager_PullMessagesEvent(object sender, PullMessagesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.Messages == null || e.Contents == null) return;

                var messageList = e.Messages.ToList();
                var contentList = e.Contents.ToList();

                if (messageList.Count != contentList.Count) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull Messages ({0})", messageList.Count));

                int priority = 0;

                for (int i = 0; i < messageList.Count && i < _maxContentCount; i++)
                {
                    var message = messageList[i];
                    var content = contentList[i];

                    if (_settings.SetMessage(message))
                    {
                        try
                        {
                            _cacheManager[message.Content] = content;

                            if (_trustSignatures.Contains(message.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveMessage(message);
                        }
                    }

                    var key = new Key(message.GetHash(_hashAlgorithm), _hashAlgorithm);

                    messageManager.PushMessages.Add(key);
                    _pullMessageCount++;
                }

                messageManager.Priority += (priority + (priority - messageList.Count));
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

        void connectionManager_PullSignaturesRequestEvent(object sender, PullSignaturesRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Signatures == null
                || messageManager.PullSignaturesRequest.Count > _maxRequestCount * 60) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SignaturesRequest {0} ({1})", String.Join(", ", e.Signatures), e.Signatures.Count()));

            foreach (var s in e.Signatures.Take(_maxRequestCount))
            {
                if (s == null || !Signature.HasSignature(s)) continue;

                messageManager.PullSignaturesRequest.Add(s);
                _pullSignatureRequestCount++;
            }
        }

        void connectionManager_PullMailMessagesEvent(object sender, PullMailMessagesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.MailMessages == null || e.Contents == null) return;

                var mailList = e.MailMessages.ToList();
                var contentList = e.Contents.ToList();

                if (mailList.Count != contentList.Count) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull MailMessages ({0})", mailList.Count));

                int priority = 0;

                for (int i = 0; i < mailList.Count && i < _maxContentCount; i++)
                {
                    var mail = mailList[i];
                    var content = contentList[i];

                    if (_settings.SetMailMessage(mail))
                    {
                        try
                        {
                            _cacheManager[mail.Content] = content;

                            if (_trustSignatures.Contains(mail.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveMailMessage(mail);
                        }
                    }

                    var key = new Key(mail.GetHash(_hashAlgorithm), _hashAlgorithm);

                    messageManager.PushMailMessages.Add(key);
                    _pullMailMessageCount++;
                }

                messageManager.Priority += (priority + (priority - mailList.Count));
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

        public void SendSectionRequest(Section section)
        {
            lock (this.ThisLock)
            {
                _pushSectionsRequestList.Add(section);
            }
        }

        public void SendChatRequest(Chat channel)
        {
            lock (this.ThisLock)
            {
                _pushChatsRequestList.Add(channel);
            }
        }

        public void SendSignatureRequest(string signature)
        {
            lock (this.ThisLock)
            {
                _pushSignaturesRequestList.Add(signature);
            }
        }

        public IEnumerable<Section> GetSections()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetSections();
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

        public IEnumerable<string> GetSignatures()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetSignatures();
            }
        }

        public IEnumerable<SectionProfile> GetProfiles(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetProfiles(section);
            }
        }

        public IEnumerable<DocumentPage> GetDocumentPages(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetDocumentPages(section);
            }
        }

        public IEnumerable<DocumentOpinion> GetDocumentOpinions(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetDocumentOpinions(section);
            }
        }

        public IEnumerable<ChatTopic> GetTopics(Chat channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetTopics(channel);
            }
        }

        public IEnumerable<ChatMessage> GetMessages(Chat channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetMessages(channel);
            }
        }

        public IEnumerable<MailMessage> GetMailMessages(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetMailMessages(signature);
            }
        }

        public SectionProfileContent GetContent(SectionProfile profile)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[profile.Content];

                    return ContentConverter.FromProfileContentBlock(buffer);
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

        public DocumentPageContent GetContent(DocumentPage document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[document.Content];

                    return ContentConverter.FromDocumentPageContentBlock(buffer);
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

        public ChatTopicContent GetContent(ChatTopic topic)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[topic.Content];

                    return ContentConverter.FromTopicContentBlock(buffer);
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

        public ChatMessageContent GetContent(ChatMessage message)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[message.Content];

                    return ContentConverter.FromMessageContentBlock(buffer);
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

        public MailMessageContent GetContent(MailMessage mail, IExchangeDecrypt exchangeDecrypt)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[mail.Content];

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

        public SectionProfile UploadProfile(Section section,
            IEnumerable<string> trustSignatures, IEnumerable<Chat> channels, string comment, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new SectionProfileContent(exchangeEncrypt.ExchangeAlgorithm, exchangeEncrypt.PublicKey, trustSignatures, comment, channels);
                    buffer = ContentConverter.ToProfileContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var profile = new SectionProfile(section, key, digitalSignature);

                    if (_settings.SetProfile(profile))
                    {
                        _cacheManager[key] = buffer;
                    }

                    return profile;
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

        public DocumentPage UploadDocumentPage(Section section, string name,
            string comment, HypertextFormatType formatType, string hypertext, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new DocumentPageContent(formatType, hypertext, comment);
                    buffer = ContentConverter.ToDocumentPageContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var document = new DocumentPage(section, name, key, digitalSignature);

                    if (_settings.SetDocumentPage(document))
                    {
                        _cacheManager[key] = buffer;
                    }

                    return document;
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

        public DocumentOpinion UploadDocumentOpinion(Section section,
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

                    var document = new DocumentOpinion(section, key, digitalSignature);

                    if (_settings.SetDocumentOpinion(document))
                    {
                        _cacheManager[key] = buffer;
                    }

                    return document;
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

        public ChatTopic UploadTopic(Chat channel,
            string comment, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new ChatTopicContent(comment);
                    buffer = ContentConverter.ToTopicContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var topic = new ChatTopic(channel, key, digitalSignature);

                    if (_settings.SetTopic(topic))
                    {
                        _cacheManager[key] = buffer;
                    }

                    return topic;
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

        public ChatMessage UploadMessage(Chat channel,
            string comment, IEnumerable<Key> anchors, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new ChatMessageContent(comment, anchors);
                    buffer = ContentConverter.ToMessageContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var message = new ChatMessage(channel, key, digitalSignature);

                    if (_settings.SetMessage(message))
                    {
                        _cacheManager[key] = buffer;
                    }

                    return message;
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

        public MailMessage UploadMailMessage(string recipientSignature,
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

                    var mail = new MailMessage(recipientSignature, key, digitalSignature);

                    if (_settings.SetMailMessage(mail))
                    {
                        _cacheManager[key] = buffer;
                    }

                    return mail;
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
                    new Library.Configuration.SettingsContext<Dictionary<Section, Dictionary<string, SectionProfile>>>() { Name = "Profiles", Value = new Dictionary<Section, Dictionary<string, SectionProfile>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Section, Dictionary<string, HashSet<DocumentPage>>>>() { Name = "DocumentPages", Value = new Dictionary<Section, Dictionary<string, HashSet<DocumentPage>>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Section, Dictionary<string, DocumentOpinion>>>() { Name = "DocumentOpinions", Value = new Dictionary<Section, Dictionary<string, DocumentOpinion>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Chat, Dictionary<string, ChatTopic>>>() { Name = "Topics", Value = new Dictionary<Chat, Dictionary<string, ChatTopic>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Chat, HashSet<ChatMessage>>>() { Name = "Messages", Value = new Dictionary<Chat, HashSet<ChatMessage>>() },
                    new Library.Configuration.SettingsContext<Dictionary<string, HashSet<MailMessage>>>() { Name = "MailMessages", Value = new Dictionary<string, HashSet<MailMessage>>() },
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

                        contexts.Add(new InformationContext("SectionCount", this.GetSections().Count()));
                        contexts.Add(new InformationContext("ProfileCount", this.Profiles.Values.Sum(n => n.Count)));
                        contexts.Add(new InformationContext("DocumentPageCount", this.DocumentPages.Values.Sum(n => n.Values.Sum(m => m.Count))));
                        contexts.Add(new InformationContext("DocumentOpinionCount", this.DocumentOpinions.Values.Sum(n => n.Count)));

                        contexts.Add(new InformationContext("ChatCount", this.GetChats().Count()));
                        contexts.Add(new InformationContext("TopicCount", this.Topics.Values.Sum(n => n.Count)));
                        contexts.Add(new InformationContext("MessageCount", this.Messages.Sum(n => n.Value.Count)));

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

            public IEnumerable<Section> GetSections()
            {
                lock (_thisLock)
                {
                    HashSet<Section> hashset = new HashSet<Section>();

                    hashset.UnionWith(this.Profiles.Keys);
                    hashset.UnionWith(this.DocumentPages.Keys);
                    hashset.UnionWith(this.DocumentOpinions.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Chat> GetChats()
            {
                lock (_thisLock)
                {
                    HashSet<Chat> hashset = new HashSet<Chat>();

                    hashset.UnionWith(this.Topics.Keys);
                    hashset.UnionWith(this.Messages.Keys);

                    return hashset;
                }
            }

            public IEnumerable<string> GetSignatures()
            {
                lock (_thisLock)
                {
                    return this.MailMessages.Keys.ToArray();
                }
            }

            public void RemoveSections(IEnumerable<Section> sections)
            {
                lock (_thisLock)
                {
                    foreach (var section in sections)
                    {
                        this.Profiles.Remove(section);
                        this.DocumentPages.Remove(section);
                        this.DocumentOpinions.Remove(section);
                    }
                }
            }

            public void RemoveChats(IEnumerable<Chat> channels)
            {
                lock (_thisLock)
                {
                    foreach (var channel in channels)
                    {
                        this.Topics.Remove(channel);
                        this.Messages.Remove(channel);
                    }
                }
            }

            public void RemoveSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        this.MailMessages.Remove(signature);
                    }
                }
            }

            public IEnumerable<SectionProfile> GetProfiles(Section section)
            {
                lock (_thisLock)
                {
                    Dictionary<string, SectionProfile> dic = null;

                    if (this.Profiles.TryGetValue(section, out dic))
                    {
                        return dic.Values;
                    }

                    return new SectionProfile[0];
                }
            }

            public IEnumerable<DocumentPage> GetDocumentPages(Section section, string name)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<DocumentPage>> dic = null;

                    if (this.DocumentPages.TryGetValue(section, out dic))
                    {
                        HashSet<DocumentPage> hashset = null;

                        if (dic.TryGetValue(name, out hashset))
                        {
                            return hashset;
                        }
                    }

                    return new DocumentPage[0];
                }
            }

            public IEnumerable<DocumentPage> GetDocumentPages(Section section)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<DocumentPage>> dic = null;

                    if (this.DocumentPages.TryGetValue(section, out dic))
                    {
                        List<DocumentPage> list = new List<DocumentPage>();

                        foreach (var hashset in dic.Values)
                        {
                            list.AddRange(hashset);
                        }
                    }

                    return new DocumentPage[0];
                }
            }

            public IEnumerable<DocumentOpinion> GetDocumentOpinions(Section section)
            {
                lock (_thisLock)
                {
                    Dictionary<string, DocumentOpinion> dic = null;

                    if (this.DocumentOpinions.TryGetValue(section, out dic))
                    {
                        return dic.Values;
                    }

                    return new DocumentOpinion[0];
                }
            }

            public IEnumerable<ChatTopic> GetTopics(Chat channel)
            {
                lock (_thisLock)
                {
                    Dictionary<string, ChatTopic> dic = null;

                    if (this.Topics.TryGetValue(channel, out dic))
                    {
                        return dic.Values;
                    }

                    return new ChatTopic[0];
                }
            }

            public IEnumerable<ChatMessage> GetMessages(Chat channel)
            {
                lock (_thisLock)
                {
                    HashSet<ChatMessage> hashset = null;

                    if (this.Messages.TryGetValue(channel, out hashset))
                    {
                        return hashset;
                    }

                    return new ChatMessage[0];
                }
            }

            public IEnumerable<MailMessage> GetMailMessages(string signature)
            {
                lock (_thisLock)
                {
                    HashSet<MailMessage> hashset = null;

                    if (this.MailMessages.TryGetValue(signature, out hashset))
                    {
                        return hashset;
                    }

                    return new MailMessage[0];
                }
            }

            public bool SetProfile(SectionProfile profile)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (profile == null || profile.Section == null || profile.Section.Id == null || profile.Section.Id.Length == 0 || string.IsNullOrWhiteSpace(profile.Section.Name)
                        || (profile.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || profile.Certificate == null || !profile.VerifyCertificate()) return false;

                    var signature = profile.Certificate.ToString();

                    Dictionary<string, SectionProfile> dic = null;

                    if (!this.Profiles.TryGetValue(profile.Section, out dic))
                    {
                        dic = new Dictionary<string, SectionProfile>();
                        this.Profiles[profile.Section] = dic;

                        dic[signature] = profile;

                        return true;
                    }

                    SectionProfile tempProfile = null;

                    if (!dic.TryGetValue(signature, out tempProfile)
                        || profile.CreationTime > tempProfile.CreationTime)
                    {
                        dic[signature] = profile;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetDocumentPage(DocumentPage document)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (document == null || document.Section == null || document.Section.Id == null || document.Section.Id.Length == 0 || string.IsNullOrWhiteSpace(document.Section.Name)
                        || (document.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || document.Certificate == null || !document.VerifyCertificate()) return false;

                    var signature = document.Certificate.ToString();

                    Dictionary<string, HashSet<DocumentPage>> dic = null;
                    HashSet<DocumentPage> hashset = null;

                    if (!this.DocumentPages.TryGetValue(document.Section, out dic))
                    {
                        dic = new Dictionary<string, HashSet<DocumentPage>>();
                        this.DocumentPages[document.Section] = dic;

                        hashset = new HashSet<DocumentPage>();
                        hashset.Add(document);
                        dic[document.Name] = hashset;

                        return true;
                    }

                    if (!dic.TryGetValue(document.Name, out hashset))
                    {
                        hashset = new HashSet<DocumentPage>();
                        hashset.Add(document);
                        dic[document.Name] = hashset;

                        return true;
                    }

                    var currentDocumentPage = hashset.FirstOrDefault(n => signature == n.Certificate.ToString());

                    if (currentDocumentPage == null)
                    {
                        hashset.Add(document);

                        return true;
                    }

                    if (currentDocumentPage.CreationTime < document.CreationTime)
                    {
                        hashset.Remove(currentDocumentPage);
                        hashset.Add(document);

                        return true;
                    }

                    return false;
                }
            }

            public bool SetDocumentOpinion(DocumentOpinion vote)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (vote == null || vote.Section == null || vote.Section.Id == null || vote.Section.Id.Length == 0 || string.IsNullOrWhiteSpace(vote.Section.Name)
                        || (vote.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || vote.Certificate == null || !vote.VerifyCertificate()) return false;

                    var signature = vote.Certificate.ToString();

                    Dictionary<string, DocumentOpinion> dic = null;

                    if (!this.DocumentOpinions.TryGetValue(vote.Section, out dic))
                    {
                        dic = new Dictionary<string, DocumentOpinion>();
                        this.DocumentOpinions[vote.Section] = dic;

                        dic[signature] = vote;

                        return true;
                    }

                    DocumentOpinion tempDocumentOpinion = null;

                    if (!dic.TryGetValue(signature, out tempDocumentOpinion)
                        || vote.CreationTime > tempDocumentOpinion.CreationTime)
                    {
                        dic[signature] = vote;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetTopic(ChatTopic topic)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (topic == null || topic.Chat == null || topic.Chat.Id == null || topic.Chat.Id.Length == 0 || string.IsNullOrWhiteSpace(topic.Chat.Name)
                        || (topic.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || topic.Certificate == null || !topic.VerifyCertificate()) return false;

                    var signature = topic.Certificate.ToString();

                    Dictionary<string, ChatTopic> dic = null;

                    if (!this.Topics.TryGetValue(topic.Chat, out dic))
                    {
                        dic = new Dictionary<string, ChatTopic>();
                        this.Topics[topic.Chat] = dic;

                        dic[signature] = topic;

                        return true;
                    }

                    ChatTopic tempTopic = null;

                    if (!dic.TryGetValue(signature, out tempTopic)
                        || topic.CreationTime > tempTopic.CreationTime)
                    {
                        dic[signature] = topic;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetMessage(ChatMessage message)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (message == null || message.Chat == null || message.Chat.Id == null || message.Chat.Id.Length == 0 || string.IsNullOrWhiteSpace(message.Chat.Name)
                        || (now - message.CreationTime) > new TimeSpan(64, 0, 0, 0)
                        || (message.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || message.Certificate == null || !message.VerifyCertificate()) return false;

                    HashSet<ChatMessage> hashset = null;

                    if (!this.Messages.TryGetValue(message.Chat, out hashset))
                    {
                        hashset = new HashSet<ChatMessage>();
                        this.Messages[message.Chat] = hashset;
                    }

                    return hashset.Add(message);
                }
            }

            public bool SetMailMessage(MailMessage mail)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (mail == null || !Signature.HasSignature(mail.RecipientSignature)
                        || (now - mail.CreationTime) > new TimeSpan(32, 0, 0, 0)
                        || (mail.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || mail.Certificate == null || !mail.VerifyCertificate()) return false;

                    HashSet<MailMessage> hashset = null;

                    if (!this.MailMessages.TryGetValue(mail.RecipientSignature, out hashset))
                    {
                        hashset = new HashSet<MailMessage>();
                        this.MailMessages[mail.RecipientSignature] = hashset;
                    }

                    return hashset.Add(mail);
                }
            }

            public void RemoveProfile(SectionProfile profile)
            {
                lock (_thisLock)
                {
                    var signature = profile.Certificate.ToString();

                    Dictionary<string, SectionProfile> dic = null;

                    if (this.Profiles.TryGetValue(profile.Section, out dic))
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            this.Profiles.Remove(profile.Section);
                        }
                    }
                }
            }

            public void RemoveDocumentPage(DocumentPage document)
            {
                lock (_thisLock)
                {
                    var signature = document.Certificate.ToString();

                    Dictionary<string, HashSet<DocumentPage>> dic = null;

                    if (this.DocumentPages.TryGetValue(document.Section, out dic))
                    {
                        HashSet<DocumentPage> hashset = null;

                        if (dic.TryGetValue(document.Name, out hashset))
                        {
                            hashset.Remove(document);

                            if (hashset.Count == 0)
                            {
                                dic.Remove(document.Name);
                            }
                        }

                        if (dic.Count == 0)
                        {
                            this.DocumentPages.Remove(document.Section);
                        }
                    }
                }
            }

            public void RemoveDocumentOpinion(DocumentOpinion vote)
            {
                lock (_thisLock)
                {
                    var signature = vote.Certificate.ToString();

                    Dictionary<string, DocumentOpinion> dic = null;

                    if (this.DocumentOpinions.TryGetValue(vote.Section, out dic))
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            this.DocumentOpinions.Remove(vote.Section);
                        }
                    }
                }
            }

            public void RemoveTopic(ChatTopic topic)
            {
                lock (_thisLock)
                {
                    var signature = topic.Certificate.ToString();

                    Dictionary<string, ChatTopic> dic = null;

                    if (this.Topics.TryGetValue(topic.Chat, out dic))
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            this.Topics.Remove(topic.Chat);
                        }
                    }
                }
            }

            public void RemoveMessage(ChatMessage message)
            {
                lock (_thisLock)
                {
                    HashSet<ChatMessage> hashset = null;

                    if (this.Messages.TryGetValue(message.Chat, out hashset))
                    {
                        hashset.Remove(message);

                        if (hashset.Count == 0)
                        {
                            this.Messages.Remove(message.Chat);
                        }
                    }
                }
            }

            public void RemoveMailMessage(MailMessage mail)
            {
                lock (_thisLock)
                {
                    HashSet<MailMessage> hashset = null;

                    if (this.MailMessages.TryGetValue(mail.RecipientSignature, out hashset))
                    {
                        hashset.Remove(mail);

                        if (hashset.Count == 0)
                        {
                            this.MailMessages.Remove(mail.RecipientSignature);
                        }
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

            private Dictionary<Section, Dictionary<string, SectionProfile>> Profiles
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Section, Dictionary<string, SectionProfile>>)this["Profiles"];
                    }
                }
            }

            private Dictionary<Section, Dictionary<string, HashSet<DocumentPage>>> DocumentPages
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Section, Dictionary<string, HashSet<DocumentPage>>>)this["DocumentPages"];
                    }
                }
            }

            private Dictionary<Section, Dictionary<string, DocumentOpinion>> DocumentOpinions
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Section, Dictionary<string, DocumentOpinion>>)this["DocumentOpinions"];
                    }
                }
            }

            private Dictionary<Chat, Dictionary<string, ChatTopic>> Topics
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Chat, Dictionary<string, ChatTopic>>)this["Topics"];
                    }
                }
            }

            private Dictionary<Chat, HashSet<ChatMessage>> Messages
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Chat, HashSet<ChatMessage>>)this["Messages"];
                    }
                }
            }

            private Dictionary<string, HashSet<MailMessage>> MailMessages
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<string, HashSet<MailMessage>>)this["MailMessages"];
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
