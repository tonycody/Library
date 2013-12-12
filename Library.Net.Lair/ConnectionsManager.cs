using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Net.Connections;

namespace Library.Net.Lair
{
    public delegate IEnumerable<Criterion> GetCriteriaEventHandler(object sender);

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
        private HeaderManager _headerManager;

        private LockedDictionary<Node, HashSet<Key>> _pushBlocksLinkDictionary = new LockedDictionary<Node, HashSet<Key>>();
        private LockedDictionary<Node, HashSet<Key>> _pushBlocksRequestDictionary = new LockedDictionary<Node, HashSet<Key>>();
        private LockedDictionary<Node, HashSet<Key>> _pushBlocksDictionary = new LockedDictionary<Node, HashSet<Key>>();
        private LockedDictionary<Node, HashSet<Section>> _pushSectionHeadersRequestDictionary = new LockedDictionary<Node, HashSet<Section>>();
        private LockedDictionary<Node, HashSet<Document>> _pushDocumentHeadersRequestDictionary = new LockedDictionary<Node, HashSet<Document>>();
        private LockedDictionary<Node, HashSet<Chat>> _pushChatHeadersRequestDictionary = new LockedDictionary<Node, HashSet<Chat>>();

        private LockedList<Node> _creatingNodes;
        private VolatileCollection<Node> _waitingNodes;
        private VolatileCollection<Node> _cuttingNodes;
        private VolatileCollection<Node> _removeNodes;
        private VolatileDictionary<Node, int> _nodesStatus;

        private VolatileCollection<Section> _pushSectionHeadersRequestList;
        private VolatileCollection<Document> _pushDocumentHeadersRequestList;
        private VolatileCollection<Chat> _pushChatHeadersRequestList;
        private VolatileCollection<Key> _downloadBlocks;

        private LockedDictionary<Section, DateTime> _lastUsedSectionHeaderTimes = new LockedDictionary<Section, DateTime>();
        private LockedDictionary<Document, DateTime> _lastUsedDocumentHeaderTimes = new LockedDictionary<Document, DateTime>();
        private LockedDictionary<Chat, DateTime> _lastUsedChatHeaderTimes = new LockedDictionary<Chat, DateTime>();

        private volatile Thread _connectionsManagerThread;
        private volatile Thread _createClientConnection1Thread;
        private volatile Thread _createClientConnection2Thread;
        private volatile Thread _createClientConnection3Thread;
        private volatile Thread _createServerConnection1Thread;
        private volatile Thread _createServerConnection2Thread;
        private volatile Thread _createServerConnection3Thread;

        private ManagerState _state = ManagerState.Stop;

        private Dictionary<Node, string> _nodeToUri = new Dictionary<Node, string>();

        private BandwidthLimit _bandwidthLimit = new BandwidthLimit();

        private long _receivedByteCount;
        private long _sentByteCount;

        private volatile int _pushNodeCount;
        private volatile int _pushBlockLinkCount;
        private volatile int _pushBlockRequestCount;
        private volatile int _pushBlockCount;
        private volatile int _pushHeaderRequestCount;
        private volatile int _pushHeaderCount;

        private volatile int _pullNodeCount;
        private volatile int _pullBlockLinkCount;
        private volatile int _pullBlockRequestCount;
        private volatile int _pullBlockCount;
        private volatile int _pullHeaderRequestCount;
        private volatile int _pullHeaderCount;

        private VolatileCollection<Key> _relayBlocks;
        private volatile int _relayBlockCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        private GetCriteriaEventHandler _getLockCriteriaEvent;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        private const int _maxNodeCount = 128;
        private const int _maxBlockLinkCount = 2048;
        private const int _maxBlockRequestCount = 2048;
        private const int _maxHeaderRequestCount = 1024;
        private const int _maxHeaderCount = 1024;

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
            _cuttingNodes = new VolatileCollection<Node>(new TimeSpan(0, 10, 0));
            _removeNodes = new VolatileCollection<Node>(new TimeSpan(0, 10, 0));
            _nodesStatus = new VolatileDictionary<Node, int>(new TimeSpan(0, 30, 0));

            _downloadBlocks = new VolatileCollection<Key>(new TimeSpan(0, 3, 0));
            _pushSectionHeadersRequestList = new VolatileCollection<Section>(new TimeSpan(0, 3, 0));
            _pushDocumentHeadersRequestList = new VolatileCollection<Document>(new TimeSpan(0, 3, 0));
            _pushChatHeadersRequestList = new VolatileCollection<Chat>(new TimeSpan(0, 3, 0));

            _relayBlocks = new VolatileCollection<Key>(new TimeSpan(0, 30, 0));

            this.UpdateSessionId();
        }

        public GetCriteriaEventHandler GetLockCriteriaEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockCriteriaEvent = value;
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
                    contexts.Add(new InformationContext("PushHeaderRequestCount", _pushHeaderRequestCount));
                    contexts.Add(new InformationContext("PushHeaderCount", _pushHeaderCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PullBlockLinkCount", _pullBlockLinkCount));
                    contexts.Add(new InformationContext("PullBlockRequestCount", _pullBlockRequestCount));
                    contexts.Add(new InformationContext("PullBlockCount", _pullBlockCount));
                    contexts.Add(new InformationContext("PullHeaderRequestCount", _pullHeaderRequestCount));
                    contexts.Add(new InformationContext("PullHeaderCount", _pullHeaderCount));

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

        private static bool Check(Node node)
        {
            return !(node == null || node.Id == null || node.Id.Length == 0);
        }

        private static bool Check(Key key)
        {
            return !(key == null || key.Hash == null || key.Hash.Length == 0 || key.HashAlgorithm != HashAlgorithm.Sha512);
        }

        private static bool Check(ITag tag)
        {
            return !(tag == null || tag.Id == null || tag.Id.Length == 0 || string.IsNullOrWhiteSpace(tag.Name));
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
                            connectionCount = _connectionManagers.Count(n => n.Type == ConnectionManagerType.Server);
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
                        ThreadPool.QueueUserWorkItem((object state) =>
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
                        });

                        return;
                    }
                }

                Debug.WriteLine("ConnectionManager: Connect");

                connectionManager.PullNodesEvent += this.connectionManager_NodesEvent;
                connectionManager.PullBlocksLinkEvent += this.connectionManager_BlocksLinkEvent;
                connectionManager.PullBlocksRequestEvent += this.connectionManager_BlocksRequestEvent;
                connectionManager.PullBlockEvent += this.connectionManager_BlockEvent;
                connectionManager.PullHeadersRequestEvent += this.connectionManager_HeadersRequestEvent;
                connectionManager.PullHeadersEvent += this.connectionManager_HeadersEvent;
                connectionManager.PullCancelEvent += this.connectionManager_PullCancelEvent;
                connectionManager.CloseEvent += this.connectionManager_CloseEvent;

                _nodeToUri.Add(connectionManager.Node, uri);
                _connectionManagers.Add(connectionManager);

                if (_messagesManager[connectionManager.Node].SessionId != null
                    && !Collection.Equals(_messagesManager[connectionManager.Node].SessionId, connectionManager.SesstionId))
                {
                    _messagesManager.Remove(connectionManager.Node);
                }

                _messagesManager[connectionManager.Node].SessionId = connectionManager.SesstionId;
                _messagesManager[connectionManager.Node].LastPullTime = DateTime.UtcNow;

                ThreadPool.QueueUserWorkItem(this.ConnectionManagerThread, connectionManager);
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
                        connectionCount = _connectionManagers.Count(n => n.Type == ConnectionManagerType.Client);
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

                    this.RemoveNode(node);
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
                            _routeTable.Add(connectionManager.Node);
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

        private volatile bool _refreshThreadRunning;

        private void ConnectionsManagerThread()
        {
            Stopwatch connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();

            Stopwatch refreshStopwatch = new Stopwatch();

            Stopwatch pushBlockUploadStopwatch = new Stopwatch();
            pushBlockUploadStopwatch.Start();
            Stopwatch pushBlockDownloadStopwatch = new Stopwatch();
            pushBlockDownloadStopwatch.Start();

            Stopwatch pushHeaderUploadStopwatch = new Stopwatch();
            pushHeaderUploadStopwatch.Start();
            Stopwatch pushHeaderDownloadStopwatch = new Stopwatch();
            pushHeaderDownloadStopwatch.Start();

            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                var connectionCount = 0;

                lock (this.ThisLock)
                {
                    connectionCount = _connectionManagers.Count(n => n.Type == ConnectionManagerType.Client);
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

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    refreshStopwatch.Restart();

                    ThreadPool.QueueUserWorkItem((object wstate) =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            var lockCriteriaList = this.OnGetLockCriteriaEvent();

                            if (lockCriteriaList != null)
                            {
                                var now = DateTime.UtcNow;

                                // 非トラストなタグを1024残し、他をLRU順に破棄。
                                {
                                    var removeLinks = new HashSet<Link>();
                                    removeLinks.UnionWith(_headerManager.GetLinks());
                                    removeLinks.ExceptWith(lockCriteriaList.SelectMany(n => n.Links));

                                    var sortList = removeLinks.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedHeaderTimes.TryGetValue(x, out tx);
                                        _lastUsedHeaderTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _headerManager.RemoveLinks(sortList.Take(sortList.Count - 1024));

                                    var liveLinks = new HashSet<Link>(_headerManager.GetLinks());

                                    foreach (var link in _lastUsedHeaderTimes.Keys.ToArray())
                                    {
                                        if (liveLinks.Contains(link)) continue;

                                        _lastUsedHeaderTimes.Remove(link);
                                    }
                                }

                                // Link毎にトラストリストを適応する。
                                var patternlist = new List<KeyValuePair<HashSet<Link>, HashSet<string>>>();

                                foreach (var criterion in lockCriteriaList)
                                {
                                    var linkHashset = new HashSet<Link>(criterion.Links);
                                    var SignatureHashset = new HashSet<string>(criterion.TrustSignatures);

                                    patternlist.Add(new KeyValuePair<HashSet<Link>, HashSet<string>>(linkHashset, SignatureHashset));
                                }

                                {
                                    var removeHeaders = new List<Header>();

                                    foreach (var link in _headerManager.GetLinks())
                                    {
                                        List<HashSet<string>> trustList;

                                        {
                                            var tempList = patternlist.Where(n => n.Key.Contains(link)).ToList();
                                            if (tempList.Count == 0) continue;

                                            trustList = new List<HashSet<string>>(tempList.Select(n => n.Value));
                                        }

                                        var headers = _headerManager.GetHeaders(link).ToList();

                                        if (link.Type == "Section")
                                        {
                                            List<Header> trustProfileHeaders = new List<Header>();
                                            List<Header> untrustProfileHeaders = new List<Header>();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (header.Type == "Profile")
                                                {
                                                    if (trustList.Any(n => n.Contains(signature)))
                                                    {
                                                        trustProfileHeaders.Add(header);
                                                    }
                                                    else
                                                    {
                                                        untrustProfileHeaders.Add(header);
                                                    }
                                                }
                                            }

                                            removeHeaders.AddRange(trustProfileHeaders);
                                            removeHeaders.AddRange(untrustProfileHeaders.Randomize().Skip(32));
                                        }
                                        else if (link.Type == "Document")
                                        {
                                            Dictionary<string, List<Header>> trustPageHeaders = new Dictionary<string, List<Header>>();
                                            Dictionary<string, List<Header>> untrustPageHeaders = new Dictionary<string, List<Header>>();

                                            List<Header> trustVoteHeaders = new List<Header>();
                                            List<Header> untrustVoteHeaders = new List<Header>();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (header.Type == "Page")
                                                {
                                                    if (trustList.Any(n => n.Contains(signature)))
                                                    {
                                                        List<Header> list;

                                                        if (!trustPageHeaders.TryGetValue(signature, out list))
                                                        {
                                                            list = new List<Header>();
                                                            trustPageHeaders[signature] = list;
                                                        }

                                                        list.Add(header);
                                                    }
                                                    else
                                                    {
                                                        List<Header> list;

                                                        if (!untrustPageHeaders.TryGetValue(signature, out list))
                                                        {
                                                            list = new List<Header>();
                                                            untrustPageHeaders[signature] = list;
                                                        }

                                                        list.Add(header);
                                                    }
                                                }
                                                else if (header.Type == "Vote")
                                                {
                                                    if (trustList.Any(n => n.Contains(signature)))
                                                    {
                                                        trustVoteHeaders.Add(header);
                                                    }
                                                    else
                                                    {
                                                        untrustVoteHeaders.Add(header);
                                                    }
                                                }

                                            }

                                            removeHeaders.AddRange(trustPageHeaders.SelectMany(n => n.Value));
                                            removeHeaders.AddRange(untrustPageHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in Collection.Merge(trustPageHeaders.Values, untrustPageHeaders.Values))
                                            {
                                                if (list.Count <= 256) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeHeaders.AddRange(list.Take(list.Count - 256));
                                            }

                                            removeHeaders.AddRange(trustVoteHeaders);
                                            removeHeaders.AddRange(untrustVoteHeaders.Randomize().Skip(32));
                                        }
                                        else if (link.Type == "Chat")
                                        {
                                            Dictionary<string, List<Header>> trustTopicHeaders = new Dictionary<string, List<Header>>();
                                            Dictionary<string, List<Header>> untrustTopicHeaders = new Dictionary<string, List<Header>>();

                                            Dictionary<string, List<Header>> trustMessageHeaders = new Dictionary<string, List<Header>>();
                                            Dictionary<string, List<Header>> untrustMessageHeaders = new Dictionary<string, List<Header>>();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (header.Type == "Topic")
                                                {
                                                    if (trustList.Any(n => n.Contains(signature)))
                                                    {
                                                        List<Header> list;

                                                        if (!trustTopicHeaders.TryGetValue(signature, out list))
                                                        {
                                                            list = new List<Header>();
                                                            trustTopicHeaders[signature] = list;
                                                        }

                                                        list.Add(header);
                                                    }
                                                    else
                                                    {
                                                        List<Header> list;

                                                        if (!untrustTopicHeaders.TryGetValue(signature, out list))
                                                        {
                                                            list = new List<Header>();
                                                            untrustTopicHeaders[signature] = list;
                                                        }

                                                        list.Add(header);
                                                    }
                                                }
                                                else if (header.Type == "Message")
                                                {
                                                    if (trustList.Any(n => n.Contains(signature)))
                                                    {
                                                        List<Header> list;

                                                        if (!trustMessageHeaders.TryGetValue(signature, out list))
                                                        {
                                                            list = new List<Header>();
                                                            trustMessageHeaders[signature] = list;
                                                        }

                                                        list.Add(header);
                                                    }
                                                    else
                                                    {
                                                        List<Header> list;

                                                        if (!untrustMessageHeaders.TryGetValue(signature, out list))
                                                        {
                                                            list = new List<Header>();
                                                            untrustMessageHeaders[signature] = list;
                                                        }

                                                        list.Add(header);
                                                    }
                                                }
                                            }

                                            removeHeaders.AddRange(trustTopicHeaders.SelectMany(n => n.Value));
                                            removeHeaders.AddRange(untrustTopicHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in Collection.Merge(trustTopicHeaders.Values, untrustTopicHeaders.Values))
                                            {
                                                if (list.Count <= 256) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeHeaders.AddRange(list.Take(list.Count - 256));
                                            }

                                            removeHeaders.AddRange(trustMessageHeaders.SelectMany(n => n.Value));
                                            removeHeaders.AddRange(untrustMessageHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in Collection.Merge(trustMessageHeaders.Values, untrustMessageHeaders.Values))
                                            {
                                                var tempList = new List<Header>();

                                                foreach (var header in list)
                                                {
                                                    if ((now - header.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                                    {
                                                        removeHeaders.Add(header);
                                                    }
                                                    else
                                                    {
                                                        tempList.Add(header);
                                                    }
                                                }

                                                if (list.Count <= 8192) continue;

                                                tempList.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeHeaders.AddRange(tempList.Take(tempList.Count - 1024));
                                            }
                                        }
                                        else if (link.Type == "Message")
                                        {
                                            Dictionary<string, List<Header>> trustMessageHeaders = new Dictionary<string, List<Header>>();
                                            Dictionary<string, List<Header>> untrustMessageHeaders = new Dictionary<string, List<Header>>();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (header.Type == "Message")
                                                {
                                                    if (trustList.Any(n => n.Contains(signature)))
                                                    {
                                                        List<Header> list;

                                                        if (!trustMessageHeaders.TryGetValue(signature, out list))
                                                        {
                                                            list = new List<Header>();
                                                            trustMessageHeaders[signature] = list;
                                                        }

                                                        list.Add(header);
                                                    }
                                                    else
                                                    {
                                                        List<Header> list;

                                                        if (!untrustMessageHeaders.TryGetValue(signature, out list))
                                                        {
                                                            list = new List<Header>();
                                                            untrustMessageHeaders[signature] = list;
                                                        }

                                                        list.Add(header);
                                                    }
                                                }
                                            }

                                            removeHeaders.AddRange(trustMessageHeaders.SelectMany(n => n.Value));
                                            removeHeaders.AddRange(untrustMessageHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in Collection.Merge(trustMessageHeaders.Values, untrustMessageHeaders.Values))
                                            {
                                                var tempList = new List<Header>();

                                                foreach (var header in list)
                                                {
                                                    if ((now - header.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                                    {
                                                        removeHeaders.Add(header);
                                                    }
                                                    else
                                                    {
                                                        tempList.Add(header);
                                                    }
                                                }

                                                if (list.Count <= 32) continue;

                                                tempList.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeHeaders.AddRange(tempList.Take(tempList.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var header in removeHeaders)
                                    {
                                        _headerManager.RemoveHeader(header);
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
                    });
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
                    && pushHeaderUploadStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    pushHeaderUploadStopwatch.Restart();

                    foreach (var item in _headerManager.GetLinks())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Tag.Id, 1));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullHeadersRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }
                }

                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushHeaderDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushHeaderDownloadStopwatch.Restart();

                    HashSet<Link> pushHeadersRequestList = new HashSet<Link>();
                    List<Node> nodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        nodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    {
                        {
                            var list = _pushHeadersRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushHeadersRequest.Contains(list[i])))
                                {
                                    pushHeadersRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullHeadersRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushHeadersRequest.Contains(list[i])))
                                {
                                    pushHeadersRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }
                    }

                    {
                        Dictionary<Node, HashSet<Link>> pushHeadersRequestDictionary = new Dictionary<Node, HashSet<Link>>();

                        foreach (var item in pushHeadersRequestList)
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(item.Tag.Id, 1));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Link> hashset;

                                    if (!pushHeadersRequestDictionary.TryGetValue(requestNodes[i], out hashset))
                                    {
                                        hashset = new HashSet<Link>();
                                        pushHeadersRequestDictionary[requestNodes[i]] = hashset;
                                    }

                                    hashset.Add(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushHeadersRequestDictionary.ThisLock)
                        {
                            _pushHeadersRequestDictionary.Clear();

                            foreach (var item in pushHeadersRequestDictionary)
                            {
                                _pushHeadersRequestDictionary.Add(item.Key, item.Value);
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
                Stopwatch headerUpdateTime = new Stopwatch();
                headerUpdateTime.Start();

                for (; ; )
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;
                    if (!_connectionManagers.Contains(connectionManager)) return;

                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers.Count(n => n.Type == ConnectionManagerType.Client);
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

                        // PushHeadersRequest
                        if (_connectionManagers.Count >= _downloadingConnectionCountLowerLimit)
                        {
                            LinkCollection tempList = null;
                            int count = (int)(_maxHeaderRequestCount * this.ResponseTimePriority(connectionManager.Node));

                            lock (_pushHeadersRequestDictionary.ThisLock)
                            {
                                HashSet<Link> hashset;

                                if (_pushHeadersRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    tempList = new LinkCollection(hashset.Randomize().Take(count));

                                    hashset.ExceptWith(tempList);
                                    messageManager.PushHeadersRequest.AddRange(tempList);
                                }
                            }

                            if (tempList != null && tempList.Count > 0)
                            {
                                try
                                {
                                    connectionManager.PushHeadersRequest(tempList);

                                    foreach (var item in tempList)
                                    {
                                        _pushHeadersRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push HeadersRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                    _pushHeaderRequestCount += tempList.Count;
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        messageManager.PushHeadersRequest.Remove(item);
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

                    if (headerUpdateTime.Elapsed.TotalSeconds >= 60)
                    {
                        headerUpdateTime.Restart();

                        // PushHeader
                        if (_connectionManagers.Count >= _uploadingConnectionCountLowerLimit)
                        {
                            var links = new List<Link>(messageManager.PullHeadersRequest.Randomize());

                            if (links.Count > 0)
                            {
                                var headers = new List<Header>();

                                foreach (var link in links.Randomize())
                                {
                                    foreach (var header in _headerManager.GetHeaders(link))
                                    {
                                        if (!messageManager.PushHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                        {
                                            headers.Add(header);

                                            if (headers.Count >= _maxHeaderCount) goto End;
                                        }
                                    }
                                }

                            End: ;

                                connectionManager.PushHeaders(headers);

                                Debug.WriteLine(string.Format("ConnectionManager: Push Headers ({0})", headers.Count));
                                _pushHeaderCount += headers.Count;

                                foreach (var header in headers)
                                {
                                    messageManager.PushHeaders.Add(header.GetHash(_hashAlgorithm));
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
                if (!ConnectionsManager.Check(node) || _removeNodes.Contains(node)) continue;

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
                if (!ConnectionsManager.Check(key)) continue;

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
                if (!ConnectionsManager.Check(key)) continue;

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
                if (!ConnectionsManager.Check(e.Key) || e.Value.Array == null) return;

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

        private void connectionManager_HeadersRequestEvent(object sender, PullHeadersRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Links == null) return;
            if (messageManager.PullHeadersRequest.Count > _maxHeaderRequestCount * messageManager.PullHeadersRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull HeadersRequest ({0})", e.Links.Count()));

            foreach (var link in e.Links.Take(_maxHeaderRequestCount))
            {
                if (!ConnectionsManager.Check(link)) continue;

                messageManager.PullHeadersRequest.Add(link);
                _pullHeaderRequestCount++;

                _lastUsedHeaderTimes[link] = DateTime.UtcNow;
            }
        }

        private void connectionManager_HeadersEvent(object sender, PullHeadersEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (e.Headers == null) return;
            if (messageManager.PushHeaders.Count > _maxHeaderCount * messageManager.PushHeaders.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Headers ({0})", e.Headers.Count()));

            foreach (var header in e.Headers.Take(_maxHeaderCount))
            {
                if (_headerManager.SetHeader(header))
                {
                    messageManager.PushHeaders.Add(header.GetHash(_hashAlgorithm));
                    messageManager.LastPullTime = DateTime.UtcNow;

                    _lastUsedHeaderTimes[header.Link] = DateTime.UtcNow;
                }

                _pullHeaderCount++;
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
                    this.RemoveNode(connectionManager.Node);

                    if (!_removeNodes.Contains(connectionManager.Node))
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

        protected virtual IEnumerable<Criterion> OnGetLockCriteriaEvent()
        {
            if (_getLockCriteriaEvent != null)
            {
                return _getLockCriteriaEvent(this);
            }

            return null;
        }

        public void SetBaseNode(Node baseNode)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (baseNode == null) throw new ArgumentNullException("baseNode");
            if (baseNode.Id == null) throw new ArgumentNullException("baseNode.Id");
            if (baseNode.Id.Length == 0) throw new ArgumentException("baseNode.Id.Length");

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
                    if (!ConnectionsManager.Check(node) || _removeNodes.Contains(node)) continue;

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

        public IEnumerable<SectionProfileHeader> GetSectionProfileHeaders(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushSectionHeadersRequestList.Add(section);

                return _headerManager.GetSectionProfileHeaders(section);
            }
        }

        public IEnumerable<SectionMessageHeader> GetSectionMessageHeaders(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushSectionHeadersRequestList.Add(section);

                return _headerManager.GetSectionMessageHeaders(section);
            }
        }

        public IEnumerable<DocumentArchiveHeader> GetDocumentArchiveHeaders(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _headerManager.GetDocumentArchiveHeaders(document);
            }
        }

        public IEnumerable<DocumentVoteHeader> GetDocumentVoteHeaders(Document document)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushDocumentHeadersRequestList.Add(document);

                return _headerManager.GetDocumentVoteHeaders(document);
            }
        }

        public IEnumerable<ChatTopicHeader> GetChatTopicHeaders(Chat chat)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushChatHeadersRequestList.Add(chat);

                return _headerManager.GetChatTopicHeaders(chat);
            }
        }

        public IEnumerable<ChatMessageHeader> GetChatMessageHeaders(Chat chat)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushChatHeadersRequestList.Add(chat);

                return _headerManager.GetChatMessageHeaders(chat);
            }
        }

        public void Upload(SectionProfileHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _headerManager.SetHeader(header);
            }
        }

        public void Upload(SectionMessageHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _headerManager.SetHeader(header);
            }
        }

        public void Upload(DocumentArchiveHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _headerManager.SetHeader(header);
            }
        }

        public void Upload(DocumentVoteHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _headerManager.SetHeader(header);
            }
        }

        public void Upload(ChatTopicHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _headerManager.SetHeader(header);
            }
        }

        public void Upload(ChatMessageHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _headerManager.SetHeader(header);
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

                _cuttingNodes.Clear();
                _removeNodes.Clear();
                _nodesStatus.Clear();

                _messagesManager.Clear();
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
                    if (node == null || node.Id == null) continue;

                    _routeTable.Add(node);
                }

                _bandwidthLimit.In = _settings.BandwidthLimit;
                _bandwidthLimit.Out = _settings.BandwidthLimit;

                _headerManager = new HeaderManager();

                foreach (var header in _settings.SectionProfileHeaders)
                {
                    _headerManager.SetHeader(header);
                }

                foreach (var header in _settings.SectionMessageHeaders)
                {
                    _headerManager.SetHeader(header);
                }

                foreach (var header in _settings.DocumentArchiveHeaders)
                {
                    _headerManager.SetHeader(header);
                }

                foreach (var header in _settings.DocumentVoteHeaders)
                {
                    _headerManager.SetHeader(header);
                }

                foreach (var header in _settings.ChatTopicHeaders)
                {
                    _headerManager.SetHeader(header);
                }

                foreach (var header in _settings.ChatMessageHeaders)
                {
                    _headerManager.SetHeader(header);
                }
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                {
                    lock (_settings.SectionProfileHeaders.ThisLock)
                    {
                        _settings.SectionProfileHeaders.Clear();
                        _settings.SectionProfileHeaders.AddRange(_headerManager.GetSectionProfileHeaders());
                    }

                    lock (_settings.SectionMessageHeaders.ThisLock)
                    {
                        _settings.SectionMessageHeaders.Clear();
                        _settings.SectionMessageHeaders.AddRange(_headerManager.GetSectionMessageHeaders());
                    }

                    lock (_settings.DocumentArchiveHeaders.ThisLock)
                    {
                        _settings.DocumentArchiveHeaders.Clear();
                        _settings.DocumentArchiveHeaders.AddRange(_headerManager.GetDocumentArchiveHeaders());
                    }

                    lock (_settings.DocumentVoteHeaders.ThisLock)
                    {
                        _settings.DocumentVoteHeaders.Clear();
                        _settings.DocumentVoteHeaders.AddRange(_headerManager.GetDocumentVoteHeaders());
                    }

                    lock (_settings.ChatTopicHeaders.ThisLock)
                    {
                        _settings.ChatTopicHeaders.Clear();
                        _settings.ChatTopicHeaders.AddRange(_headerManager.GetChatTopicHeaders());
                    }

                    lock (_settings.ChatMessageHeaders.ThisLock)
                    {
                        _settings.ChatMessageHeaders.Clear();
                        _settings.ChatMessageHeaders.AddRange(_headerManager.GetChatMessageHeaders());
                    }
                }

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
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<Node>() { Name = "BaseNode", Value = new Node(new byte[64], null)},
                    new Library.Configuration.SettingContent<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingContent<int>() { Name = "ConnectionCountLimit", Value = 12 },
                    new Library.Configuration.SettingContent<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "DiffusionBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "UploadBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<LockedList<SectionProfileHeader>>() { Name = "SectionProfileHeaders", Value = new LockedList<SectionProfileHeader>() },
                    new Library.Configuration.SettingContent<LockedList<SectionMessageHeader>>() { Name = "SectionMessageHeaders", Value = new LockedList<SectionMessageHeader>() },
                    new Library.Configuration.SettingContent<LockedList<DocumentArchiveHeader>>() { Name = "DocumentArchiveHeaders", Value = new LockedList<DocumentArchiveHeader>() },
                    new Library.Configuration.SettingContent<LockedList<DocumentVoteHeader>>() { Name = "DocumentVoteHeaders", Value = new LockedList<DocumentVoteHeader>() },
                    new Library.Configuration.SettingContent<LockedList<ChatTopicHeader>>() { Name = "ChatTopicHeaders", Value = new LockedList<ChatTopicHeader>() },
                    new Library.Configuration.SettingContent<LockedList<ChatMessageHeader>>() { Name = "ChatMessageHeaders", Value = new LockedList<ChatMessageHeader>() },
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

            public LockedList<SectionProfileHeader> SectionProfileHeaders
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<SectionProfileHeader>)this["SectionProfileHeaders"];
                    }
                }
            }

            public LockedList<SectionMessageHeader> SectionMessageHeaders
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<SectionMessageHeader>)this["SectionMessageHeaders"];
                    }
                }
            }

            public LockedList<DocumentArchiveHeader> DocumentArchiveHeaders
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<DocumentArchiveHeader>)this["DocumentArchiveHeaders"];
                    }
                }
            }

            public LockedList<DocumentVoteHeader> DocumentVoteHeaders
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<DocumentVoteHeader>)this["DocumentVoteHeaders"];
                    }
                }
            }

            public LockedList<ChatTopicHeader> ChatTopicHeaders
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<ChatTopicHeader>)this["ChatTopicHeaders"];
                    }
                }
            }

            public LockedList<ChatMessageHeader> ChatMessageHeaders
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<ChatMessageHeader>)this["ChatMessageHeaders"];
                    }
                }
            }
        }

        private class HeaderManager
        {
            private Dictionary<Section, Dictionary<string, SectionProfileHeader>> _sectionProfileHeaders = new Dictionary<Section, Dictionary<string, SectionProfileHeader>>();
            private Dictionary<Section, Dictionary<string, HashSet<SectionMessageHeader>>> _sectionMessageHeaders = new Dictionary<Section, Dictionary<string, HashSet<SectionMessageHeader>>>();
            private Dictionary<Document, Dictionary<string, DocumentArchiveHeader>> _documentArchiveHeaders = new Dictionary<Document, Dictionary<string, DocumentArchiveHeader>>();
            private Dictionary<Document, Dictionary<string, DocumentVoteHeader>> _documentVoteHeaders = new Dictionary<Document, Dictionary<string, DocumentVoteHeader>>();
            private Dictionary<Chat, Dictionary<string, ChatTopicHeader>> _chatTopicHeaders = new Dictionary<Chat, Dictionary<string, ChatTopicHeader>>();
            private Dictionary<Chat, Dictionary<string, HashSet<ChatMessageHeader>>> _chatMessageHeaders = new Dictionary<Chat, Dictionary<string, HashSet<ChatMessageHeader>>>();

            public HeaderManager()
            {

            }

            public IEnumerable<Section> GetSections()
            {
                var hashset = new HashSet<Section>();

                hashset.UnionWith(_sectionProfileHeaders.Keys);
                hashset.UnionWith(_sectionMessageHeaders.Keys);

                return hashset;
            }

            public IEnumerable<Document> GetDocuments()
            {
                var hashset = new HashSet<Document>();

                hashset.UnionWith(_documentArchiveHeaders.Keys);
                hashset.UnionWith(_documentVoteHeaders.Keys);

                return hashset;
            }

            public IEnumerable<Chat> GetChats()
            {
                var hashset = new HashSet<Chat>();

                hashset.UnionWith(_chatTopicHeaders.Keys);
                hashset.UnionWith(_chatMessageHeaders.Keys);

                return hashset;
            }

            public void RemoveSections(IEnumerable<Section> sections)
            {
                foreach (var section in sections)
                {
                    _sectionProfileHeaders.Remove(section);
                    _sectionMessageHeaders.Remove(section);
                }
            }

            public void RemoveDocuments(IEnumerable<Document> documents)
            {
                foreach (var document in documents)
                {
                    _documentArchiveHeaders.Remove(document);
                    _documentVoteHeaders.Remove(document);
                }
            }

            public void RemoveChats(IEnumerable<Chat> chats)
            {
                foreach (var chat in chats)
                {
                    _chatTopicHeaders.Remove(chat);
                    _chatMessageHeaders.Remove(chat);
                }
            }

            public IEnumerable<SectionProfileHeader> GetSectionProfileHeaders()
            {
                return _sectionProfileHeaders.Values.SelectMany(n => n.Values);
            }

            public IEnumerable<SectionProfileHeader> GetSectionProfileHeaders(Section section)
            {
                Dictionary<string, SectionProfileHeader> dic = null;

                if (_sectionProfileHeaders.TryGetValue(section, out dic))
                {
                    return dic.Values.ToArray();
                }

                return new SectionProfileHeader[0];
            }

            public IEnumerable<SectionMessageHeader> GetSectionMessageHeaders()
            {
                return _sectionMessageHeaders.Values.SelectMany(n => n.Values.SelectMany(m => m));
            }

            public IEnumerable<SectionMessageHeader> GetSectionMessageHeaders(Section section)
            {
                Dictionary<string, HashSet<SectionMessageHeader>> dic = null;

                if (_sectionMessageHeaders.TryGetValue(section, out dic))
                {
                    return dic.Values.SelectMany(n => n).ToArray();
                }

                return new SectionMessageHeader[0];
            }

            public IEnumerable<DocumentArchiveHeader> GetDocumentArchiveHeaders()
            {
                return _documentArchiveHeaders.Values.SelectMany(n => n.Values);
            }

            public IEnumerable<DocumentArchiveHeader> GetDocumentArchiveHeaders(Document document)
            {
                Dictionary<string, DocumentArchiveHeader> dic = null;

                if (_documentArchiveHeaders.TryGetValue(document, out dic))
                {
                    return dic.Values.ToArray();
                }

                return new DocumentArchiveHeader[0];
            }

            public IEnumerable<DocumentVoteHeader> GetDocumentVoteHeaders()
            {
                return _documentVoteHeaders.Values.SelectMany(n => n.Values);
            }

            public IEnumerable<DocumentVoteHeader> GetDocumentVoteHeaders(Document document)
            {
                Dictionary<string, DocumentVoteHeader> dic = null;

                if (_documentVoteHeaders.TryGetValue(document, out dic))
                {
                    return dic.Values.ToArray();
                }

                return new DocumentVoteHeader[0];
            }

            public IEnumerable<ChatTopicHeader> GetChatTopicHeaders()
            {
                return _chatTopicHeaders.Values.SelectMany(n => n.Values);
            }

            public IEnumerable<ChatTopicHeader> GetChatTopicHeaders(Chat chat)
            {
                Dictionary<string, ChatTopicHeader> dic = null;

                if (_chatTopicHeaders.TryGetValue(chat, out dic))
                {
                    return dic.Values.ToArray();
                }

                return new ChatTopicHeader[0];
            }

            public IEnumerable<ChatMessageHeader> GetChatMessageHeaders()
            {
                return _chatMessageHeaders.Values.SelectMany(n => n.Values.SelectMany(m => m));
            }

            public IEnumerable<ChatMessageHeader> GetChatMessageHeaders(Chat chat)
            {
                Dictionary<string, HashSet<ChatMessageHeader>> dic = null;

                if (_chatMessageHeaders.TryGetValue(chat, out dic))
                {
                    return dic.Values.SelectMany(n => n).ToArray();
                }

                return new ChatMessageHeader[0];
            }

            public bool SetHeader(SectionProfileHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return false;

                var signature = header.Certificate.ToString();

                Dictionary<string, SectionProfileHeader> dic = null;

                if (!_sectionProfileHeaders.TryGetValue(header.Tag, out dic))
                {
                    dic = new Dictionary<string, SectionProfileHeader>();
                    _sectionProfileHeaders[header.Tag] = dic;
                }

                SectionProfileHeader tempHeader = null;

                if (!dic.TryGetValue(signature, out tempHeader)
                    || header.CreationTime > tempHeader.CreationTime)
                {
                    dic[signature] = header;

                    return true;
                }

                return false;
            }

            public bool SetHeader(SectionMessageHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || (now - header.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return false;

                var signature = header.Certificate.ToString();

                Dictionary<string, HashSet<SectionMessageHeader>> dic = null;

                if (!_sectionMessageHeaders.TryGetValue(header.Tag, out dic))
                {
                    dic = new Dictionary<string, HashSet<SectionMessageHeader>>();
                    _sectionMessageHeaders[header.Tag] = dic;
                }

                HashSet<SectionMessageHeader> hashset = null;

                if (!dic.TryGetValue(signature, out hashset))
                {
                    hashset = new HashSet<SectionMessageHeader>();
                    dic[signature] = hashset;
                }

                return hashset.Add(header);
            }

            public bool SetHeader(DocumentArchiveHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return false;

                var signature = header.Certificate.ToString();

                Dictionary<string, DocumentArchiveHeader> dic = null;

                if (!_documentArchiveHeaders.TryGetValue(header.Tag, out dic))
                {
                    dic = new Dictionary<string, DocumentArchiveHeader>();
                    _documentArchiveHeaders[header.Tag] = dic;
                }

                DocumentArchiveHeader tempHeader = null;

                if (!dic.TryGetValue(signature, out tempHeader)
                    || header.CreationTime > tempHeader.CreationTime)
                {
                    dic[signature] = header;

                    return true;
                }

                return false;
            }

            public bool SetHeader(DocumentVoteHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return false;

                var signature = header.Certificate.ToString();

                Dictionary<string, DocumentVoteHeader> dic = null;

                if (!_documentVoteHeaders.TryGetValue(header.Tag, out dic))
                {
                    dic = new Dictionary<string, DocumentVoteHeader>();
                    _documentVoteHeaders[header.Tag] = dic;
                }

                DocumentVoteHeader tempHeader = null;

                if (!dic.TryGetValue(signature, out tempHeader)
                    || header.CreationTime > tempHeader.CreationTime)
                {
                    dic[signature] = header;

                    return true;
                }

                return false;
            }

            public bool SetHeader(ChatTopicHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return false;

                var signature = header.Certificate.ToString();

                Dictionary<string, ChatTopicHeader> dic = null;

                if (!_chatTopicHeaders.TryGetValue(header.Tag, out dic))
                {
                    dic = new Dictionary<string, ChatTopicHeader>();
                    _chatTopicHeaders[header.Tag] = dic;
                }

                ChatTopicHeader tempHeader = null;

                if (!dic.TryGetValue(signature, out tempHeader)
                    || header.CreationTime > tempHeader.CreationTime)
                {
                    dic[signature] = header;

                    return true;
                }

                return false;
            }

            public bool SetHeader(ChatMessageHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || (now - header.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return false;

                var signature = header.Certificate.ToString();

                Dictionary<string, HashSet<ChatMessageHeader>> dic = null;

                if (!_chatMessageHeaders.TryGetValue(header.Tag, out dic))
                {
                    dic = new Dictionary<string, HashSet<ChatMessageHeader>>();
                    _chatMessageHeaders[header.Tag] = dic;
                }

                HashSet<ChatMessageHeader> hashset = null;

                if (!dic.TryGetValue(signature, out hashset))
                {
                    hashset = new HashSet<ChatMessageHeader>();
                    dic[signature] = hashset;
                }

                return hashset.Add(header);
            }

            public void RemoveHeader(SectionProfileHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return;

                var signature = header.Certificate.ToString();

                Dictionary<string, SectionProfileHeader> dic = null;

                if (!_sectionProfileHeaders.TryGetValue(header.Tag, out dic)) return;

                SectionProfileHeader tempHeader = null;

                if (dic.TryGetValue(signature, out tempHeader)
                    && header == tempHeader)
                {
                    dic.Remove(signature);
                }
            }

            public void RemoveHeader(SectionMessageHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || (now - header.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return;

                var signature = header.Certificate.ToString();

                Dictionary<string, HashSet<SectionMessageHeader>> dic = null;

                if (!_sectionMessageHeaders.TryGetValue(header.Tag, out dic)) return;

                HashSet<SectionMessageHeader> hashset = null;

                if (dic.TryGetValue(signature, out hashset))
                {
                    hashset.Remove(header);
                }
            }

            public void RemoveHeader(DocumentArchiveHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return;

                var signature = header.Certificate.ToString();

                Dictionary<string, DocumentArchiveHeader> dic = null;

                if (!_documentArchiveHeaders.TryGetValue(header.Tag, out dic)) return;

                DocumentArchiveHeader tempHeader = null;

                if (dic.TryGetValue(signature, out tempHeader)
                    && header == tempHeader)
                {
                    dic.Remove(signature);
                }
            }

            public void RemoveHeader(DocumentVoteHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return;

                var signature = header.Certificate.ToString();

                Dictionary<string, DocumentVoteHeader> dic = null;

                if (!_documentVoteHeaders.TryGetValue(header.Tag, out dic)) return;

                DocumentVoteHeader tempHeader = null;

                if (dic.TryGetValue(signature, out tempHeader)
                    && header == tempHeader)
                {
                    dic.Remove(signature);
                }
            }

            public void RemoveHeader(ChatTopicHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return;

                var signature = header.Certificate.ToString();

                Dictionary<string, ChatTopicHeader> dic = null;

                if (!_chatTopicHeaders.TryGetValue(header.Tag, out dic)) return;

                ChatTopicHeader tempHeader = null;

                if (dic.TryGetValue(signature, out tempHeader)
                    && header == tempHeader)
                {
                    dic.Remove(signature);
                }
            }

            public void RemoveHeader(ChatMessageHeader header)
            {
                var now = DateTime.UtcNow;

                if (header == null
                    || header.Tag == null
                        || header.Tag.Id == null || header.Tag.Id.Length == 0
                        || string.IsNullOrWhiteSpace(header.Tag.Name)
                    || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || (now - header.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || header.Certificate == null || !header.VerifyCertificate()) return;

                var signature = header.Certificate.ToString();

                Dictionary<string, HashSet<ChatMessageHeader>> dic = null;

                if (!_chatMessageHeaders.TryGetValue(header.Tag, out dic)) return;

                HashSet<ChatMessageHeader> hashset = null;

                if (dic.TryGetValue(signature, out hashset))
                {
                    hashset.Remove(header);
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
