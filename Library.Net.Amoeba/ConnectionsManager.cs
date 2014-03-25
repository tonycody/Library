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

        private Kademlia<Node> _routeTable;
        private static Random _random = new Random();

        private byte[] _mySessionId;

        private LockedList<ConnectionManager> _connectionManagers;
        private MessagesManager _messagesManager;

        private LockedHashDictionary<Node, LockedSortedKeySet> _pushBlocksLinkDictionary = new LockedHashDictionary<Node, LockedSortedKeySet>();
        private LockedHashDictionary<Node, LockedSortedKeySet> _pushBlocksRequestDictionary = new LockedHashDictionary<Node, LockedSortedKeySet>();
        private LockedHashDictionary<Node, LockedSortedStringSet> _pushSeedsRequestDictionary = new LockedHashDictionary<Node, LockedSortedStringSet>();

        private LockedHashDictionary<Node, LockedSortedKeySet> _diffusionBlocksDictionary = new LockedHashDictionary<Node, LockedSortedKeySet>();
        private LockedHashDictionary<Node, LockedSortedKeySet> _uploadBlocksDictionary = new LockedHashDictionary<Node, LockedSortedKeySet>();

        private LockedList<Node> _creatingNodes;
        private VolatileHashSet<Node> _waitingNodes;
        private VolatileHashSet<Node> _cuttingNodes;
        private VolatileHashSet<Node> _removeNodes;
        private VolatileHashDictionary<Node, int> _nodesStatus;

        private VolatileHashSet<string> _pushSeedsRequestList;
        private VolatileHashSet<Key> _downloadBlocks;

        private LockedHashDictionary<string, DateTime> _lastUsedSeedTimes = new LockedHashDictionary<string, DateTime>();

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
        private volatile int _pushSeedRequestCount;
        private volatile int _pushSeedCount;

        private volatile int _pullNodeCount;
        private volatile int _pullBlockLinkCount;
        private volatile int _pullBlockRequestCount;
        private volatile int _pullBlockCount;
        private volatile int _pullSeedRequestCount;
        private volatile int _pullSeedCount;

        private VolatileHashSet<Key> _relayBlocks;
        private volatile int _relayBlockCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        private GetSignaturesEventHandler _getLockSignaturesEvent;
        private UploadedEventHandler _uploadedEvent;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

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

            _downloadBlocks = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pushSeedsRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));

            _relayBlocks = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
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

                    foreach (var item in _connectionManagers.ToArray())
                    {
                        List<InformationContext> contexts = new List<InformationContext>();

                        var messageManager = _messagesManager[item.Node];

                        contexts.Add(new InformationContext("Id", messageManager.Id));
                        contexts.Add(new InformationContext("Node", item.Node));
                        contexts.Add(new InformationContext("Uri", _nodeToUri[item.Node]));
                        contexts.Add(new InformationContext("Priority", messageManager.Priority));
                        contexts.Add(new InformationContext("ReceivedByteCount", item.ReceivedByteCount + messageManager.ReceivedByteCount));
                        contexts.Add(new InformationContext("SentByteCount", item.SentByteCount + messageManager.SentByteCount));

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
            return !(node == null || node.Id == null || node.Id.Length == 0);
        }

        private static bool Check(Key key)
        {
            return !(key == null || key.Hash == null || key.Hash.Length == 0 || key.HashAlgorithm != HashAlgorithm.Sha512);
        }

        private static bool Check(string signature)
        {
            return !(signature == null || !Signature.HasSignature(signature));
        }

        private void UpdateSessionId()
        {
            lock (this.ThisLock)
            {
                _mySessionId = new byte[64];
                RandomNumberGenerator.Create().GetBytes(_mySessionId);
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
                connectionManager.PullSeedsRequestEvent += this.connectionManager_SeedsRequestEvent;
                connectionManager.PullSeedsEvent += this.connectionManager_SeedsEvent;
                connectionManager.PullCancelEvent += this.connectionManager_PullCancelEvent;
                connectionManager.CloseEvent += this.connectionManager_CloseEvent;

                _nodeToUri.Add(connectionManager.Node, uri);
                _connectionManagers.Add(connectionManager);

                {
                    var termpMessageManager = _messagesManager[connectionManager.Node];

                    if (termpMessageManager.SessionId != null
                        && !Collection.Equals(termpMessageManager.SessionId, connectionManager.SesstionId))
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
                            messageManager.SentByteCount += connectionManager.SentByteCount;
                            messageManager.ReceivedByteCount += connectionManager.ReceivedByteCount;

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
                                if (!ConnectionsManager.Check(connectionManager.Node)) continue;

                                lock (this.ThisLock)
                                {
                                    _cuttingNodes.Remove(node);

                                    if (node != connectionManager.Node)
                                    {
                                        _removeNodes.Add(node);
                                        _routeTable.Remove(node);
                                    }

                                    if (connectionManager.Node.Uris.Count() != 0)
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
                        if (!ConnectionsManager.Check(connectionManager.Node) || _removeNodes.Contains(connectionManager.Node)) throw new ArgumentException();

                        lock (this.ThisLock)
                        {
                            if (connectionManager.Node.Uris.Count() != 0)
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
        }

        private volatile bool _refreshThreadRunning;

        private void ConnectionsManagerThread()
        {
            Stopwatch connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();

            Stopwatch checkSeedsStopwatch = new Stopwatch();
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
                    && connectionCheckStopwatch.Elapsed.TotalMinutes >= 10)
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

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalSeconds >= 30)
                {
                    refreshStopwatch.Restart();

                    lock (_settings.DiffusionBlocksRequest.ThisLock)
                    {
                        if (_settings.DiffusionBlocksRequest.Count > 10000)
                        {
                            var tempList = _settings.DiffusionBlocksRequest.Randomize().Take(10000).ToList();
                            _settings.DiffusionBlocksRequest.Clear();
                            _settings.DiffusionBlocksRequest.UnionWith(tempList);
                        }
                    }

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
                                var removeSignatures = new HashSet<string>();
                                removeSignatures.UnionWith(_settings.GetSignatures());
                                removeSignatures.ExceptWith(lockSignatures);

                                var sortList = removeSignatures.ToList();

                                sortList.Sort((x, y) =>
                                {
                                    DateTime tx;
                                    DateTime ty;

                                    _lastUsedSeedTimes.TryGetValue(x, out tx);
                                    _lastUsedSeedTimes.TryGetValue(y, out ty);

                                    return tx.CompareTo(ty);
                                });

                                _settings.RemoveSignatures(sortList.Take(sortList.Count - 8192));

                                var liveSignatures = new HashSet<string>(_settings.GetSignatures());

                                foreach (var signature in _lastUsedSeedTimes.Keys.ToArray())
                                {
                                    if (liveSignatures.Contains(signature)) continue;

                                    _lastUsedSeedTimes.Remove(signature);
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

                    var baseNode = this.BaseNode;
                    List<Node> otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    Dictionary<Node, MessageManager> messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    HashSet<Key> diffusionBlocksList = new HashSet<Key>();

                    {
                        {
                            var list = _settings.UploadBlocksRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            int count = 1024;

                            for (int i = 0; i < count && i < list.Count; i++)
                            {
                                diffusionBlocksList.Add(list[i]);
                            }
                        }

                        {
                            var list = _settings.DiffusionBlocksRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            int count = 1024;

                            for (int i = 0; i < count && i < list.Count; i++)
                            {
                                diffusionBlocksList.Add(list[i]);
                            }
                        }
                    }

                    {
                        Dictionary<Node, LockedSortedKeySet> diffusionBlocksDictionary = new Dictionary<Node, LockedSortedKeySet>();

                        foreach (var key in diffusionBlocksList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Sort(baseNode.Id, key.Hash, otherNodes).Take(1).ToList())
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
                                    LockedSortedKeySet collection;

                                    if (!diffusionBlocksDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new LockedSortedKeySet();
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

                            foreach (var item in diffusionBlocksDictionary)
                            {
                                _diffusionBlocksDictionary.Add(item.Key, item.Value);
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
                    List<Node> otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    Dictionary<Node, MessageManager> messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    {
                        Dictionary<Node, LockedSortedKeySet> uploadBlocksDictionary = new Dictionary<Node, LockedSortedKeySet>();

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            uploadBlocksDictionary.Add(node, new LockedSortedKeySet(_cacheManager.IntersectFrom(messageManager.PullBlocksRequest).Take(128)));
                        }

                        lock (_uploadBlocksDictionary.ThisLock)
                        {
                            _uploadBlocksDictionary.Clear();

                            foreach (var item in uploadBlocksDictionary)
                            {
                                _uploadBlocksDictionary.Add(item.Key, item.Value);
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
                    List<Node> otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    Dictionary<Node, MessageManager> messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    LockedSortedKeySet pullBlocksLinkList = new LockedSortedKeySet();
                    LockedSortedKeySet pullBlocksRequestList = new LockedSortedKeySet();

                    {
                        {
                            var list = _cacheManager
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < _maxBlockLinkCount && i < list.Count; i++)
                            {
                                if (!messageManagers.Values.Any(n => n.PushBlocksLink.Contains(list[i])))
                                {
                                    pullBlocksLinkList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            var list = messageManager.PullBlocksLink
                                .ToArray()
                                .Randomize()
                                .ToList();

                            int count = (int)(_maxBlockLinkCount * ((double)12 / otherNodes.Count));

                            for (int i = 0, j = 0; j < count && i < list.Count; i++)
                            {
                                if (!messageManagers.Values.Any(n => n.PushBlocksLink.Contains(list[i])))
                                {
                                    pullBlocksLinkList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        {
                            var list = _cacheManager.ExceptFrom(_downloadBlocks
                                .ToArray()
                                .Randomize())
                                .ToList();

                            for (int i = 0, j = 0; j < _maxBlockRequestCount && i < list.Count; i++)
                            {
                                if (!messageManagers.Values.Any(n => n.PushBlocksRequest.Contains(list[i])))
                                {
                                    pullBlocksRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            var list = _cacheManager.ExceptFrom(messageManager.PullBlocksRequest
                                .ToArray()
                                .Randomize())
                                .ToList();

                            int count = (int)(_maxBlockRequestCount * ((double)12 / otherNodes.Count));

                            for (int i = 0, j = 0; j < count && i < list.Count; i++)
                            {
                                if (!messageManagers.Values.Any(n => n.PushBlocksRequest.Contains(list[i])))
                                {
                                    pullBlocksRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }
                    }

                    {
                        Dictionary<Node, LockedSortedKeySet> pushBlocksLinkDictionary = new Dictionary<Node, LockedSortedKeySet>();

                        foreach (var key in pullBlocksLinkList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Sort(baseNode.Id, key.Hash, otherNodes).Take(1).ToList())
                                {
                                    if (messageManagers[node].PullBlocksLink.Contains(key)) continue;
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                                {
                                    LockedSortedKeySet collection;

                                    if (!pushBlocksLinkDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new LockedSortedKeySet();
                                        pushBlocksLinkDictionary[requestNodes[i]] = collection;
                                    }

                                    collection.Add(key);
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
                        Dictionary<Node, LockedSortedKeySet> pushBlocksRequestDictionary = new Dictionary<Node, LockedSortedKeySet>();

                        foreach (var key in pullBlocksRequestList)
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();

                                foreach (var node in otherNodes)
                                {
                                    if (messageManagers[node].PullBlocksRequest.Contains(key)) continue;

                                    if (messageManagers[node].PullBlocksLink.Contains(key))
                                    {
                                        requestNodes.Add(node);
                                    }
                                }

                                if (requestNodes.Count != 0)
                                {
                                    requestNodes = requestNodes.Randomize().ToList();
                                }
                                else
                                {
                                    foreach (var node in Kademlia<Node>.Sort(baseNode.Id, key.Hash, otherNodes).Take(1).ToList())
                                    {
                                        if (messageManagers[node].PullBlocksRequest.Contains(key)) continue;
                                        requestNodes.Add(node);
                                    }
                                }

                                for (int i = 0; i < 1 && i < requestNodes.Count; i++)
                                {
                                    LockedSortedKeySet collection;

                                    if (!pushBlocksRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new LockedSortedKeySet();
                                        pushBlocksRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    collection.Add(key);
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

                // Seedのアップロード
                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushSeedUploadStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    pushSeedUploadStopwatch.Restart();

                    var baseNode = this.BaseNode;
                    List<Node> otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    Dictionary<Node, MessageManager> messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    foreach (var signature in _settings.GetSignatures())
                    {
                        try
                        {
                            var requestNodes = new List<Node>();

                            foreach (var node in Kademlia<Node>.Sort(baseNode.Id, Signature.GetSignatureHash(signature), otherNodes).Take(2).ToList())
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
                    List<Node> otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    Dictionary<Node, MessageManager> messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    LockedSortedStringSet pushSeedsRequestList = new LockedSortedStringSet();

                    {
                        {
                            var list = _pushSeedsRequestList
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < _maxSeedRequestCount && i < list.Count; i++)
                            {
                                if (!messageManagers.Values.Any(n => n.PushSeedsRequest.Contains(list[i])))
                                {
                                    pushSeedsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            var list = messageManager.PullSeedsRequest
                                .ToArray()
                                .Randomize()
                                .ToList();

                            for (int i = 0, j = 0; j < _maxSeedRequestCount && i < list.Count; i++)
                            {
                                if (!messageManagers.Values.Any(n => n.PushSeedsRequest.Contains(list[i])))
                                {
                                    pushSeedsRequestList.Add(list[i]);
                                    j++;
                                }
                            }
                        }
                    }

                    {
                        Dictionary<Node, LockedSortedStringSet> pushSeedsRequestDictionary = new Dictionary<Node, LockedSortedStringSet>();

                        foreach (var signature in pushSeedsRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Sort(baseNode.Id, Signature.GetSignatureHash(signature), otherNodes).Take(2).ToList())
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    LockedSortedStringSet collection;

                                    if (!pushSeedsRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new LockedSortedStringSet();
                                        pushSeedsRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    collection.Add(signature);
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
                Stopwatch blockDiffusionTime = new Stopwatch();
                blockDiffusionTime.Start();
                Stopwatch seedUpdateTime = new Stopwatch();
                seedUpdateTime.Start();

                for (; ; )
                {
                    Thread.Sleep(300);
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

                        List<Node> nodes = new List<Node>();

                        lock (this.ThisLock)
                        {
                            nodes.AddRange(_routeTable.Randomize().ToArray());
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
                                LockedSortedKeySet collection;

                                if (_pushBlocksLinkDictionary.TryGetValue(connectionManager.Node, out collection))
                                {
                                    tempList.AddRange(collection.Randomize().Take(_maxBlockLinkCount));

                                    collection.ExceptWith(tempList);
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
                                LockedSortedKeySet collection;

                                if (_pushBlocksRequestDictionary.TryGetValue(connectionManager.Node, out collection))
                                {
                                    tempList.AddRange(collection.Randomize().Take(_maxBlockRequestCount));

                                    collection.ExceptWith(tempList);
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
                            SignatureCollection tempList = new SignatureCollection();

                            lock (_pushSeedsRequestDictionary.ThisLock)
                            {
                                LockedSortedStringSet collection;

                                if (_pushSeedsRequestDictionary.TryGetValue(connectionManager.Node, out collection))
                                {
                                    tempList.AddRange(collection.Randomize().Take(_maxSeedRequestCount));

                                    collection.ExceptWith(tempList);
                                    messageManager.PushSeedsRequest.AddRange(tempList);
                                }
                            }

                            if (tempList.Count > 0)
                            {
                                try
                                {
                                    connectionManager.PushSeedsRequest(tempList);

                                    foreach (var item in tempList)
                                    {
                                        _pushSeedsRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push SeedsRequest ({0})", tempList.Count));
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

                    if (blockDiffusionTime.Elapsed.TotalSeconds >= 5)
                    {
                        blockDiffusionTime.Restart();

                        // PushBlock
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            Key key = null;

                            lock (_diffusionBlocksDictionary.ThisLock)
                            {
                                LockedSortedKeySet collection;

                                if (_diffusionBlocksDictionary.TryGetValue(connectionManager.Node, out collection))
                                {
                                    key = collection.Randomize().FirstOrDefault();

                                    if (key != null)
                                    {
                                        collection.Remove(key);
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
                                    _pushBlockCount++;

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
                                LockedSortedKeySet collection;

                                if (_uploadBlocksDictionary.TryGetValue(connectionManager.Node, out collection))
                                {
                                    key = collection.Randomize().FirstOrDefault();

                                    if (key != null)
                                    {
                                        collection.Remove(key);
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
                                    _pushBlockCount++;

                                    messageManager.PullBlocksRequest.Remove(key);

                                    messageManager.Priority--;

                                    // Infomation
                                    {
                                        if (_relayBlocks.Contains(key))
                                        {
                                            _relayBlockCount++;
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

                        // PushSeed
                        if (_connectionManagers.Count >= _uploadingConnectionCountLowerLimit)
                        {
                            var signatures = new List<string>(messageManager.PullSeedsRequest
                                .ToArray()
                                .Randomize());

                            var linkSeeds = new List<Seed>();

                            // Link
                            foreach (var signature in signatures.Randomize())
                            {
                                Seed tempSeed = this.GetLinkSeed(signature);
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
                            foreach (var signature in signatures.Randomize())
                            {
                                Seed tempSeed = this.GetStoreSeed(signature);
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

                                connectionManager.PushSeeds(seeds.Randomize());

                                Debug.WriteLine(string.Format("ConnectionManager: Push Seeds ({0})", seeds.Count));
                                _pushSeedCount += seeds.Count;

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

            Debug.WriteLine(string.Format("ConnectionManager: Pull Nodes ({0})", e.Nodes.Count()));

            _routeTable.Live(connectionManager.Node);

            foreach (var node in e.Nodes.Take(_maxNodeCount))
            {
                if (!ConnectionsManager.Check(node) || node.Uris.Count() == 0 || _removeNodes.Contains(node)) continue;

                _routeTable.Add(node);
                _pullNodeCount++;
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
                _pullBlockLinkCount++;
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

                if (messageManager.PushBlocksRequest.Remove(e.Key))
                {
                    Debug.WriteLine(string.Format("ConnectionManager: Pull Block (Upload) ({0})", NetworkConverter.ToBase64UrlString(e.Key.Hash)));

                    messageManager.LastPullTime = DateTime.UtcNow;
                    messageManager.Priority++;

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
                _pullBlockCount++;
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
                _pullSeedRequestCount++;

                _lastUsedSeedTimes[signature] = DateTime.UtcNow;
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

                    _lastUsedSeedTimes[signature] = DateTime.UtcNow;
                }
                else if (_settings.SetStoreSeed(seed))
                {
                    var signature = seed.Certificate.ToString();

                    messageManager.StockStoreSeeds[signature] = seed.CreationTime;

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
            if (baseNode == null) throw new ArgumentNullException("baseNode");
            if (baseNode.Id == null) throw new ArgumentNullException("baseNode.Id");
            if (baseNode.Id.Length == 0) throw new ArgumentException("baseNode.Id.Length");

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
                _settings.SetLinkSeed(seed);
                _settings.SetStoreSeed(seed);
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

                    _createClientConnection1Thread = new Thread(this.CreateClientConnectionThread);
                    _createClientConnection1Thread.Name = "ConnectionsManager_CreateClientConnection1Thread";
                    _createClientConnection1Thread.Priority = ThreadPriority.Lowest;
                    _createClientConnection1Thread.Start();
                    _createClientConnection2Thread = new Thread(this.CreateClientConnectionThread);
                    _createClientConnection2Thread.Name = "ConnectionsManager_CreateClientConnection2Thread";
                    _createClientConnection2Thread.Priority = ThreadPriority.Lowest;
                    _createClientConnection2Thread.Start();
                    _createClientConnection3Thread = new Thread(this.CreateClientConnectionThread);
                    _createClientConnection3Thread.Name = "ConnectionsManager_CreateClientConnection3Thread";
                    _createClientConnection3Thread.Priority = ThreadPriority.Lowest;
                    _createClientConnection3Thread.Start();
                    _createServerConnection1Thread = new Thread(this.CreateServerConnectionThread);
                    _createServerConnection1Thread.Name = "ConnectionsManager_CreateServerConnection1Thread";
                    _createServerConnection1Thread.Priority = ThreadPriority.Lowest;
                    _createServerConnection1Thread.Start();
                    _createServerConnection2Thread = new Thread(this.CreateServerConnectionThread);
                    _createServerConnection2Thread.Name = "ConnectionsManager_CreateServerConnection2Thread";
                    _createServerConnection2Thread.Priority = ThreadPriority.Lowest;
                    _createServerConnection2Thread.Start();
                    _createServerConnection3Thread = new Thread(this.CreateServerConnectionThread);
                    _createServerConnection3Thread.Name = "ConnectionsManager_CreateServerConnection3Thread";
                    _createServerConnection3Thread.Priority = ThreadPriority.Lowest;
                    _createServerConnection3Thread.Start();
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
                    new Library.Configuration.SettingContent<int>() { Name = "ConnectionCountLimit", Value = 25 },
                    new Library.Configuration.SettingContent<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingContent<LockedSortedKeySet>() { Name = "DiffusionBlocksRequest", Value = new LockedSortedKeySet() },
                    new Library.Configuration.SettingContent<LockedSortedKeySet>() { Name = "UploadBlocksRequest", Value = new LockedSortedKeySet() },
                    new Library.Configuration.SettingContent<LockedSortedStringDictionary<Seed>>() { Name = "LinkSeeds", Value = new LockedSortedStringDictionary<Seed>() },
                    new Library.Configuration.SettingContent<LockedSortedStringDictionary<Seed>>() { Name = "StoreSeeds", Value = new LockedSortedStringDictionary<Seed>() },
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
                    HashSet<string> signatures = new HashSet<string>();
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

            public LockedSortedKeySet DiffusionBlocksRequest
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedSortedKeySet)this["DiffusionBlocksRequest"];
                    }
                }
            }

            public LockedSortedKeySet UploadBlocksRequest
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedSortedKeySet)this["UploadBlocksRequest"];
                    }
                }
            }

            private LockedSortedStringDictionary<Seed> LinkSeeds
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedSortedStringDictionary<Seed>)this["LinkSeeds"];
                    }
                }
            }

            private LockedSortedStringDictionary<Seed> StoreSeeds
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedSortedStringDictionary<Seed>)this["StoreSeeds"];
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
