using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Library.Collections;
using Library.Net;
using Library.Net.Connection;

namespace Library.Net.Amoeba
{
    delegate void UploadedEventHandler(object sender, IEnumerable<Key> keys);

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

        private LockedDictionary<Node, LockedHashSet<Key>> _pushBlocksLinkDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();
        private LockedDictionary<Node, LockedHashSet<Key>> _pushBlocksRequestDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();
        private LockedDictionary<Node, LockedHashSet<Key>> _pushBlocksDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();

        private LockedList<Node> _creatingNodes;
        private CirculationCollection<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private LockedDictionary<Node, int> _nodesStatus;

        private CirculationCollection<Key> _downloadBlocks;

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
        private volatile int _pushBlockLinkCount;
        private volatile int _pushBlockRequestCount;
        private volatile int _pushBlockCount;

        private volatile int _pullNodeCount;
        private volatile int _pullBlockLinkCount;
        private volatile int _pullBlockRequestCount;
        private volatile int _pullBlockCount;

        private CirculationCollection<Key> _relayBlocks;
        private volatile int _relayBlockCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        internal UploadedEventHandler UploadedEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        private readonly int _maxNodeCount = 128;
        private readonly int _maxLinkCount = 8192;
        private readonly int _maxRequestCount = 8192;

        private readonly int _downloadingConnectionCountLowerLimit = 3;
        private readonly int _uploadingConnectionCountLowerLimit = 3;

        private int _threadCount = 2;

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
            _removeNodes = new CirculationCollection<Node>(new TimeSpan(0, 30, 0));
            _nodesStatus = new LockedDictionary<Node, int>();

            _downloadBlocks = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));

            _relayBlocks = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));

            this.UpdateSessionId();

#if !MONO
            {
                SYSTEM_INFO info = new SYSTEM_INFO();
                NativeMethods.GetSystemInfo(ref info);

                _threadCount = Math.Max(1, Math.Min(info.dwNumberOfProcessors, 32) / 2);
            }
#endif
        }

        public Node BaseNode
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _settings.BaseNode;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _routeTable.ToArray();
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _settings.ConnectionCountLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _settings.ConnectionCountLimit = value;
                }
            }
        }

        public long BandWidthLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _settings.BandwidthLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    List<InformationContext> contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("PushNodeCount", _pushNodeCount));
                    contexts.Add(new InformationContext("PushBlockLinkCount", _pushBlockLinkCount));
                    contexts.Add(new InformationContext("PushBlockRequestCount", _pushBlockRequestCount));
                    contexts.Add(new InformationContext("PushBlockCount", _pushBlockCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PullBlockLinkCount", _pullBlockLinkCount));
                    contexts.Add(new InformationContext("PullBlockRequestCount", _pullBlockRequestCount));
                    contexts.Add(new InformationContext("PullBlockCount", _pullBlockCount));

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

                    contexts.Add(new InformationContext("BlockCount", _cacheManager.Count));
                    contexts.Add(new InformationContext("RelayBlockCount", _relayBlockCount));

                    return new Information(contexts);
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _receivedByteCount + _connectionManagers.Sum(n => n.ReceivedByteCount);
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _sentByteCount + _connectionManagers.Sum(n => n.SentByteCount);
                }
            }
        }

        private void UpdateSessionId()
        {
            lock (this.ThisLock)
            {
                _mySessionId = new byte[64];
                (new System.Security.Cryptography.RNGCryptoServiceProvider()).GetBytes(_mySessionId);
            }
        }

        private double BlockPriority(Node node)
        {
            lock (this.ThisLock)
            {
                return _messagesManager[node].Priority + 128;
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

                nodes.Sort(new Comparison<KeyValuePair<Node, TimeSpan>>((KeyValuePair<Node, TimeSpan> x, KeyValuePair<Node, TimeSpan> y) =>
                {
                    return y.Value.CompareTo(x.Value);
                }));

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

                        list.Sort(new Comparison<Node>((Node x, Node y) =>
                        {
                            return _responseTimeDic[x].CompareTo(_responseTimeDic[y]);
                        }));

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

        //            list.Sort(new Comparison<Node>((Node x, Node y) =>
        //            {
        //                var tx = _connectionManagers.FirstOrDefault(n => n.Node == x);
        //                var ty = _connectionManagers.FirstOrDefault(n => n.Node == y);

        //                if (tx == null && ty != null) return 1;
        //                else if (tx != null && ty == null) return -1;
        //                else if (tx == null && ty == null) return 0;

        //                return tx.ResponseTime.CompareTo(ty.ResponseTime);
        //            }));

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

                //if (Collection.Equals(connectionManager.Node.Id, this.BaseNode.Id))
                //{
                //    connectionManager.Dispose();
                //    return;
                //}

                //var oldConnectionManager = _connectionManagers.FirstOrDefault(n => Collection.Equals(n.Node.Id, connectionManager.Node.Id));

                //if (oldConnectionManager != null)
                //{
                //    this.RemoveConnectionManager(oldConnectionManager);
                //}

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
                        ThreadPool.QueueUserWorkItem(new WaitCallback((object state) =>
                        {
                            // PushNodes
                            try
                            {
                                List<Node> nodes = new List<Node>();

                                lock (this.ThisLock)
                                {
                                    var clist = _connectionManagers.ToList();
                                    clist.Remove(connectionManager);

                                    clist.Sort(new Comparison<ConnectionManager>((ConnectionManager x, ConnectionManager y) =>
                                    {
                                        return x.ResponseTime.CompareTo(y.ResponseTime);
                                    }));

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
                            catch (Exception)
                            {

                            }

                            try
                            {
                                connectionManager.PushCancel();

                                Debug.WriteLine("ConnectionManager: Push Cancel");
                            }
                            catch (Exception)
                            {

                            }

                            connectionManager.Dispose();
                        }));

                        return;
                    }
                }

                Debug.WriteLine("ConnectionManager: Connect");

                connectionManager.PullNodesEvent += new PullNodesEventHandler(connectionManager_NodesEvent);
                connectionManager.PullBlocksLinkEvent += new PullBlocksLinkEventHandler(connectionManager_BlocksLinkEvent);
                connectionManager.PullBlocksRequestEvent += new PullBlocksRequestEventHandler(connectionManager_BlocksRequestEvent);
                connectionManager.PullBlockEvent += new PullBlockEventHandler(connectionManager_BlockEvent);
                connectionManager.PullSeedsRequestEvent += new PullSeedsRequestEventHandler(connectionManager_SeedsRequestEvent);
                connectionManager.PullSeedEvent += new PullSeedEventHandler(connectionManager_SeedEvent);
                connectionManager.PullCancelEvent += new PullCancelEventHandler(connectionManager_PullCancelEvent);
                connectionManager.CloseEvent += new CloseEventHandler(connectionManager_CloseEvent);

                var limit = connectionManager.Connection.GetLayers().OfType<IBandwidthLimit>().FirstOrDefault();

                if (limit != null)
                {
                    limit.BandwidthLimit = _bandwidthLimit;
                }

                _nodeToUri.Add(connectionManager.Node, uri);
                _connectionManagers.Add(connectionManager);

                if (_messagesManager[connectionManager.Node].SessionId != null
                    && !Collection.Equals(_messagesManager[connectionManager.Node].SessionId, connectionManager.SesstionId))
                {
                    _messagesManager.Remove(connectionManager.Node);
                }

                _messagesManager[connectionManager.Node].SessionId = connectionManager.SesstionId;

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
                        .OrderBy(n => _random.Next())
                        .FirstOrDefault();

                    if (node == null)
                    {
                        node = _routeTable
                            .ToArray()
                            .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id)) && !_creatingNodes.Contains(n))
                            .OrderBy(n => _random.Next())
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
                        .OrderBy(n => _random.Next()));

                    if (uris.Count == 0)
                    {
                        lock (this.ThisLock)
                        {
                            _nodesStatus.Remove(node);
                            _removeNodes.Remove(node);
                            _cuttingNodes.Remove(node);
                            _routeTable.Remove(node);
                        }

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

                        var connection = _clientManager.CreateConnection(uri);

                        if (connection != null)
                        {
                            var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectionManagerType.Client, _bufferManager);

                            try
                            {
                                connectionManager.Connect();
                                if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();

                                lock (this.ThisLock)
                                {
                                    _nodesStatus.Remove(node);
                                    _cuttingNodes.Remove(node);

                                    if (node != connectionManager.Node)
                                    {
                                        _removeNodes.Add(node);
                                        _routeTable.Remove(node);
                                    }
                                }

                                _routeTable.Live(connectionManager.Node);

                                _createConnectionCount++;

                                this.AddConnectionManager(connectionManager, uri);
                            }
                            catch (Exception)
                            {
                                connectionManager.Dispose();
                            }
                        }
                        else
                        {
                            bool flag = false;

                            lock (this.ThisLock)
                            {
                                if (!_nodesStatus.ContainsKey(node)) _nodesStatus[node] = 0;
                                _nodesStatus[node]++;

                                if (_nodesStatus[node] >= 3)
                                {
                                    flag = true;
                                }
                            }

                            if (flag)
                            {
                                _nodesStatus.Remove(node);
                                _removeNodes.Add(node);
                                _cuttingNodes.Remove(node);

                                if (_routeTable.Count > 100)
                                {
                                    _routeTable.Remove(node);
                                }
                            }
                        }
                    }
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

                string uri;
                var connection = _serverManager.AcceptConnection(out uri);

                if (connection != null)
                {
                    var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectionManagerType.Server, _bufferManager);

                    try
                    {
                        connectionManager.Connect();
                        if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();
                        if (_removeNodes.Contains(connectionManager.Node)) throw new ArgumentException();

                        lock (this.ThisLock)
                        {
                            _nodesStatus.Remove(connectionManager.Node);
                        }
                        
                        _cuttingNodes.Remove(connectionManager.Node);
                        
                        if (connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                            _routeTable.Add(connectionManager.Node);

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
            public DateTime LastPullTime { get; set; }
        }

        private void ConnectionsManagerThread()
        {
            Stopwatch connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();
            Stopwatch seedRemoveStopwatch = new Stopwatch();
            seedRemoveStopwatch.Start();
            Stopwatch pushDownloadStopwatch = new Stopwatch();
            pushDownloadStopwatch.Start();
            Stopwatch pushUploadStopwatch = new Stopwatch();
            pushUploadStopwatch.Start();

            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                if (seedRemoveStopwatch.Elapsed.TotalMinutes >= 30)
                {
                    seedRemoveStopwatch.Restart();

                    _cacheManager.CheckSeeds();
                }

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
                                LastPullTime = _messagesManager[connectionManager.Node].LastPullTime,
                            });
                        }
                    }

                    nodeSortItems.Sort(new Comparison<NodeSortItem>((NodeSortItem x, NodeSortItem y) =>
                    {
                        int c = x.LastPullTime.CompareTo(y.LastPullTime);
                        if (c != 0) return c;

                        return y.ResponseTime.CompareTo(x.ResponseTime);
                    }));

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
                                    lock (this.ThisLock)
                                    {
                                        _nodesStatus.Remove(connectionManager.Node);
                                    }

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
                
                if (connectionCount >= _downloadingConnectionCountLowerLimit && pushDownloadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushDownloadStopwatch.Restart();

                    HashSet<Key> pushBlocksLinkList = new HashSet<Key>();
                    HashSet<Key> pushBlocksRequestList = new HashSet<Key>();
                    List<Node> nodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        nodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    {
                        {
                            var list = _cacheManager
                                .ToArray()
                                .Where(n => n.HashAlgorithm == HashAlgorithm.Sha512)
                                .OrderBy(n => _random.Next())
                                .ToList();

                            for (int i = 0, j = 0; j < 256 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushBlocksLink.Contains(list[i])))
                                {
                                    pushBlocksLinkList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _downloadBlocks
                                .ToArray()
                                .Where(n => n.HashAlgorithm == HashAlgorithm.Sha512)
                                .OrderBy(n => _random.Next())
                                .ToList();

                            for (int i = 0, j = 0; j < 256 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushBlocksRequest.Contains(list[i])) && !_cacheManager.Contains(list[i]))
                                {
                                    pushBlocksRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }
                    }

                    {
                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullBlocksLink
                                .ToArray()
                                .OrderBy(n => _random.Next())
                                .ToList();

                            for (int i = 0, j = 0; j < 256 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushBlocksLink.Contains(list[i])))
                                {
                                    pushBlocksLinkList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullBlocksRequest
                                .ToArray()
                                .OrderBy(n => _random.Next())
                                .ToList();

                            if (list.Any(n => _cacheManager.Contains(n))) continue;

                            for (int i = 0, j = 0; j < 256 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushBlocksRequest.Contains(list[i])) && !_cacheManager.Contains(list[i]))
                                {
                                    pushBlocksRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }
                    }

                    {
                        LockedDictionary<Node, LockedHashSet<Key>> pushBlocksLinkDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();
                        LockedDictionary<Node, LockedHashSet<Key>> pushBlocksRequestDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();

                        //foreach (var item in pushBlocksLinkList)
                        Parallel.ForEach(pushBlocksLinkList.OrderBy(n => _random.Next()), new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, item =>
                        {
                            try
                            {
                                var requestNodes = this.GetSearchNode(item.Hash, 1).ToList();

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                                {
                                    if (!_messagesManager[requestNodes[i]].PullBlocksLink.Contains(item))
                                    {
                                        lock (pushBlocksLinkDictionary.ThisLock)
                                        {
                                            if (!pushBlocksLinkDictionary.ContainsKey(requestNodes[i]))
                                                pushBlocksLinkDictionary[requestNodes[i]] = new LockedHashSet<Key>();

                                            pushBlocksLinkDictionary[requestNodes[i]].Add(item);
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        });

                        lock (_pushBlocksLinkDictionary.ThisLock)
                        {
                            _pushBlocksLinkDictionary.Clear();

                            foreach (var item in pushBlocksLinkDictionary)
                            {
                                _pushBlocksLinkDictionary.Add(item.Key, item.Value);
                            }
                        }

                        //foreach (var item in pushBlocksRequestList)
                        Parallel.ForEach(pushBlocksRequestList.OrderBy(n => _random.Next()), new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, item =>
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();

                                foreach (var node in nodes)
                                {
                                    if (_messagesManager[node].PullBlocksLink.Contains(item))
                                    {
                                        requestNodes.Add(node);
                                    }
                                }

                                requestNodes = requestNodes
                                    .OrderBy(n => _random.Next())
                                    .ToList();

                                if (requestNodes.Count == 0)
                                    requestNodes.AddRange(this.GetSearchNode(item.Hash, 1));

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                                {
                                    if (!_messagesManager[requestNodes[i]].PullBlocksRequest.Contains(item))
                                    {
                                        lock (pushBlocksRequestDictionary.ThisLock)
                                        {
                                            if (!pushBlocksRequestDictionary.ContainsKey(requestNodes[i]))
                                                pushBlocksRequestDictionary[requestNodes[i]] = new LockedHashSet<Key>();

                                            pushBlocksRequestDictionary[requestNodes[i]].Add(item);
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        });

                        lock (_pushBlocksRequestDictionary.ThisLock)
                        {
                            _pushBlocksRequestDictionary.Clear();

                            foreach (var item in pushBlocksRequestDictionary)
                            {
                                _pushBlocksRequestDictionary.Add(item.Key, item.Value);
                            }
                        }
                    }
                }

                if (connectionCount >= _uploadingConnectionCountLowerLimit && pushUploadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushUploadStopwatch.Restart();

                    HashSet<Key> pushBlocksList = new HashSet<Key>();

                    {
                        {
                            var list = _settings.UploadBlocksRequest
                                .ToArray()
                                .Where(n => n.HashAlgorithm == HashAlgorithm.Sha512)
                                .OrderBy(n => _random.Next())
                                .ToList();

                            int count = 1024;

                            for (int i = 0; i < count && i < list.Count; i++)
                            {
                                pushBlocksList.Add(list[i]);
                            }
                        }

                        {
                            var list = _settings.DiffusionBlocksRequest
                                .ToArray()
                                .Where(n => n.HashAlgorithm == HashAlgorithm.Sha512)
                                .OrderBy(n => _random.Next())
                                .ToList();

                            int count = 1024;

                            for (int i = 0; i < count && i < list.Count; i++)
                            {
                                pushBlocksList.Add(list[i]);
                            }
                        }
                    }

                    {
                        LockedDictionary<Node, LockedHashSet<Key>> pushBlocksDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();

                        //foreach (var item in pushBlocksList)
                        Parallel.ForEach(pushBlocksList.OrderBy(n => _random.Next()), new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, item =>
                        {
                            try
                            {
                                var requestNodes = this.GetSearchNode(item.Hash, 1).ToList();

                                if (requestNodes.Count == 0)
                                {
                                    _settings.UploadBlocksRequest.Remove(item);
                                    _settings.DiffusionBlocksRequest.Remove(item);

                                    this.OnUploadedEvent(new Key[] { item });

                                    return;
                                }

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                                {
                                    if (!_messagesManager[requestNodes[i]].PushBlocks.Contains(item))
                                    {
                                        lock (pushBlocksDictionary.ThisLock)
                                        {
                                            if (!pushBlocksDictionary.ContainsKey(requestNodes[i]))
                                                pushBlocksDictionary[requestNodes[i]] = new LockedHashSet<Key>();

                                            pushBlocksDictionary[requestNodes[i]].Add(item);
                                        }
                                    }
                                    else
                                    {
                                        _settings.UploadBlocksRequest.Remove(item);
                                        _settings.DiffusionBlocksRequest.Remove(item);

                                        this.OnUploadedEvent(new Key[] { item });
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        });

                        lock (_pushBlocksDictionary.ThisLock)
                        {
                            _pushBlocksDictionary.Clear();

                            foreach (var item in pushBlocksDictionary)
                            {
                                _pushBlocksDictionary.Add(item.Key, item.Value);
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

                        if ((DateTime.UtcNow - messageManager.LastPullTime) > new TimeSpan(3, 0, 0))
                        {
                            lock (this.ThisLock)
                            {
                                _nodesStatus.Remove(connectionManager.Node);
                                _removeNodes.Add(connectionManager.Node);
                                _routeTable.Remove(connectionManager.Node);
                            }

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

                            clist.Sort(new Comparison<ConnectionManager>((ConnectionManager x, ConnectionManager y) =>
                            {
                                return x.ResponseTime.CompareTo(y.ResponseTime);
                            }));

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

                        // PushBlocksLink
                        if (_connectionManagers.Count >= _uploadingConnectionCountLowerLimit)
                        {
                            KeyCollection tempList = null;

                            lock (_pushBlocksLinkDictionary.ThisLock)
                            {
                                if (_pushBlocksLinkDictionary.ContainsKey(connectionManager.Node))
                                {
                                    tempList = new KeyCollection(_pushBlocksLinkDictionary[connectionManager.Node]
                                        .ToArray()
                                        .OrderBy(n => _random.Next())
                                        .Take(2048));

                                    _pushBlocksLinkDictionary[connectionManager.Node].ExceptWith(tempList);
                                    _messagesManager[connectionManager.Node].PushBlocksLink.AddRange(tempList);
                                }
                            }

                            if (tempList != null && tempList.Count != 0)
                            {
                                try
                                {
                                    connectionManager.PushBlocksLink(tempList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BlocksLink ({0})", tempList.Count));
                                    _pushBlockLinkCount += tempList.Count;
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        _messagesManager[connectionManager.Node].PushBlocksLink.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }

                        // PushBlocksRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            KeyCollection tempList = null;

                            lock (_pushBlocksRequestDictionary.ThisLock)
                            {
                                if (_pushBlocksRequestDictionary.ContainsKey(connectionManager.Node))
                                {
                                    tempList = new KeyCollection(_pushBlocksRequestDictionary[connectionManager.Node]
                                        .ToArray()
                                        .OrderBy(n => _random.Next())
                                        .Take(2048));

                                    _pushBlocksRequestDictionary[connectionManager.Node].ExceptWith(tempList);
                                    _messagesManager[connectionManager.Node].PushBlocksRequest.AddRange(tempList);
                                }
                            }

                            if (tempList != null && tempList.Count != 0)
                            {
                                try
                                {
                                    connectionManager.PushBlocksRequest(tempList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BlocksRequest ({0})", tempList.Count));
                                    _pushBlockRequestCount += tempList.Count;

                                    foreach (var header in tempList)
                                    {
                                        _downloadBlocks.Remove(header);
                                    }
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        _messagesManager[connectionManager.Node].PushBlocksRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }
                    }

                    if (connectionCount >= _uploadingConnectionCountLowerLimit)
                    {
                        // PushBlock (Upload)
                        if ((_random.Next(0, 100) + 1) <= (int)(100 * this.ResponseTimePriority(connectionManager.Node)))
                        {
                            Key key = null;

                            lock (_pushBlocksDictionary.ThisLock)
                            {
                                if (_pushBlocksDictionary.ContainsKey(connectionManager.Node))
                                {
                                    key = _pushBlocksDictionary[connectionManager.Node]
                                        .ToArray()
                                        .OrderBy(n => _random.Next())
                                        .FirstOrDefault();

                                    if (key != null)
                                    {
                                        _pushBlocksDictionary[connectionManager.Node].Remove(key);
                                        _messagesManager[connectionManager.Node].PushBlocks.Add(key);
                                    }
                                }
                            }

                            if (key != null)
                            {
                                ArraySegment<byte> buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = _cacheManager[key];

                                    connectionManager.PushBlock(key, buffer);

                                    Debug.WriteLine(string.Format("ConnectionManager: Upload Push Block ({0})", NetworkConverter.ToBase64String(key.Hash)));
                                    _pushBlockCount++;

                                    messageManager.PullBlocksRequest.Remove(key);
                                    messageManager.PushBlocks.Add(key);
                                }
                                catch (ConnectionManagerException e)
                                {
                                    _messagesManager[connectionManager.Node].PushBlocks.Remove(key);

                                    throw e;
                                }
                                catch (BlockNotFoundException)
                                {

                                }
                                finally
                                {
                                    if (buffer.Array != null)
                                    {
                                        _bufferManager.ReturnBuffer(buffer.Array);
                                    }
                                }

                                _settings.UploadBlocksRequest.Remove(key);
                                _settings.DiffusionBlocksRequest.Remove(key);

                                this.OnUploadedEvent(new Key[] { key });
                            }
                        }

                        // PushBlock
                        if (messageManager.Priority > -128 && (_random.Next(0, 256 + 1) <= this.BlockPriority(connectionManager.Node)))
                        {
                            foreach (var key in messageManager.PullBlocksRequest
                                .ToArray()
                                .OrderBy(n => _random.Next()))
                            {
                                if (!_cacheManager.Contains(key)) continue;

                                ArraySegment<byte> buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = _cacheManager[key];

                                    connectionManager.PushBlock(key, buffer);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Block ({0})", NetworkConverter.ToBase64String(key.Hash)));
                                    _pushBlockCount++;

                                    messageManager.PullBlocksRequest.Remove(key);
                                    messageManager.PushBlocks.Add(key);

                                    messageManager.Priority--;

                                    // Infomation
                                    {
                                        if (_relayBlocks.Contains(key))
                                        {
                                            _relayBlockCount++;
                                        }
                                    }

                                    break;
                                }
                                catch (BlockNotFoundException)
                                {

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
                                .OrderBy(n => _random.Next())
                                .Take(12));
                        }
                    }
                }
            }
        }

        private void connectionManager_BlocksLinkEvent(object sender, PullBlocksLinkEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Keys == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BlocksLink ({0})", e.Keys.Count()));

            foreach (var header in e.Keys.Take(_maxLinkCount))
            {
                if (header == null || header.Hash == null || header.HashAlgorithm != HashAlgorithm.Sha512) continue;

                _messagesManager[connectionManager.Node].PullBlocksLink.Add(header);
                _pullBlockLinkCount++;
            }
        }

        private void connectionManager_BlocksRequestEvent(object sender, PullBlocksRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Keys == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BlocksRequest ({0})", e.Keys.Count()));

            foreach (var header in e.Keys.Take(_maxRequestCount))
            {
                if (header == null || header.Hash == null || header.HashAlgorithm != HashAlgorithm.Sha512) continue;

                _messagesManager[connectionManager.Node].PullBlocksRequest.Add(header);
                _pullBlockRequestCount++;
            }
        }

        private void connectionManager_BlockEvent(object sender, PullBlockEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            try
            {
                if (e.Key == null || e.Key.Hash == null || e.Key.HashAlgorithm != HashAlgorithm.Sha512 || e.Value.Array == null) return;

                Debug.WriteLine(string.Format("ConnectionManager: Pull Block ({0})", NetworkConverter.ToBase64String(e.Key.Hash)));
                _pullBlockCount++;

                if (_messagesManager[connectionManager.Node].PushBlocksRequest.Contains(e.Key))
                {
                    _messagesManager[connectionManager.Node].PushBlocksRequest.Remove(e.Key);
                    _messagesManager[connectionManager.Node].PushBlocks.Add(e.Key);
                    _messagesManager[connectionManager.Node].LastPullTime = DateTime.UtcNow;
                    _messagesManager[connectionManager.Node].Priority++;

                    // Infomathon
                    {
                        _relayBlocks.Add(e.Key);
                    }
                }
                else
                {
                    _settings.DiffusionBlocksRequest.Add(e.Key);
                }

                try
                {
                    _cacheManager[e.Key] = e.Value;
                }
                catch (Exception)
                {

                }
            }
            finally
            {
                if (e.Value.Array != null)
                {
                    _bufferManager.ReturnBuffer(e.Value.Array);
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

                if (_routeTable.Count > 100)
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
                if (!_removeNodes.Contains(connectionManager.Node))
                {
                    if (connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                        _cuttingNodes.Add(connectionManager.Node);
                }

                this.RemoveConnectionManager(connectionManager);
            }
            catch (Exception)
            {

            }
        }

        #endregion

        protected virtual void OnUploadedEvent(IEnumerable<Key> keys)
        {
            if (this.UploadedEvent != null)
            {
                this.UploadedEvent(this, keys);
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                foreach (var node in nodes)
                {
                    if (node == null || node.Id == null || node.Uris.Where(n => _clientManager.CheckUri(n)).Count() == 0 || _removeNodes.Contains(node)) continue;

                    _routeTable.Live(node);
                }
            }
        }

        public bool DownloadWaiting(Key key)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                if (_downloadBlocks.Contains(key))
                    return true;

                List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node));

                foreach (var node in nodes)
                {
                    if (_messagesManager[node].PushBlocksRequest.Contains(key))
                        return true;
                }

                return false;
            }
        }

        public void Download(Key key)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _downloadBlocks.Add(key);
            }
        }

        public bool UploadWaiting(Key key)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                if (_settings.UploadBlocksRequest.Contains(key))
                    return true;

                return false;
            }
        }

        public void Upload(Key key)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _settings.UploadBlocksRequest.Add(key);
            }
        }

        public override ManagerState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                    if (node == null || node.Id == null || node.Uris.Count == 0) return;

                    _routeTable.Add(node);
                }

                _cacheManager.CheckSeeds();

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
                    new Library.Configuration.SettingsContext<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingsContext<Node>() { Name = "BaseNode", Value = new Node() },
                    new Library.Configuration.SettingsContext<int>() { Name = "ConnectionCountLimit", Value = 12 },
                    new Library.Configuration.SettingsContext<int>() { Name = "UploadingConnectionCountLowerLimit", Value = 3 },
                    new Library.Configuration.SettingsContext<int>() { Name = "DownloadingConnectionCountLowerLimit", Value = 3 },
                    new Library.Configuration.SettingsContext<long>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingsContext<LockedHashSet<Key>>() { Name = "DiffusionBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingsContext<LockedHashSet<Key>>() { Name = "UploadBlocksRequest", Value = new LockedHashSet<Key>() },
                })
            {

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

            public long BandwidthLimit
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (long)this["BandwidthLimit"];
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

            public LockedHashSet<Key> DiffusionBlocksRequest
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedHashSet<Key>)this["DiffusionBlocksRequest"];
                    }
                }
            }

            public LockedHashSet<Key> UploadBlocksRequest
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedHashSet<Key>)this["UploadBlocksRequest"];
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

            if (disposing)
            {

            }

            _disposed = true;
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
