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
    public delegate IEnumerable<Channel> LockChannelsEventHandler(object sender);

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
        private LockedDictionary<Node, HashSet<Channel>> _pushChannelsRequestDictionary = new LockedDictionary<Node, HashSet<Channel>>();
        private LockedDictionary<Node, HashSet<string>> _pushSignaturesRequestDictionary = new LockedDictionary<Node, HashSet<string>>();

        private LockedHashSet<string> _trustSignatures = new LockedHashSet<string>();

        private LockedList<Node> _creatingNodes;
        private CirculationCollection<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private CirculationDictionary<Node, int> _nodesStatus;

        private CirculationCollection<Section> _pushSectionsRequestList;
        private CirculationCollection<Channel> _pushChannelsRequestList;
        private CirculationCollection<string> _pushSignaturesRequestList;

        private LockedDictionary<Section, DateTime> _lastUsedSectionTimes = new LockedDictionary<Section, DateTime>();
        private LockedDictionary<Channel, DateTime> _lastUsedChannelTimes = new LockedDictionary<Channel, DateTime>();
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
        private volatile int _pushDocumentCount;
        private volatile int _pushChannelRequestCount;
        private volatile int _pushTopicCount;
        private volatile int _pushMessageCount;
        private volatile int _pushSignatureRequestCount;
        private volatile int _pushMailCount;

        private volatile int _pullNodeCount;
        private volatile int _pullSectionRequestCount;
        private volatile int _pullProfileCount;
        private volatile int _pullDocumentCount;
        private volatile int _pullChannelRequestCount;
        private volatile int _pullTopicCount;
        private volatile int _pullMessageCount;
        private volatile int _pullSignatureRequestCount;
        private volatile int _pullMailCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        private TrustSignaturesEventHandler _trustSignaturesEvent;
        private LockSectionsEventHandler _lockSectionsEvent;
        private LockChannelsEventHandler _lockChannelsEvent;

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
            _pushChannelsRequestList = new CirculationCollection<Channel>(new TimeSpan(0, 3, 0));
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

        public LockChannelsEventHandler LockChannelsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockChannelsEvent = value;
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
                    contexts.Add(new InformationContext("PushDocumentCount", _pushDocumentCount));
                    contexts.Add(new InformationContext("PushChannelRequestCount", _pushChannelRequestCount));
                    contexts.Add(new InformationContext("PushTopicCount", _pushTopicCount));
                    contexts.Add(new InformationContext("PushMessageCount", _pushMessageCount));
                    contexts.Add(new InformationContext("PushMailCount", _pushMailCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PullSectionRequestCount", _pullSectionRequestCount));
                    contexts.Add(new InformationContext("PullProfileCount", _pullProfileCount));
                    contexts.Add(new InformationContext("PullDocumentCount", _pullDocumentCount));
                    contexts.Add(new InformationContext("PullChannelRequestCount", _pullChannelRequestCount));
                    contexts.Add(new InformationContext("PullTopicCount", _pullTopicCount));
                    contexts.Add(new InformationContext("PullMessageCount", _pullMessageCount));
                    contexts.Add(new InformationContext("PullMailCount", _pullMailCount));

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

        protected virtual IEnumerable<Channel> OnLockChannelsEvent()
        {
            if (_lockChannelsEvent != null)
            {
                return _lockChannelsEvent(this);
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
                connectionManager.PullDocumentsEvent += new PullDocumentsEventHandler(connectionManager_PullDocumentsEvent);
                connectionManager.PullChannelsRequestEvent += new PullChannelsRequestEventHandler(connectionManager_PullChannelsRequestEvent);
                connectionManager.PullTopicsEvent += new PullTopicsEventHandler(connectionManager_PullTopicsEvent);
                connectionManager.PullMessagesEvent += new PullMessagesEventHandler(connectionManager_PullMessagesEvent);
                connectionManager.PullSignaturesRequestEvent += new PullSignaturesRequestEventHandler(connectionManager_PullSignaturesRequestEvent);
                connectionManager.PullMailsEvent += new PullMailsEventHandler(connectionManager_PullMailsEvent);
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

                        {
                            _removeNodes.Add(node);
                            _cuttingNodes.Remove(node);

                            if (_routeTable.Count > _routeTableMinCount)
                            {
                                _routeTable.Remove(node);
                            }
                        }
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
                        lock (_settings.ThisLock)
                        {
                            {
                                var cacheKeys = new HashSet<Key>(_cacheManager.ToArray());

                                foreach (var section in _settings.GetSections())
                                {
                                    foreach (var item in _settings.GetProfiles(section))
                                    {
                                        if (!cacheKeys.Contains(item.Content)) _settings.RemoveProfile(item);
                                    }

                                    foreach (var item in _settings.GetDocuments(section))
                                    {
                                        if (!cacheKeys.Contains(item.Content)) _settings.RemoveDocument(item);
                                    }
                                }

                                foreach (var channel in _settings.GetChannels())
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
                                    foreach (var item in _settings.GetMails(signature))
                                    {
                                        if (!cacheKeys.Contains(item.Content)) _settings.RemoveMail(item);
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

                                    foreach (var item in _settings.GetDocuments(section))
                                    {
                                        linkKeys.Add(item.Content);
                                    }
                                }

                                foreach (var channel in _settings.GetChannels())
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
                                    foreach (var item in _settings.GetMails(signature))
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
                                var lockChannels = this.OnLockChannelsEvent();

                                if (lockChannels != null)
                                {
                                    var removeChannels = new HashSet<Channel>();
                                    removeChannels.UnionWith(_settings.GetChannels());
                                    removeChannels.ExceptWith(lockChannels);

                                    var sortList = removeChannels.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedChannelTimes.TryGetValue(x, out tx);
                                        _lastUsedChannelTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.RemoveChannels(sortList.Take(sortList.Count - 1024));

                                    var liveChannels = new HashSet<Channel>(_settings.GetChannels());

                                    foreach (var signature in _lastUsedChannelTimes.Keys.ToArray())
                                    {
                                        if (liveChannels.Contains(signature)) continue;

                                        _lastUsedChannelTimes.Remove(signature);
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
                                        lock (_settings.ThisLock)
                                        {
                                            var removeProfiles = new List<Profile>();
                                            var removeDocuments = new List<Document>();
                                            var removeTopics = new List<Topic>();
                                            var removeMessages = new List<Message>();
                                            var removeMails = new List<Mail>();

                                            foreach (var section in _settings.GetSections())
                                            {
                                                {
                                                    var untrustProfiles = new List<Profile>();

                                                    foreach (var item in _settings.GetProfiles(section))
                                                    {
                                                        if (lockSignatureHashset.Contains(item.Certificate.ToString())) continue;

                                                        untrustProfiles.Add(item);
                                                    }

                                                    removeProfiles.AddRange(untrustProfiles.Randomize().Take(untrustProfiles.Count - 256));
                                                }

                                                {
                                                    var untrustDocuments = new List<Document>();

                                                    foreach (var item in _settings.GetDocuments(section))
                                                    {
                                                        if (lockSignatureHashset.Contains(item.Certificate.ToString())) continue;

                                                        untrustDocuments.Add(item);
                                                    }

                                                    removeDocuments.AddRange(untrustDocuments.Randomize().Take(untrustDocuments.Count - 256));
                                                }
                                            }

                                            foreach (var channel in _settings.GetChannels())
                                            {
                                                {
                                                    var trustTopics = new List<Topic>();
                                                    var untrustTopics = new List<Topic>();

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

                                                {
                                                    var trustMessages = new List<Message>();
                                                    var untrustMessages = new List<Message>();

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

                                                    if (untrustMessages.Count > 64)
                                                    {
                                                        untrustMessages.Sort((x, y) =>
                                                        {
                                                            return x.CreationTime.CompareTo(y);
                                                        });

                                                        removeMessages.AddRange(untrustMessages.Take(untrustMessages.Count - 64));
                                                    }
                                                }
                                            }

                                            foreach (var signature in _settings.GetSignatures())
                                            {
                                                {
                                                    var trustMailDic = new Dictionary<string, List<Mail>>();
                                                    var untrustMailDic = new Dictionary<string, List<Mail>>();

                                                    foreach (var item in _settings.GetMails(signature))
                                                    {
                                                        if ((now - item.CreationTime) > new TimeSpan(32, 0, 0, 0))
                                                        {
                                                            removeMails.Add(item);
                                                        }
                                                        else
                                                        {
                                                            var creatorsignature = item.Certificate.ToString();

                                                            if (lockSignatureHashset.Contains(creatorsignature))
                                                            {
                                                                List<Mail> list;

                                                                if (trustMailDic.TryGetValue(creatorsignature, out list))
                                                                {
                                                                    list = new List<Mail>();
                                                                    trustMailDic[creatorsignature] = list;
                                                                }

                                                                list.Add(item);
                                                            }
                                                            else
                                                            {
                                                                List<Mail> list;

                                                                if (untrustMailDic.TryGetValue(creatorsignature, out list))
                                                                {
                                                                    list = new List<Mail>();
                                                                    untrustMailDic[creatorsignature] = list;
                                                                }

                                                                list.Add(item);
                                                            }
                                                        }
                                                    }

                                                    foreach (var list in trustMailDic.Values)
                                                    {
                                                        list.Sort((x, y) =>
                                                        {
                                                            return x.CreationTime.CompareTo(y);
                                                        });

                                                        removeMails.AddRange(list.Take(list.Count - 8));
                                                    }

                                                    int i = 0;

                                                    foreach (var list in untrustMailDic.Values.Randomize())
                                                    {
                                                        if (i < 64)
                                                        {
                                                            list.Sort((x, y) =>
                                                            {
                                                                return x.CreationTime.CompareTo(y);
                                                            });

                                                            removeMails.AddRange(list.Take(list.Count - 2));
                                                        }
                                                        else
                                                        {
                                                            removeMails.AddRange(list);
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

                                            foreach (var item in removeDocuments)
                                            {
                                                _settings.RemoveDocument(item);
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

                                            foreach (var item in removeMails)
                                            {
                                                _settings.RemoveMail(item);
                                                _cacheManager.Remove(item.Content);
                                            }
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

                    foreach (var item in _settings.GetChannels())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullChannelsRequest.Add(item);
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
                    HashSet<Channel> pushChannelsRequestList = new HashSet<Channel>();
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
                            var list = _pushChannelsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushChannelsRequest.Contains(list[i])))
                                {
                                    pushChannelsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullChannelsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 32 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushChannelsRequest.Contains(list[i])))
                                {
                                    pushChannelsRequestList.Add(list[i]);
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
                        Dictionary<Node, HashSet<Channel>> pushChannelsRequestDictionary = new Dictionary<Node, HashSet<Channel>>();

                        foreach (var item in pushChannelsRequestList)
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 1));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Channel> hashset;

                                    if (!pushChannelsRequestDictionary.TryGetValue(requestNodes[i], out hashset))
                                    {
                                        hashset = new HashSet<Channel>();
                                        pushChannelsRequestDictionary[requestNodes[i]] = hashset;
                                    }

                                    hashset.Add(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushChannelsRequestDictionary.ThisLock)
                        {
                            _pushChannelsRequestDictionary.Clear();

                            foreach (var item in pushChannelsRequestDictionary)
                            {
                                _pushChannelsRequestDictionary.Add(item.Key, item.Value);
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

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
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

                            // PushChannelsRequest
                            {
                                ChannelCollection tempList = null;
                                int count = (int)(_maxRequestCount * this.ResponseTimePriority(connectionManager.Node));

                                lock (_pushChannelsRequestDictionary.ThisLock)
                                {
                                    HashSet<Channel> hashset;

                                    if (_pushChannelsRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                    {
                                        tempList = new ChannelCollection(hashset.Randomize().Take(count));

                                        hashset.ExceptWith(tempList);
                                        messageManager.PushChannelsRequest.AddRange(tempList);
                                    }
                                }

                                if (tempList != null && tempList.Count > 0)
                                {
                                    try
                                    {
                                        connectionManager.PushChannelsRequest(tempList);

                                        foreach (var item in tempList)
                                        {
                                            _pushChannelsRequestList.Remove(item);
                                        }

                                        Debug.WriteLine(string.Format("ConnectionManager: Push ChannelsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                        _pushChannelRequestCount += tempList.Count;
                                    }
                                    catch (Exception e)
                                    {
                                        foreach (var item in tempList)
                                        {
                                            messageManager.PushChannelsRequest.Remove(item);
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
                                    var profiles = new List<Profile>();

                                    lock (this.ThisLock)
                                    {
                                        lock (_settings.ThisLock)
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

                                        messageManager.Priority -= profiles.Count;
                                    }
                                    finally
                                    {
                                        foreach (var content in contents)
                                        {
                                            _bufferManager.ReturnBuffer(content.Array);
                                        }
                                    }
                                }

                                // PushDocuments
                                {
                                    var documents = new List<Document>();

                                    lock (this.ThisLock)
                                    {
                                        lock (_settings.ThisLock)
                                        {
                                            foreach (var section in sections)
                                            {
                                                foreach (var item in _settings.GetDocuments(section).Randomize())
                                                {
                                                    DateTime creationTime;

                                                    if (!messageManager.PushDocuments.TryGetValue(item.Certificate.ToString(), out creationTime)
                                                        || item.CreationTime > creationTime)
                                                    {
                                                        documents.Add(item);

                                                        if (documents.Count >= 1) goto End;
                                                    }
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
                                                _settings.RemoveDocument(item);
                                            }
                                        }

                                        connectionManager.PushDocuments(documents, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push Documents ({0})", documents.Count));
                                        _pushDocumentCount += documents.Count;

                                        foreach (var item in documents)
                                        {
                                            messageManager.PushDocuments.Add(item.Certificate.ToString(), item.CreationTime);
                                        }

                                        messageManager.Priority -= documents.Count;
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
                            var channels = new List<Channel>(messageManager.PullChannelsRequest.Randomize());

                            if (channels.Count > 0)
                            {
                                // PushTopics
                                {
                                    var topics = new List<Topic>();

                                    lock (this.ThisLock)
                                    {
                                        lock (_settings.ThisLock)
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

                                        messageManager.Priority -= topics.Count;
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
                                    var messages = new List<Message>();

                                    lock (this.ThisLock)
                                    {
                                        lock (_settings.ThisLock)
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

                                        messageManager.Priority -= messages.Count;
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
                                // PushMails
                                {
                                    var mails = new List<Mail>();

                                    lock (this.ThisLock)
                                    {
                                        lock (_settings.ThisLock)
                                        {
                                            foreach (var signature in signatures)
                                            {
                                                foreach (var item in _settings.GetMails(signature).Randomize())
                                                {
                                                    var key = new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm);

                                                    if (!messageManager.PushMails.Contains(key))
                                                    {
                                                        mails.Add(item);

                                                        if (mails.Count >= 256) goto End;
                                                    }
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
                                                _settings.RemoveMail(item);
                                            }
                                        }

                                        connectionManager.PushMails(mails, contents);

                                        Debug.WriteLine(string.Format("ConnectionManager: Push Mails ({0})", mails.Count));
                                        _pushMailCount += mails.Count;

                                        foreach (var item in mails)
                                        {
                                            messageManager.PushMails.Add(new Key(item.GetHash(_hashAlgorithm), _hashAlgorithm));
                                        }

                                        messageManager.Priority -= mails.Count;
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

        void connectionManager_PullDocumentsEvent(object sender, PullDocumentsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.Documents == null || e.Contents == null) return;

                var documentList = e.Documents.ToList();
                var contentList = e.Contents.ToList();

                if (documentList.Count != contentList.Count) return;

                int priority = 0;

                Debug.WriteLine(string.Format("ConnectionManager: Pull Documents ({0})", documentList.Count));

                for (int i = 0; i < documentList.Count && i < _maxContentCount; i++)
                {
                    var document = documentList[i];
                    var content = contentList[i];

                    if (_settings.SetDocument(document))
                    {
                        try
                        {
                            _cacheManager[document.Content] = content;

                            if (_trustSignatures.Contains(document.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveDocument(document);
                        }
                    }

                    messageManager.PushDocuments[document.Certificate.ToString()] = document.CreationTime;
                    _pullDocumentCount++;
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

        private void connectionManager_PullChannelsRequestEvent(object sender, PullChannelsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Channels == null
                || messageManager.PullChannelsRequest.Count > _maxRequestCount * 60) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull ChannelsRequest {0} ({1})", String.Join(", ", e.Channels), e.Channels.Count()));

            foreach (var c in e.Channels.Take(_maxRequestCount))
            {
                if (c == null || c.Id == null || string.IsNullOrWhiteSpace(c.Name)) continue;

                messageManager.PullChannelsRequest.Add(c);
                _pullChannelRequestCount++;
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

        void connectionManager_PullMailsEvent(object sender, PullMailsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.Mails == null || e.Contents == null) return;

                var mailList = e.Mails.ToList();
                var contentList = e.Contents.ToList();

                if (mailList.Count != contentList.Count) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull Mails ({0})", mailList.Count));

                int priority = 0;

                for (int i = 0; i < mailList.Count && i < _maxContentCount; i++)
                {
                    var mail = mailList[i];
                    var content = contentList[i];

                    if (_settings.SetMail(mail))
                    {
                        try
                        {
                            _cacheManager[mail.Content] = content;

                            if (_trustSignatures.Contains(mail.Certificate.ToString())) priority++;
                        }
                        catch (Exception)
                        {
                            _settings.RemoveMail(mail);
                        }
                    }

                    var key = new Key(mail.GetHash(_hashAlgorithm), _hashAlgorithm);

                    messageManager.PushMails.Add(key);
                    _pullMailCount++;
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
                int closeCount;

                _nodesStatus.TryGetValue(connectionManager.Node, out closeCount);
                _nodesStatus[connectionManager.Node] = ++closeCount;

                if (closeCount >= 3)
                {
                    _removeNodes.Add(connectionManager.Node);

                    if (_routeTable.Count > _routeTableMinCount)
                    {
                        _routeTable.Remove(connectionManager.Node);
                    }

                    _nodesStatus.Remove(connectionManager.Node);
                }
                else
                {
                    if (!_removeNodes.Contains(connectionManager.Node))
                    {
                        if (connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                            _cuttingNodes.Add(connectionManager.Node);
                    }
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

        public void SendChannelRequest(Channel channel)
        {
            lock (this.ThisLock)
            {
                _pushChannelsRequestList.Add(channel);
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

        public IEnumerable<Channel> GetChannels()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetChannels();
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

        public IEnumerable<Profile> GetProfiles(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetProfiles(section);
            }
        }

        public IEnumerable<Document> GetDocuments(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetDocuments(section);
            }
        }

        public IEnumerable<Topic> GetTopics(Channel channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetTopics(channel);
            }
        }

        public IEnumerable<Message> GetMessages(Channel channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetMessages(channel);
            }
        }

        public IEnumerable<Mail> GetMails(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetMails(signature);
            }
        }

        public ProfileContent GetContent(Profile profile)
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

        public DocumentContent GetContent(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[document.Content];

                    return ContentConverter.FromDocumentContentBlock(buffer);
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

        public TopicContent GetContent(Topic topic)
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

        public MessageContent GetContent(Message message)
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

        public MailContent GetContent(Mail mail, IExchangeDecrypt exchangeDecrypt)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[mail.Content];

                    return ContentConverter.FromMailContentBlock(buffer, exchangeDecrypt);
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

        public Profile UploadProfile(Section section,
            IEnumerable<string> trustSignatures, IEnumerable<Channel> channels, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new ProfileContent(trustSignatures, channels, exchangeEncrypt.ExchangeAlgorithm, exchangeEncrypt.PublicKey);
                    buffer = ContentConverter.ToProfileContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var profile = new Profile(section, key, digitalSignature);

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

        public Document UploadDocument(Section section,
            IEnumerable<Page> pages, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var documentContent = new DocumentContent(pages);
                    buffer = ContentConverter.ToDocumentContentBlock(documentContent);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var document = new Document(section, key, digitalSignature);

                    if (_settings.SetDocument(document))
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

        public Topic UploadTopic(Channel channel,
            string text, ContentFormatType formatType, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new TopicContent(text, formatType);
                    buffer = ContentConverter.ToTopicContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var topic = new Topic(channel, key, digitalSignature);

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

        public Message UploadMessage(Channel channel,
            string text, IEnumerable<Key> anchors, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new MessageContent(text, anchors);
                    buffer = ContentConverter.ToMessageContentBlock(content);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var message = new Message(channel, key, digitalSignature);

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

        public Mail UploadMail(string recipientSignature,
            string text, IExchangeEncrypt exchangeEncrypt, DigitalSignature digitalSignature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>();

                try
                {
                    var content = new MailContent(text);
                    buffer = ContentConverter.ToMailContentBlock(content, exchangeEncrypt);
                    var key = new Key(Sha512.ComputeHash(buffer), HashAlgorithm.Sha512);

                    var mail = new Mail(recipientSignature, key, digitalSignature);

                    if (_settings.SetMail(mail))
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
                lock (_settings.ThisLock)
                {
                    _settings.OtherNodes.Clear();
                    _settings.OtherNodes.AddRange(_routeTable.ToArray());

                    _settings.Save(directoryPath);
                }
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase, IThisLock
        {
            private object _thisLock = new object();

            public Settings()
                : base(new List<Library.Configuration.ISettingsContext>() { 
                    new Library.Configuration.SettingsContext<Node>() { Name = "BaseNode", Value = new Node() },
                    new Library.Configuration.SettingsContext<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingsContext<int>() { Name = "ConnectionCountLimit", Value = 12 },
                    new Library.Configuration.SettingsContext<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingsContext<Dictionary<Section, Dictionary<string, Profile>>>() { Name = "Profiles", Value = new Dictionary<Section, Dictionary<string, Profile>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Section, Dictionary<string, Document>>>() { Name = "Documents", Value = new Dictionary<Section, Dictionary<string, Document>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Channel, Dictionary<string, Topic>>>() { Name = "Topics", Value = new Dictionary<Channel, Dictionary<string, Topic>>() },
                    new Library.Configuration.SettingsContext<Dictionary<Channel, HashSet<Message>>>() { Name = "Messages", Value = new Dictionary<Channel, HashSet<Message>>() },
                    new Library.Configuration.SettingsContext<Dictionary<string, HashSet<Mail>>>() { Name = "Mails", Value = new Dictionary<string, HashSet<Mail>>() },
                })
            {

            }

            public Information Information
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("SectionCount", this.GetSections().Count()));
                        contexts.Add(new InformationContext("ProfileCount", this.Profiles.Values.Sum(n => n.Count)));
                        contexts.Add(new InformationContext("DocumentCount", this.Documents.Values.Sum(n => n.Count)));

                        contexts.Add(new InformationContext("ChannelCount", this.GetChannels().Count()));
                        contexts.Add(new InformationContext("TopicCount", this.Topics.Values.Sum(n => n.Count)));
                        contexts.Add(new InformationContext("MessageCount", this.Messages.Sum(n => n.Value.Count)));

                        contexts.Add(new InformationContext("MailCount", this.Mails.Sum(n => n.Value.Count)));

                        return new Information(contexts);
                    }
                }
            }

            public override void Load(string directoryPath)
            {
                lock (this.ThisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (this.ThisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public IEnumerable<Section> GetSections()
            {
                lock (this.ThisLock)
                {
                    HashSet<Section> hashset = new HashSet<Section>();

                    hashset.UnionWith(this.Profiles.Keys);
                    hashset.UnionWith(this.Documents.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Channel> GetChannels()
            {
                lock (this.ThisLock)
                {
                    HashSet<Channel> hashset = new HashSet<Channel>();

                    hashset.UnionWith(this.Topics.Keys);
                    hashset.UnionWith(this.Messages.Keys);

                    return hashset;
                }
            }

            public IEnumerable<string> GetSignatures()
            {
                lock (this.ThisLock)
                {
                    return this.Mails.Keys.ToArray();
                }
            }

            public void RemoveSections(IEnumerable<Section> sections)
            {
                lock (this.ThisLock)
                {
                    foreach (var section in sections)
                    {
                        this.Profiles.Remove(section);
                        this.Documents.Remove(section);
                    }
                }
            }

            public void RemoveChannels(IEnumerable<Channel> channels)
            {
                lock (this.ThisLock)
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
                lock (this.ThisLock)
                {
                    foreach (var signature in signatures)
                    {
                        this.Mails.Remove(signature);
                    }
                }
            }

            public IEnumerable<Profile> GetProfiles(Section section)
            {
                lock (this.ThisLock)
                {
                    return this.Profiles[section].Values;
                }
            }

            public IEnumerable<Document> GetDocuments(Section section)
            {
                lock (this.ThisLock)
                {
                    return this.Documents[section].Values;
                }
            }

            public IEnumerable<Topic> GetTopics(Channel channel)
            {
                lock (this.ThisLock)
                {
                    return this.Topics[channel].Values;
                }
            }

            public IEnumerable<Message> GetMessages(Channel channel)
            {
                lock (this.ThisLock)
                {
                    return this.Messages[channel];
                }
            }

            public IEnumerable<Mail> GetMails(string signature)
            {
                lock (this.ThisLock)
                {
                    return this.Mails[signature];
                }
            }

            public bool SetProfile(Profile profile)
            {
                lock (this.ThisLock)
                {
                    var now = DateTime.UtcNow;

                    if (profile == null || profile.Section == null || profile.Section.Id == null || profile.Section.Id.Length == 0 || string.IsNullOrWhiteSpace(profile.Section.Name)
                        || (profile.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || profile.Certificate == null || !profile.VerifyCertificate()) return false;

                    var signature = profile.Certificate.ToString();

                    Dictionary<string, Profile> dic = null;

                    if (!this.Profiles.TryGetValue(profile.Section, out dic))
                    {
                        dic = new Dictionary<string, Profile>();
                        this.Profiles[profile.Section] = dic;

                        dic[signature] = profile;

                        return true;
                    }

                    Profile tempProfile = null;

                    if (!dic.TryGetValue(signature, out tempProfile)
                        || profile.CreationTime > tempProfile.CreationTime)
                    {
                        dic[signature] = profile;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetDocument(Document document)
            {
                lock (this.ThisLock)
                {
                    var now = DateTime.UtcNow;

                    if (document == null || document.Section == null || document.Section.Id == null || document.Section.Id.Length == 0 || string.IsNullOrWhiteSpace(document.Section.Name)
                        || (document.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || document.Certificate == null || !document.VerifyCertificate()) return false;

                    var signature = document.Certificate.ToString();

                    Dictionary<string, Document> dic = null;

                    if (!this.Documents.TryGetValue(document.Section, out dic))
                    {
                        dic = new Dictionary<string, Document>();
                        this.Documents[document.Section] = dic;

                        dic[signature] = document;

                        return true;
                    }

                    Document tempDocument = null;

                    if (!dic.TryGetValue(signature, out tempDocument)
                        || document.CreationTime > tempDocument.CreationTime)
                    {
                        dic[signature] = document;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetTopic(Topic topic)
            {
                lock (this.ThisLock)
                {
                    var now = DateTime.UtcNow;

                    if (topic == null || topic.Channel == null || topic.Channel.Id == null || topic.Channel.Id.Length == 0 || string.IsNullOrWhiteSpace(topic.Channel.Name)
                        || (topic.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || topic.Certificate == null || !topic.VerifyCertificate()) return false;

                    var signature = topic.Certificate.ToString();

                    Dictionary<string, Topic> dic = null;

                    if (!this.Topics.TryGetValue(topic.Channel, out dic))
                    {
                        dic = new Dictionary<string, Topic>();
                        this.Topics[topic.Channel] = dic;

                        dic[signature] = topic;

                        return true;
                    }

                    Topic tempTopic = null;

                    if (!dic.TryGetValue(signature, out tempTopic)
                        || topic.CreationTime > tempTopic.CreationTime)
                    {
                        dic[signature] = topic;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetMessage(Message message)
            {
                lock (this.ThisLock)
                {
                    var now = DateTime.UtcNow;

                    if (message == null || message.Channel == null || message.Channel.Id == null || message.Channel.Id.Length == 0 || string.IsNullOrWhiteSpace(message.Channel.Name)
                        || (now - message.CreationTime) > new TimeSpan(64, 0, 0, 0)
                        || (message.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || message.Certificate == null || !message.VerifyCertificate()) return false;

                    HashSet<Message> hashset = null;

                    if (!this.Messages.TryGetValue(message.Channel, out hashset))
                    {
                        hashset = new HashSet<Message>();
                        this.Messages[message.Channel] = hashset;
                    }

                    return hashset.Add(message);
                }
            }

            public bool SetMail(Mail mail)
            {
                lock (this.ThisLock)
                {
                    var now = DateTime.UtcNow;

                    if (mail == null || !Signature.HasSignature(mail.RecipientSignature)
                        || (now - mail.CreationTime) > new TimeSpan(32, 0, 0, 0)
                        || (mail.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || mail.Certificate == null || !mail.VerifyCertificate()) return false;

                    HashSet<Mail> hashset = null;

                    if (!this.Mails.TryGetValue(mail.RecipientSignature, out hashset))
                    {
                        hashset = new HashSet<Mail>();
                        this.Mails[mail.RecipientSignature] = hashset;
                    }

                    return hashset.Add(mail);
                }
            }

            public void RemoveProfile(Profile profile)
            {
                lock (this.ThisLock)
                {
                    var signature = profile.Certificate.ToString();

                    Dictionary<string, Profile> dic = null;

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

            public void RemoveDocument(Document document)
            {
                lock (this.ThisLock)
                {
                    var signature = document.Certificate.ToString();

                    Dictionary<string, Document> dic = null;

                    if (this.Documents.TryGetValue(document.Section, out dic))
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            this.Documents.Remove(document.Section);
                        }
                    }
                }
            }

            public void RemoveTopic(Topic topic)
            {
                lock (this.ThisLock)
                {
                    var signature = topic.Certificate.ToString();

                    Dictionary<string, Topic> dic = null;

                    if (this.Topics.TryGetValue(topic.Channel, out dic))
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            this.Topics.Remove(topic.Channel);
                        }
                    }
                }
            }

            public void RemoveMessage(Message message)
            {
                lock (this.ThisLock)
                {
                    HashSet<Message> hashset = null;

                    if (this.Messages.TryGetValue(message.Channel, out hashset))
                    {
                        hashset.Remove(message);

                        if (hashset.Count == 0)
                        {
                            this.Messages.Remove(message.Channel);
                        }
                    }
                }
            }

            public void RemoveMail(Mail mail)
            {
                lock (this.ThisLock)
                {
                    HashSet<Mail> hashset = null;

                    if (this.Mails.TryGetValue(mail.RecipientSignature, out hashset))
                    {
                        hashset.Remove(mail);

                        if (hashset.Count == 0)
                        {
                            this.Mails.Remove(mail.RecipientSignature);
                        }
                    }
                }
            }

            public NodeCollection OtherNodes
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (NodeCollection)this["OtherNodes"];
                    }
                }
            }

            public Node BaseNode
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (Node)this["BaseNode"];
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        this["BaseNode"] = value;
                    }
                }
            }

            public int ConnectionCountLimit
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (int)this["ConnectionCountLimit"];
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        this["ConnectionCountLimit"] = value;
                    }
                }
            }

            public int BandwidthLimit
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (int)this["BandwidthLimit"];
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        this["BandwidthLimit"] = value;
                    }
                }
            }

            private Dictionary<Section, Dictionary<string, Profile>> Profiles
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (Dictionary<Section, Dictionary<string, Profile>>)this["Profiles"];
                    }
                }
            }

            private Dictionary<Section, Dictionary<string, Document>> Documents
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (Dictionary<Section, Dictionary<string, Document>>)this["Documents"];
                    }
                }
            }

            private Dictionary<Channel, Dictionary<string, Topic>> Topics
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (Dictionary<Channel, Dictionary<string, Topic>>)this["Topics"];
                    }
                }
            }

            private Dictionary<Channel, HashSet<Message>> Messages
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (Dictionary<Channel, HashSet<Message>>)this["Messages"];
                    }
                }
            }

            private Dictionary<string, HashSet<Mail>> Mails
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (Dictionary<string, HashSet<Mail>>)this["Mails"];
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
