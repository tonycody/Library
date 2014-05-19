using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Library.Collections;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Amoeba
{
    public delegate IEnumerable<string> GetSignaturesEventHandler(object sender);

    delegate void UploadedEventHandler(object sender, IEnumerable<Key> keys);

    class ConnectionsManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Random _random = new Random();

        private Kademlia<Node> _routeTable;

        private byte[] _mySessionId;

        private LockedList<ConnectionManager> _connectionManagers;
        private MessagesManager _messagesManager;

        private LockedHashDictionary<Node, List<Key>> _pushBlocksLinkDictionary = new LockedHashDictionary<Node, List<Key>>();
        private LockedHashDictionary<Node, List<Key>> _pushBlocksRequestDictionary = new LockedHashDictionary<Node, List<Key>>();
        private LockedHashDictionary<Node, List<string>> _pushSeedsRequestDictionary = new LockedHashDictionary<Node, List<string>>();

        private LockedHashDictionary<Node, Queue<Key>> _diffusionBlocksDictionary = new LockedHashDictionary<Node, Queue<Key>>();
        private LockedHashDictionary<Node, Queue<Key>> _uploadBlocksDictionary = new LockedHashDictionary<Node, Queue<Key>>();

        private WatchTimer _refreshTimer;

        private LockedList<Node> _creatingNodes;

        private VolatileHashSet<Node> _waitingNodes;
        private VolatileHashSet<Node> _cuttingNodes;
        private VolatileHashSet<Node> _removeNodes;
        private VolatileHashDictionary<Node, int> _nodesStatus;

        private VolatileHashSet<string> _succeededUris;
        private VolatileHashSet<string> _failedUris;

        private VolatileHashSet<string> _pushSeedsRequestList;
        private VolatileHashSet<Key> _downloadBlocks;

        private LockedHashDictionary<string, DateTime> _seedLastAccessTimes = new LockedHashDictionary<string, DateTime>();

        private volatile Thread _connectionsManagerThread;
        private volatile Thread _createConnection1Thread;
        private volatile Thread _createConnection2Thread;
        private volatile Thread _createConnection3Thread;
        private volatile Thread _acceptConnection1Thread;
        private volatile Thread _acceptConnection2Thread;
        private volatile Thread _acceptConnection3Thread;

        private volatile ManagerState _state = ManagerState.Stop;

        private Dictionary<Node, string> _nodeToUri = new Dictionary<Node, string>();

        private BandwidthLimit _bandwidthLimit = new BandwidthLimit();

        private long _receivedByteCount;
        private long _sentByteCount;

        private readonly SafeInteger _pushNodeCount = new SafeInteger();
        private readonly SafeInteger _pushBlockLinkCount = new SafeInteger();
        private readonly SafeInteger _pushBlockRequestCount = new SafeInteger();
        private readonly SafeInteger _pushBlockCount = new SafeInteger();
        private readonly SafeInteger _pushSeedRequestCount = new SafeInteger();
        private readonly SafeInteger _pushSeedCount = new SafeInteger();

        private readonly SafeInteger _pullNodeCount = new SafeInteger();
        private readonly SafeInteger _pullBlockLinkCount = new SafeInteger();
        private readonly SafeInteger _pullBlockRequestCount = new SafeInteger();
        private readonly SafeInteger _pullBlockCount = new SafeInteger();
        private readonly SafeInteger _pullSeedRequestCount = new SafeInteger();
        private readonly SafeInteger _pullSeedCount = new SafeInteger();

        private VolatileHashSet<Key> _relayBlocks;
        private readonly SafeInteger _relayBlockCount = new SafeInteger();

        private readonly SafeInteger _connectConnectionCount = new SafeInteger();
        private readonly SafeInteger _acceptConnectionCount = new SafeInteger();

        private GetSignaturesEventHandler _getLockSignaturesEvent;
        private UploadedEventHandler _uploadedEvent;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxNodeCount = 128;
        private const int _maxBlockLinkCount = 8192;
        private const int _maxBlockRequestCount = 8192;
        private const int _maxSeedRequestCount = 1024;
        private const int _maxSeedCount = 1024;

        private const int _routeTableMinCount = 100;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        public static readonly string Keyword_Link = "_link_";
        public static readonly string Keyword_Store = "_store_";

        //#if DEBUG
        //        private const int _downloadingConnectionCountLowerLimit = 0;
        //        private const int _uploadingConnectionCountLowerLimit = 0;
        //        private const int _diffusionConnectionCountLowerLimit = 3;
        //#else
        private const int _downloadingConnectionCountLowerLimit = 3;
        private const int _uploadingConnectionCountLowerLimit = 3;
        private const int _diffusionConnectionCountLowerLimit = 12;
        //#endif

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

            _waitingNodes = new VolatileHashSet<Node>(new TimeSpan(0, 0, 10));
            _cuttingNodes = new VolatileHashSet<Node>(new TimeSpan(0, 10, 0));
            _removeNodes = new VolatileHashSet<Node>(new TimeSpan(0, 30, 0));
            _nodesStatus = new VolatileHashDictionary<Node, int>(new TimeSpan(0, 30, 0));

            _succeededUris = new VolatileHashSet<string>(new TimeSpan(1, 0, 0));
            _failedUris = new VolatileHashSet<string>(new TimeSpan(0, 10, 0));

            _downloadBlocks = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pushSeedsRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));

            _relayBlocks = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));

            _refreshTimer = new WatchTimer(this.RefreshTimer, new TimeSpan(0, 0, 5));
        }

        private void RefreshTimer()
        {
            _waitingNodes.TrimExcess();
            _cuttingNodes.TrimExcess();
            _removeNodes.TrimExcess();
            _nodesStatus.TrimExcess();

            _succeededUris.TrimExcess();
            _failedUris.TrimExcess();

            _downloadBlocks.TrimExcess();
            _pushSeedsRequestList.TrimExcess();

            _relayBlocks.TrimExcess();
        }

        public GetSignaturesEventHandler GetLockSignaturesEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockSignaturesEvent = value;
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

                    foreach (var connectionManager in _connectionManagers.ToArray())
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        var messageManager = _messagesManager[connectionManager.Node];

                        contexts.Add(new InformationContext("Id", messageManager.Id));
                        contexts.Add(new InformationContext("Node", connectionManager.Node));
                        contexts.Add(new InformationContext("Uri", _nodeToUri[connectionManager.Node]));
                        contexts.Add(new InformationContext("Priority", (long)messageManager.Priority));
                        contexts.Add(new InformationContext("ReceivedByteCount", (long)messageManager.ReceivedByteCount + connectionManager.ReceivedByteCount));
                        contexts.Add(new InformationContext("SentByteCount", (long)messageManager.SentByteCount + connectionManager.SentByteCount));
                        contexts.Add(new InformationContext("Direction", connectionManager.Direction));

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

                    contexts.Add(new InformationContext("PushNodeCount", (long)_pushNodeCount));
                    contexts.Add(new InformationContext("PushBlockLinkCount", (long)_pushBlockLinkCount));
                    contexts.Add(new InformationContext("PushBlockRequestCount", (long)_pushBlockRequestCount));
                    contexts.Add(new InformationContext("PushBlockCount", (long)_pushBlockCount));
                    contexts.Add(new InformationContext("PushSeedRequestCount", (long)_pushSeedRequestCount));
                    contexts.Add(new InformationContext("PushSeedCount", (long)_pushSeedCount));

                    contexts.Add(new InformationContext("PullNodeCount", (long)_pullNodeCount));
                    contexts.Add(new InformationContext("PullBlockLinkCount", (long)_pullBlockLinkCount));
                    contexts.Add(new InformationContext("PullBlockRequestCount", (long)_pullBlockRequestCount));
                    contexts.Add(new InformationContext("PullBlockCount", (long)_pullBlockCount));
                    contexts.Add(new InformationContext("PullSeedRequestCount", (long)_pullSeedRequestCount));
                    contexts.Add(new InformationContext("PullSeedCount", (long)_pullSeedCount));

                    contexts.Add(new InformationContext("CreateConnectionCount", (long)_connectConnectionCount));
                    contexts.Add(new InformationContext("AcceptConnectionCount", (long)_acceptConnectionCount));

                    contexts.Add(new InformationContext("OtherNodeCount", _routeTable.Count));

                    {
                        var nodes = new HashSet<Node>();

                        foreach (var connectionManager in _connectionManagers)
                        {
                            nodes.Add(connectionManager.Node);
                        }

                        contexts.Add(new InformationContext("SurroundingNodeCount", nodes.Count));
                    }

                    contexts.Add(new InformationContext("BlockCount", _cacheManager.Count));
                    contexts.Add(new InformationContext("RelayBlockCount", (long)_relayBlockCount));

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

        protected virtual IEnumerable<string> OnLockSignaturesEvent()
        {
            if (_getLockSignaturesEvent != null)
            {
                return _getLockSignaturesEvent(this);
            }

            return null;
        }

        private static bool Check(Node node)
        {
            return !(node == null
                || node.Id == null || node.Id.Length == 0);
        }

        private static bool Check(Key key)
        {
            return !(key == null
                || key.Hash == null || key.Hash.Length == 0
                || key.HashAlgorithm != HashAlgorithm.Sha512);
        }

        private static bool Check(string signature)
        {
            return !(signature == null || !Signature.IsSignature(signature));
        }

        private void UpdateSessionId()
        {
            lock (this.ThisLock)
            {
                _mySessionId = new byte[64];

                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(_mySessionId);
                }
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
            const int average = 256;

            lock (this.ThisLock)
            {
                var priority = (long)_messagesManager[node].Priority;

                return ((double)(priority + average)) / (average * 2);
            }
        }

        private void AddConnectionManager(ConnectionManager connectionManager, string uri)
        {
            lock (this.ThisLock)
            {
                if (CollectionUtilities.Equals(connectionManager.Node.Id, this.BaseNode.Id)
                    || _connectionManagers.Any(n => CollectionUtilities.Equals(n.Node.Id, connectionManager.Node.Id)))
                {
                    connectionManager.Dispose();
                    return;
                }

                //if (CollectionUtilities.Equals(connectionManager.Node.Id, this.BaseNode.Id))
                //{
                //    connectionManager.Dispose();
                //    return;
                //}

                //var oldConnectionManager = _connectionManagers.FirstOrDefault(n => CollectionUtilities.Equals(n.Node.Id, connectionManager.Node.Id));

                //if (oldConnectionManager != null)
                //{
                //    this.RemoveConnectionManager(oldConnectionManager);
                //}

                {
                    bool flag = false;

                    if (connectionManager.Direction == ConnectDirection.In)
                    {
                        var connectionCount = 0;

                        lock (this.ThisLock)
                        {
                            connectionCount = _connectionManagers.Count(n => n.Direction == ConnectDirection.In);
                        }

                        if (connectionCount > ((this.ConnectionCountLimit / 3) * 2))
                        {
                            flag = true;
                        }
                    }

                    if (_connectionManagers.Count >= this.ConnectionCountLimit)
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
                                    _pushNodeCount.Add(nodes.Count);
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
                connectionManager.PullSeedsRequestEvent += this.connectionManager_SeedsRequestEvent;
                connectionManager.PullSeedsEvent += this.connectionManager_SeedsEvent;
                connectionManager.PullCancelEvent += this.connectionManager_PullCancelEvent;
                connectionManager.CloseEvent += this.connectionManager_CloseEvent;

                _nodeToUri.Add(connectionManager.Node, uri);
                _connectionManagers.Add(connectionManager);

                {
                    var termpMessageManager = _messagesManager[connectionManager.Node];

                    if (termpMessageManager.SessionId != null
                        && !CollectionUtilities.Equals(termpMessageManager.SessionId, connectionManager.SesstionId))
                    {
                        _messagesManager.Remove(connectionManager.Node);
                    }
                }

                var messageManager = _messagesManager[connectionManager.Node];
                messageManager.SessionId = connectionManager.SesstionId;
                messageManager.LastPullTime = DateTime.UtcNow;

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

                            var messageManager = _messagesManager[connectionManager.Node];
                            messageManager.SentByteCount.Add(connectionManager.SentByteCount);
                            messageManager.ReceivedByteCount.Add(connectionManager.ReceivedByteCount);

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

        private void CreateConnectionThread()
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
                        .Where(n => !_connectionManagers.Any(m => CollectionUtilities.Equals(m.Node.Id, n.Id))
                            && !_creatingNodes.Contains(n)
                            && !_waitingNodes.Contains(n))
                        .Randomize()
                        .FirstOrDefault();

                    if (node == null)
                    {
                        node = _routeTable
                            .ToArray()
                            .Where(n => !_connectionManagers.Any(m => CollectionUtilities.Equals(m.Node.Id, n.Id))
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
                    var uris = new HashSet<string>();
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
                        connectionCount = _connectionManagers.Count(n => n.Direction == ConnectDirection.Out);
                    }

                    if (connectionCount > ((this.ConnectionCountLimit / 3) * 1))
                    {
                        continue;
                    }

                    foreach (var uri in uris.Randomize())
                    {
                        if (this.State == ManagerState.Stop) return;

                        if (_failedUris.Contains(uri)) continue;

                        var connection = _clientManager.CreateConnection(uri, _bandwidthLimit);

                        if (connection != null)
                        {
                            var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectDirection.Out, _bufferManager);

                            try
                            {
                                connectionManager.Connect();
                                if (!ConnectionsManager.Check(connectionManager.Node)) continue;

                                _succeededUris.Add(uri);

                                lock (this.ThisLock)
                                {
                                    _cuttingNodes.Remove(node);

                                    if (node != connectionManager.Node)
                                    {
                                        _removeNodes.Add(node);
                                        _routeTable.Remove(node);
                                    }

                                    if (connectionManager.Node.Uris.Count() != 0)
                                    {
                                        _routeTable.Live(connectionManager.Node);
                                    }
                                }

                                _connectConnectionCount.Increment();

                                this.AddConnectionManager(connectionManager, uri);

                                goto End;
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine(e);

                                connectionManager.Dispose();
                            }
                        }
                        else
                        {
                            _failedUris.Add(uri);
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

        private void AcceptConnectionThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                string uri;
                var connection = _serverManager.AcceptConnection(out uri, _bandwidthLimit);

                if (connection != null)
                {
                    var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectDirection.In, _bufferManager);

                    try
                    {
                        connectionManager.Connect();
                        if (!ConnectionsManager.Check(connectionManager.Node) || _removeNodes.Contains(connectionManager.Node)) throw new ArgumentException();

                        lock (this.ThisLock)
                        {
                            if (connectionManager.Node.Uris.Count() != 0)
                            {
                                _routeTable.Add(connectionManager.Node);
                            }

                            _cuttingNodes.Remove(connectionManager.Node);
                        }

                        this.AddConnectionManager(connectionManager, uri);

                        _acceptConnectionCount.Increment();
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);

                        connectionManager.Dispose();
                    }
                }
            }
        }

        private class NodeSortItem
        {
            public Node Node { get; set; }
            public long Priority { get; set; }
            public DateTime LastPullTime { get; set; }
        }

        private class SignatureSortItem
        {
            public string Signature { get; set; }
            public DateTime LastAccessTime { get; set; }
        }

        private volatile bool _refreshThreadRunning;

        private void ConnectionsManagerThread()
        {
            Stopwatch connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();

            Stopwatch refreshStopwatch = new Stopwatch();

            Stopwatch pushBlockDiffusionStopwatch = new Stopwatch();
            pushBlockDiffusionStopwatch.Start();
            Stopwatch pushBlockUploadStopwatch = new Stopwatch();
            pushBlockUploadStopwatch.Start();
            Stopwatch pushBlockDownloadStopwatch = new Stopwatch();
            pushBlockDownloadStopwatch.Start();

            Stopwatch pushSeedUploadStopwatch = new Stopwatch();
            pushSeedUploadStopwatch.Start();
            Stopwatch pushSeedDownloadStopwatch = new Stopwatch();
            pushSeedDownloadStopwatch.Start();

            // 電子署名を検証して破損しているSeedを検索し、削除。
            {
                var removeSignatures = new SortedSet<string>();

                {
                    var signatures = _settings.GetSignatures().ToArray();

                    // Link
                    _random.Shuffle(signatures);
                    foreach (var signature in signatures)
                    {
                        Seed tempSeed = _settings.GetLinkSeed(signature);
                        if (tempSeed == null) continue;

                        if (!tempSeed.VerifyCertificate()) removeSignatures.Add(signature);
                    }

                    // Store
                    _random.Shuffle(signatures);
                    foreach (var signature in signatures)
                    {
                        Seed tempSeed = _settings.GetStoreSeed(signature);
                        if (tempSeed == null) continue;

                        if (!tempSeed.VerifyCertificate()) removeSignatures.Add(signature);
                    }
                }

                _settings.RemoveSignatures(removeSignatures);
            }

            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                var connectionCount = 0;

                lock (this.ThisLock)
                {
                    connectionCount = _connectionManagers.Count;
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
                                Priority = _messagesManager[connectionManager.Node].Priority,
                                LastPullTime = _messagesManager[connectionManager.Node].LastPullTime,
                            });
                        }
                    }

                    nodeSortItems.Sort((x, y) =>
                    {
                        int c = x.Priority.CompareTo(y.Priority);
                        if (c != 0) return c;

                        return x.LastPullTime.CompareTo(y.LastPullTime);
                    });

                    foreach (var node in nodeSortItems.Select(n => n.Node).Take(1))
                    {
                        ConnectionManager connectionManager = null;

                        lock (this.ThisLock)
                        {
                            connectionManager = _connectionManagers.FirstOrDefault(n => n.Node == node);
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
                        }
                    }
                }

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalSeconds >= 30)
                {
                    refreshStopwatch.Restart();

                    // トラストにより必要なSeedを選択し、不要なSeedを削除する。
                    //　非トラストなSeedでアクセスが頻繁なSeedを優先して保護する。
                    ThreadPool.QueueUserWorkItem((object wstate) =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            var lockSignatures = this.OnLockSignaturesEvent();

                            if (lockSignatures != null)
                            {
                                var removeSignatures = new SortedSet<string>();
                                removeSignatures.UnionWith(_settings.GetSignatures());
                                removeSignatures.ExceptWith(lockSignatures);

                                var signatureSortItems = new List<SignatureSortItem>();

                                foreach (var signature in removeSignatures)
                                {
                                    DateTime lastAccessTime;
                                    _seedLastAccessTimes.TryGetValue(signature, out lastAccessTime);

                                    signatureSortItems.Add(new SignatureSortItem()
                                    {
                                        Signature = signature,
                                        LastAccessTime = lastAccessTime,
                                    });
                                }

                                signatureSortItems.Sort((x, y) =>
                                {
                                    return x.LastAccessTime.CompareTo(y.LastAccessTime);
                                });

                                _settings.RemoveSignatures(signatureSortItems.Select(n => n.Signature).Take(signatureSortItems.Count - 8192));

                                var liveSignatures = new SortedSet<string>(_settings.GetSignatures());

                                foreach (var signature in _seedLastAccessTimes.Keys.ToArray())
                                {
                                    if (liveSignatures.Contains(signature)) continue;

                                    _seedLastAccessTimes.Remove(signature);
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

                // 拡散アップロード
                if (connectionCount > _diffusionConnectionCountLowerLimit
                    && pushBlockDiffusionStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushBlockDiffusionStopwatch.Restart();

                    // 拡散アップロードするブロック数を10000以下に抑える。
                    lock (_settings.DiffusionBlocksRequest.ThisLock)
                    {
                        if (_settings.DiffusionBlocksRequest.Count > 10000)
                        {
                            foreach (var key in _settings.DiffusionBlocksRequest.ToArray().Randomize()
                                .Take(_settings.DiffusionBlocksRequest.Count - 10000).ToList())
                            {
                                _settings.DiffusionBlocksRequest.Remove(key);
                            }
                        }
                    }

                    // 存在しないブロックのKeyをRemoveする。
                    {
                        lock (_settings.DiffusionBlocksRequest.ThisLock)
                        {
                            foreach (var key in _cacheManager.ExceptFrom(_settings.DiffusionBlocksRequest.ToArray()).ToArray())
                            {
                                _settings.DiffusionBlocksRequest.Remove(key);
                            }
                        }

                        lock (_settings.UploadBlocksRequest.ThisLock)
                        {
                            foreach (var key in _cacheManager.ExceptFrom(_settings.UploadBlocksRequest.ToArray()).ToArray())
                            {
                                _settings.UploadBlocksRequest.Remove(key);
                            }
                        }
                    }

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    var diffusionBlocksList = new List<Key>();

                    {
                        {
                            var array = _settings.UploadBlocksRequest.ToArray();
                            _random.Shuffle(array);

                            int count = 8192;

                            for (int i = 0; i < count && i < array.Length; i++)
                            {
                                diffusionBlocksList.Add(array[i]);
                            }
                        }

                        {
                            var array = _settings.DiffusionBlocksRequest.ToArray();
                            _random.Shuffle(array);

                            int count = 8192;

                            for (int i = 0; i < count && i < array.Length; i++)
                            {
                                diffusionBlocksList.Add(array[i]);
                            }
                        }
                    }

                    _random.Shuffle(diffusionBlocksList);

                    {
                        var diffusionBlocksDictionary = new Dictionary<Node, SortedSet<Key>>();

                        foreach (var key in diffusionBlocksList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                // 自分より距離が2～3番目に遠いノードにもアップロードを試みる。
                                foreach (var node in Kademlia<Node>.Search(key.Hash, otherNodes, 2))
                                {
                                    if (messageManagers[node].StockBlocks.Contains(key)) continue;
                                    requestNodes.Add(node);
                                }

                                if (requestNodes.Count == 0)
                                {
                                    _settings.UploadBlocksRequest.Remove(key);
                                    _settings.DiffusionBlocksRequest.Remove(key);

                                    this.OnUploadedEvent(new Key[] { key });

                                    continue;
                                }

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                                {
                                    SortedSet<Key> collection;

                                    if (!diffusionBlocksDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new SortedSet<Key>(new KeyComparer());
                                        diffusionBlocksDictionary[requestNodes[i]] = collection;
                                    }

                                    collection.Add(key);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_diffusionBlocksDictionary.ThisLock)
                        {
                            _diffusionBlocksDictionary.Clear();

                            foreach (var pair in diffusionBlocksDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _diffusionBlocksDictionary.Add(node, new Queue<Key>(targets.Randomize()));
                            }
                        }
                    }
                }

                // アップロード
                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushBlockUploadStopwatch.Elapsed.TotalSeconds >= 10)
                {
                    pushBlockUploadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    {
                        var uploadBlocksDictionary = new Dictionary<Node, List<Key>>();

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            uploadBlocksDictionary.Add(node, _cacheManager.IntersectFrom(messageManager.PullBlocksRequest.ToArray().Randomize()).Take(128).ToList());
                        }

                        lock (_uploadBlocksDictionary.ThisLock)
                        {
                            _uploadBlocksDictionary.Clear();

                            foreach (var pair in uploadBlocksDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _uploadBlocksDictionary.Add(node, new Queue<Key>(targets.Randomize()));
                            }
                        }
                    }
                }

                // ダウンロード
                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushBlockDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushBlockDownloadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    var pushBlocksLinkList = new List<Key>();
                    var pushBlocksRequestList = new List<Key>();

                    {
                        {
                            {
                                var array = _cacheManager.ToArray();
                                _random.Shuffle(array);

                                int count = _maxBlockLinkCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushBlocksLink.Contains(array[i])))
                                    {
                                        pushBlocksLinkList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                //var array = _cacheManager.ToArray();
                                //_random.Shuffle(array);

                                //IEnumerable<Key> items = array;

                                //foreach (var tempMessageManager in messageManagers.Values.OrderBy(n => n.Id))
                                //{
                                //    items = tempMessageManager.PushBlocksLink.ExceptFrom(items);
                                //}

                                //int count = _maxBlockLinkCount;

                                //foreach (var item in items.Take(count))
                                //{
                                //    pushBlocksLinkList.Add(item);
                                //}
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullBlocksLink.ToArray();
                                _random.Shuffle(array);

                                int count = (int)(_maxBlockLinkCount * ((double)12 / otherNodes.Count));

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushBlocksLink.Contains(array[i])))
                                    {
                                        pushBlocksLinkList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                //var array = messageManager.PullBlocksLink.ToArray();
                                //_random.Shuffle(array);

                                //IEnumerable<Key> items = array;

                                //foreach (var tempMessageManager in messageManagers.Values.OrderBy(n => n.Id))
                                //{
                                //    items = tempMessageManager.PushBlocksLink.ExceptFrom(items);
                                //}

                                //int count = (int)(_maxBlockLinkCount * ((double)12 / otherNodes.Count));

                                //foreach (var item in items.Take(count))
                                //{
                                //    pushBlocksLinkList.Add(item);
                                //}
                            }
                        }

                        {
                            {
                                var array = _cacheManager.ExceptFrom(_downloadBlocks.ToArray()).ToArray();
                                _random.Shuffle(array);

                                int count = _maxBlockRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushBlocksRequest.Contains(array[i])))
                                    {
                                        pushBlocksRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                //var array = _cacheManager.ExceptFrom(_downloadBlocks.ToArray()).ToArray();
                                //_random.Shuffle(array);

                                //IEnumerable<Key> items = array;

                                //foreach (var tempMessageManager in messageManagers.Values.OrderBy(n => n.Id))
                                //{
                                //    items = tempMessageManager.PushBlocksRequest.ExceptFrom(items);
                                //}

                                //int count = _maxBlockRequestCount;

                                //foreach (var item in items.Take(count))
                                //{
                                //    pushBlocksRequestList.Add(item);
                                //}
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = _cacheManager.ExceptFrom(messageManager.PullBlocksRequest.ToArray()).ToArray();
                                _random.Shuffle(array);

                                int count = (int)(_maxBlockRequestCount * ((double)12 / otherNodes.Count));

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushBlocksRequest.Contains(array[i])))
                                    {
                                        pushBlocksRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                //var array = _cacheManager.ExceptFrom(messageManager.PullBlocksRequest.ToArray()).ToArray();
                                //_random.Shuffle(array);

                                //IEnumerable<Key> items = array;

                                //foreach (var tempMessageManager in messageManagers.Values.OrderBy(n => n.Id))
                                //{
                                //    items = tempMessageManager.PushBlocksRequest.ExceptFrom(items);
                                //}

                                //int count = (int)(_maxBlockRequestCount * ((double)12 / otherNodes.Count));

                                //foreach (var item in items.Take(count))
                                //{
                                //    pushBlocksRequestList.Add(item);
                                //}
                            }
                        }
                    }

                    _random.Shuffle(pushBlocksLinkList);
                    _random.Shuffle(pushBlocksRequestList);

                    {
                        var pushBlocksLinkDictionary = new Dictionary<Node, SortedSet<Key>>();

                        foreach (var key in pushBlocksLinkList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(key.Hash, baseNode.Id, otherNodes, 1))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    SortedSet<Key> collection;

                                    if (!pushBlocksLinkDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new SortedSet<Key>(new KeyComparer());
                                        pushBlocksLinkDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxBlockLinkCount)
                                    {
                                        collection.Add(key);
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

                            foreach (var pair in pushBlocksLinkDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushBlocksLinkDictionary.Add(node, new List<Key>(targets.Randomize()));
                            }
                        }
                    }

                    {
                        var pushBlocksRequestDictionary = new Dictionary<Node, SortedSet<Key>>();

                        foreach (var key in pushBlocksRequestList)
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(key.Hash, baseNode.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                foreach (var pair in messageManagers)
                                {
                                    var node = pair.Key;
                                    var messageManager = pair.Value;

                                    if (messageManager.PullBlocksLink.Contains(key))
                                    {
                                        requestNodes.Add(node);
                                    }
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    SortedSet<Key> collection;

                                    if (!pushBlocksRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new SortedSet<Key>(new KeyComparer());
                                        pushBlocksRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxBlockRequestCount)
                                    {
                                        collection.Add(key);
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

                            foreach (var pair in pushBlocksRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushBlocksRequestDictionary.Add(node, new List<Key>(targets.Randomize()));
                            }
                        }
                    }
                }

                // Seedのアップロード
                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushSeedUploadStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    pushSeedUploadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    foreach (var signature in _settings.GetSignatures())
                    {
                        try
                        {
                            var requestNodes = new List<Node>();

                            foreach (var node in Kademlia<Node>.Search(Signature.GetSignatureHash(signature), baseNode.Id, otherNodes, 2))
                            {
                                requestNodes.Add(node);
                            }

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                messageManagers[requestNodes[i]].PullSeedsRequest.Add(signature);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }
                }

                // Seedのダウンロード
                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushSeedDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushSeedDownloadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    var pushSeedsRequestList = new List<string>();

                    {
                        {
                            {
                                var array = _pushSeedsRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxSeedRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushSeedsRequest.Contains(array[i])))
                                    {
                                        pushSeedsRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                //var array = _pushSeedsRequestList.ToArray();
                                //_random.Shuffle(array);

                                //IEnumerable<string> items = array;

                                //foreach (var tempMessageManager in messageManagers.Values.OrderBy(n => n.Id))
                                //{
                                //    items = tempMessageManager.PushSeedsRequest.ExceptFrom(items);
                                //}

                                //int count = _maxSeedRequestCount;

                                //foreach (var item in items.Take(count))
                                //{
                                //    pushSeedsRequestList.Add(item);
                                //}
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullSeedsRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxSeedRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushSeedsRequest.Contains(array[i])))
                                    {
                                        pushSeedsRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                //var array = messageManager.PullSeedsRequest.ToArray();
                                //_random.Shuffle(array);

                                //IEnumerable<string> items = array;

                                //foreach (var tempMessageManager in messageManagers.Values.OrderBy(n => n.Id))
                                //{
                                //    items = tempMessageManager.PushSeedsRequest.ExceptFrom(items);
                                //}

                                //int count = _maxSeedRequestCount;

                                //foreach (var item in items.Take(count))
                                //{
                                //    pushSeedsRequestList.Add(item);
                                //}
                            }
                        }
                    }

                    _random.Shuffle(pushSeedsRequestList);

                    {
                        var pushSeedsRequestDictionary = new Dictionary<Node, SortedSet<string>>();

                        foreach (var signature in pushSeedsRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(Signature.GetSignatureHash(signature), baseNode.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    SortedSet<string> collection;

                                    if (!pushSeedsRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new SortedSet<string>();
                                        pushSeedsRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxSeedRequestCount)
                                    {
                                        collection.Add(signature);
                                    }
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

                            foreach (var pair in pushSeedsRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushSeedsRequestDictionary.Add(node, new List<string>(targets.Randomize()));
                            }
                        }
                    }
                }
            }
        }

        private void ConnectionManagerThread(object state)
        {
            Thread.CurrentThread.Name = "ConnectionsManager_ConnectionManagerThread";
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

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
                Stopwatch blockDiffusionTime = new Stopwatch();
                blockDiffusionTime.Start();
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
                        connectionCount = _connectionManagers.Count;
                    }

                    // Check
                    if (messageManager.Priority < 0 && checkTime.Elapsed.TotalSeconds >= 5)
                    {
                        checkTime.Restart();

                        if ((DateTime.UtcNow - messageManager.LastPullTime).TotalMinutes >= 10)
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
                    if (!nodeUpdateTime.IsRunning || nodeUpdateTime.Elapsed.TotalMinutes >= 3)
                    {
                        nodeUpdateTime.Restart();

                        var nodes = new HashSet<Node>();

                        lock (this.ThisLock)
                        {
                            foreach (var node in _routeTable.Randomize())
                            {
                                if (nodes.Count >= 64) break;

                                if (node.Uris.Any(n => _succeededUris.Contains(n)))
                                {
                                    nodes.Add(node);
                                }
                            }

                            foreach (var node in _routeTable.Randomize())
                            {
                                if (nodes.Count >= 128) break;

                                nodes.Add(node);
                            }
                        }

                        if (nodes.Count > 0)
                        {
                            connectionManager.PushNodes(nodes.Randomize());

                            Debug.WriteLine(string.Format("ConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodeCount.Add(nodes.Count);
                        }
                    }

                    if (updateTime.Elapsed.TotalSeconds >= 60)
                    {
                        updateTime.Restart();

                        // PushBlocksLink
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            List<Key> targetList = null;

                            lock (_pushBlocksLinkDictionary.ThisLock)
                            {
                                if (_pushBlocksLinkDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushBlocksLinkDictionary.Remove(connectionManager.Node);
                                    messageManager.PushBlocksLink.AddRange(targetList);
                                }
                            }

                            if (targetList != null)
                            {
                                try
                                {
                                    connectionManager.PushBlocksLink(targetList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BlocksLink ({0})", targetList.Count));
                                    _pushBlockLinkCount.Add(targetList.Count);
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in targetList)
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
                            List<Key> targetList = null;

                            lock (_pushBlocksRequestDictionary.ThisLock)
                            {
                                if (_pushBlocksRequestDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushBlocksRequestDictionary.Remove(connectionManager.Node);
                                    messageManager.PushBlocksRequest.AddRange(targetList);
                                }
                            }

                            if (targetList != null)
                            {
                                try
                                {
                                    connectionManager.PushBlocksRequest(targetList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BlocksRequest ({0})", targetList.Count));
                                    _pushBlockRequestCount.Add(targetList.Count);
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in targetList)
                                    {
                                        messageManager.PushBlocksRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }

                        // PushSeedsRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            List<string> targetList = null;

                            lock (_pushSeedsRequestDictionary.ThisLock)
                            {
                                if (_pushSeedsRequestDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushSeedsRequestDictionary.Remove(connectionManager.Node);
                                    messageManager.PushSeedsRequest.AddRange(targetList);
                                }
                            }

                            if (targetList != null)
                            {
                                try
                                {
                                    connectionManager.PushSeedsRequest(targetList);

                                    foreach (var item in targetList)
                                    {
                                        _pushSeedsRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push SeedsRequest ({0})", targetList.Count));
                                    _pushSeedRequestCount.Add(targetList.Count);
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in targetList)
                                    {
                                        messageManager.PushSeedsRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }
                    }

                    if (blockDiffusionTime.Elapsed.TotalSeconds >= 5)
                    {
                        blockDiffusionTime.Restart();

                        // PushBlock
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            Key key = null;

                            lock (_diffusionBlocksDictionary.ThisLock)
                            {
                                Queue<Key> queue;

                                if (_diffusionBlocksDictionary.TryGetValue(connectionManager.Node, out queue))
                                {
                                    if (queue.Count > 0)
                                    {
                                        key = queue.Dequeue();
                                        messageManager.StockBlocks.Add(key);
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

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Block (Diffusion) ({0})", NetworkConverter.ToBase64UrlString(key.Hash)));
                                    _pushBlockCount.Increment();

                                    messageManager.PullBlocksRequest.Remove(key);
                                }
                                catch (ConnectionManagerException e)
                                {
                                    messageManager.StockBlocks.Remove(key);

                                    throw e;
                                }
                                catch (BlockNotFoundException)
                                {
                                    messageManager.StockBlocks.Remove(key);
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

                    if (_random.NextDouble() < this.GetPriority(connectionManager.Node))
                    {
                        // PushBlock
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            Key key = null;

                            lock (_uploadBlocksDictionary.ThisLock)
                            {
                                Queue<Key> queue;

                                if (_uploadBlocksDictionary.TryGetValue(connectionManager.Node, out queue))
                                {
                                    if (queue.Count > 0)
                                    {
                                        key = queue.Dequeue();
                                        messageManager.StockBlocks.Add(key);
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

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Block (Upload) ({0})", NetworkConverter.ToBase64UrlString(key.Hash)));
                                    _pushBlockCount.Increment();

                                    messageManager.PullBlocksRequest.Remove(key);

                                    messageManager.Priority.Decrement();

                                    // Infomation
                                    {
                                        if (_relayBlocks.Contains(key))
                                        {
                                            _relayBlockCount.Increment();
                                        }
                                    }
                                }
                                catch (ConnectionManagerException e)
                                {
                                    messageManager.StockBlocks.Remove(key);

                                    throw e;
                                }
                                catch (BlockNotFoundException)
                                {
                                    messageManager.StockBlocks.Remove(key);
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

                    if (seedUpdateTime.Elapsed.TotalSeconds >= 60)
                    {
                        seedUpdateTime.Restart();

                        // PushSeeds
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            var signatures = messageManager.PullSeedsRequest.ToArray();

                            var linkSeeds = new List<Seed>();

                            // Link
                            _random.Shuffle(signatures);
                            foreach (var signature in signatures)
                            {
                                Seed tempSeed = _settings.GetLinkSeed(signature);
                                if (tempSeed == null) continue;

                                DateTime creationTime;

                                if (!messageManager.StockLinkSeeds.TryGetValue(signature, out creationTime)
                                    || tempSeed.CreationTime > creationTime)
                                {
                                    linkSeeds.Add(tempSeed);

                                    if (linkSeeds.Count >= (_maxSeedCount / 2)) break;
                                }
                            }

                            var storeSeeds = new List<Seed>();

                            // Store
                            _random.Shuffle(signatures);
                            foreach (var signature in signatures)
                            {
                                Seed tempSeed = _settings.GetStoreSeed(signature);
                                if (tempSeed == null) continue;

                                DateTime creationTime;

                                if (!messageManager.StockStoreSeeds.TryGetValue(signature, out creationTime)
                                    || tempSeed.CreationTime > creationTime)
                                {
                                    storeSeeds.Add(tempSeed);

                                    if (storeSeeds.Count >= (_maxSeedCount / 2)) break;
                                }
                            }

                            if (linkSeeds.Count > 0 || storeSeeds.Count > 0)
                            {
                                var seeds = new List<Seed>();
                                seeds.AddRange(linkSeeds);
                                seeds.AddRange(storeSeeds);

                                _random.Shuffle(seeds);

                                connectionManager.PushSeeds(seeds);

                                Debug.WriteLine(string.Format("ConnectionManager: Push Seeds ({0})", seeds.Count));
                                _pushSeedCount.Add(seeds.Count);

                                foreach (var seed in linkSeeds)
                                {
                                    var signature = seed.Certificate.ToString();

                                    messageManager.StockLinkSeeds[signature] = seed.CreationTime;
                                }

                                foreach (var seed in storeSeeds)
                                {
                                    var signature = seed.Certificate.ToString();

                                    messageManager.StockStoreSeeds[signature] = seed.CreationTime;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
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

            Debug.WriteLine(string.Format("ConnectionManager: Pull Nodes ({0})", e.Nodes.Count()));

            foreach (var node in e.Nodes.Take(_maxNodeCount))
            {
                if (!ConnectionsManager.Check(node) || node.Uris.Count() == 0 || _removeNodes.Contains(node)) continue;

                _routeTable.Add(node);
                _pullNodeCount.Increment();
            }
        }

        private void connectionManager_BlocksLinkEvent(object sender, PullBlocksLinkEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullBlocksLink.Count > _maxBlockLinkCount * messageManager.PullBlocksLink.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BlocksLink ({0})", e.Keys.Count()));

            foreach (var key in e.Keys.Take(_maxBlockLinkCount))
            {
                if (!ConnectionsManager.Check(key)) continue;

                messageManager.PullBlocksLink.Add(key);
                _pullBlockLinkCount.Increment();
            }
        }

        private void connectionManager_BlocksRequestEvent(object sender, PullBlocksRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullBlocksRequest.Count > _maxBlockRequestCount * messageManager.PullBlocksRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BlocksRequest ({0})", e.Keys.Count()));

            foreach (var key in e.Keys.Take(_maxBlockRequestCount))
            {
                if (!ConnectionsManager.Check(key)) continue;

                messageManager.PullBlocksRequest.Add(key);
                _pullBlockRequestCount.Increment();
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

                if (messageManager.PushBlocksRequest.Remove(e.Key))
                {
                    Debug.WriteLine(string.Format("ConnectionManager: Pull Block (Upload) ({0})", NetworkConverter.ToBase64UrlString(e.Key.Hash)));

                    messageManager.LastPullTime = DateTime.UtcNow;
                    messageManager.Priority.Increment();

                    // Information
                    {
                        _relayBlocks.Add(e.Key);
                    }
                }
                else
                {
                    Debug.WriteLine(string.Format("ConnectionManager: Pull Block (Diffusion) ({0})", NetworkConverter.ToBase64UrlString(e.Key.Hash)));

                    _settings.DiffusionBlocksRequest.Add(e.Key);
                }

                messageManager.StockBlocks.Add(e.Key);
                _pullBlockCount.Increment();
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

            if (messageManager.PullSeedsRequest.Count > _maxSeedRequestCount * messageManager.PullSeedsRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SeedsRequest ({0})", e.Signatures.Count()));

            foreach (var signature in e.Signatures.Take(_maxSeedRequestCount))
            {
                if (!ConnectionsManager.Check(signature)) continue;

                messageManager.PullSeedsRequest.Add(signature);
                _pullSeedRequestCount.Increment();

                _seedLastAccessTimes[signature] = DateTime.UtcNow;
            }
        }

        private void connectionManager_SeedsEvent(object sender, PullSeedsEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockLinkSeeds.Count > _maxSeedCount * messageManager.StockLinkSeeds.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockStoreSeeds.Count > _maxSeedCount * messageManager.StockStoreSeeds.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Seeds ({0})", e.Seeds.Count()));

            foreach (var seed in e.Seeds.Take(_maxSeedCount))
            {
                if (_settings.SetLinkSeed(seed))
                {
                    var signature = seed.Certificate.ToString();

                    messageManager.StockLinkSeeds[signature] = seed.CreationTime;

                    _seedLastAccessTimes[signature] = DateTime.UtcNow;
                }
                else if (_settings.SetStoreSeed(seed))
                {
                    var signature = seed.Certificate.ToString();

                    messageManager.StockStoreSeeds[signature] = seed.CreationTime;

                    _seedLastAccessTimes[signature] = DateTime.UtcNow;
                }

                _pullSeedCount.Increment();
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
            if (!ConnectionsManager.Check(baseNode)) throw new ArgumentException("baseNode");

            lock (this.ThisLock)
            {
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
                    if (!ConnectionsManager.Check(node) || node.Uris.Count() == 0 || _removeNodes.Contains(node)) continue;

                    _routeTable.Add(node);
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

        public Seed GetLinkSeed(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.GetLinkSeed(signature);
            }
        }

        public Seed GetStoreSeed(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                _settings.SetLinkSeed(seed);
                _settings.SetStoreSeed(seed);
            }
        }

        public override ManagerState State
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _state;
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    this.UpdateSessionId();

                    _serverManager.Start();

                    _createConnection1Thread = new Thread(this.CreateConnectionThread);
                    _createConnection1Thread.Name = "ConnectionsManager_CreateConnection1Thread";
                    _createConnection1Thread.Priority = ThreadPriority.Lowest;
                    _createConnection1Thread.Start();
                    _createConnection2Thread = new Thread(this.CreateConnectionThread);
                    _createConnection2Thread.Name = "ConnectionsManager_CreateConnection2Thread";
                    _createConnection2Thread.Priority = ThreadPriority.Lowest;
                    _createConnection2Thread.Start();
                    _createConnection3Thread = new Thread(this.CreateConnectionThread);
                    _createConnection3Thread.Name = "ConnectionsManager_CreateConnection3Thread";
                    _createConnection3Thread.Priority = ThreadPriority.Lowest;
                    _createConnection3Thread.Start();
                    _acceptConnection1Thread = new Thread(this.AcceptConnectionThread);
                    _acceptConnection1Thread.Name = "ConnectionsManager_AcceptConnection1Thread";
                    _acceptConnection1Thread.Priority = ThreadPriority.Lowest;
                    _acceptConnection1Thread.Start();
                    _acceptConnection2Thread = new Thread(this.AcceptConnectionThread);
                    _acceptConnection2Thread.Name = "ConnectionsManager_AcceptConnection2Thread";
                    _acceptConnection2Thread.Priority = ThreadPriority.Lowest;
                    _acceptConnection2Thread.Start();
                    _acceptConnection3Thread = new Thread(this.AcceptConnectionThread);
                    _acceptConnection3Thread.Name = "ConnectionsManager_AcceptConnection3Thread";
                    _acceptConnection3Thread.Priority = ThreadPriority.Lowest;
                    _acceptConnection3Thread.Start();
                    _connectionsManagerThread = new Thread(this.ConnectionsManagerThread);
                    _connectionsManagerThread.Name = "ConnectionsManager_ConnectionsManagerThread";
                    _connectionsManagerThread.Priority = ThreadPriority.Lowest;
                    _connectionsManagerThread.Start();
                }
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;

                    _serverManager.Stop();
                }

                _createConnection1Thread.Join();
                _createConnection1Thread = null;
                _createConnection2Thread.Join();
                _createConnection2Thread = null;
                _createConnection3Thread.Join();
                _createConnection3Thread = null;
                _acceptConnection1Thread.Join();
                _acceptConnection1Thread = null;
                _acceptConnection2Thread.Join();
                _acceptConnection2Thread = null;
                _acceptConnection3Thread.Join();
                _acceptConnection3Thread = null;
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
                    if (!ConnectionsManager.Check(node) || node.Uris.Count() == 0) continue;

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
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<Node>() { Name = "BaseNode", Value = new Node(new byte[0], null)},
                    new Library.Configuration.SettingContent<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingContent<int>() { Name = "ConnectionCountLimit", Value = 32 },
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

            public IEnumerable<string> GetSignatures()
            {
                lock (_thisLock)
                {
                    var signatures = new HashSet<string>();
                    signatures.UnionWith(this.LinkSeeds.Keys);
                    signatures.UnionWith(this.StoreSeeds.Keys);

                    return signatures;
                }
            }

            public void RemoveSignatures(IEnumerable<string> signatures)
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
                var now = DateTime.UtcNow;

                if (seed == null
                    || seed.Name != null
                    || seed.Comment != null
                    || seed.Keywords.Count != 1 || seed.Keywords[0] != ConnectionsManager.Keyword_Link
                    || (seed.CreationTime - now).Minutes > 30) return false;

                if (seed.Certificate == null) throw new CertificateException();

                var signature = seed.Certificate.ToString();

                // なるべく電子署名の検証をさけ、CPU使用率を下げるよう工夫する。
                lock (_thisLock)
                {
                    Seed tempSeed;

                    if (!this.LinkSeeds.TryGetValue(signature, out tempSeed)
                        || seed.CreationTime > tempSeed.CreationTime)
                    {
                        if (!seed.VerifyCertificate()) throw new CertificateException();

                        this.LinkSeeds[signature] = seed;
                    }

                    return (tempSeed == null || seed.CreationTime >= tempSeed.CreationTime);
                }
            }

            public bool SetStoreSeed(Seed seed)
            {
                var now = DateTime.UtcNow;

                if (seed == null
                    || seed.Name != null
                    || seed.Comment != null
                    || seed.Keywords.Count != 1 || seed.Keywords[0] != ConnectionsManager.Keyword_Store
                    || (seed.CreationTime - now).Minutes > 30) return false;

                if (seed.Certificate == null) throw new CertificateException();

                var signature = seed.Certificate.ToString();

                // なるべく電子署名の検証をさけ、CPU使用率を下げるよう工夫する。
                lock (_thisLock)
                {
                    Seed tempSeed;

                    if (!this.StoreSeeds.TryGetValue(signature, out tempSeed)
                        || seed.CreationTime > tempSeed.CreationTime)
                    {
                        if (!seed.VerifyCertificate()) throw new CertificateException();

                        this.StoreSeeds[signature] = seed;
                    }

                    return (tempSeed == null || seed.CreationTime >= tempSeed.CreationTime);
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
                    return (Dictionary<string, Seed>)this["LinkSeeds"];
                }
            }

            private Dictionary<string, Seed> StoreSeeds
            {
                get
                {
                    return (Dictionary<string, Seed>)this["StoreSeeds"];
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_refreshTimer != null)
                {
                    try
                    {
                        _refreshTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _refreshTimer = null;
                }

                if (_messagesManager != null)
                {
                    try
                    {
                        _messagesManager.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _messagesManager = null;
                }

                if (_bandwidthLimit != null)
                {
                    try
                    {
                        _bandwidthLimit.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _bandwidthLimit = null;
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

    [Serializable]
    class ConnectionsManagerException : ManagerException
    {
        public ConnectionsManagerException() : base() { }
        public ConnectionsManagerException(string message) : base(message) { }
        public ConnectionsManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class CertificateException : ConnectionsManagerException
    {
        public CertificateException() : base() { }
        public CertificateException(string message) : base(message) { }
        public CertificateException(string message, Exception innerException) : base(message, innerException) { }
    }
}
