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
        private LockedDictionary<Node, HashSet<Section>> _pushSectionsRequestDictionary = new LockedDictionary<Node, HashSet<Section>>();
        private LockedDictionary<Node, HashSet<Wiki>> _pushWikisRequestDictionary = new LockedDictionary<Node, HashSet<Wiki>>();
        private LockedDictionary<Node, HashSet<Chat>> _pushChatsRequestDictionary = new LockedDictionary<Node, HashSet<Chat>>();

        private LockedList<Node> _creatingNodes;
        private VolatileCollection<Node> _waitingNodes;
        private VolatileCollection<Node> _cuttingNodes;
        private VolatileCollection<Node> _removeNodes;
        private VolatileDictionary<Node, int> _nodesStatus;

        private VolatileCollection<Section> _pushSectionsRequestList;
        private VolatileCollection<Wiki> _pushWikisRequestList;
        private VolatileCollection<Chat> _pushChatsRequestList;
        private VolatileCollection<Key> _downloadBlocks;

        private LockedDictionary<Section, DateTime> _lastUsedSectionTimes = new LockedDictionary<Section, DateTime>();
        private LockedDictionary<Wiki, DateTime> _lastUsedWikiTimes = new LockedDictionary<Wiki, DateTime>();
        private LockedDictionary<Chat, DateTime> _lastUsedChatTimes = new LockedDictionary<Chat, DateTime>();

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

            _routeTable = new Kademlia<Node>(512, 30);

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
            _waitingNodes = new VolatileCollection<Node>(new TimeSpan(0, 0, 10));
            _cuttingNodes = new VolatileCollection<Node>(new TimeSpan(0, 10, 0));
            _removeNodes = new VolatileCollection<Node>(new TimeSpan(0, 30, 0));
            _nodesStatus = new VolatileDictionary<Node, int>(new TimeSpan(0, 30, 0));

            _downloadBlocks = new VolatileCollection<Key>(new TimeSpan(0, 3, 0));
            _pushSectionsRequestList = new VolatileCollection<Section>(new TimeSpan(0, 3, 0));
            _pushWikisRequestList = new VolatileCollection<Wiki>(new TimeSpan(0, 3, 0));
            _pushChatsRequestList = new VolatileCollection<Chat>(new TimeSpan(0, 3, 0));

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

                    contexts.Add(new InformationContext("HeaderCount", _headerManager.Count));

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

        private double GetPriority(Node node)
        {
            lock (this.ThisLock)
            {
                var priority = _messagesManager[node].Priority;

                return ((double)Math.Max(Math.Min(priority + 64, 128), 0)) / 128;
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
                    uris.UnionWith(node.Uris.Take(12));

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
            public int Priority { get; set; }
            public TimeSpan ResponseTime { get; set; }
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
                    && connectionCheckStopwatch.Elapsed.TotalMinutes >= 20)
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
                                Priority = _messagesManager[connectionManager.Node].Priority,
                                ResponseTime = connectionManager.ResponseTime,
                            });
                        }
                    }

                    nodeSortItems.Sort((x, y) =>
                    {
                        int c = x.Priority.CompareTo(y.Priority);
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

                                // トラストなタグは常に保護、非トラストなタグはLRU順に破棄し1024残す。
                                {
                                    {
                                        var removeSections = new HashSet<Section>();
                                        removeSections.UnionWith(_headerManager.GetSections());
                                        removeSections.ExceptWith(lockCriteriaList.SelectMany(n => n.Sections));

                                        var sortList = removeSections.ToList();

                                        sortList.Sort((x, y) =>
                                        {
                                            DateTime tx;
                                            DateTime ty;

                                            _lastUsedSectionTimes.TryGetValue(x, out tx);
                                            _lastUsedSectionTimes.TryGetValue(y, out ty);

                                            return tx.CompareTo(ty);
                                        });

                                        _headerManager.RemoveTags(sortList.Take(sortList.Count - 256));

                                        var liveSections = new HashSet<Section>(_headerManager.GetSections());

                                        // _lastUsedSectionTimesの同期
                                        foreach (var section in _lastUsedSectionTimes.Keys.ToArray())
                                        {
                                            if (liveSections.Contains(section)) continue;

                                            _lastUsedSectionTimes.Remove(section);
                                        }
                                    }

                                    {
                                        var removeWikis = new HashSet<Wiki>();
                                        removeWikis.UnionWith(_headerManager.GetWikis());
                                        removeWikis.ExceptWith(lockCriteriaList.SelectMany(n => n.Wikis));

                                        var sortList = removeWikis.ToList();

                                        sortList.Sort((x, y) =>
                                        {
                                            DateTime tx;
                                            DateTime ty;

                                            _lastUsedWikiTimes.TryGetValue(x, out tx);
                                            _lastUsedWikiTimes.TryGetValue(y, out ty);

                                            return tx.CompareTo(ty);
                                        });

                                        _headerManager.RemoveTags(sortList.Take(sortList.Count - 256));

                                        var liveWikis = new HashSet<Wiki>(_headerManager.GetWikis());

                                        foreach (var section in _lastUsedWikiTimes.Keys.ToArray())
                                        {
                                            if (liveWikis.Contains(section)) continue;

                                            _lastUsedWikiTimes.Remove(section);
                                        }
                                    }

                                    {
                                        var removeChats = new HashSet<Chat>();
                                        removeChats.UnionWith(_headerManager.GetChats());
                                        removeChats.ExceptWith(lockCriteriaList.SelectMany(n => n.Chats));

                                        var sortList = removeChats.ToList();

                                        sortList.Sort((x, y) =>
                                        {
                                            DateTime tx;
                                            DateTime ty;

                                            _lastUsedChatTimes.TryGetValue(x, out tx);
                                            _lastUsedChatTimes.TryGetValue(y, out ty);

                                            return tx.CompareTo(ty);
                                        });

                                        _headerManager.RemoveTags(sortList.Take(sortList.Count - 256));

                                        var liveChats = new HashSet<Chat>(_headerManager.GetChats());

                                        foreach (var section in _lastUsedChatTimes.Keys.ToArray())
                                        {
                                            if (liveChats.Contains(section)) continue;

                                            _lastUsedChatTimes.Remove(section);
                                        }
                                    }
                                }

                                {
                                    var patternList = new List<KeyValuePair<HashSet<Section>, HashSet<string>>>();

                                    foreach (var criterion in lockCriteriaList)
                                    {
                                        var tagHashset = new HashSet<Section>(criterion.Sections);
                                        var signatureHashset = new HashSet<string>(criterion.TrustSignatures);

                                        patternList.Add(new KeyValuePair<HashSet<Section>, HashSet<string>>(tagHashset, signatureHashset));
                                    }

                                    var removeSectionProfileHeaders = new HashSet<SectionProfileHeader>();
                                    var removeSectionMessageHeaders = new HashSet<SectionMessageHeader>();

                                    foreach (var section in _headerManager.GetSections())
                                    {
                                        List<HashSet<string>> trustList;

                                        // Turstリストの抽出
                                        {
                                            var tempList = patternList.Where(n => n.Key.Contains(section)).ToList();
                                            trustList = new List<HashSet<string>>(tempList.Select(n => n.Value));
                                        }

                                        // SectionProfileの選別
                                        {
                                            var untrustHeaders = new List<SectionProfileHeader>();

                                            var headers = _headerManager.GetSectionProfileHeaders(section).ToList();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (!trustList.Any(n => n.Contains(signature)))
                                                {
                                                    untrustHeaders.Add(header);
                                                }
                                            }

                                            removeSectionProfileHeaders.UnionWith(untrustHeaders.Randomize().Skip(32));
                                        }

                                        // SectionMessageの選別
                                        {
                                            var trustMessageHeaders = new Dictionary<string, List<SectionMessageHeader>>();
                                            var untrustMessageHeaders = new Dictionary<string, List<SectionMessageHeader>>();

                                            var headers = _headerManager.GetSectionMessageHeaders(section).ToList();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (trustList.Any(n => n.Contains(signature)))
                                                {
                                                    List<SectionMessageHeader> list;

                                                    if (!trustMessageHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<SectionMessageHeader>();
                                                        trustMessageHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                                else
                                                {
                                                    List<SectionMessageHeader> list;

                                                    if (!untrustMessageHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<SectionMessageHeader>();
                                                        untrustMessageHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                            }

                                            removeSectionMessageHeaders.UnionWith(untrustMessageHeaders.SelectMany(n => n.Value).Randomize().Skip(32));

                                            foreach (var list in Collection.Merge(trustMessageHeaders.Values, untrustMessageHeaders.Values))
                                            {
                                                var tempList = new List<SectionMessageHeader>();

                                                foreach (var header in list)
                                                {
                                                    if ((now - header.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                                    {
                                                        removeSectionMessageHeaders.Add(header);
                                                    }
                                                    else
                                                    {
                                                        tempList.Add(header);
                                                    }
                                                }

                                                if (list.Count <= 32) continue;

                                                tempList.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeSectionMessageHeaders.UnionWith(tempList.Take(tempList.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var header in removeSectionProfileHeaders)
                                    {
                                        _headerManager.RemoveHeader(header);
                                    }

                                    foreach (var header in removeSectionMessageHeaders)
                                    {
                                        _headerManager.RemoveHeader(header);
                                    }
                                }

                                {
                                    var patternList = new List<KeyValuePair<HashSet<Wiki>, HashSet<string>>>();

                                    foreach (var criterion in lockCriteriaList)
                                    {
                                        var tagHashset = new HashSet<Wiki>(criterion.Wikis);
                                        var signatureHashset = new HashSet<string>(criterion.TrustSignatures);

                                        patternList.Add(new KeyValuePair<HashSet<Wiki>, HashSet<string>>(tagHashset, signatureHashset));
                                    }

                                    var removeWikiPageHeaders = new HashSet<WikiPageHeader>();
                                    var removeWikiVoteHeaders = new HashSet<WikiVoteHeader>();

                                    foreach (var wiki in _headerManager.GetWikis())
                                    {
                                        List<HashSet<string>> trustList;

                                        // Turstリストの抽出
                                        {
                                            var tempList = patternList.Where(n => n.Key.Contains(wiki)).ToList();
                                            trustList = new List<HashSet<string>>(tempList.Select(n => n.Value));
                                        }

                                        // WikiPageの選別
                                        {
                                            var trustMessageHeaders = new Dictionary<string, List<WikiPageHeader>>();
                                            var untrustMessageHeaders = new Dictionary<string, List<WikiPageHeader>>();

                                            var headers = _headerManager.GetWikiPageHeaders(wiki).ToList();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (trustList.Any(n => n.Contains(signature)))
                                                {
                                                    List<WikiPageHeader> list;

                                                    if (!trustMessageHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<WikiPageHeader>();
                                                        trustMessageHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                                else
                                                {
                                                    List<WikiPageHeader> list;

                                                    if (!untrustMessageHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<WikiPageHeader>();
                                                        untrustMessageHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                            }

                                            removeWikiPageHeaders.UnionWith(untrustMessageHeaders.SelectMany(n => n.Value).Randomize().Skip(32));

                                            foreach (var list in Collection.Merge(trustMessageHeaders.Values, untrustMessageHeaders.Values))
                                            {
                                                if (list.Count <= 32) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeWikiPageHeaders.UnionWith(list.Take(list.Count - 32));
                                            }
                                        }

                                        // WikiVoteの選別
                                        {
                                            var untrustHeaders = new List<WikiVoteHeader>();

                                            var headers = _headerManager.GetWikiVoteHeaders(wiki).ToList();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (!trustList.Any(n => n.Contains(signature)))
                                                {
                                                    untrustHeaders.Add(header);
                                                }
                                            }

                                            removeWikiVoteHeaders.UnionWith(untrustHeaders.Randomize().Skip(32));
                                        }
                                    }

                                    foreach (var header in removeWikiPageHeaders)
                                    {
                                        _headerManager.RemoveHeader(header);
                                    }

                                    foreach (var header in removeWikiVoteHeaders)
                                    {
                                        _headerManager.RemoveHeader(header);
                                    }
                                }

                                {
                                    var patternList = new List<KeyValuePair<HashSet<Chat>, HashSet<string>>>();

                                    foreach (var criterion in lockCriteriaList)
                                    {
                                        var tagHashset = new HashSet<Chat>(criterion.Chats);
                                        var signatureHashset = new HashSet<string>(criterion.TrustSignatures);

                                        patternList.Add(new KeyValuePair<HashSet<Chat>, HashSet<string>>(tagHashset, signatureHashset));
                                    }

                                    var removeChatTopicHeaders = new HashSet<ChatTopicHeader>();
                                    var removeChatMessageHeaders = new HashSet<ChatMessageHeader>();

                                    foreach (var chat in _headerManager.GetChats())
                                    {
                                        List<HashSet<string>> trustList;

                                        // Turstリストの抽出
                                        {
                                            var tempList = patternList.Where(n => n.Key.Contains(chat)).ToList();
                                            trustList = new List<HashSet<string>>(tempList.Select(n => n.Value));
                                        }

                                        // ChatTopicの選別
                                        {
                                            var untrustHeaders = new List<ChatTopicHeader>();

                                            var headers = _headerManager.GetChatTopicHeaders(chat).ToList();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (!trustList.Any(n => n.Contains(signature)))
                                                {
                                                    untrustHeaders.Add(header);
                                                }
                                            }

                                            removeChatTopicHeaders.UnionWith(untrustHeaders.Randomize().Skip(32));
                                        }

                                        // ChatMessageの選別
                                        {
                                            var trustMessageHeaders = new Dictionary<string, List<ChatMessageHeader>>();
                                            var untrustMessageHeaders = new Dictionary<string, List<ChatMessageHeader>>();

                                            var headers = _headerManager.GetChatMessageHeaders(chat).ToList();

                                            foreach (var header in headers)
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (trustList.Any(n => n.Contains(signature)))
                                                {
                                                    List<ChatMessageHeader> list;

                                                    if (!trustMessageHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<ChatMessageHeader>();
                                                        trustMessageHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                                else
                                                {
                                                    List<ChatMessageHeader> list;

                                                    if (!untrustMessageHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<ChatMessageHeader>();
                                                        untrustMessageHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                            }

                                            removeChatMessageHeaders.UnionWith(untrustMessageHeaders.SelectMany(n => n.Value).Randomize().Skip(32));

                                            foreach (var list in Collection.Merge(trustMessageHeaders.Values, untrustMessageHeaders.Values))
                                            {
                                                var tempList = new List<ChatMessageHeader>();

                                                foreach (var header in list)
                                                {
                                                    if ((now - header.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                                    {
                                                        removeChatMessageHeaders.Add(header);
                                                    }
                                                    else
                                                    {
                                                        tempList.Add(header);
                                                    }
                                                }

                                                if (list.Count <= 32) continue;

                                                tempList.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeChatMessageHeaders.UnionWith(tempList.Take(tempList.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var header in removeChatTopicHeaders)
                                    {
                                        _headerManager.RemoveHeader(header);
                                    }

                                    foreach (var header in removeChatMessageHeaders)
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
                            lock (_settings.DiffusionBlocksRequest.ThisLock)
                            {
                                if (_settings.DiffusionBlocksRequest.Count > 10000)
                                {
                                    var tempList = _settings.DiffusionBlocksRequest.Randomize().Take(10000).ToList();
                                    _settings.DiffusionBlocksRequest.Clear();
                                    _settings.DiffusionBlocksRequest.UnionWith(tempList);
                                }
                            }

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
                                var requestNodes = this.GetSearchNode(item.Hash, 1).ToList();

                                if (requestNodes.Count == 0)
                                {
                                    _settings.UploadBlocksRequest.Remove(item);
                                    _settings.DiffusionBlocksRequest.Remove(item);

                                    continue;
                                }

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
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
                                    requestNodes.AddRange(this.GetSearchNode(item.Hash, 2));

                                for (int i = 0; i < 2 && i < requestNodes.Count; i++)
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

                    foreach (var item in _headerManager.GetSections())
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

                    foreach (var item in _headerManager.GetWikis())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullWikisRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var item in _headerManager.GetChats())
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

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
                }

                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushHeaderDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushHeaderDownloadStopwatch.Restart();

                    HashSet<Section> pushSectionsRequestList = new HashSet<Section>();
                    HashSet<Wiki> pushWikisRequestList = new HashSet<Wiki>();
                    HashSet<Chat> pushChatsRequestList = new HashSet<Chat>();
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

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
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

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushSectionsRequest.Contains(list[i])))
                                {
                                    pushSectionsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _pushWikisRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushWikisRequest.Contains(list[i])))
                                {
                                    pushWikisRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullWikisRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushWikisRequest.Contains(list[i])))
                                {
                                    pushWikisRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _pushChatsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
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

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                if (!nodes.Any(n => _messagesManager[n].PushChatsRequest.Contains(list[i])))
                                {
                                    pushChatsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }
                    }

                    {
                        Dictionary<Node, HashSet<Section>> pushSectionsRequestDictionary = new Dictionary<Node, HashSet<Section>>();

                        foreach (var item in pushSectionsRequestList)
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

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
                        Dictionary<Node, HashSet<Wiki>> pushWikisRequestDictionary = new Dictionary<Node, HashSet<Wiki>>();

                        foreach (var item in pushWikisRequestList)
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Wiki> hashset;

                                    if (!pushWikisRequestDictionary.TryGetValue(requestNodes[i], out hashset))
                                    {
                                        hashset = new HashSet<Wiki>();
                                        pushWikisRequestDictionary[requestNodes[i]] = hashset;
                                    }

                                    hashset.Add(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushWikisRequestDictionary.ThisLock)
                        {
                            _pushWikisRequestDictionary.Clear();

                            foreach (var item in pushWikisRequestDictionary)
                            {
                                _pushWikisRequestDictionary.Add(item.Key, item.Value);
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
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

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
                Stopwatch diffusionTime = new Stopwatch();
                diffusionTime.Start();
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
                    if (messageManager.Priority < 0 && checkTime.Elapsed.TotalSeconds >= 60)
                    {
                        checkTime.Restart();

                        if ((DateTime.UtcNow - messageManager.LastPullTime) > new TimeSpan(0, 10, 0))
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
                            KeyCollection tempList = new KeyCollection();

                            lock (_pushBlocksLinkDictionary.ThisLock)
                            {
                                HashSet<Key> hashset;

                                if (_pushBlocksLinkDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    tempList.AddRange(hashset.Randomize().Take(_maxBlockLinkCount));

                                    hashset.ExceptWith(tempList);
                                    messageManager.PushBlocksLink.AddRange(tempList);
                                }
                            }

                            if (tempList.Count > 0)
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
                            KeyCollection tempList = new KeyCollection();

                            lock (_pushBlocksRequestDictionary.ThisLock)
                            {
                                HashSet<Key> hashset;

                                if (_pushBlocksRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    tempList.AddRange(hashset.Randomize().Take(_maxBlockRequestCount));

                                    hashset.ExceptWith(tempList);
                                    messageManager.PushBlocksRequest.AddRange(tempList);
                                }
                            }

                            if (tempList.Count > 0)
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
                            SectionCollection sectionList = new SectionCollection();
                            WikiCollection wikiList = new WikiCollection();
                            ChatCollection chatList = new ChatCollection();

                            lock (_pushSectionsRequestDictionary.ThisLock)
                            {
                                HashSet<Section> hashset;

                                if (_pushSectionsRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    sectionList.AddRange(hashset.Randomize().Take(_maxHeaderRequestCount));

                                    hashset.ExceptWith(sectionList);
                                    messageManager.PushSectionsRequest.AddRange(sectionList);
                                }
                            }

                            lock (_pushWikisRequestDictionary.ThisLock)
                            {
                                HashSet<Wiki> hashset;

                                if (_pushWikisRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    wikiList.AddRange(hashset.Randomize().Take(_maxHeaderRequestCount));

                                    hashset.ExceptWith(wikiList);
                                    messageManager.PushWikisRequest.AddRange(wikiList);
                                }
                            }

                            lock (_pushChatsRequestDictionary.ThisLock)
                            {
                                HashSet<Chat> hashset;

                                if (_pushChatsRequestDictionary.TryGetValue(connectionManager.Node, out hashset))
                                {
                                    chatList.AddRange(hashset.Randomize().Take(_maxHeaderRequestCount));

                                    hashset.ExceptWith(chatList);
                                    messageManager.PushChatsRequest.AddRange(chatList);
                                }
                            }

                            if (sectionList.Count > 0 || wikiList.Count > 0 || chatList.Count > 0)
                            {
                                try
                                {
                                    connectionManager.PushHeadersRequest(sectionList, wikiList, chatList);

                                    foreach (var item in sectionList)
                                    {
                                        _pushSectionsRequestList.Remove(item);
                                    }

                                    foreach (var item in wikiList)
                                    {
                                        _pushWikisRequestList.Remove(item);
                                    }

                                    foreach (var item in chatList)
                                    {
                                        _pushChatsRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push HeadersRequest ({0})",
                                        sectionList.Count + wikiList.Count + chatList.Count));

                                    _pushHeaderRequestCount += sectionList.Count;
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in sectionList)
                                    {
                                        messageManager.PushSectionsRequest.Remove(item);
                                    }

                                    foreach (var item in wikiList)
                                    {
                                        messageManager.PushWikisRequest.Remove(item);
                                    }

                                    foreach (var item in chatList)
                                    {
                                        messageManager.PushChatsRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }
                    }

                    if (diffusionTime.Elapsed.TotalSeconds >= 5)
                    {
                        diffusionTime.Restart();

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

                    if (_random.NextDouble() < this.GetPriority(connectionManager.Node))
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
                            var sections = messageManager.PullSectionsRequest.ToList();
                            var wikis = messageManager.PullWikisRequest.ToList();
                            var chats = messageManager.PullChatsRequest.ToList();

                            var sectionProfileHeaders = new List<SectionProfileHeader>();
                            var sectionMessageHeaders = new List<SectionMessageHeader>();
                            var wikiPageHeaders = new List<WikiPageHeader>();
                            var wikiVoteHeaders = new List<WikiVoteHeader>();
                            var chatTopicHeaders = new List<ChatTopicHeader>();
                            var chatMessageHeaders = new List<ChatMessageHeader>();

                            foreach (var tag in sections.Randomize())
                            {
                                foreach (var header in _headerManager.GetSectionProfileHeaders(tag))
                                {
                                    if (!messageManager.PushSectionProfileHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                    {
                                        sectionProfileHeaders.Add(header);

                                        if (sectionProfileHeaders.Count >= _maxHeaderCount) break;
                                    }
                                }

                                foreach (var header in _headerManager.GetSectionMessageHeaders(tag))
                                {
                                    if (!messageManager.PushSectionMessageHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                    {
                                        sectionMessageHeaders.Add(header);

                                        if (sectionMessageHeaders.Count >= _maxHeaderCount) break;
                                    }
                                }

                                if (sectionProfileHeaders.Count >= _maxHeaderCount) break;
                                if (sectionMessageHeaders.Count >= _maxHeaderCount) break;
                            }

                            foreach (var tag in wikis.Randomize())
                            {
                                foreach (var header in _headerManager.GetWikiPageHeaders(tag))
                                {
                                    if (!messageManager.PushWikiPageHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                    {
                                        wikiPageHeaders.Add(header);

                                        if (wikiPageHeaders.Count >= _maxHeaderCount) break;
                                    }
                                }

                                foreach (var header in _headerManager.GetWikiVoteHeaders(tag))
                                {
                                    if (!messageManager.PushWikiVoteHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                    {
                                        wikiVoteHeaders.Add(header);

                                        if (wikiVoteHeaders.Count >= _maxHeaderCount) break;
                                    }
                                }

                                if (wikiPageHeaders.Count >= _maxHeaderCount) break;
                                if (wikiVoteHeaders.Count >= _maxHeaderCount) break;
                            }

                            foreach (var tag in chats.Randomize())
                            {
                                foreach (var header in _headerManager.GetChatTopicHeaders(tag))
                                {
                                    if (!messageManager.PushChatTopicHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                    {
                                        chatTopicHeaders.Add(header);

                                        if (chatTopicHeaders.Count >= _maxHeaderCount) break;
                                    }
                                }

                                foreach (var header in _headerManager.GetChatMessageHeaders(tag))
                                {
                                    if (!messageManager.PushChatMessageHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                    {
                                        chatMessageHeaders.Add(header);

                                        if (chatMessageHeaders.Count >= _maxHeaderCount) break;
                                    }
                                }

                                if (chatTopicHeaders.Count >= _maxHeaderCount) break;
                                if (chatMessageHeaders.Count >= _maxHeaderCount) break;
                            }

                            if (sectionProfileHeaders.Count > 0
                                || sectionMessageHeaders.Count > 0
                                || wikiPageHeaders.Count > 0
                                || wikiVoteHeaders.Count > 0
                                || chatTopicHeaders.Count > 0
                                || chatMessageHeaders.Count > 0)
                            {
                                connectionManager.PushHeaders(sectionProfileHeaders,
                                    sectionMessageHeaders,
                                    wikiPageHeaders,
                                    wikiVoteHeaders,
                                    chatTopicHeaders,
                                    chatMessageHeaders);

                                var headerCount = sectionProfileHeaders.Count
                                    + sectionMessageHeaders.Count
                                    + wikiPageHeaders.Count
                                    + wikiVoteHeaders.Count
                                    + chatTopicHeaders.Count
                                    + chatMessageHeaders.Count;

                                Debug.WriteLine(string.Format("ConnectionManager: Push Headers ({0})", headerCount));
                                _pushHeaderCount += headerCount;

                                foreach (var header in sectionProfileHeaders)
                                {
                                    messageManager.PushSectionProfileHeaders.Add(header.GetHash(_hashAlgorithm));
                                }

                                foreach (var header in sectionMessageHeaders)
                                {
                                    messageManager.PushSectionMessageHeaders.Add(header.GetHash(_hashAlgorithm));
                                }

                                foreach (var header in wikiPageHeaders)
                                {
                                    messageManager.PushWikiPageHeaders.Add(header.GetHash(_hashAlgorithm));
                                }

                                foreach (var header in wikiVoteHeaders)
                                {
                                    messageManager.PushWikiVoteHeaders.Add(header.GetHash(_hashAlgorithm));
                                }

                                foreach (var header in chatTopicHeaders)
                                {
                                    messageManager.PushChatTopicHeaders.Add(header.GetHash(_hashAlgorithm));
                                }

                                foreach (var header in chatMessageHeaders)
                                {
                                    messageManager.PushChatMessageHeaders.Add(header.GetHash(_hashAlgorithm));
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
            // tryですべて囲まないとメモリーリークの恐れあり。
            try
            {
                var connectionManager = sender as ConnectionManager;
                if (connectionManager == null) return;

                var messageManager = _messagesManager[connectionManager.Node];

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

            if (messageManager.PullSectionsRequest.Count > _maxHeaderRequestCount * messageManager.PullSectionsRequest.SurvivalTime.TotalMinutes) return;
            if (messageManager.PullWikisRequest.Count > _maxHeaderRequestCount * messageManager.PullWikisRequest.SurvivalTime.TotalMinutes) return;
            if (messageManager.PullChatsRequest.Count > _maxHeaderRequestCount * messageManager.PullChatsRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull HeadersRequest ({0})",
                e.Sections.Count() + e.Wikis.Count() + e.Chats.Count()));

            foreach (var tag in e.Sections.Take(_maxHeaderRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullSectionsRequest.Add(tag);
                _pullHeaderRequestCount++;

                _lastUsedSectionTimes[tag] = DateTime.UtcNow;
            }

            foreach (var tag in e.Wikis.Take(_maxHeaderRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullWikisRequest.Add(tag);
                _pullHeaderRequestCount++;

                _lastUsedWikiTimes[tag] = DateTime.UtcNow;
            }

            foreach (var tag in e.Chats.Take(_maxHeaderRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullChatsRequest.Add(tag);
                _pullHeaderRequestCount++;

                _lastUsedChatTimes[tag] = DateTime.UtcNow;
            }
        }

        private void connectionManager_HeadersEvent(object sender, PullHeadersEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PushSectionProfileHeaders.Count > _maxHeaderCount * messageManager.PushSectionProfileHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.PushSectionMessageHeaders.Count > _maxHeaderCount * messageManager.PushSectionMessageHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.PushWikiPageHeaders.Count > _maxHeaderCount * messageManager.PushWikiPageHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.PushWikiVoteHeaders.Count > _maxHeaderCount * messageManager.PushWikiVoteHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.PushChatTopicHeaders.Count > _maxHeaderCount * messageManager.PushChatTopicHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.PushChatMessageHeaders.Count > _maxHeaderCount * messageManager.PushChatMessageHeaders.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Headers ({0})", e.SectionProfileHeaders.Count()
                + e.SectionMessageHeaders.Count()
                + e.WikiPageHeaders.Count()
                + e.WikiVoteHeaders.Count()
                + e.ChatTopicHeaders.Count()
                + e.ChatMessageHeaders.Count()));

            foreach (var header in e.SectionProfileHeaders.Take(_maxHeaderCount))
            {
                if (_headerManager.SetHeader(header))
                {
                    messageManager.PushSectionProfileHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedSectionTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount++;
            }

            foreach (var header in e.SectionMessageHeaders.Take(_maxHeaderCount))
            {
                if (_headerManager.SetHeader(header))
                {
                    messageManager.PushSectionMessageHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedSectionTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount++;
            }

            foreach (var header in e.WikiPageHeaders.Take(_maxHeaderCount))
            {
                if (_headerManager.SetHeader(header))
                {
                    messageManager.PushWikiPageHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedWikiTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount++;
            }

            foreach (var header in e.WikiVoteHeaders.Take(_maxHeaderCount))
            {
                if (_headerManager.SetHeader(header))
                {
                    messageManager.PushWikiVoteHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedWikiTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount++;
            }

            foreach (var header in e.ChatTopicHeaders.Take(_maxHeaderCount))
            {
                if (_headerManager.SetHeader(header))
                {
                    messageManager.PushChatTopicHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedChatTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount++;
            }

            foreach (var header in e.ChatMessageHeaders.Take(_maxHeaderCount))
            {
                if (_headerManager.SetHeader(header))
                {
                    messageManager.PushChatMessageHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedChatTimes[header.Tag] = DateTime.UtcNow;
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

        public IEnumerable<SectionProfileHeader> GetSectionProfileHeaders(Section tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushSectionsRequestList.Add(tag);

                return _headerManager.GetSectionProfileHeaders(tag);
            }
        }

        public IEnumerable<SectionMessageHeader> GetSectionMessageHeaders(Section tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushSectionsRequestList.Add(tag);

                return _headerManager.GetSectionMessageHeaders(tag);
            }
        }

        public IEnumerable<WikiPageHeader> GetWikiPageHeaders(Wiki tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _headerManager.GetWikiPageHeaders(tag);
            }
        }

        public IEnumerable<WikiVoteHeader> GetWikiVoteHeaders(Wiki tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushWikisRequestList.Add(tag);

                return _headerManager.GetWikiVoteHeaders(tag);
            }
        }

        public IEnumerable<ChatTopicHeader> GetChatTopicHeaders(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushChatsRequestList.Add(tag);

                return _headerManager.GetChatTopicHeaders(tag);
            }
        }

        public IEnumerable<ChatMessageHeader> GetChatMessageHeaders(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushChatsRequestList.Add(tag);

                return _headerManager.GetChatMessageHeaders(tag);
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

        public void Upload(WikiPageHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _headerManager.SetHeader(header);
            }
        }

        public void Upload(WikiVoteHeader header)
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

                foreach (var header in _settings.WikiPageHeaders)
                {
                    _headerManager.SetHeader(header);
                }

                foreach (var header in _settings.WikiVoteHeaders)
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

                    lock (_settings.WikiPageHeaders.ThisLock)
                    {
                        _settings.WikiPageHeaders.Clear();
                        _settings.WikiPageHeaders.AddRange(_headerManager.GetWikiPageHeaders());
                    }

                    lock (_settings.WikiVoteHeaders.ThisLock)
                    {
                        _settings.WikiVoteHeaders.Clear();
                        _settings.WikiVoteHeaders.AddRange(_headerManager.GetWikiVoteHeaders());
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
                    new Library.Configuration.SettingContent<LockedList<WikiPageHeader>>() { Name = "WikiPageHeaders", Value = new LockedList<WikiPageHeader>() },
                    new Library.Configuration.SettingContent<LockedList<WikiVoteHeader>>() { Name = "WikiVoteHeaders", Value = new LockedList<WikiVoteHeader>() },
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

            public LockedList<WikiPageHeader> WikiPageHeaders
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<WikiPageHeader>)this["WikiPageHeaders"];
                    }
                }
            }

            public LockedList<WikiVoteHeader> WikiVoteHeaders
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<WikiVoteHeader>)this["WikiVoteHeaders"];
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
            private Dictionary<Wiki, Dictionary<string, HashSet<WikiPageHeader>>> _wikiPageHeaders = new Dictionary<Wiki, Dictionary<string, HashSet<WikiPageHeader>>>();
            private Dictionary<Wiki, Dictionary<string, WikiVoteHeader>> _wikiVoteHeaders = new Dictionary<Wiki, Dictionary<string, WikiVoteHeader>>();
            private Dictionary<Chat, Dictionary<string, ChatTopicHeader>> _chatTopicHeaders = new Dictionary<Chat, Dictionary<string, ChatTopicHeader>>();
            private Dictionary<Chat, Dictionary<string, HashSet<ChatMessageHeader>>> _chatMessageHeaders = new Dictionary<Chat, Dictionary<string, HashSet<ChatMessageHeader>>>();

            private readonly object _thisLock = new object();

            public HeaderManager()
            {

            }

            public int Count
            {
                get
                {
                    lock (_thisLock)
                    {
                        int count = 0;

                        count += _sectionProfileHeaders.Values.Sum(n => n.Values.Count);
                        count += _sectionMessageHeaders.Values.Sum(n => n.Values.Sum(m => m.Count));
                        count += _wikiPageHeaders.Values.Sum(n => n.Values.Sum(m => m.Count));
                        count += _wikiVoteHeaders.Values.Sum(n => n.Values.Count);
                        count += _chatTopicHeaders.Values.Sum(n => n.Values.Count);
                        count += _chatMessageHeaders.Values.Sum(n => n.Values.Sum(m => m.Count));

                        return count;
                    }
                }
            }

            public IEnumerable<Section> GetSections()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<Section>();

                    hashset.UnionWith(_sectionProfileHeaders.Keys);
                    hashset.UnionWith(_sectionMessageHeaders.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Wiki> GetWikis()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<Wiki>();

                    hashset.UnionWith(_wikiPageHeaders.Keys);
                    hashset.UnionWith(_wikiVoteHeaders.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Chat> GetChats()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<Chat>();

                    hashset.UnionWith(_chatTopicHeaders.Keys);
                    hashset.UnionWith(_chatMessageHeaders.Keys);

                    return hashset;
                }
            }

            public void RemoveTags(IEnumerable<Section> tags)
            {
                lock (_thisLock)
                {
                    foreach (var section in tags)
                    {
                        _sectionProfileHeaders.Remove(section);
                        _sectionMessageHeaders.Remove(section);
                    }
                }
            }

            public void RemoveTags(IEnumerable<Wiki> tags)
            {
                lock (_thisLock)
                {
                    foreach (var wiki in tags)
                    {
                        _wikiPageHeaders.Remove(wiki);
                        _wikiVoteHeaders.Remove(wiki);
                    }
                }
            }

            public void RemoveTags(IEnumerable<Chat> tags)
            {
                lock (_thisLock)
                {
                    foreach (var chat in tags)
                    {
                        _chatTopicHeaders.Remove(chat);
                        _chatMessageHeaders.Remove(chat);
                    }
                }
            }

            public IEnumerable<SectionProfileHeader> GetSectionProfileHeaders()
            {
                lock (_thisLock)
                {
                    return _sectionProfileHeaders.Values.SelectMany(n => n.Values);
                }
            }

            public IEnumerable<SectionProfileHeader> GetSectionProfileHeaders(Section tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, SectionProfileHeader> dic = null;

                    if (_sectionProfileHeaders.TryGetValue(tag, out dic))
                    {
                        return dic.Values.ToArray();
                    }

                    return new SectionProfileHeader[0];
                }
            }

            public IEnumerable<SectionMessageHeader> GetSectionMessageHeaders()
            {
                lock (_thisLock)
                {
                    return _sectionMessageHeaders.Values.SelectMany(n => n.Values.SelectMany(m => m));
                }
            }

            public IEnumerable<SectionMessageHeader> GetSectionMessageHeaders(Section tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<SectionMessageHeader>> dic = null;

                    if (_sectionMessageHeaders.TryGetValue(tag, out dic))
                    {
                        return dic.Values.SelectMany(n => n).ToArray();
                    }

                    return new SectionMessageHeader[0];
                }
            }

            public IEnumerable<WikiPageHeader> GetWikiPageHeaders()
            {
                lock (_thisLock)
                {
                    return _wikiPageHeaders.Values.SelectMany(n => n.Values.SelectMany(m => m));
                }
            }

            public IEnumerable<WikiPageHeader> GetWikiPageHeaders(Wiki tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<WikiPageHeader>> dic = null;

                    if (_wikiPageHeaders.TryGetValue(tag, out dic))
                    {
                        return dic.Values.SelectMany(n => n).ToArray();
                    }

                    return new WikiPageHeader[0];
                }
            }

            public IEnumerable<WikiVoteHeader> GetWikiVoteHeaders()
            {
                lock (_thisLock)
                {
                    return _wikiVoteHeaders.Values.SelectMany(n => n.Values);
                }
            }

            public IEnumerable<WikiVoteHeader> GetWikiVoteHeaders(Wiki tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, WikiVoteHeader> dic = null;

                    if (_wikiVoteHeaders.TryGetValue(tag, out dic))
                    {
                        return dic.Values.ToArray();
                    }

                    return new WikiVoteHeader[0];
                }
            }

            public IEnumerable<ChatTopicHeader> GetChatTopicHeaders()
            {
                lock (_thisLock)
                {
                    return _chatTopicHeaders.Values.SelectMany(n => n.Values);
                }
            }

            public IEnumerable<ChatTopicHeader> GetChatTopicHeaders(Chat chat)
            {
                lock (_thisLock)
                {
                    Dictionary<string, ChatTopicHeader> dic = null;

                    if (_chatTopicHeaders.TryGetValue(chat, out dic))
                    {
                        return dic.Values.ToArray();
                    }

                    return new ChatTopicHeader[0];
                }
            }

            public IEnumerable<ChatMessageHeader> GetChatMessageHeaders()
            {
                lock (_thisLock)
                {
                    return _chatMessageHeaders.Values.SelectMany(n => n.Values.SelectMany(m => m));
                }
            }

            public IEnumerable<ChatMessageHeader> GetChatMessageHeaders(Chat tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<ChatMessageHeader>> dic = null;

                    if (_chatMessageHeaders.TryGetValue(tag, out dic))
                    {
                        return dic.Values.SelectMany(n => n).ToArray();
                    }

                    return new ChatMessageHeader[0];
                }
            }

            public bool SetHeader(SectionProfileHeader header)
            {
                lock (_thisLock)
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
            }

            public bool SetHeader(SectionMessageHeader header)
            {
                lock (_thisLock)
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

                    if (hashset.Any(n => n.CreationTime == header.CreationTime)) return false;

                    return hashset.Add(header);
                }
            }

            public bool SetHeader(WikiPageHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || header.Tag == null
                            || header.Tag.Id == null || header.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(header.Tag.Name)
                        || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || header.Certificate == null || !header.VerifyCertificate()) return false;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, HashSet<WikiPageHeader>> dic = null;

                    if (!_wikiPageHeaders.TryGetValue(header.Tag, out dic))
                    {
                        dic = new Dictionary<string, HashSet<WikiPageHeader>>();
                        _wikiPageHeaders[header.Tag] = dic;
                    }

                    HashSet<WikiPageHeader> hashset = null;

                    if (!dic.TryGetValue(signature, out hashset))
                    {
                        hashset = new HashSet<WikiPageHeader>();
                        dic[signature] = hashset;
                    }

                    if (hashset.Any(n => n.CreationTime == header.CreationTime)) return false;

                    return hashset.Add(header);
                }
            }

            public bool SetHeader(WikiVoteHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || header.Tag == null
                            || header.Tag.Id == null || header.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(header.Tag.Name)
                        || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || header.Certificate == null || !header.VerifyCertificate()) return false;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, WikiVoteHeader> dic = null;

                    if (!_wikiVoteHeaders.TryGetValue(header.Tag, out dic))
                    {
                        dic = new Dictionary<string, WikiVoteHeader>();
                        _wikiVoteHeaders[header.Tag] = dic;
                    }

                    WikiVoteHeader tempHeader = null;

                    if (!dic.TryGetValue(signature, out tempHeader)
                        || header.CreationTime > tempHeader.CreationTime)
                    {
                        dic[signature] = header;

                        return true;
                    }

                    return false;
                }
            }

            public bool SetHeader(ChatTopicHeader header)
            {
                lock (_thisLock)
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
            }

            public bool SetHeader(ChatMessageHeader header)
            {
                lock (_thisLock)
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

                    if (hashset.Any(n => n.CreationTime == header.CreationTime)) return false;

                    return hashset.Add(header);
                }
            }

            public void RemoveHeader(SectionProfileHeader header)
            {
                lock (_thisLock)
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

                    if (dic.Count == 0) _sectionProfileHeaders.Remove(header.Tag);
                }
            }

            public void RemoveHeader(SectionMessageHeader header)
            {
                lock (_thisLock)
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

                    if (hashset.Count == 0) dic.Remove(signature);
                    if (dic.Count == 0) _sectionMessageHeaders.Remove(header.Tag);
                }
            }

            public void RemoveHeader(WikiPageHeader header)
            {
                lock (_thisLock)
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

                    Dictionary<string, HashSet<WikiPageHeader>> dic = null;

                    if (!_wikiPageHeaders.TryGetValue(header.Tag, out dic)) return;

                    HashSet<WikiPageHeader> hashset = null;

                    if (dic.TryGetValue(signature, out hashset))
                    {
                        hashset.Remove(header);
                    }

                    if (hashset.Count == 0) dic.Remove(signature);
                    if (dic.Count == 0) _wikiPageHeaders.Remove(header.Tag);
                }
            }

            public void RemoveHeader(WikiVoteHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || header.Tag == null
                            || header.Tag.Id == null || header.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(header.Tag.Name)
                        || (header.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                        || header.Certificate == null || !header.VerifyCertificate()) return;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, WikiVoteHeader> dic = null;

                    if (!_wikiVoteHeaders.TryGetValue(header.Tag, out dic)) return;

                    WikiVoteHeader tempHeader = null;

                    if (dic.TryGetValue(signature, out tempHeader)
                        && header == tempHeader)
                    {
                        dic.Remove(signature);
                    }

                    if (dic.Count == 0) _wikiVoteHeaders.Remove(header.Tag);
                }
            }

            public void RemoveHeader(ChatTopicHeader header)
            {
                lock (_thisLock)
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

                    if (dic.Count == 0) _chatTopicHeaders.Remove(header.Tag);
                }
            }

            public void RemoveHeader(ChatMessageHeader header)
            {
                lock (_thisLock)
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

                    if (hashset.Count == 0) dic.Remove(signature);
                    if (dic.Count == 0) _chatMessageHeaders.Remove(header.Tag);
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
