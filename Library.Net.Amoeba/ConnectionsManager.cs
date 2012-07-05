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
    delegate void UploadedEventHandler(object sender, Key key);

    class ConnectionsManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Kademlia<Node> _routeTable;
        private Random _random = new Random();

        private byte[] _mySessionId;

        private LockedList<ConnectionManager> _connectionManagers;
        private MessagesManager _messagesManager;

        private LockedDictionary<Node, LockedHashSet<Key>> _pushBlocksLinkDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();
        private LockedDictionary<Node, LockedHashSet<Key>> _pushBlocksRequestDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();

        private LockedList<Node> _creatingNodes;
        private LockedHashSet<Node> _cuttingNodes;
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

        private bool _disposed = false;
        private object _thisLock = new object();

        private readonly int _maxNodeCount = 128;
        private readonly int _maxLinkCount = 8192;
        private readonly int _maxRequestCount = 8192;

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

            _creatingNodes = new LockedList<Node>();
            _cuttingNodes = new LockedHashSet<Node>();
            _removeNodes = new CirculationCollection<Node>(new TimeSpan(0, 10, 0));
            _nodesStatus = new LockedDictionary<Node, int>();

            _downloadBlocks = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));

            _relayBlocks = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));

            this.UpdateSessionId();
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

        public int UploadingConnectionCountLowerLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _settings.UploadingConnectionCountLowerLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _settings.UploadingConnectionCountLowerLimit = value;
                }
            }
        }

        public int DownloadingConnectionCountLowerLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _settings.DownloadingConnectionCountLowerLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _settings.DownloadingConnectionCountLowerLimit = value;
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

                    contexts.Add(new InformationContext("BlockCount", _cacheManager.Count));
                    contexts.Add(new InformationContext("RelayBlockCount", _relayBlockCount));

                    contexts.Add(new InformationContext("AcceptConnectionCount", _acceptConnectionCount));
                    contexts.Add(new InformationContext("CreateConnectionCount", _createConnectionCount));

                    contexts.Add(new InformationContext("OtherNodeCount", _routeTable.Count));

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
                List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node));

                if (nodes.Count <= 1) return 0.5;

                nodes.Sort(new Comparison<Node>((Node x, Node y) =>
                {
                    return _messagesManager[x].Priority.CompareTo(_messagesManager[y].Priority);
                }));

                int i = 1;
                while (i < nodes.Count && nodes[i] != node) i++;

                return ((double)i / (double)nodes.Count);
            }
        }

        private double ResponseTimePriority(Node node)
        {
            lock (this.ThisLock)
            {
                List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node));

                if (nodes.Count <= 1) return 0.5;

                nodes.Sort(new Comparison<Node>((Node x, Node y) =>
                {
                    var tx = _connectionManagers.FirstOrDefault(n => n.Node == x);
                    var ty = _connectionManagers.FirstOrDefault(n => n.Node == y);

                    if (tx == null && ty == null) return 0;
                    else if (tx == null) return -1;
                    else if (ty == null) return 1;

                    return ty.ResponseTime.CompareTo(tx.ResponseTime);
                }));

                int i = 1;
                while (i < nodes.Count && nodes[i] != node) i++;

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
                if (_searchNodeStopwatch.Elapsed.TotalSeconds > 10 || !_searchNodeStopwatch.IsRunning)
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

            var requestNodes = Kademlia<Node>.Sort(this.BaseNode, id, _searchNodes).ToList();
            var returnNodes = new List<Node>();

            foreach (var item in requestNodes)
            {
                if (_connectionsNodes.Contains(item))
                {
                    returnNodes.Add(item);
                }
                else
                {
                    var list = _connectionsNodes.Where(n => _messagesManager[n].SurroundingNodes.Contains(item)).ToList();

                    list.Sort(new Comparison<Node>((Node x, Node y) =>
                    {
                        return _responseTimeDic[x].CompareTo(_responseTimeDic[y]);
                    }));

                    returnNodes.AddRange(list.Where(n => !returnNodes.Contains(n)));
                }

                if (returnNodes.Count >= count) break;
            }

            return returnNodes.Take(count);
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

                if (_connectionManagers.Count >= this.ConnectionCountLimit)
                {
                    // PushNodes
                    try
                    {
                        var nodes = _connectionManagers.Select(n => n.Node).ToList();
                        nodes.Remove(connectionManager.Node);

                        if (nodes.Count > 0)
                        {
                            connectionManager.PushNodes(nodes);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodeCount += nodes.Count;
                        }
                    }
                    catch (ConnectionManagerException)
                    {

                    }

                    try
                    {
                        connectionManager.PushCancel();
                    }
                    catch (ConnectionManagerException)
                    {

                    }

                    connectionManager.Dispose();

                    Debug.WriteLine("ConnectionManager: Push Cancel");
                    return;
                }

                Debug.WriteLine("ConnectionManager: Connect");

                connectionManager.PullNodesEvent += new PullNodesEventHandler(connectionManager_NodesEvent);
                connectionManager.PullBlocksLinkEvent += new PullBlocksLinkEventHandler(connectionManager_BlocksLinkEvent);
                connectionManager.PullBlocksRequestEvent += new PullBlocksRequestEventHandler(connectionManager_BlocksRequestEvent);
                connectionManager.PullBlockEvent += new PullBlockEventHandler(connectionManager_BlockEvent);
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

                            _removeNodes.Add(connectionManager.Node);
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

                if (_serverManager.ListenUris.Count > 0
                    && _connectionManagers.Count > (this.ConnectionCountLimit / 3))
                {
                    continue;
                }
                else if (_connectionManagers.Count > this.ConnectionCountLimit)
                {
                    continue;
                }

                if (_routeTable.Count > 0)
                {
                    Node node = null;

                    lock (this.ThisLock)
                    {
                        node = _cuttingNodes
                            .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id)) && !_creatingNodes.Contains(n))
                            .FirstOrDefault();

                        if (node == null)
                        {
                            node = _routeTable.ToArray()
                                .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id)) && !_creatingNodes.Contains(n))
                                .OrderBy(n => _random.Next())
                                .FirstOrDefault();
                        }

                        if (node == null) continue;

                        _creatingNodes.Add(node);
                    }

                    Thread.Sleep(1000 * 3);

                    try
                    {
                        foreach (var uri in node.Uris.Take(5).ToArray())
                        {
                            if (this.State == ManagerState.Stop) return;

                            var connection = _clientManager.CreateConnection(uri);

                            if (connection != null)
                            {
                                var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, _bufferManager);

                                try
                                {
                                    connectionManager.Connect();
                                    if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();

                                    _nodesStatus.Remove(node);
                                    _cuttingNodes.Remove(node);

                                    if (node != connectionManager.Node)
                                    {
                                        _removeNodes.Add(node);
                                        _routeTable.Remove(node);
                                    }

                                    if (connectionManager.Node.Uris.Count != 0) _routeTable.Live(connectionManager.Node);

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
                                if (!_nodesStatus.ContainsKey(node)) _nodesStatus[node] = 0;
                                _nodesStatus[node]++;

                                if (_nodesStatus[node] >= 10)
                                {
                                    _nodesStatus.Remove(node);
                                    _removeNodes.Add(node);
                                    _cuttingNodes.Remove(node);

                                    if (_routeTable.Count > 50)
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
                    var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, _bufferManager);

                    try
                    {
                        connectionManager.Connect();
                        if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();

                        if (connectionManager.Node.Uris.Count != 0) _routeTable.Add(connectionManager.Node);

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

        private void ConnectionsManagerThread()
        {
            Stopwatch pushStopwatch = new Stopwatch();
            pushStopwatch.Start();
            Stopwatch seedRemoveStopwatch = new Stopwatch();
            seedRemoveStopwatch.Start();

            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                if (seedRemoveStopwatch.Elapsed.Minutes >= 30)
                {
                    seedRemoveStopwatch.Restart();

                    _cacheManager.CheckSeeds();
                }

                if (_connectionManagers.Count >= this.DownloadingConnectionCountLowerLimit && pushStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushStopwatch.Restart();

                    HashSet<Key> pushBlocksLinkList = new HashSet<Key>();
                    HashSet<Key> pushBlocksRequestList = new HashSet<Key>();
                    List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node));

                    {
                        {
                            var list = _cacheManager.Where(n => n != null && n.Hash != null && n.HashAlgorithm == HashAlgorithm.Sha512)
                                .OrderBy(n => _random.Next()).ToList();

                            int count = (int)((8192) / (_connectionManagers.Count + 1));

                            for (int i = 0, j = 0; j < count && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushBlocksLink.Contains(list[i])))
                                {
                                    pushBlocksLinkList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _downloadBlocks.Where(n => n != null && n.Hash != null && n.HashAlgorithm == HashAlgorithm.Sha512)
                                .OrderBy(n => _random.Next()).ToList();

                            int count = (int)((8192) / (_connectionManagers.Count + 1));

                            for (int i = 0, j = 0; j < count && i < list.Count; i++)
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
                            var list = messageManager.PullBlocksLink.OrderBy(n => _random.Next()).ToList();

                            int count = (int)((8192) / (_connectionManagers.Count + 1));

                            for (int i = 0, j = 0; j < count && i < list.Count; i++)
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
                            var list = messageManager.PullBlocksRequest.OrderBy(n => _random.Next()).ToList();

                            if (list.Any(n => _cacheManager.Contains(n))) continue;

                            int count = (int)((8192) / (_connectionManagers.Count + 1));

                            for (int i = 0, j = 0; j < count && i < list.Count; i++)
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
                        Parallel.ForEach(pushBlocksLinkList, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, item =>
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
                        });

                        lock (this.ThisLock)
                        {
                            lock (_pushBlocksLinkDictionary.ThisLock)
                            {
                                _pushBlocksLinkDictionary.Clear();

                                foreach (var item in pushBlocksLinkDictionary)
                                {
                                    _pushBlocksLinkDictionary.Add(item.Key, item.Value);
                                }
                            }
                        }

                        //foreach (var item in pushBlocksRequestList)
                        Parallel.ForEach(pushBlocksRequestList, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, item =>
                        {
                            List<Node> requestNodes = new List<Node>();

                            foreach (var node in nodes)
                            {
                                if (_messagesManager[node].PullBlocksLink.Contains(item))
                                {
                                    requestNodes.Add(node);
                                }
                            }

                            requestNodes = requestNodes.OrderBy(n => _random.Next()).ToList();

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
                        });

                        lock (this.ThisLock)
                        {
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
                }
            }
        }

        private void ConnectionManagerThread(object state)
        {
            Thread.CurrentThread.Name = "ConnectionManagerThread";
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            var connectionManager = state as ConnectionManager;
            if (connectionManager == null) return;

            try
            {
                var messageManager = _messagesManager[connectionManager.Node];

                Stopwatch nodeUpdateTime = new Stopwatch();
                Stopwatch updateTime = new Stopwatch();
                Stopwatch checkTime = new Stopwatch();
                checkTime.Start();

                for (; ; )
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;
                    if (!_connectionManagers.Contains(connectionManager)) return;

                    // Check
                    if (checkTime.Elapsed.TotalSeconds > 180)
                    {
                        checkTime.Restart();

                        if ((this.ConnectionCountLimit - _connectionManagers.Count) < (this.ConnectionCountLimit / 3)
                            && _connectionManagers.Count >= 3)
                        {
                            List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node));

                            nodes.Sort(new Comparison<Node>((Node x, Node y) =>
                            {
                                return _messagesManager[x].Priority.CompareTo(_messagesManager[y].Priority);
                            }));

                            if (nodes.IndexOf(connectionManager.Node) < 3)
                            {
                                connectionManager.PushCancel();

                                Debug.WriteLine("ConnectionManager: Push Cancel");
                                return;
                            }
                        }
                    }

                    // PushNodes
                    if (!nodeUpdateTime.IsRunning || nodeUpdateTime.Elapsed.TotalSeconds > 60)
                    {
                        nodeUpdateTime.Restart();

                        var nodes = _connectionManagers.Select(n => n.Node).ToList();
                        nodes.Remove(connectionManager.Node);

                        if (nodes.Count > 0)
                        {
                            connectionManager.PushNodes(nodes);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodeCount += nodes.Count;
                        }
                    }

                    if (!updateTime.IsRunning || updateTime.Elapsed.TotalSeconds > 60)
                    {
                        updateTime.Restart();

                        // PushBlocksLink
                        if (_connectionManagers.Count >= this.UploadingConnectionCountLowerLimit)
                        {
                            KeyCollection tempList = null;
                            int count = (int)((4096 / _connectionManagers.Count) * this.ResponseTimePriority(connectionManager.Node));

                            lock (this.ThisLock)
                            {
                                lock (_pushBlocksLinkDictionary.ThisLock)
                                {
                                    if (_pushBlocksLinkDictionary.ContainsKey(connectionManager.Node))
                                    {
                                        tempList = new KeyCollection(_pushBlocksLinkDictionary[connectionManager.Node]
                                            .OrderBy(n => _random.Next()).Take(count));

                                        _pushBlocksLinkDictionary[connectionManager.Node].ExceptWith(tempList);
                                        _messagesManager[connectionManager.Node].PushBlocksLink.AddRange(tempList);
                                    }
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
                        if (_connectionManagers.Count >= this.DownloadingConnectionCountLowerLimit)
                        {
                            KeyCollection tempList = null;
                            int count = (int)((4096 / _connectionManagers.Count) * this.ResponseTimePriority(connectionManager.Node));

                            lock (this.ThisLock)
                            {
                                lock (_pushBlocksRequestDictionary.ThisLock)
                                {
                                    if (_pushBlocksRequestDictionary.ContainsKey(connectionManager.Node))
                                    {
                                        tempList = new KeyCollection(_pushBlocksRequestDictionary[connectionManager.Node]
                                            .OrderBy(n => _random.Next()).Take(count));

                                        _pushBlocksRequestDictionary[connectionManager.Node].ExceptWith(tempList);
                                        _messagesManager[connectionManager.Node].PushBlocksRequest.AddRange(tempList);
                                    }
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

                    if (_connectionManagers.Count >= this.UploadingConnectionCountLowerLimit)
                    {
                        // PushBlock (Upload)
                        {
                            List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node.DeepClone()));
                            KeyCollection uploadKeys = new KeyCollection();

                            {
                                KeyCollection tempKeys = new KeyCollection();

                                tempKeys.AddRange(_settings.UploadBlocksRequest);
                                tempKeys.AddRange(_settings.DiffusionBlocksRequest);

                                foreach (var key in tempKeys.OrderBy(n => _random.Next()))
                                {
                                    if (nodes.Any(n => _messagesManager[n].PushBlocks.Contains(key)))
                                    {
                                        _settings.UploadBlocksRequest.Remove(key);
                                        _settings.DiffusionBlocksRequest.Remove(key);

                                        this.OnUploadedEvent(key);
                                    }
                                    else if (!_cacheManager.Contains(key))
                                    {
                                        _settings.UploadBlocksRequest.Remove(key);
                                        _settings.DiffusionBlocksRequest.Remove(key);

                                        this.OnUploadedEvent(key);
                                    }
                                    else
                                    {
                                        var searchNodes = this.GetSearchNode(key.Hash, 1);

                                        if (searchNodes.Count() == 0)
                                        {
                                            _settings.UploadBlocksRequest.Remove(key);
                                            _settings.DiffusionBlocksRequest.Remove(key);

                                            this.OnUploadedEvent(key);
                                        }
                                        else if (searchNodes.First() == connectionManager.Node)
                                        {
                                            _settings.UploadBlocksRequest.Remove(key);
                                            _settings.DiffusionBlocksRequest.Remove(key);

                                            uploadKeys.Add(key);

                                            break;
                                        }
                                    }
                                }
                            }

                            foreach (var key in uploadKeys)
                            {
                                ArraySegment<byte> buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = _cacheManager[key];

                                    connectionManager.PushBlock(key, buffer);
                                  
                                    Debug.WriteLine(string.Format("ConnectionManager: Push Block ({0})", NetworkConverter.ToBase64String(key.Hash)));
                                    _pushBlockCount++;
                                    
                                    messageManager.PullBlocksRequest.Remove(key);
                                    messageManager.PushBlocks.Add(key);
                                }
                                catch (ConnectionManagerException e)
                                {
                                    _settings.DiffusionBlocksRequest.Add(key);

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

                                this.OnUploadedEvent(key);
                            }
                        }

                        // PushBlock
                        if ((_random.Next(0, 100) + 1) <= (int)(100 * this.BlockPriority(connectionManager.Node)))
                        {
                            foreach (var key in messageManager.PullBlocksRequest.OrderBy(n => _random.Next()).ToArray())
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
                if (node == null || node.Id == null || node.Uris.Count == 0 || _removeNodes.Contains(node)) continue;

                _routeTable.Add(node);
                _pullNodeCount++;
            }

            lock (this.ThisLock)
            {
                lock (_messagesManager.ThisLock)
                {
                    _messagesManager[connectionManager.Node].SurroundingNodes.Clear();
                    _messagesManager[connectionManager.Node].SurroundingNodes
                        .UnionWith(e.Nodes.Take(_maxNodeCount).Where(n => n != null && n.Id != null));
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

            if (connectionManager.Node.Uris.Count != 0) _routeTable.Live(connectionManager.Node);
        }

        private void connectionManager_PullCancelEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            Debug.WriteLine("ConnectionManager: Pull Cancel");

            try
            {
                _cuttingNodes.Remove(connectionManager.Node);

                if (_routeTable.Count > 50)
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
                    _cuttingNodes.Add(connectionManager.Node);
                }

                this.RemoveConnectionManager(connectionManager);
            }
            catch (Exception)
            {

            }
        }

        #endregion

        protected virtual void OnUploadedEvent(Key key)
        {
            if (this.UploadedEvent != null)
            {
                this.UploadedEvent(this, key);
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                foreach (var node in nodes)
                {
                    if (node == null || node.Id == null || node.Uris.Count == 0 || _removeNodes.Contains(node)) continue;

                    _routeTable.Add(node);
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
                _createClientConnection1Thread.Name = "CreateClientConnection1Thread";
                _createClientConnection1Thread.Start();
                _createClientConnection2Thread = new Thread(this.CreateClientConnectionThread);
                _createClientConnection2Thread.Name = "CreateClientConnection2Thread";
                _createClientConnection2Thread.Start();
                _createClientConnection3Thread = new Thread(this.CreateClientConnectionThread);
                _createClientConnection3Thread.Name = "CreateClientConnection3Thread";
                _createClientConnection3Thread.Start();
                _createServerConnection1Thread = new Thread(this.CreateServerConnectionThread);
                _createServerConnection1Thread.Name = "CreateServerConnection1Thread";
                _createServerConnection1Thread.Start();
                _createServerConnection2Thread = new Thread(this.CreateServerConnectionThread);
                _createServerConnection2Thread.Name = "CreateServerConnection2Thread";
                _createServerConnection2Thread.Start();
                _createServerConnection3Thread = new Thread(this.CreateServerConnectionThread);
                _createServerConnection3Thread.Name = "CreateServerConnection3Thread";
                _createServerConnection3Thread.Start();
                _connectionsManagerThread = new Thread(this.ConnectionsManagerThread);
                _connectionsManagerThread.Priority = ThreadPriority.Lowest;
                _connectionsManagerThread.Name = "ConnectionsManagerThread";
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

            public int UploadingConnectionCountLowerLimit
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (int)this["UploadingConnectionCountLowerLimit"];
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        this["UploadingConnectionCountLowerLimit"] = value;
                    }
                }
            }

            public int DownloadingConnectionCountLowerLimit
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (int)this["DownloadingConnectionCountLowerLimit"];
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        this["DownloadingConnectionCountLowerLimit"] = value;
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
            lock (this.ThisLock)
            {
                if (_disposed) return;

                if (disposing)
                {
                    this.Stop();
                }

                _disposed = true;
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
