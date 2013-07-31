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
        private BufferManager _bufferManager;

        private Settings _settings;

        private Kademlia<Node> _routeTable;
        private static Random _random = new Random();

        private byte[] _mySessionId;

        private LockedList<ConnectionManager> _connectionManagers;
        private MessagesManager _messagesManager;

        private LockedDictionary<Node, LockedHashSet<Section>> _pushSectionsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Section>>();
        private LockedDictionary<Node, LockedHashSet<Channel>> _pushChannelsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Channel>>();
        private LockedDictionary<Node, LockedHashSet<string>> _pushSignaturesRequestDictionary = new LockedDictionary<Node, LockedHashSet<string>>();

        private LockedList<Node> _creatingNodes;
        private CirculationCollection<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private CirculationDictionary<Node, int> _nodesStatus;

        private CirculationCollection<Section> _pushSectionsRequestList;
        private CirculationCollection<Channel> _pushChannelsRequestList;
        private CirculationCollection<string> _pushSignaturesRequestList;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

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
        private volatile int _pushMailCount;

        private volatile int _pullNodeCount;
        private volatile int _pullSectionRequestCount;
        private volatile int _pullProfileCount;
        private volatile int _pullDocumentCount;
        private volatile int _pullChannelRequestCount;
        private volatile int _pullTopicCount;
        private volatile int _pullMessageCount;
        private volatile int _pullMailCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        private TrustSignaturesEventHandler _trustSignaturesEvent;
        private LockSectionsEventHandler _lockSectionsEvent;
        private LockChannelsEventHandler _lockChannelsEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        private const int _maxNodeCount = 128;
        private const int _maxRequestCount = 128;
        private const int _routeTableMinCount = 100;

#if DEBUG
        private const int _downloadingConnectionCountLowerLimit = 0;
        private const int _uploadingConnectionCountLowerLimit = 0;
#else
        private const int _downloadingConnectionCountLowerLimit = 3;
        private const int _uploadingConnectionCountLowerLimit = 3;
#endif

        public ConnectionsManager(ClientManager clientManager, ServerManager serverManager, BufferManager bufferManager)
        {
            _clientManager = clientManager;
            _serverManager = serverManager;
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

        public LockChannelsEventHandler LockchannelsEvent
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

        protected virtual IEnumerable<string> OnRemoveTrustSignaturesEvent()
        {
            if (_trustSignaturesEvent != null)
            {
                return _trustSignaturesEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Section> OnRemoveSectionsEvent()
        {
            if (_lockSectionsEvent != null)
            {
                return _lockSectionsEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Channel> OnRemoveChannelsEvent()
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

        private void ConnectionsManagerThread()
        {
            Stopwatch connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();

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

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    refreshStopwatch.Restart();

                    var now = DateTime.UtcNow;

                    //lock (this.ThisLock)
                    //{
                    //    lock (_settings.ThisLock)
                    //    {
                    //        foreach (var c in _settings.Messages.Keys.ToArray())
                    //        {
                    //            var list = _settings.Messages[c];

                    //            foreach (var m in list.ToArray())
                    //            {
                    //                if ((now - m.CreationTime) > new TimeSpan(32, 0, 0, 0))
                    //                {
                    //                    list.Remove(m);
                    //                }
                    //            }

                    //            if (list.Count == 0) _settings.Messages.Remove(c);
                    //        }
                    //    }
                    //}

                    ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            //{
                            //    var removeSections = this.OnRemoveSectionsEvent();

                            //    if (removeSections != null && removeSections.Count() > 0)
                            //    {
                            //        lock (this.ThisLock)
                            //        {
                            //            lock (_settings.ThisLock)
                            //            {
                            //                foreach (var section in removeSections)
                            //                {
                            //                    _settings.Leaders.Remove(section);
                            //                    _settings.Creators.Remove(section);
                            //                    _settings.Managers.Remove(section);
                            //                }
                            //            }
                            //        }
                            //    }
                            //}

                            //{
                            //    List<Section> sections = new List<Section>();

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var section in _settings.Leaders.Keys)
                            //            {
                            //                sections.Add(section);
                            //            }
                            //        }
                            //    }

                            //    Dictionary<Section, IEnumerable<string>> removeLeadersDictionary = new Dictionary<Section, IEnumerable<string>>();

                            //    foreach (var section in sections)
                            //    {
                            //        var removeLeaders = this.OnRemoveLeadersEvent(section);

                            //        if (removeLeaders != null && removeLeaders.Count() > 0)
                            //        {
                            //            removeLeadersDictionary.Add(section, removeLeaders);
                            //        }
                            //    }

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var section in removeLeadersDictionary.Keys)
                            //            {
                            //                LockedDictionary<string, Leader> list;

                            //                if (_settings.Leaders.TryGetValue(section, out list))
                            //                {
                            //                    foreach (var leader in removeLeadersDictionary[section])
                            //                    {
                            //                        list.Remove(leader);
                            //                    }
                            //                }
                            //            }
                            //        }
                            //    }
                            //}

                            //{
                            //    List<Section> sections = new List<Section>();

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var section in _settings.Creators.Keys)
                            //            {
                            //                sections.Add(section);
                            //            }
                            //        }
                            //    }

                            //    Dictionary<Section, IEnumerable<string>> removeCreatorsDictionary = new Dictionary<Section, IEnumerable<string>>();

                            //    foreach (var section in sections)
                            //    {
                            //        var removeCreators = this.OnRemoveCreatorsEvent(section);

                            //        if (removeCreators != null && removeCreators.Count() > 0)
                            //        {
                            //            removeCreatorsDictionary.Add(section, removeCreators);
                            //        }
                            //    }

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var section in removeCreatorsDictionary.Keys)
                            //            {
                            //                LockedDictionary<string, Creator> list;

                            //                if (_settings.Creators.TryGetValue(section, out list))
                            //                {
                            //                    foreach (var creator in removeCreatorsDictionary[section])
                            //                    {
                            //                        list.Remove(creator);
                            //                    }
                            //                }
                            //            }
                            //        }
                            //    }
                            //}

                            //{
                            //    List<Section> sections = new List<Section>();

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var section in _settings.Managers.Keys)
                            //            {
                            //                sections.Add(section);
                            //            }
                            //        }
                            //    }

                            //    Dictionary<Section, IEnumerable<string>> removeManagersDictionary = new Dictionary<Section, IEnumerable<string>>();

                            //    foreach (var section in sections)
                            //    {
                            //        var removeManagers = this.OnRemoveManagersEvent(section);

                            //        if (removeManagers != null && removeManagers.Count() > 0)
                            //        {
                            //            removeManagersDictionary.Add(section, removeManagers);
                            //        }
                            //    }

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var section in removeManagersDictionary.Keys)
                            //            {
                            //                LockedDictionary<string, Manager> list;

                            //                if (_settings.Managers.TryGetValue(section, out list))
                            //                {
                            //                    foreach (var manager in removeManagersDictionary[section])
                            //                    {
                            //                        list.Remove(manager);
                            //                    }
                            //                }
                            //            }
                            //        }
                            //    }
                            //}

                            //{
                            //    var removeChannels = this.OnRemoveChannelsEvent();

                            //    if (removeChannels != null && removeChannels.Count() > 0)
                            //    {
                            //        lock (this.ThisLock)
                            //        {
                            //            lock (_settings.ThisLock)
                            //            {
                            //                foreach (var channel in removeChannels)
                            //                {
                            //                    _settings.Topics.Remove(channel);
                            //                    _settings.Messages.Remove(channel);
                            //                }
                            //            }
                            //        }
                            //    }
                            //}

                            //{
                            //    List<Channel> channels = new List<Channel>();

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var channel in _settings.Topics.Keys)
                            //            {
                            //                channels.Add(channel);
                            //            }
                            //        }
                            //    }

                            //    Dictionary<Channel, IEnumerable<string>> removeTopicsDictionary = new Dictionary<Channel, IEnumerable<string>>();

                            //    foreach (var channel in channels)
                            //    {
                            //        var removeTopics = this.OnRemoveTopicsEvent(channel);

                            //        if (removeTopics != null && removeTopics.Count() > 0)
                            //        {
                            //            removeTopicsDictionary.Add(channel, removeTopics);
                            //        }
                            //    }

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var channel in removeTopicsDictionary.Keys)
                            //            {
                            //                LockedDictionary<string, Topic> list;

                            //                if (_settings.Topics.TryGetValue(channel, out list))
                            //                {
                            //                    foreach (var topic in removeTopicsDictionary[channel])
                            //                    {
                            //                        list.Remove(topic);
                            //                    }
                            //                }
                            //            }
                            //        }
                            //    }
                            //}

                            //{
                            //    List<Channel> channels = new List<Channel>();

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var channel in _settings.Messages.Keys)
                            //            {
                            //                channels.Add(channel);
                            //            }
                            //        }
                            //    }

                            //    Dictionary<Channel, IEnumerable<Message>> removeMessagesDictionary = new Dictionary<Channel, IEnumerable<Message>>();

                            //    foreach (var channel in channels)
                            //    {
                            //        var removeMessages = this.OnRemoveMessagesEvent(channel);

                            //        if (removeMessages != null && removeMessages.Count() > 0)
                            //        {
                            //            removeMessagesDictionary.Add(channel, removeMessages);
                            //        }
                            //    }

                            //    lock (this.ThisLock)
                            //    {
                            //        lock (_settings.ThisLock)
                            //        {
                            //            foreach (var channel in removeMessagesDictionary.Keys)
                            //            {
                            //                LockedHashSet<Message> list;

                            //                if (_settings.Messages.TryGetValue(channel, out list))
                            //                {
                            //                    foreach (var message in removeMessagesDictionary[channel])
                            //                    {
                            //                        list.Remove(message);
                            //                    }
                            //                }
                            //            }
                            //        }
                            //    }
                            //}
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

                if (connectionCount >= _uploadingConnectionCountLowerLimit && pushUploadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushUploadStopwatch.Restart();

                    foreach (var item in this.GetSections())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

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

                    foreach (var item in this.GetChannels())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

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
                }

                if (connectionCount >= _downloadingConnectionCountLowerLimit && pushDownloadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushDownloadStopwatch.Restart();

                    HashSet<Section> pushSectionsRequestList = new HashSet<Section>();
                    HashSet<Channel> pushChannelsRequestList = new HashSet<Channel>();
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

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushSectionsRequestList.Add(list[i]);
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullSectionsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushSectionsRequestList.Add(list[i]);
                            }
                        }
                    }

                    {
                        {
                            var list = _pushChannelsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushChannelsRequestList.Add(list[i]);
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullChannelsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushChannelsRequestList.Add(list[i]);
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
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    if (!pushSectionsRequestDictionary.ContainsKey(requestNodes[i]))
                                        pushSectionsRequestDictionary[requestNodes[i]] = new HashSet<Section>();

                                    pushSectionsRequestDictionary[requestNodes[i]].Add(item);
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
                                _pushSectionsRequestDictionary.Add(item.Key, new LockedHashSet<Section>(item.Value));
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
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    if (!pushChannelsRequestDictionary.ContainsKey(requestNodes[i]))
                                        pushChannelsRequestDictionary[requestNodes[i]] = new HashSet<Channel>();

                                    pushChannelsRequestDictionary[requestNodes[i]].Add(item);
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
                                _pushChannelsRequestDictionary.Add(item.Key, new LockedHashSet<Channel>(item.Value));
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
                                    LockedHashSet<Section> sectionHashset;

                                    if (_pushSectionsRequestDictionary.TryGetValue(connectionManager.Node, out sectionHashset))
                                    {
                                        tempList = new SectionCollection(sectionHashset.ToArray().Randomize().Take(count));

                                        _pushSectionsRequestDictionary[connectionManager.Node].ExceptWith(tempList);
                                        _messagesManager[connectionManager.Node].PushSectionsRequest.AddRange(tempList);
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
                                            _messagesManager[connectionManager.Node].PushSectionsRequest.Remove(item);
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
                                    LockedHashSet<Channel> channelHashset;

                                    if (_pushChannelsRequestDictionary.TryGetValue(connectionManager.Node, out channelHashset))
                                    {
                                        tempList = new ChannelCollection(channelHashset.ToArray().Randomize().Take(count));

                                        _pushChannelsRequestDictionary[connectionManager.Node].ExceptWith(tempList);
                                        _messagesManager[connectionManager.Node].PushChannelsRequest.AddRange(tempList);
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
                                            _messagesManager[connectionManager.Node].PushChannelsRequest.Remove(item);
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
                                // PushLeader
                                {
                                    //    List<Leader> leaders = new List<Leader>();

                                    //    lock (this.ThisLock)
                                    //    {
                                    //        lock (_settings.ThisLock)
                                    //        {
                                    //            foreach (var section in sections)
                                    //            {
                                    //                LockedDictionary<string, Leader> leaderDictionary;

                                    //                if (_settings.Leaders.TryGetValue(section, out leaderDictionary))
                                    //                {
                                    //                    foreach (var l in leaderDictionary.Values.Randomize())
                                    //                    {
                                    //                        if (!messageManager.PushLeaders.Contains(l.GetHash(_hashAlgorithm)))
                                    //                        {
                                    //                            leaders.Add(l);

                                    //                            if (leaders.Count >= 1) goto End;
                                    //                        }
                                    //                    }
                                    //                }
                                    //            }
                                    //        }
                                    //    }

                                    //End: ;

                                    //    foreach (var leader in leaders.Randomize())
                                    //    {
                                    //        connectionManager.PushLeader(leader);

                                    //        Debug.WriteLine(string.Format("ConnectionManager: Push Leader ({0})", leader.Section.Name));
                                    //        _pushLeaderCount++;

                                    //        messageManager.PushLeaders.Add(leader.GetHash(_hashAlgorithm));
                                    //    }
                                }
                            }
                        }

                        {
                            List<Channel> channels = new List<Channel>(messageManager.PullChannelsRequest.Randomize());

                            if (channels.Count > 0)
                            {
                                // PushMessage
                                {
                                    //    List<Message> messages = new List<Message>();

                                    //    lock (this.ThisLock)
                                    //    {
                                    //        lock (_settings.ThisLock)
                                    //        {
                                    //            foreach (var channel in channels)
                                    //            {
                                    //                LockedHashSet<Message> tempMessages;

                                    //                if (_settings.Messages.TryGetValue(channel, out tempMessages))
                                    //                {
                                    //                    foreach (var m in tempMessages.Randomize())
                                    //                    {
                                    //                        if (!messageManager.PushMessages.Contains(m.GetHash(_hashAlgorithm)))
                                    //                        {
                                    //                            messages.Add(m);

                                    //                            if (messages.Count >= 1) goto End;
                                    //                        }
                                    //                    }
                                    //                }
                                    //            }
                                    //        }
                                    //    }

                                    //End: ;

                                    //    foreach (var message in messages.Randomize())
                                    //    {
                                    //        connectionManager.PushMessage(message);

                                    //        Debug.WriteLine(string.Format("ConnectionManager: Push Message ({0})", message.Channel.Name));
                                    //        _pushMessageCount++;

                                    //        messageManager.PushMessages.Add(message.GetHash(_hashAlgorithm));
                                    //        messageManager.Priority--;
                                    //    }
                                    //
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
                    lock (_messagesManager[connectionManager.Node].ThisLock)
                    {
                        lock (_messagesManager[connectionManager.Node].SurroundingNodes.ThisLock)
                        {
                            _messagesManager[connectionManager.Node].SurroundingNodes.Clear();
                            _messagesManager[connectionManager.Node].SurroundingNodes.UnionWith(e.Nodes
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

            if (e.Sections == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SectionsRequest {0} ({1})", String.Join(", ", e.Sections), e.Sections.Count()));

            foreach (var c in e.Sections.Take(_maxRequestCount))
            {
                if (c == null || c.Id == null || string.IsNullOrWhiteSpace(c.Name)) continue;

                _messagesManager[connectionManager.Node].PullSectionsRequest.Add(c);
                _pullSectionRequestCount++;
            }
        }

        void connectionManager_PullProfilesEvent(object sender, PullProfilesEventArgs e)
        {
            throw new NotImplementedException();
        }

        void connectionManager_PullDocumentsEvent(object sender, PullDocumentsEventArgs e)
        {
            throw new NotImplementedException();
        }
        
        private void connectionManager_PullChannelsRequestEvent(object sender, PullChannelsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Channels == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull ChannelsRequest {0} ({1})", String.Join(", ", e.Channels), e.Channels.Count()));

            foreach (var c in e.Channels.Take(_maxRequestCount))
            {
                if (c == null || c.Id == null || string.IsNullOrWhiteSpace(c.Name)) continue;

                _messagesManager[connectionManager.Node].PullChannelsRequest.Add(c);
                _pullChannelRequestCount++;
            }
        }

        void connectionManager_PullTopicsEvent(object sender, PullTopicsEventArgs e)
        {
            throw new NotImplementedException();
        }

        void connectionManager_PullMessagesEvent(object sender, PullMessagesEventArgs e)
        {
            throw new NotImplementedException();
        }

        void connectionManager_PullSignaturesRequestEvent(object sender, PullSignaturesRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Signatures == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SignaturesRequest {0} ({1})", String.Join(", ", e.Signatures), e.Signatures.Count()));

            foreach (var s in e.Signatures.Take(_maxRequestCount))
            {
                if (s == null || s.Id == null || string.IsNullOrWhiteSpace(s.Name)) continue;

                _messagesManager[connectionManager.Node].PullSignaturesRequest.Add(s);
                _pullChannelRequestCount++;
            }
        }

        void connectionManager_PullMailsEvent(object sender, PullMailsEventArgs e)
        {
            throw new NotImplementedException();
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

        public void SendRequest(Section section)
        {
            lock (this.ThisLock)
            {
                _pushSectionsRequestList.Add(section);
            }
        }

        public void SendRequest(Channel channel)
        {
            lock (this.ThisLock)
            {
                _pushChannelsRequestList.Add(channel);
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

        public void Upload(Topic topic)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.SetTopics(topic);
            }
        }

        public void Upload(Message message)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.SetMessages(message);
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
                _connectionsManagerThread.Priority = ThreadPriority.Lowest;
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

            public bool SetProfiles(Profile profile)
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

            public bool SetDocuments(Document document)
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

            public bool SetTopics(Topic topic)
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

            public bool SetMessages(Message message)
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

            public bool SetMails(Mail mail)
            {
                lock (this.ThisLock)
                {
                    var now = DateTime.UtcNow;

                    if (mail == null || !Signature.HasSignature(mail.RecipientSignature)
                        || (now - mail.CreationTime) > new TimeSpan(64, 0, 0, 0)
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
