using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Amoeba
{
    public delegate IEnumerable<string> LockSeedSignaturesEventHandler(object sender);
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

        private LockedDictionary<Node, HashSet<Key>> _pushBlocksLinkDictionary = new LockedDictionary<Node, HashSet<Key>>();
        private LockedDictionary<Node, HashSet<Key>> _pushBlocksRequestDictionary = new LockedDictionary<Node, HashSet<Key>>();
        private LockedDictionary<Node, HashSet<Key>> _pushBlocksDictionary = new LockedDictionary<Node, HashSet<Key>>();
        private LockedDictionary<Node, HashSet<string>> _pushSeedsRequestDictionary = new LockedDictionary<Node, HashSet<string>>();

        private LockedList<Node> _creatingNodes;
        private VolatileCollection<Node> _waitingNodes;
        private VolatileCollection<Node> _cuttingNodes;
        private VolatileCollection<Node> _removeNodes;
        private VolatileDictionary<Node, int> _nodesStatus;

        private VolatileCollection<string> _pushSeedsRequestList;
        private VolatileCollection<Key> _downloadBlocks;

        private LockedDictionary<string, DateTime> _lastUsedSeedTimes = new LockedDictionary<string, DateTime>();

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
        private volatile int _pushSeedRequestCount;
        private volatile int _pushSeedCount;

        private volatile int _pullNodeCount;
        private volatile int _pullBlockLinkCount;
        private volatile int _pullBlockRequestCount;
        private volatile int _pullBlockCount;
        private volatile int _pullSeedRequestCount;
        private volatile int _pullSeedCount;

        private VolatileCollection<Key> _relayBlocks;
        private volatile int _relayBlockCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        private LockSeedSignaturesEventHandler _lockSeedSignaturesEvent;
        private UploadedEventHandler _uploadedEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        private const int _maxNodeCount = 128;
        private const int _maxBlockLinkCount = 2048;
        private const int _maxBlockRequestCount = 2048;
        private const int _maxSeedRequestCount = 1024;
        private const int _maxSeedCount = 1024;

        private const int _routeTableMinCount = 100;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        public static readonly string Keyword_Link = "_link_";
        public static readonly string Keyword_Store = "_store_";

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

            _settings = new Settings(this.ThisLock);

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
            _waitingNodes = new VolatileCollection<Node>(new TimeSpan(0, 0, 30));
            _cuttingNodes = new VolatileCollection<Node>(new TimeSpan(0, 30, 0));
            _removeNodes = new VolatileCollection<Node>(new TimeSpan(0, 10, 0));
            _nodesStatus = new VolatileDictionary<Node, int>(new TimeSpan(0, 30, 0));

            _downloadBlocks = new VolatileCollection<Key>(new TimeSpan(0, 30, 0));
            _pushSeedsRequestList = new VolatileCollection<string>(new TimeSpan(0, 3, 0));

            _relayBlocks = new VolatileCollection<Key>(new TimeSpan(0, 30, 0));

            this.UpdateSessionId();
        }

        public LockSeedSignaturesEventHandler LockSeedSignaturesEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _lockSeedSignaturesEvent = value;
                }
            }
        }

        public event UploadedEventHandler UploadedEvent
        {
            add
            {
                lock (this.ThisLock)
                {
                    _uploadedEvent += value;
                }
            }
            remove
            {
                lock (this.ThisLock)
                {
                    _uploadedEvent -= value;
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
                    contexts.Add(new InformationContext("PushBlockLinkCount", _pushBlockLinkCount));
                    contexts.Add(new InformationContext("PushBlockRequestCount", _pushBlockRequestCount));
                    contexts.Add(new InformationContext("PushBlockCount", _pushBlockCount));
                    contexts.Add(new InformationContext("PushSeedRequestCount", _pushSeedRequestCount));
                    contexts.Add(new InformationContext("PushSeedCount", _pushSeedCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PullBlockLinkCount", _pullBlockLinkCount));
                    contexts.Add(new InformationContext("PullBlockRequestCount", _pullBlockRequestCount));
                    contexts.Add(new InformationContext("PullBlockCount", _pullBlockCount));
                    contexts.Add(new InformationContext("PullSeedRequestCount", _pullSeedRequestCount));
                    contexts.Add(new InformationContext("PullSeedCount", _pullSeedCount));

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

        protected virtual IEnumerable<string> OnLockSeedSignaturesEvent()
        {
            if (_lockSeedSignaturesEvent != null)
            {
                return _lockSeedSignaturesEvent(this);
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

        private void CheckNode(Node node)
        {
            lock (this.ThisLock)
            {
                if (!_removeNodes.Contains(node))
                {
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
                }
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
                if (!_searchNodeStopwatch.IsRunning || _searchNodeStopwatch.Elapsed.TotalSeconds >= 10)
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
                var requestNodes = Kademlia<Node>.Sort(this.BaseNode.Id, id, _searchNodes).ToList();
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
                                    foreach (var node in _routeTable)
                                    {
                                        if (connectionManager.Node == node) continue;
                                        nodes.Add(node);

                                        if (nodes.Count >= 50) break;
                                    }
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
                connectionManager.PullSeedsEvent += new PullSeedsEventHandler(connectionManager_SeedsEvent);
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
                        .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id))
                            && !_creatingNodes.Contains(n)
                            && !_waitingNodes.Contains(n))
                        .Randomize()
                        .FirstOrDefault();

                    if (node == null)
                    {
                        node = _routeTable
                            .ToArray()
                            .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id))
                                && !_creatingNodes.Contains(n)
                                && !_waitingNodes.Contains(n))
                            .Randomize()
                            .FirstOrDefault();
                    }

                    if (node == null) continue;

                    _creatingNodes.Add(node);
                    _waitingNodes.Add(node);
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
                        lock (this.ThisLock)
                        {
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

                        var connection = _clientManager.CreateConnection(uri, _bandwidthLimit);

                        if (connection != null)
                        {
                            var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectionManagerType.Client, _bufferManager);

                            try
                            {
                                connectionManager.Connect();
                                if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();

                                lock (this.ThisLock)
                                {
                                    _cuttingNodes.Remove(node);

                                    if (node != connectionManager.Node)
                                    {
                                        _removeNodes.Add(node);
                                        _routeTable.Remove(node);
                                    }

                                    _routeTable.Live(connectionManager.Node);
                                }

                                _createConnectionCount++;

                                this.AddConnectionManager(connectionManager, uri);

                                goto End;
                            }
#if DEBUG
                            catch (Exception e)
                            {
                                Log.Information(e);

                                connectionManager.Dispose();
                            }
#else
                            catch (Exception)
                            {
                                connectionManager.Dispose();
                            }
#endif
                        }
                    }

                    this.CheckNode(node);
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

                        lock (this.ThisLock)
                        {
                            if (connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                            {
                                _routeTable.Add(connectionManager.Node);
                            }

                            _cuttingNodes.Remove(connectionManager.Node);
                        }

                        this.AddConnectionManager(connectionManager, uri);

                        _acceptConnectionCount++;
                    }
#if DEBUG
                    catch (Exception e)
                    {
                        Log.Information(e);

                        connectionManager.Dispose();
                    }
#else
                    catch (Exception)
                    {
                        connectionManager.Dispose();
                    }
#endif
                }
            }
        }

        private class NodeSortItem
        {
            public Node Node { get; set; }
            public TimeSpan ResponseTime { get; set; }
            public DateTime LastPullTime { get; set; }
        }

        private volatile bool _refreshThreadRunning = false;

        private void ConnectionsManagerThread()
        {
            Stopwatch connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();

            Stopwatch checkSeedsStopwatch = new Stopwatch();
            Stopwatch refreshStopwatch = new Stopwatch();

            Stopwatch pushBlockUploadStopwatch = new Stopwatch();
            pushBlockUploadStopwatch.Start();
            Stopwatch pushBlockDownloadStopwatch = new Stopwatch();
            pushBlockDownloadStopwatch.Start();

            Stopwatch pushSeedUploadStopwatch = new Stopwatch();
            pushSeedUploadStopwatch.Start();
            Stopwatch pushSeedDownloadStopwatch = new Stopwatch();
            pushSeedDownloadStopwatch.Start();

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
                    && connectionCheckStopwatch.Elapsed.TotalMinutes >= 30)
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

                    nodeSortItems.Sort((x, y) =>
                    {
                        int c = x.LastPullTime.CompareTo(y.LastPullTime);
                        if (c != 0) return c;

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
                                    lock (this.ThisLock)
                                    {
                                        _removeNodes.Add(connectionManager.Node);
                                        _routeTable.Remove(connectionManager.Node);
                                    }

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

                if (!checkSeedsStopwatch.IsRunning || checkSeedsStopwatch.Elapsed.TotalMinutes >= 30)
                {
                    checkSeedsStopwatch.Restart();

                    _cacheManager.CheckSeeds();
                }

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    refreshStopwatch.Restart();

                    var now = DateTime.UtcNow;

                    ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            {
                                var lockSeedSignatures = this.OnLockSeedSignaturesEvent();

                                if (lockSeedSignatures != null)
                                {
                                    var removeSignatures = new HashSet<string>();
                                    removeSignatures.UnionWith(_settings.GetSeedSignatures());
                                    removeSignatures.ExceptWith(lockSeedSignatures);

                                    var sortList = removeSignatures.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedSeedTimes.TryGetValue(x, out tx);
                                        _lastUsedSeedTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.RemoveSeedSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new HashSet<string>(_settings.GetSeedSignatures());

                                    foreach (var signature in _lastUsedSeedTimes.Keys.ToArray())
                                    {
                                        if (liveSignatures.Contains(signature)) continue;

                                        _lastUsedSeedTimes.Remove(signature);
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
                    && pushBlockUploadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushBlockUploadStopwatch.Restart();

                    HashSet<Key> pushBlocksList = new HashSet<Key>();

                    {
                        {
                            var list = _settings.UploadBlocksRequest
                                .ToArray()
                                .Where(n => n.HashAlgorithm == HashAlgorithm.Sha512)
                                .Randomize()
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
                                .Randomize()
                                .ToList();

                            int count = 1024;

                            for (int i = 0; i < count && i < list.Count; i++)
                            {
                                pushBlocksList.Add(list[i]);
                            }
                        }
                    }

                    {
                        Dictionary<Node, HashSet<Key>> pushBlocksDictionary = new Dictionary<Node, HashSet<Key>>();

                        foreach (var item in pushBlocksList)
                        {
                            try
                            {
                                var requestNodes = this.GetSearchNode(item.Hash, 2).ToList();

                                if (requestNodes.Count == 0)
                                {
                                    _settings.UploadBlocksRequest.Remove(item);
                                    _settings.DiffusionBlocksRequest.Remove(item);

                                    this.OnUploadedEvent(new Key[] { item });

                                    continue;
                                }

                                for (int i = 0; i < 2 && i < requestNodes.Count; i++)
                                {
                                    HashSet<Key> hashset;

                                    if (!pushBlocksDictionary.TryGetValue(requestNodes[i], out hashset))
                                    {
                                        hashset = new HashSet<Key>();
                                        pushBlocksDictionary[requestNodes[i]] = hashset;
                                    }

                                    hashset.Add(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

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

                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushBlockDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushBlockDownloadStopwatch.Restart();

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
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 2048 && i < list.Count; i++)
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
                            var list = messageManager.PullBlocksLink
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 2048 && i < list.Count; i++)
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
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 2048 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushBlocksRequest.Contains(list[i])) && !_cacheManager.Contains(list[i]))
                                {
                                    pushBlocksRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullBlocksRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            if (list.Any(n => _cacheManager.Contains(n))) continue;

                            for (int i = 0, j = 0; j < 2048 && i < list.Count; i++)
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
                        Dictionary<Node, HashSet<Key>> pushBlocksLinkDictionary = new Dictionary<Node, HashSet<Key>>();

                        foreach (var item in pushBlocksLinkList)
                        {
                            try
                            {
                                var requestNodes = this.GetSearchNode(item.Hash, 1).ToList();

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                                {
                                    if (!_messagesManager[requestNodes[i]].PullBlocksLink.Contains(item))
                                    {
                                        HashSet<Key> hashset;

                                        if (!pushBlocksLinkDictionary.TryGetValue(requestNodes[i], out hashset))
                                        {
                                            hashset = new HashSet<Key>();
                                            pushBlocksLinkDictionary[requestNodes[i]] = hashset;
                                        }

                                        hashset.Add(item);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushBlocksLinkDictionary.ThisLock)
                        {
                            _pushBlocksLinkDictionary.Clear();

                            foreach (var item in pushBlocksLinkDictionary)
                            {
                                _pushBlocksLinkDictionary.Add(item.Key, item.Value);
                            }
                        }
                    }

                    {
                        Dictionary<Node, HashSet<Key>> pushBlocksRequestDictionary = new Dictionary<Node, HashSet<Key>>();

                        foreach (var item in pushBlocksRequestList)
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
                                    .Randomize()
                                    .ToList();

                                if (requestNodes.Count == 0)
                                    requestNodes.AddRange(this.GetSearchNode(item.Hash, 1));

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                                {
                                    if (!_messagesManager[requestNodes[i]].PullBlocksRequest.Contains(item))
                                    {
                                        HashSet<Key> hashset;

                                        if (!pushBlocksRequestDictionary.TryGetValue(requestNodes[i], out hashset))
                                        {
                                            hashset = new HashSet<Key>();
                                            pushBlocksRequestDictionary[requestNodes[i]] = hashset;
                                        }

                                        hashset.Add(item);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

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

                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushSeedUploadStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    pushSeedUploadStopwatch.Restart();

                    foreach (var item in _settings.GetSeedSignatures())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(Signature.GetSignatureHash(item), 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullSeedsRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }
                }

                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushSeedDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushSeedDownloadStopwatch.Restart();

                    HashSet<string> pushSeedsRequestList = new HashSet<string>();
                    List<Node> nodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        nodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    {
                        {
                            var list = _pushSeedsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSeedsRequest.Contains(list[i])))
                                {
                                    pushSeedsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullSeedsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSeedsRequest.Contains(list[i])))
                                {
                                    pushSeedsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }
                    }

                    {
                        Dictionary<Node, HashSet<string>> pushSeedsRequestDictionary = new Dictionary<Node, HashSet<string>>();

                        foreach (var item in pushSeedsRequestList)
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(Signature.GetSignatureHash(item), 1));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<string> hashset;

                                    if (!pushSeedsRequestDictionary.TryGetValue(requestNodes[i], out hashset))
                                    {
                                        hashset = new HashSet<string>();
                                        pushSeedsRequestDictionary[requestNodes[i]] = hashset;
                                    }

                                    hashset.Add(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushSeedsRequestDictionary.ThisLock)
                        {
                            _pushSeedsRequestDictionary.Clear();

                            foreach (var item in pushSeedsRequestDictionary)
                            {
                                _pushSeedsRequestDictionary.Add(item.Key, item.Value);
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
                Stopwatch seedUpdateTime = new Stopwatch();
                seedUpdateTime.Start();

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
                    if (checkTime.Elapsed.TotalSeconds >= 60)
                    {
                        checkTime.Restart();

                        if ((DateTime.UtcNow - messageManager.LastPullTime) > new TimeSpan(0, 30, 0))
                        {
                            lock (this.ThisLock)
                            {
                                _removeNodes.Add(connectionManager.Node);
                                _routeTable.Remove(connectionManager.Node);
                            }

                            connectionManager.PushCancel();

                            Debug.WriteLine("ConnectionManager: Push Cancel");
                            return;
                        }
                    }

                    // PushNodes
                    if (!nodeUpdateTime.IsRunning || nodeUpdateTime.Elapsed.TotalSeconds >= 60)
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
                                .Where(n => n.Uris.Count() > 0)
                                .Take(12));
                        }

                        if (nodes.Count > 0)
                        {
                            connectionManager.PushNodes(nodes);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodeCount += nodes.Count;
                        }
                    }

                    if (updateTime.Elapsed.TotalSeconds >= 60)
                    {
                        updateTime.Restart();

                        // PushBlocksLink
                        if (_connectionManagers.Count >= _uploadingConnectionCountLowerLimit)
                        {
                            KeyCollection tempList = null;
                            int count = (int)(_maxBlockLinkCount * this.ResponseTimePriority(connectionManager.Node));

                            lock (_pushBlocksLinkDictionary.ThisLock)
                            {
                                HashSet<Key> hashset;

                                if (_pushBlocksLinkDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    tempList = new KeyCollection(hashset.Randomize().Take(count));

                                    hashset.ExceptWith(tempList);
                                    messageManager.PushBlocksLink.AddRange(tempList);
                                }
                            }

                            if (tempList != null && tempList.Count > 0)
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
                                        messageManager.PushBlocksLink.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }

                        // PushBlocksRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            KeyCollection tempList = null;
                            int count = (int)(_maxBlockRequestCount * this.ResponseTimePriority(connectionManager.Node));

                            lock (_pushBlocksRequestDictionary.ThisLock)
                            {
                                HashSet<Key> hashset;

                                if (_pushBlocksRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    tempList = new KeyCollection(hashset.Randomize().Take(count));

                                    hashset.ExceptWith(tempList);
                                    messageManager.PushBlocksRequest.AddRange(tempList);
                                }
                            }

                            if (tempList != null && tempList.Count > 0)
                            {
                                try
                                {
                                    connectionManager.PushBlocksRequest(tempList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BlocksRequest ({0})", tempList.Count));
                                    _pushBlockRequestCount += tempList.Count;

                                    foreach (var key in tempList)
                                    {
                                        _downloadBlocks.Remove(key);
                                    }
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        messageManager.PushBlocksRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }

                        // PushSeedsRequest
                        if (_connectionManagers.Count >= _downloadingConnectionCountLowerLimit)
                        {
                            SignatureCollection tempList = null;
                            int count = (int)(_maxSeedRequestCount * this.ResponseTimePriority(connectionManager.Node));

                            lock (_pushSeedsRequestDictionary.ThisLock)
                            {
                                HashSet<string> hashset;

                                if (_pushSeedsRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    tempList = new SignatureCollection(hashset.Randomize().Take(count));

                                    hashset.ExceptWith(tempList);
                                    messageManager.PushSeedsRequest.AddRange(tempList);
                                }
                            }

                            if (tempList != null && tempList.Count > 0)
                            {
                                try
                                {
                                    connectionManager.PushSeedsRequest(tempList);

                                    foreach (var item in tempList)
                                    {
                                        _pushSeedsRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push SeedsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                    _pushSeedRequestCount += tempList.Count;
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        messageManager.PushSeedsRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }
                    }

                    if ((_random.Next(0, 100) + 1) <= (int)(100 * this.ResponseTimePriority(connectionManager.Node)))
                    {
                        // PushBlock (Upload)
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            Key key = null;

                            lock (_pushBlocksDictionary.ThisLock)
                            {
                                if (_pushBlocksDictionary.ContainsKey(connectionManager.Node))
                                {
                                    key = _pushBlocksDictionary[connectionManager.Node]
                                        .ToArray()
                                        .Randomize()
                                        .FirstOrDefault();

                                    if (key != null)
                                    {
                                        _pushBlocksDictionary[connectionManager.Node].Remove(key);
                                        messageManager.PushBlocks.Add(key);
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

                                    Debug.WriteLine(string.Format("ConnectionManager: Upload Push Block ({0})", NetworkConverter.ToBase64UrlString(key.Hash)));
                                    _pushBlockCount++;

                                    messageManager.PullBlocksRequest.Remove(key);
                                    messageManager.PushBlocks.Add(key);
                                }
                                catch (ConnectionManagerException e)
                                {
                                    messageManager.PushBlocks.Remove(key);

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
                    }

                    if (messageManager.Priority > -128 && (_random.Next(0, 256 + 1) <= this.BlockPriority(connectionManager.Node)))
                    {
                        // PushBlock
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            foreach (var key in messageManager.PullBlocksRequest
                                .ToArray()
                                .Randomize())
                            {
                                if (!_cacheManager.Contains(key)) continue;

                                ArraySegment<byte> buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = _cacheManager[key];

                                    connectionManager.PushBlock(key, buffer);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Block ({0})", NetworkConverter.ToBase64UrlString(key.Hash)));
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

                    if (seedUpdateTime.Elapsed.TotalSeconds >= 60)
                    {
                        seedUpdateTime.Restart();

                        // PushSeed
                        if (_connectionManagers.Count >= _uploadingConnectionCountLowerLimit)
                        {
                            var signatures = new List<string>(messageManager.PullSeedsRequest.Randomize());

                            if (signatures.Count > 0)
                            {
                                // Link
                                {
                                    var seeds = new List<Seed>();

                                    foreach (var signature in signatures.Randomize())
                                    {
                                        Seed tempSeed = this.GetLinkSeed(signature);
                                        if (tempSeed == null) continue;

                                        DateTime creationTime;

                                        if (!messageManager.PushLinkSeeds.TryGetValue(signature, out creationTime)
                                            || tempSeed.CreationTime > creationTime)
                                        {
                                            seeds.Add(tempSeed);

                                            if (seeds.Count >= _maxSeedCount) goto End;
                                        }
                                    }

                                End: ;

                                    connectionManager.PushSeeds(seeds);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Seeds ({0})", seeds.Count));
                                    _pushSeedCount += seeds.Count;

                                    foreach (var seed in seeds)
                                    {
                                        var signature = seed.Certificate.ToString();

                                        messageManager.PushLinkSeeds[signature] = seed.CreationTime;
                                    }
                                }

                                // Store
                                {
                                    var seeds = new List<Seed>();

                                    foreach (var signature in signatures.Randomize())
                                    {
                                        Seed tempSeed = this.GetStoreSeed(signature);
                                        if (tempSeed == null) continue;

                                        DateTime creationTime;

                                        if (!messageManager.PushStoreSeeds.TryGetValue(signature, out creationTime)
                                            || tempSeed.CreationTime > creationTime)
                                        {
                                            seeds.Add(tempSeed);

                                            if (seeds.Count >= _maxSeedCount) goto End;
                                        }
                                    }

                                End: ;

                                    connectionManager.PushSeeds(seeds);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Seeds ({0})", seeds.Count));
                                    _pushSeedCount += seeds.Count;

                                    foreach (var seed in seeds)
                                    {
                                        var signature = seed.Certificate.ToString();

                                        messageManager.PushStoreSeeds[signature] = seed.CreationTime;
                                    }
                                }
                            }
                        }
                    }
                }
            }
#if DEBUG
            catch (Exception e)
            {
                Log.Information(e);
            }
#else
            catch (Exception)
            {

            }
#endif
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

        private void connectionManager_BlocksLinkEvent(object sender, PullBlocksLinkEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Keys == null) return;
            if (messageManager.PullBlocksLink.Count > _maxBlockLinkCount * messageManager.PullBlocksLink.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BlocksLink ({0})", e.Keys.Count()));

            foreach (var key in e.Keys.Take(_maxBlockLinkCount))
            {
                if (key == null || key.Hash == null || key.HashAlgorithm != HashAlgorithm.Sha512) continue;

                messageManager.PullBlocksLink.Add(key);
                _pullBlockLinkCount++;
            }
        }

        private void connectionManager_BlocksRequestEvent(object sender, PullBlocksRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Keys == null) return;
            if (messageManager.PullBlocksRequest.Count > _maxBlockRequestCount * messageManager.PullBlocksRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BlocksRequest ({0})", e.Keys.Count()));

            foreach (var key in e.Keys.Take(_maxBlockRequestCount))
            {
                if (key == null || key.Hash == null || key.HashAlgorithm != HashAlgorithm.Sha512) continue;

                messageManager.PullBlocksRequest.Add(key);
                _pullBlockRequestCount++;
            }
        }

        private void connectionManager_BlockEvent(object sender, PullBlockEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            try
            {
                if (e.Key == null || e.Key.Hash == null || e.Key.HashAlgorithm != HashAlgorithm.Sha512 || e.Value.Array == null) return;

                _cacheManager[e.Key] = e.Value;

                Debug.WriteLine(string.Format("ConnectionManager: Pull Block ({0})", NetworkConverter.ToBase64UrlString(e.Key.Hash)));
                _pullBlockCount++;

                if (messageManager.PushBlocksRequest.Contains(e.Key))
                {
                    messageManager.PushBlocksRequest.Remove(e.Key);
                    messageManager.PushBlocks.Add(e.Key);
                    messageManager.LastPullTime = DateTime.UtcNow;
                    messageManager.Priority++;

                    // Information
                    {
                        _relayBlocks.Add(e.Key);
                    }
                }
                else
                {
                    _settings.DiffusionBlocksRequest.Add(e.Key);
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

        private void connectionManager_SeedsRequestEvent(object sender, PullSeedsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Signatures == null) return;
            if (messageManager.PullSeedsRequest.Count > _maxSeedRequestCount * messageManager.PullSeedsRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SeedsRequest ({0})", e.Signatures.Count()));

            foreach (var signature in e.Signatures.Take(_maxSeedRequestCount))
            {
                if (signature == null || !Signature.HasSignature(signature)) continue;

                messageManager.PullSeedsRequest.Add(signature);
                _pullSeedRequestCount++;

                _lastUsedSeedTimes[signature] = DateTime.UtcNow;
            }
        }

        private void connectionManager_SeedsEvent(object sender, PullSeedsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Seeds == null) return;
            if (messageManager.PushLinkSeeds.Count > _maxSeedCount * messageManager.PushLinkSeeds.SurvivalTime.TotalMinutes) return;
            if (messageManager.PushStoreSeeds.Count > _maxSeedCount * messageManager.PushStoreSeeds.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Seeds ({0})", e.Seeds.Count()));

            foreach (var seed in e.Seeds.Take(_maxSeedCount))
            {
                if (_settings.SetLinkSeed(seed))
                {
                    var signature = seed.Certificate.ToString();

                    messageManager.PushLinkSeeds[signature] = seed.CreationTime;
                    messageManager.LastPullTime = DateTime.UtcNow;

                    _lastUsedSeedTimes[signature] = DateTime.UtcNow;
                }
                else if (_settings.SetStoreSeed(seed))
                {
                    var signature = seed.Certificate.ToString();

                    messageManager.PushStoreSeeds[signature] = seed.CreationTime;
                    messageManager.LastPullTime = DateTime.UtcNow;

                    _lastUsedSeedTimes[signature] = DateTime.UtcNow;
                }

                _pullSeedCount++;
            }
        }

        private void connectionManager_PullCancelEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            Debug.WriteLine("ConnectionManager: Pull Cancel");

            try
            {
                lock (this.ThisLock)
                {
                    _removeNodes.Add(connectionManager.Node);

                    if (_routeTable.Count > _routeTableMinCount)
                    {
                        _routeTable.Remove(connectionManager.Node);
                    }
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
                lock (this.ThisLock)
                {
                    this.CheckNode(connectionManager.Node);

                    if (!_removeNodes.Contains(connectionManager.Node)
                        && connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                    {
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

        protected virtual void OnUploadedEvent(IEnumerable<Key> keys)
        {
            if (_uploadedEvent != null)
            {
                _uploadedEvent(this, keys);
            }
        }

        public void SetBaseNode(Node baseNode)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (baseNode == null) throw new ArgumentNullException("baseNode");
            if (baseNode.Id == null) throw new ArgumentNullException("baseNode.Id");

            lock (this.ThisLock)
            {
                if (!Collection.Equals(_settings.BaseNode.Id, baseNode.Id))
                {
                    this.UpdateSessionId();
                }

                _settings.BaseNode = baseNode;
                _routeTable.BaseNode = baseNode;
            }
        }

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

        public bool IsDownloadWaiting(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
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

        public bool IsUploadWaiting(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (_settings.UploadBlocksRequest.Contains(key))
                    return true;

                return false;
            }
        }

        public void Download(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _downloadBlocks.Add(key);
            }
        }

        public void Upload(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.UploadBlocksRequest.Add(key);
            }
        }

        public void SendSeedRequest(string signature)
        {
            lock (this.ThisLock)
            {
                _pushSeedsRequestList.Add(signature);
            }
        }

        public IEnumerable<string> GetSeedSignatures()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetSeedSignatures();
            }
        }

        public Seed GetLinkSeed(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!Signature.HasSignature(signature)) return null;

            lock (this.ThisLock)
            {
                return _settings.GetLinkSeed(signature);
            }
        }

        public Seed GetStoreSeed(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!Signature.HasSignature(signature)) return null;

            lock (this.ThisLock)
            {
                return _settings.GetStoreSeed(signature);
            }
        }

        public void Upload(Seed seed)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.SetStoreSeed(seed);
                _settings.SetLinkSeed(seed);
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

                foreach (var node in _settings.OtherNodes.ToArray())
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
                {
                    var otherNodes = _routeTable.ToArray();

                    lock (_settings.OtherNodes.ThisLock)
                    {
                        _settings.OtherNodes.Clear();
                        _settings.OtherNodes.AddRange(otherNodes);
                    }
                }

                _settings.Save(directoryPath);
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<Node>() { Name = "BaseNode", Value = new Node(new byte[0], null)},
                    new Library.Configuration.SettingContent<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingContent<int>() { Name = "ConnectionCountLimit", Value = 12 },
                    new Library.Configuration.SettingContent<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "DiffusionBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "UploadBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<Dictionary<string, Seed>>() { Name = "LinkSeeds", Value = new Dictionary<string, Seed>() },
                    new Library.Configuration.SettingContent<Dictionary<string, Seed>>() { Name = "StoreSeeds", Value = new Dictionary<string, Seed>() },
                })
            {
                _thisLock = lockObject;
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

            public IEnumerable<string> GetSeedSignatures()
            {
                lock (_thisLock)
                {
                    HashSet<string> signatures = new HashSet<string>();
                    signatures.UnionWith(this.LinkSeeds.Keys);
                    signatures.UnionWith(this.StoreSeeds.Keys);

                    return signatures;
                }
            }

            public void RemoveSeedSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        this.LinkSeeds.Remove(signature);
                        this.StoreSeeds.Remove(signature);
                    }
                }
            }

            public Seed GetLinkSeed(string signature)
            {
                lock (_thisLock)
                {
                    if (!Signature.HasSignature(signature)) return null;

                    Seed seed;

                    if (this.LinkSeeds.TryGetValue(signature, out seed))
                    {
                        return seed;
                    }

                    return null;
                }
            }

            public Seed GetStoreSeed(string signature)
            {
                lock (_thisLock)
                {
                    if (!Signature.HasSignature(signature)) return null;

                    Seed seed;

                    if (this.StoreSeeds.TryGetValue(signature, out seed))
                    {
                        return seed;
                    }

                    return null;
                }
            }

            public bool SetLinkSeed(Seed seed)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (seed == null || seed.Name != null || seed.Comment != null
                        || seed.Keywords.Count != 1 || seed.Keywords[0] != ConnectionsManager.Keyword_Link
                        || (seed.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || seed.Certificate == null || !seed.VerifyCertificate()) return false;

                    var signature = seed.Certificate.ToString();

                    Seed tempSeed;

                    if (!this.LinkSeeds.TryGetValue(signature, out tempSeed)
                        || seed.CreationTime > tempSeed.CreationTime)
                    {
                        this.LinkSeeds[signature] = seed;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetStoreSeed(Seed seed)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (seed == null || seed.Name != null || seed.Comment != null
                        || seed.Keywords.Count != 1 || seed.Keywords[0] != ConnectionsManager.Keyword_Store
                        || (seed.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || seed.Certificate == null || !seed.VerifyCertificate()) return false;

                    var signature = seed.Certificate.ToString();

                    Seed tempSeed;

                    if (!this.StoreSeeds.TryGetValue(signature, out tempSeed)
                        || seed.CreationTime > tempSeed.CreationTime)
                    {
                        this.StoreSeeds[signature] = seed;

                        return true;
                    }

                    return false;
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

            public LockedHashSet<Key> DiffusionBlocksRequest
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<Key>)this["DiffusionBlocksRequest"];
                    }
                }
            }

            public LockedHashSet<Key> UploadBlocksRequest
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<Key>)this["UploadBlocksRequest"];
                    }
                }
            }

            private Dictionary<string, Seed> LinkSeeds
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<string, Seed>)this["LinkSeeds"];
                    }
                }
            }

            private Dictionary<string, Seed> StoreSeeds
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<string, Seed>)this["StoreSeeds"];
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
