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
    public delegate bool GetFilterSeedEventHandler(object sender, Seed seed);
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
        private LockedDictionary<Node, LockedHashSet<Keyword>> _pushSeedsLinkDictionary = new LockedDictionary<Node, LockedHashSet<Keyword>>();
        private LockedDictionary<Node, LockedHashSet<Keyword>> _pushSeedsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Keyword>>();

        private LockedList<Node> _creatingNodes;
        private LockedHashSet<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private LockedDictionary<Node, int> _nodesStatus;

        private CirculationCollection<Seed> _seeds;
        private CirculationCollection<Seed> _uploadSeeds;
        private CirculationCollection<Key> _downloadBlocks;

        private volatile Thread _connectionsManagerThread = null;
        private volatile Thread _createClientConnection1Thread = null;
        private volatile Thread _createClientConnection2Thread = null;
        private volatile Thread _createClientConnection3Thread = null;
        private volatile Thread _createServerConnectionThread = null;

        private ManagerState _state = ManagerState.Stop;

        private Dictionary<Node, string> _nodeToUri = new Dictionary<Node, string>();

        private long _receivedByteCount = 0;
        private long _sentByteCount = 0;

        private volatile int _pullNodesRequestCount;
        private volatile int _pullNodesCount;
        private volatile int _pullBlocksLinkCount;
        private volatile int _pullBlocksRequestCount;
        private volatile int _pullBlockCount;
        private volatile int _pullSeedsLinkCount;
        private volatile int _pullSeedsRequestCount;
        private volatile int _pullSeedsCount;

        private volatile int _pushNodesRequestCount;
        private volatile int _pushNodesCount;
        private volatile int _pushBlocksLinkCount;
        private volatile int _pushBlocksRequestCount;
        private volatile int _pushBlockCount;
        private volatile int _pushSeedsLinkCount;
        private volatile int _pushSeedsRequestCount;
        private volatile int _pushSeedsCount;

        private CirculationCollection<Key> _relayBlocks;
        private volatile int _relayBlockCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        public GetFilterSeedEventHandler GetFilterSeedEvent;
        internal UploadedEventHandler UploadedEvent;

        private bool _disposed = false;
        private object _thisLock = new object();

        private readonly int _seedMaxCount = 1000000;
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

            _seeds = new CirculationCollection<Seed>(new TimeSpan(1, 0, 0, 0));
            _uploadSeeds = new CirculationCollection<Seed>(new TimeSpan(1, 0, 0, 0));
            _downloadBlocks = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));

            _relayBlocks = new CirculationCollection<Key>(new TimeSpan(0, 30, 0));

            this.UpdateSessionId();
        }

        public Node BaseNode
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.BaseNode;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
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

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _routeTable.ToArray();
                }
            }
        }

        public IEnumerable<Seed> Seeds
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<Seed> list = new List<Seed>();
                    list.AddRange(_cacheManager.Seeds);
                    list.AddRange(_seeds);
                    list.AddRange(_uploadSeeds);

                    return list;
                }
            }
        }

        public KeywordCollection SearchKeywords
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.SearchKeywords;
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.ConnectionCountLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _settings.ConnectionCountLimit = value;
                }
            }
        }

        public int UploadingConnectionCountLowerLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.UploadingConnectionCountLowerLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _settings.UploadingConnectionCountLowerLimit = value;
                }
            }
        }

        public int DownloadingConnectionCountLowerLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.DownloadingConnectionCountLowerLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _settings.DownloadingConnectionCountLowerLimit = value;
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
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

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<InformationContext> contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("PullNodesRequestCount", _pullNodesRequestCount));
                    contexts.Add(new InformationContext("PullNodesCount", _pullNodesCount));
                    contexts.Add(new InformationContext("PullSeedsLinkCount", _pullSeedsLinkCount));
                    contexts.Add(new InformationContext("PullSeedsRequestCount", _pullSeedsRequestCount));
                    contexts.Add(new InformationContext("PullSeedsCount", _pullSeedsCount));
                    contexts.Add(new InformationContext("PullBlocksLinkCount", _pullBlocksLinkCount));
                    contexts.Add(new InformationContext("PullBlocksRequestCount", _pullBlocksRequestCount));
                    contexts.Add(new InformationContext("PullBlockCount", _pullBlockCount));

                    contexts.Add(new InformationContext("PushNodesRequestCount", _pushNodesRequestCount));
                    contexts.Add(new InformationContext("PushNodesCount", _pushNodesCount));
                    contexts.Add(new InformationContext("PushSeedsLinkCount", _pushSeedsLinkCount));
                    contexts.Add(new InformationContext("PushSeedsRequestCount", _pushSeedsRequestCount));
                    contexts.Add(new InformationContext("PushSeedsCount", _pushSeedsCount));
                    contexts.Add(new InformationContext("PushBlocksLinkCount", _pushBlocksLinkCount));
                    contexts.Add(new InformationContext("PushBlocksRequestCount", _pushBlocksRequestCount));
                    contexts.Add(new InformationContext("PushBlockCount", _pushBlockCount));

                    contexts.Add(new InformationContext("BlockCount", _cacheManager.Count));
                    contexts.Add(new InformationContext("RelayBlockCount", _relayBlockCount));

                    contexts.Add(new InformationContext("AcceptConnectionCount", _acceptConnectionCount));
                    contexts.Add(new InformationContext("CreateConnectionCount", _createConnectionCount));

                    return new Information(contexts);
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
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

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _sentByteCount + _connectionManagers.Sum(n => n.SentByteCount);
                }
            }
        }

        private void UpdateSessionId()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _mySessionId = new byte[64];
                (new System.Security.Cryptography.RNGCryptoServiceProvider()).GetBytes(_mySessionId);
            }
        }

        private double BlockPriority(Node node)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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
            using (DeadlockMonitor.Lock(this.ThisLock))
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
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_searchNodeStopwatch.Elapsed.TotalSeconds > 10 || !_searchNodeStopwatch.IsRunning)
                {
                    using (DeadlockMonitor.Lock(_connectionsNodes.ThisLock))
                    {
                        _connectionsNodes.Clear();
                        _connectionsNodes.UnionWith(_connectionManagers.Select(n => n.Node));
                    }

                    using (DeadlockMonitor.Lock(_searchNodes.ThisLock))
                    {
                        _searchNodes.Clear();

                        foreach (var node in _connectionsNodes)
                        {
                            var messageManager = _messagesManager[node];

                            _searchNodes.UnionWith(messageManager.SurroundingNodes);
                            _searchNodes.Add(node);
                        }

                        _searchNodeStopwatch.Restart();
                    }

                    using (DeadlockMonitor.Lock(_responseTimeDic.ThisLock))
                    {
                        _responseTimeDic.Clear();

                        foreach (var connectionManager in _connectionManagers)
                        {
                            _responseTimeDic.Add(connectionManager.Node, connectionManager.ResponseTime);
                        }
                    }
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

        //    using (DeadlockMonitor.Lock(this.ThisLock))
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
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (Collection.Equals(connectionManager.Node.Id, this.BaseNode.Id)
                    || _connectionManagers.Any(n => Collection.Equals(n.Node.Id, connectionManager.Node.Id)))
                {
                    connectionManager.Dispose();
                    return;
                }

                if (_connectionManagers.Count >= this.ConnectionCountLimit)
                {
                    connectionManager.PushCancel();
                    connectionManager.Dispose();

                    Debug.WriteLine("ConnectionManager: Push Cancel");
                    return;
                }

                Debug.WriteLine("ConnectionManager: Connect");

                connectionManager.PullNodesRequestEvent += new PullNodesRequestEventHandler(connectionManager_NodesRequestEvent);
                connectionManager.PullNodesEvent += new PullNodesEventHandler(connectionManager_NodesEvent);
                connectionManager.PullSeedsLinkEvent += new PullSeedsLinkEventHandler(connectionManager_SeedsLinkEvent);
                connectionManager.PullSeedsRequestEvent += new PullSeedsRequestEventHandler(connectionManager_SeedsRequestEvent);
                connectionManager.PullSeedsEvent += new PullSeedsEventHandler(connectionManager_SeedsEvent);
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
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                using (DeadlockMonitor.Lock(_connectionManagers.ThisLock))
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

                    using (DeadlockMonitor.Lock(this.ThisLock))
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

                if (seedRemoveStopwatch.Elapsed.TotalSeconds > 60)
                {
                    seedRemoveStopwatch.Restart();

                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        foreach (var key in _settings.UploadBlocksRequest.ToArray())
                        {
                            if (!_cacheManager.Contains(key))
                            {
                                _settings.UploadBlocksRequest.Remove(key);
                            }
                        }

                        foreach (var key in _settings.DiffusionBlocksRequest.ToArray())
                        {
                            if (!_cacheManager.Contains(key))
                            {
                                _settings.DiffusionBlocksRequest.Remove(key);
                            }
                        }
                    }

                    foreach (var item in _seeds.ToArray().Where(n => !this.OnGetFilterSeedEvent(n)))
                    {
                        _seeds.Remove(item);
                    }

                    foreach (var item in _uploadSeeds.ToArray().Where(n => !this.OnGetFilterSeedEvent(n)))
                    {
                        _uploadSeeds.Remove(item);
                    }

                    _cacheManager.ChecksSeed();

                    foreach (var item in _cacheManager.Seeds.ToArray().Where(n => !this.OnGetFilterSeedEvent(n)))
                    {
                        _cacheManager.RemoveSeed(item);
                    }
                }

                if (_connectionManagers.Count >= this.DownloadingConnectionCountLowerLimit && pushStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushStopwatch.Restart();

                    HashSet<Keyword> pushSeedsLinkList = new HashSet<Keyword>();
                    HashSet<Keyword> pushSeedsRequestList = new HashSet<Keyword>();
                    HashSet<Key> pushBlocksLinkList = new HashSet<Key>();
                    HashSet<Key> pushBlocksRequestList = new HashSet<Key>();
                    List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node));

                    {
                        {
                            HashSet<Keyword> tempList = new HashSet<Keyword>();

                            foreach (var key in _cacheManager.Seeds)
                            {
                                tempList.UnionWith(key.Keywords.Where(n => n != null && n.Value != null && n.HashAlgorithm == HashAlgorithm.Sha512));
                            }

                            foreach (var key in _uploadSeeds)
                            {
                                tempList.UnionWith(key.Keywords.Where(n => n != null && n.Value != null && n.HashAlgorithm == HashAlgorithm.Sha512));
                            }

                            var list = tempList.OrderBy(n => _random.Next()).ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSeedsLink.Contains(list[i])))
                                {
                                    pushSeedsLinkList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = this.SearchKeywords.Where(n => n != null && n.Value != null && n.HashAlgorithm == HashAlgorithm.Sha512)
                                .OrderBy(n => _random.Next()).ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                pushSeedsRequestList.Add(list[i]);
                                j++;
                            }
                        }

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
                            var list = messageManager.PullSeedsLink.OrderBy(n => _random.Next()).ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSeedsLink.Contains(list[i])))
                                {
                                    pushSeedsLinkList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullSeedsRequest.OrderBy(n => _random.Next()).ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                pushSeedsRequestList.Add(list[i]);
                                j++;
                            }
                        }

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
                        LockedDictionary<Node, LockedHashSet<Keyword>> pushSeedsLinkDictionary = new LockedDictionary<Node, LockedHashSet<Keyword>>();
                        LockedDictionary<Node, LockedHashSet<Keyword>> pushSeedsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Keyword>>();
                        LockedDictionary<Node, LockedHashSet<Key>> pushBlocksLinkDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();
                        LockedDictionary<Node, LockedHashSet<Key>> pushBlocksRequestDictionary = new LockedDictionary<Node, LockedHashSet<Key>>();

                        //foreach (var item in pushSeedsLinkList)
                        Parallel.ForEach(pushSeedsLinkList, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, item =>
                        {
                            var requestNodes = this.GetSearchNode(item.Hash, 1).ToList();

                            for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                            {
                                if (!_messagesManager[requestNodes[i]].PullSeedsLink.Contains(item))
                                {
                                    lock (pushSeedsLinkDictionary.ThisLock)
                                    {
                                        if (!pushSeedsLinkDictionary.ContainsKey(requestNodes[i]))
                                            pushSeedsLinkDictionary[requestNodes[i]] = new LockedHashSet<Keyword>();

                                        pushSeedsLinkDictionary[requestNodes[i]].Add(item);
                                    }
                                }
                            }
                        });

                        using (DeadlockMonitor.Lock(this.ThisLock))
                        {
                            using (DeadlockMonitor.Lock(_pushSeedsLinkDictionary.ThisLock))
                            {
                                _pushSeedsLinkDictionary.Clear();

                                foreach (var item in pushSeedsLinkDictionary)
                                {
                                    _pushSeedsLinkDictionary.Add(item.Key, item.Value);
                                }
                            }
                        }

                        //foreach (var item in pushSeedsRequestList)
                        Parallel.ForEach(pushSeedsRequestList, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, item =>
                        {
                            List<Node> requestNodes = new List<Node>();

                            foreach (var node in nodes)
                            {
                                if (_messagesManager[node].PullSeedsLink.Contains(item))
                                {
                                    requestNodes.Add(node);
                                }
                            }

                            requestNodes = requestNodes.OrderBy(n => _random.Next()).ToList();

                            var node2 = this.GetSearchNode(item.Hash, 1).Where(n => !requestNodes.Contains(n)).FirstOrDefault();
                            if (node2 != null) requestNodes.Add(node2);

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                lock (pushSeedsRequestDictionary.ThisLock)
                                {
                                    if (!pushSeedsRequestDictionary.ContainsKey(requestNodes[i]))
                                        pushSeedsRequestDictionary[requestNodes[i]] = new LockedHashSet<Keyword>();

                                    pushSeedsRequestDictionary[requestNodes[i]].Add(item);
                                }
                            }
                        });

                        using (DeadlockMonitor.Lock(this.ThisLock))
                        {
                            using (DeadlockMonitor.Lock(_pushSeedsRequestDictionary.ThisLock))
                            {
                                _pushSeedsRequestDictionary.Clear();

                                foreach (var item in pushSeedsRequestDictionary)
                                {
                                    _pushSeedsRequestDictionary.Add(item.Key, item.Value);
                                }
                            }
                        }

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

                        using (DeadlockMonitor.Lock(this.ThisLock))
                        {
                            using (DeadlockMonitor.Lock(_pushBlocksLinkDictionary.ThisLock))
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

                        using (DeadlockMonitor.Lock(this.ThisLock))
                        {
                            using (DeadlockMonitor.Lock(_pushBlocksRequestDictionary.ThisLock))
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

                // PushNodesRequest
                {
                    messageManager.PushNodesRequest = true;
                    connectionManager.PushNodesRequest();

                    Debug.WriteLine("ConnectionManager: Push NodesRequest");
                    _pushNodesRequestCount++;
                }

                Stopwatch nodeUpdateTime = new Stopwatch();
                nodeUpdateTime.Start();
                Stopwatch updateTime = new Stopwatch();
                updateTime.Start();

                for (; ; )
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;
                    if (!_connectionManagers.Contains(connectionManager)) return;

                    if (nodeUpdateTime.Elapsed.TotalSeconds > 180)
                    {
                        nodeUpdateTime.Restart();

                        // PushNodesRequest
                        if (!messageManager.PushNodesRequest)
                        {
                            messageManager.PushNodesRequest = true;
                            connectionManager.PushNodesRequest();

                            Debug.WriteLine("ConnectionManager: Push NodesRequest");
                            _pushNodesRequestCount++;
                        }
                    }

                    if (_connectionManagers.Count >= this.DownloadingConnectionCountLowerLimit && updateTime.Elapsed.TotalSeconds > 60)
                    {
                        updateTime.Restart();

                        // PushSeedsLink
                        {
                            KeywordCollection tempList = null;
                            int count = (int)(1024 * this.ResponseTimePriority(connectionManager.Node));

                            using (DeadlockMonitor.Lock(this.ThisLock))
                            {
                                using (DeadlockMonitor.Lock(_pushSeedsLinkDictionary.ThisLock))
                                {
                                    if (_pushSeedsLinkDictionary.ContainsKey(connectionManager.Node))
                                    {
                                        tempList = new KeywordCollection(_pushSeedsLinkDictionary[connectionManager.Node]
                                            .OrderBy(n => _random.Next()).Take(count));

                                        _pushSeedsLinkDictionary[connectionManager.Node].ExceptWith(tempList);
                                        _messagesManager[connectionManager.Node].PushSeedsLink.AddRange(tempList);
                                    }
                                }
                            }

                            if (tempList != null && tempList.Count != 0)
                            {
                                try
                                {
                                    connectionManager.PushSeedsLink(tempList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push SeedsLink {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                    _pushSeedsLinkCount += tempList.Count;
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        _messagesManager[connectionManager.Node].PushSeedsLink.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }

                        // PushSeedsRequest
                        {
                            KeywordCollection tempList = null;
                            int count = (int)(1024 * this.ResponseTimePriority(connectionManager.Node));

                            using (DeadlockMonitor.Lock(this.ThisLock))
                            {
                                using (DeadlockMonitor.Lock(_pushSeedsRequestDictionary.ThisLock))
                                {
                                    if (_pushSeedsRequestDictionary.ContainsKey(connectionManager.Node))
                                    {
                                        tempList = new KeywordCollection(_pushSeedsRequestDictionary[connectionManager.Node]
                                            .OrderBy(n => _random.Next()).Take(count));

                                        _pushSeedsRequestDictionary[connectionManager.Node].ExceptWith(tempList);
                                        _messagesManager[connectionManager.Node].PushSeedsRequest.AddRange(tempList);
                                    }
                                }
                            }

                            if (tempList != null && tempList.Count != 0)
                            {
                                try
                                {
                                    connectionManager.PushSeedsRequest(tempList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push SeedsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                    _pushSeedsRequestCount += tempList.Count;
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        _messagesManager[connectionManager.Node].PushSeedsRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }

                        // PushBlocksLink
                        {
                            KeyCollection tempList = null;
                            int count = (int)((4096 / _connectionManagers.Count) * this.ResponseTimePriority(connectionManager.Node));

                            using (DeadlockMonitor.Lock(this.ThisLock))
                            {
                                using (DeadlockMonitor.Lock(_pushBlocksLinkDictionary.ThisLock))
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
                                    _pushBlocksLinkCount += tempList.Count;
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
                        {
                            KeyCollection tempList = null;
                            int count = (int)((4096 / _connectionManagers.Count) * this.ResponseTimePriority(connectionManager.Node));

                            using (DeadlockMonitor.Lock(this.ThisLock))
                            {
                                using (DeadlockMonitor.Lock(_pushBlocksRequestDictionary.ThisLock))
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

                                    foreach (var header in tempList)
                                    {
                                        _downloadBlocks.Remove(header);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BlocksRequest ({0})", tempList.Count));
                                    _pushBlocksRequestCount += tempList.Count;
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

                    // PushNodes
                    if (messageManager.PullNodesRequest)
                    {
                        var nodes = _connectionManagers.Select(n => n.Node).ToList();
                        nodes.Remove(connectionManager.Node);

                        if (nodes.Count > 0)
                        {
                            connectionManager.PushNodes(nodes);
                            messageManager.PullNodesRequest = false;

                            Debug.WriteLine(string.Format("ConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodesCount += nodes.Count;
                        }
                    }

                    // PushBlock (Upload)
                    if (_connectionManagers.Count >= this.UploadingConnectionCountLowerLimit)
                    {
                        List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node.DeepClone()));
                        KeyCollection uploadKeys = new KeyCollection();
                        KeyCollection removeKeys = new KeyCollection();

                        {
                            KeyCollection tempKeys = new KeyCollection();

                            tempKeys.AddRange(_settings.UploadBlocksRequest);
                            tempKeys.AddRange(_settings.DiffusionBlocksRequest);

                            foreach (var key in tempKeys.OrderBy(n => _random.Next()))
                            {
                                var searchNodes = this.GetSearchNode(key.Hash, 1);

                                if (searchNodes.Count() == 0)
                                {
                                    _settings.UploadBlocksRequest.Remove(key);
                                    _settings.DiffusionBlocksRequest.Remove(key);

                                    removeKeys.Add(key);
                                }
                                else if (searchNodes.First() == connectionManager.Node && _cacheManager.Contains(key))
                                {
                                    _settings.UploadBlocksRequest.Remove(key);
                                    _settings.DiffusionBlocksRequest.Remove(key);

                                    uploadKeys.Add(key);

                                    break;
                                }
                            }
                        }

                        foreach (var key in removeKeys)
                        {
                            this.OnUploadedEvent(key);
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

                    // PushSeeds
                    {
                        List<Keyword> keywords = new List<Keyword>();
                        keywords.AddRange(messageManager.PullSeedsRequest);
                        keywords = keywords.OrderBy(n => _random.Next()).ToList();

                        for (int i = 0; i < keywords.Count && i < 1; i++)
                        {
                            HashSet<Seed> seeds = new HashSet<Seed>();
                            seeds.UnionWith(_seeds.Where(n => n.Keywords.Contains(keywords[i]) && !messageManager.PushSeeds.Contains(n)));
                            seeds.UnionWith(_uploadSeeds.Where(n => n.Keywords.Contains(keywords[i]) && !messageManager.PushSeeds.Contains(n)));
                            seeds.UnionWith(_cacheManager.Seeds.Where(n => n.Keywords.Contains(keywords[i]) && !messageManager.PushSeeds.Contains(n)));

                            if (seeds.Count > 0)
                            {
                                //int count = (int)(1024 * this.BlockPriority(requestConnectionManager.OtherNode));

                                var tempKeyList = seeds.OrderBy(n => _random.Next()).Take(1024).ToList();

                                connectionManager.PushSeeds(keywords[i], new SeedCollection(tempKeyList));
                                messageManager.PullSeedsRequest.Remove(keywords[i]);
                                messageManager.PushSeeds.AddRange(tempKeyList);

                                Debug.WriteLine(string.Format("ConnectionManager: Push Seeds {0} ({1})", keywords[i].Value), tempKeyList.Count);
                                _pushSeedsCount += tempKeyList.Count;
                            }
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
                                messageManager.PullBlocksRequest.Remove(key);
                                messageManager.Priority--;

                                Debug.WriteLine(string.Format("ConnectionManager: Push Block ({0})", NetworkConverter.ToBase64String(key.Hash)));
                                _pushBlockCount++;

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
            catch (Exception)
            {
                this.RemoveConnectionManager(connectionManager);

                return;
            }
        }

        #region connectionManager_Event

        private void connectionManager_NodesRequestEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            Debug.WriteLine("ConnectionManager: Pull NodesRequest");
            _pullNodesRequestCount++;

            _messagesManager[connectionManager.Node].PullNodesRequest = true;
        }

        private void connectionManager_NodesEvent(object sender, PullNodesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Nodes == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Nodes ({0})", e.Nodes.Count()));

            if (_messagesManager[connectionManager.Node].PushNodesRequest)
            {
                _messagesManager[connectionManager.Node].PushNodesRequest = false;
            }

            foreach (var node in e.Nodes)
            {
                if (node == null || node.Id == null || node.Uris.Count == 0 || _removeNodes.Contains(node)) continue;

                _routeTable.Add(node);
                _pullNodesCount++;
            }

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                using (DeadlockMonitor.Lock(_messagesManager.ThisLock))
                {
                    _messagesManager[connectionManager.Node].SurroundingNodes.Clear();
                    _messagesManager[connectionManager.Node].SurroundingNodes.UnionWith(e.Nodes.Where(n => n != null && n.Id != null));
                }
            }
        }

        private void connectionManager_SeedsLinkEvent(object sender, PullSeedsLinkEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Keywords == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SeedsLink {0} ({1})", String.Join(", ", e.Keywords), e.Keywords.Count()));

            foreach (var keyword in e.Keywords.Take(_maxLinkCount))
            {
                if (keyword == null || keyword.Value == null || keyword.HashAlgorithm != HashAlgorithm.Sha512) continue;

                _messagesManager[connectionManager.Node].PullSeedsLink.Add(keyword);
                _pullSeedsLinkCount++;
            }
        }

        private void connectionManager_SeedsRequestEvent(object sender, PullSeedsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Keywords == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SeedsRequest {0} ({1})", String.Join(", ", e.Keywords), e.Keywords.Count()));

            foreach (var keyword in e.Keywords.Take(_maxRequestCount))
            {
                if (keyword == null || keyword.Value == null || keyword.HashAlgorithm != HashAlgorithm.Sha512) continue;

                _messagesManager[connectionManager.Node].PullSeedsRequest.Add(keyword);
                _pullSeedsRequestCount++;
            }
        }

        private void connectionManager_SeedsEvent(object sender, PullSeedsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Keyword == null || e.Keyword.Value == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Seeds {0} ({1})", e.Keyword.Value, e.Seeds.Count()));

            if (_messagesManager[connectionManager.Node].PushSeedsRequest.Contains(e.Keyword))
            {
                _messagesManager[connectionManager.Node].PushSeedsRequest.Remove(e.Keyword);
            }

            foreach (var key in e.Seeds)
            {
                if (key == null || key.Name == null || !key.VerifyCertificate() || !this.OnGetFilterSeedEvent(key)) continue;
                if (_seedMaxCount < _seeds.Count) continue;

                _seeds.Add(key);
                _messagesManager[connectionManager.Node].PushSeeds.Add(key);
                _pullSeedsCount++;
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
                _pullBlocksLinkCount++;
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
                _pullBlocksRequestCount++;
            }
        }

        private void connectionManager_BlockEvent(object sender, PullBlockEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Key == null || e.Key.Hash == null || e.Key.HashAlgorithm != HashAlgorithm.Sha512 || e.Value.Array == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Block ({0})", NetworkConverter.ToBase64String(e.Key.Hash)));
            _pullBlockCount++;

            if (_messagesManager[connectionManager.Node].PushBlocksRequest.Contains(e.Key))
            {
                _messagesManager[connectionManager.Node].PushBlocksRequest.Remove(e.Key);
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
            catch (SpaceNotFoundException ex)
            {
                Log.Error(ex);
            }

            if (connectionManager.Node.Uris.Count != 0) _routeTable.Live(connectionManager.Node);
        }

        private void connectionManager_PullCancelEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            Debug.WriteLine("ConnectionManager: Pull Cancel");

            _cuttingNodes.Remove(connectionManager.Node);

            if (_routeTable.Count > 50)
            {
                _routeTable.Remove(connectionManager.Node);
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

        protected virtual bool OnGetFilterSeedEvent(Seed seed)
        {
            if (GetFilterSeedEvent != null)
            {
                return GetFilterSeedEvent(this, seed);
            }

            return true;
        }

        protected virtual void OnUploadedEvent(Key key)
        {
            if (UploadedEvent != null)
            {
                UploadedEvent(this, key);
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var node in nodes)
                {
                    if (node == null || node.Id == null || node.Uris.Count == 0 || _removeNodes.Contains(node)) continue;

                    _routeTable.Add(node);
                }
            }
        }

        public bool DownloadWaiting(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
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

        public void Download(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _downloadBlocks.Add(key);
            }
        }

        public bool UploadWaiting(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_settings.UploadBlocksRequest.Contains(key))
                    return true;

                return false;
            }
        }

        public void Upload(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _settings.UploadBlocksRequest.Add(key);
            }
        }

        public void Upload(Seed seed)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _uploadSeeds.Add(seed);
            }
        }

        public override ManagerState State
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
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
            while (_createServerConnectionThread != null) Thread.Sleep(1000);
            while (_connectionsManagerThread != null) Thread.Sleep(1000);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _createClientConnection1Thread = new Thread(this.CreateClientConnectionThread);
                _createClientConnection1Thread.IsBackground = true;
                _createClientConnection1Thread.Name = "CreateClientConnection1Thread";
                _createClientConnection1Thread.Start();
                _createClientConnection2Thread = new Thread(this.CreateClientConnectionThread);
                _createClientConnection2Thread.IsBackground = true;
                _createClientConnection2Thread.Name = "CreateClientConnection2Thread";
                _createClientConnection2Thread.Start();
                _createClientConnection3Thread = new Thread(this.CreateClientConnectionThread);
                _createClientConnection3Thread.IsBackground = true;
                _createClientConnection3Thread.Name = "CreateClientConnection3Thread";
                _createClientConnection3Thread.Start();
                _createServerConnectionThread = new Thread(this.CreateServerConnectionThread);
                _createServerConnectionThread.IsBackground = true;
                _createServerConnectionThread.Name = "CreateServerConnectionThread";
                _createServerConnectionThread.Start();
                _connectionsManagerThread = new Thread(this.ConnectionsManagerThread);
                _connectionsManagerThread.IsBackground = true;
                _connectionsManagerThread.Priority = ThreadPriority.Lowest;
                _connectionsManagerThread.Name = "ConnectionsManagerThread";
                _connectionsManagerThread.Start();
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;
            }

            _createClientConnection1Thread.Join();
            _createClientConnection1Thread = null;
            _createClientConnection2Thread.Join();
            _createClientConnection2Thread = null;
            _createClientConnection3Thread.Join();
            _createClientConnection3Thread = null;
            _createServerConnectionThread.Join();
            _createServerConnectionThread = null;
            _connectionsManagerThread.Join();
            _connectionsManagerThread = null;

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var item in _connectionManagers)
                {
                    this.RemoveConnectionManager(item);
                }
            }
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
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

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                using (DeadlockMonitor.Lock(_settings.ThisLock))
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
                    new Library.Configuration.SettingsContext<int>() { Name = "ConnectionCountLimit", Value = 32 },
                    new Library.Configuration.SettingsContext<int>() { Name = "UploadingConnectionCountLowerLimit", Value = 12 },
                    new Library.Configuration.SettingsContext<int>() { Name = "DownloadingConnectionCountLowerLimit", Value = 12 },
                    new Library.Configuration.SettingsContext<KeywordCollection>() { Name = "SearchKeywords", Value = new KeywordCollection() },
                    new Library.Configuration.SettingsContext<LockedHashSet<Key>>() { Name = "DiffusionBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingsContext<LockedHashSet<Key>>() { Name = "UploadBlocksRequest", Value = new LockedHashSet<Key>() },
                })
            {

            }

            public override void Load(string directoryPath)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    base.Save(directoryPath);
                }
            }

            public NodeCollection OtherNodes
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (NodeCollection)this["OtherNodes"];
                    }
                }
            }

            public Node BaseNode
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (Node)this["BaseNode"];
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        this["BaseNode"] = value;
                    }
                }
            }

            public int ConnectionCountLimit
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (int)this["ConnectionCountLimit"];
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        this["ConnectionCountLimit"] = value;
                    }
                }
            }

            public int UploadingConnectionCountLowerLimit
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (int)this["UploadingConnectionCountLowerLimit"];
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        this["UploadingConnectionCountLowerLimit"] = value;
                    }
                }
            }

            public int DownloadingConnectionCountLowerLimit
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (int)this["DownloadingConnectionCountLowerLimit"];
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        this["DownloadingConnectionCountLowerLimit"] = value;
                    }
                }
            }

            public KeywordCollection SearchKeywords
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (KeywordCollection)this["SearchKeywords"];
                    }
                }
            }

            public LockedHashSet<Key> DiffusionBlocksRequest
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (LockedHashSet<Key>)this["DiffusionBlocksRequest"];
                    }
                }
            }

            public LockedHashSet<Key> UploadBlocksRequest
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (LockedHashSet<Key>)this["UploadBlocksRequest"];
                    }
                }
            }

            #region IThisLock メンバ

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
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_createClientConnection1Thread != null)
                    {
                        try
                        {
                            _createClientConnection1Thread.Abort();
                        }
                        catch (Exception)
                        {

                        }

                        _createClientConnection1Thread = null;
                    }

                    if (_createClientConnection2Thread != null)
                    {
                        try
                        {
                            _createClientConnection2Thread.Abort();
                        }
                        catch (Exception)
                        {

                        }

                        _createClientConnection2Thread = null;
                    }

                    if (_createClientConnection3Thread != null)
                    {
                        try
                        {
                            _createClientConnection3Thread.Abort();
                        }
                        catch (Exception)
                        {

                        }

                        _createClientConnection3Thread = null;
                    }

                    if (_createServerConnectionThread != null)
                    {
                        try
                        {
                            _createServerConnectionThread.Abort();
                        }
                        catch (Exception)
                        {

                        }

                        _createServerConnectionThread = null;
                    }

                    if (_connectionsManagerThread != null)
                    {
                        try
                        {
                            _connectionsManagerThread.Abort();
                        }
                        catch (Exception)
                        {

                        }

                        _connectionsManagerThread = null;
                    }

                    if (_connectionManagers != null)
                    {
                        foreach (var item in _connectionManagers)
                        {
                            try
                            {
                                item.Dispose();
                            }
                            catch (Exception)
                            {

                            }
                        }

                        _connectionManagers = null;
                    }
                }

                _disposed = true;
            }
        }

        #region IThisLock メンバ

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
