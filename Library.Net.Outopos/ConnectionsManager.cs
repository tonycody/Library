using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Library;
using Library.Collections;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Outopos
{
    public delegate IEnumerable<string> GetSignaturesEventHandler(object sender);
    public delegate IEnumerable<Wiki> GetWikisEventHandler(object sender);
    public delegate IEnumerable<Chat> GetChatsEventHandler(object sender);

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
        private LockedHashDictionary<Node, List<string>> _pushBroadcastSignaturesRequestDictionary = new LockedHashDictionary<Node, List<string>>();
        private LockedHashDictionary<Node, List<string>> _pushUnicastSignaturesRequestDictionary = new LockedHashDictionary<Node, List<string>>();
        private LockedHashDictionary<Node, List<Wiki>> _pushMulticastWikisRequestDictionary = new LockedHashDictionary<Node, List<Wiki>>();
        private LockedHashDictionary<Node, List<Chat>> _pushMulticastChatsRequestDictionary = new LockedHashDictionary<Node, List<Chat>>();

        private LockedHashDictionary<Node, Queue<Key>> _diffusionBlocksDictionary = new LockedHashDictionary<Node, Queue<Key>>();
        private LockedHashDictionary<Node, Queue<Key>> _uploadBlocksDictionary = new LockedHashDictionary<Node, Queue<Key>>();

        private WatchTimer _refreshTimer;

        private LockedList<Node> _creatingNodes;

        private VolatileHashSet<Node> _waitingNodes;
        private VolatileHashSet<Node> _cuttingNodes;
        private VolatileHashSet<Node> _removeNodes;

        private VolatileHashSet<string> _succeededUris;

        private VolatileHashSet<Key> _downloadBlocks;
        private VolatileHashSet<string> _pushBroadcastSignaturesRequestList;
        private VolatileHashSet<string> _pushUnicastSignaturesRequestList;
        private VolatileHashSet<Wiki> _pushMulticastWikisRequestList;
        private VolatileHashSet<Chat> _pushMulticastChatsRequestList;

        private LockedHashDictionary<string, DateTime> _broadcastSignatureLastAccessTimes = new LockedHashDictionary<string, DateTime>();
        private LockedHashDictionary<string, DateTime> _unicastSignatureLastAccessTimes = new LockedHashDictionary<string, DateTime>();
        private LockedHashDictionary<Wiki, DateTime> _multicastWikiLastAccessTimes = new LockedHashDictionary<Wiki, DateTime>();
        private LockedHashDictionary<Chat, DateTime> _multicastChatLastAccessTimes = new LockedHashDictionary<Chat, DateTime>();

        private Thread _connectionsManagerThread;
        private Thread _createConnection1Thread;
        private Thread _createConnection2Thread;
        private Thread _createConnection3Thread;
        private Thread _acceptConnection1Thread;
        private Thread _acceptConnection2Thread;
        private Thread _acceptConnection3Thread;

        private volatile ManagerState _state = ManagerState.Stop;

        private Dictionary<Node, string> _nodeToUri = new Dictionary<Node, string>();

        private BandwidthLimit _bandwidthLimit = new BandwidthLimit();

        private long _receivedByteCount;
        private long _sentByteCount;

        private readonly SafeInteger _pushNodeCount = new SafeInteger();
        private readonly SafeInteger _pushBlockLinkCount = new SafeInteger();
        private readonly SafeInteger _pushBlockRequestCount = new SafeInteger();
        private readonly SafeInteger _pushBlockCount = new SafeInteger();
        private readonly SafeInteger _pushHeaderRequestCount = new SafeInteger();
        private readonly SafeInteger _pushHeaderCount = new SafeInteger();

        private readonly SafeInteger _pullNodeCount = new SafeInteger();
        private readonly SafeInteger _pullBlockLinkCount = new SafeInteger();
        private readonly SafeInteger _pullBlockRequestCount = new SafeInteger();
        private readonly SafeInteger _pullBlockCount = new SafeInteger();
        private readonly SafeInteger _pullHeaderRequestCount = new SafeInteger();
        private readonly SafeInteger _pullHeaderCount = new SafeInteger();

        private VolatileHashSet<Key> _relayBlocks;
        private readonly SafeInteger _relayBlockCount = new SafeInteger();

        private readonly SafeInteger _connectConnectionCount = new SafeInteger();
        private readonly SafeInteger _acceptConnectionCount = new SafeInteger();

        private GetSignaturesEventHandler _getLockSignaturesEvent;
        private GetWikisEventHandler _getLockWikisEvent;
        private GetChatsEventHandler _getLockChatsEvent;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxNodeCount = 128;
        private const int _maxBlockLinkCount = 8192;
        private const int _maxBlockRequestCount = 8192;
        private const int _maxHeaderRequestCount = 1024;
        private const int _maxHeaderCount = 1024;

        private const int _routeTableMinCount = 100;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

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

            _waitingNodes = new VolatileHashSet<Node>(new TimeSpan(0, 0, 30));
            _cuttingNodes = new VolatileHashSet<Node>(new TimeSpan(0, 10, 0));
            _removeNodes = new VolatileHashSet<Node>(new TimeSpan(0, 30, 0));

            _succeededUris = new VolatileHashSet<string>(new TimeSpan(1, 0, 0));

            _downloadBlocks = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pushBroadcastSignaturesRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));
            _pushUnicastSignaturesRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));
            _pushMulticastWikisRequestList = new VolatileHashSet<Wiki>(new TimeSpan(0, 3, 0));
            _pushMulticastChatsRequestList = new VolatileHashSet<Chat>(new TimeSpan(0, 3, 0));

            _relayBlocks = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));

            _refreshTimer = new WatchTimer(this.RefreshTimer, new TimeSpan(0, 0, 5));
        }

        private void RefreshTimer()
        {
            _waitingNodes.TrimExcess();
            _cuttingNodes.TrimExcess();
            _removeNodes.TrimExcess();

            _succeededUris.TrimExcess();

            _downloadBlocks.TrimExcess();
            _pushBroadcastSignaturesRequestList.TrimExcess();
            _pushUnicastSignaturesRequestList.TrimExcess();
            _pushMulticastWikisRequestList.TrimExcess();
            _pushMulticastChatsRequestList.TrimExcess();

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

        public GetWikisEventHandler GetLockWikisEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockWikisEvent = value;
                }
            }
        }

        public GetChatsEventHandler GetLockChatsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockChatsEvent = value;
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
                    contexts.Add(new InformationContext("PushHeaderRequestCount", (long)_pushHeaderRequestCount));
                    contexts.Add(new InformationContext("PushHeaderCount", (long)_pushHeaderCount));

                    contexts.Add(new InformationContext("PullNodeCount", (long)_pullNodeCount));
                    contexts.Add(new InformationContext("PullBlockLinkCount", (long)_pullBlockLinkCount));
                    contexts.Add(new InformationContext("PullBlockRequestCount", (long)_pullBlockRequestCount));
                    contexts.Add(new InformationContext("PullBlockCount", (long)_pullBlockCount));
                    contexts.Add(new InformationContext("PullHeaderRequestCount", (long)_pullHeaderRequestCount));
                    contexts.Add(new InformationContext("PullHeaderCount", (long)_pullHeaderCount));

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

                    contexts.Add(new InformationContext("HeaderCount", _settings.HeaderManager.Count));

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

        protected virtual IEnumerable<Wiki> OnLockWikisEvent()
        {
            if (_getLockWikisEvent != null)
            {
                return _getLockWikisEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Chat> OnLockChatsEvent()
        {
            if (_getLockChatsEvent != null)
            {
                return _getLockChatsEvent(this);
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

        private static bool Check(Wiki wiki)
        {
            return !(wiki == null
                || wiki.Name == null
                || wiki.Id == null || wiki.Id.Length == 0);
        }

        private static bool Check(Chat chat)
        {
            return !(chat == null
                || chat.Name == null
                || chat.Id == null || chat.Id.Length == 0);
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
                _removeNodes.Add(node);
                _cuttingNodes.Remove(node);

                if (_routeTable.Count > _routeTableMinCount)
                {
                    _routeTable.Remove(node);
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

                if (_connectionManagers.Count >= this.ConnectionCountLimit)
                {
                    connectionManager.Dispose();
                    return;
                }

                Debug.WriteLine("ConnectionManager: Connect");

                connectionManager.PullNodesEvent += this.connectionManager_NodesEvent;
                connectionManager.PullBlocksLinkEvent += this.connectionManager_BlocksLinkEvent;
                connectionManager.PullBlocksRequestEvent += this.connectionManager_BlocksRequestEvent;
                connectionManager.PullBlockEvent += this.connectionManager_BlockEvent;
                connectionManager.PullBroadcastHeadersRequestEvent += this.connectionManager_PullBroadcastHeadersRequestEvent;
                connectionManager.PullBroadcastHeadersEvent += this.connectionManager_PullBroadcastHeadersEvent;
                connectionManager.PullUnicastHeadersRequestEvent += this.connectionManager_PullUnicastHeadersRequestEvent;
                connectionManager.PullUnicastHeadersEvent += this.connectionManager_PullUnicastHeadersEvent;
                connectionManager.PullMulticastHeadersRequestEvent += this.connectionManager_PullMulticastHeadersRequestEvent;
                connectionManager.PullMulticastHeadersEvent += this.connectionManager_PullMulticastHeadersEvent;
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

                // 接続数を制限する。
                {
                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers.Count(n => n.Direction == ConnectDirection.Out);
                    }

                    if (connectionCount >= (this.ConnectionCountLimit / 2))
                    {
                        continue;
                    }
                }

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

                    foreach (var uri in uris.Randomize())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var connection = _clientManager.CreateConnection(uri, _bandwidthLimit);

                        if (connection != null)
                        {
                            var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectDirection.Out, _bufferManager);

                            try
                            {
                                connectionManager.Connect();
                                if (!ConnectionsManager.Check(connectionManager.Node)) throw new ArgumentException();

                                _succeededUris.Add(uri);

                                lock (this.ThisLock)
                                {
                                    _cuttingNodes.Remove(node);

                                    if (node != connectionManager.Node)
                                    {
                                        this.RemoveNode(connectionManager.Node);
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

                // 接続数を制限する。
                {
                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers.Count(n => n.Direction == ConnectDirection.In);
                    }

                    if (connectionCount >= ((this.ConnectionCountLimit + 1) / 2))
                    {
                        continue;
                    }
                }

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
                                    this.RemoveNode(connectionManager.Node);
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

                    // トラストにより必要なHeaderを選択し、不要なHeaderを削除する。
                    ThreadPool.QueueUserWorkItem((object wstate) =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            var lockSignatures = this.OnLockSignaturesEvent();
                            if (lockSignatures == null) return;

                            var lockWikis = this.OnLockWikisEvent();
                            if (lockWikis == null) return;

                            var lockChats = this.OnLockChatsEvent();
                            if (lockChats == null) return;

                            // Broadcast
                            {
                                // Signature
                                {
                                    var removeSignatures = new SortedSet<string>();
                                    removeSignatures.UnionWith(_settings.HeaderManager.GetBroadcastSignatures());
                                    removeSignatures.ExceptWith(lockSignatures);

                                    var sortList = removeSignatures
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _broadcastSignatureLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.HeaderManager.RemoveProfileSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new SortedSet<string>(_settings.HeaderManager.GetBroadcastSignatures());

                                    foreach (var signature in _broadcastSignatureLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveSignatures.Contains(signature)) continue;

                                        _broadcastSignatureLastAccessTimes.Remove(signature);
                                    }
                                }
                            }

                            // Unicast
                            {
                                // Signature
                                {
                                    var removeSignatures = new SortedSet<string>();
                                    removeSignatures.UnionWith(_settings.HeaderManager.GetUnicastSignatures());
                                    removeSignatures.ExceptWith(lockSignatures);

                                    var sortList = removeSignatures
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _unicastSignatureLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.HeaderManager.RemoveUnicastSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new SortedSet<string>(_settings.HeaderManager.GetUnicastSignatures());

                                    foreach (var signature in _unicastSignatureLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveSignatures.Contains(signature)) continue;

                                        _unicastSignatureLastAccessTimes.Remove(signature);
                                    }
                                }
                            }

                            // Multicast
                            {
                                // Wiki
                                {
                                    var removeWikis = new HashSet<Wiki>();
                                    removeWikis.UnionWith(_settings.HeaderManager.GetMulticastWikis());
                                    removeWikis.ExceptWith(lockWikis);

                                    var sortList = removeWikis
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _multicastWikiLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.HeaderManager.RemoveMulticastTags(sortList.Take(sortList.Count - 1024));

                                    var liveWikis = new HashSet<Wiki>(_settings.HeaderManager.GetMulticastWikis());

                                    foreach (var section in _multicastWikiLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveWikis.Contains(section)) continue;

                                        _multicastWikiLastAccessTimes.Remove(section);
                                    }
                                }

                                // Chat
                                {
                                    var removeChats = new HashSet<Chat>();
                                    removeChats.UnionWith(_settings.HeaderManager.GetMulticastChats());
                                    removeChats.ExceptWith(lockChats);

                                    var sortList = removeChats
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _multicastChatLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.HeaderManager.RemoveMulticastTags(sortList.Take(sortList.Count - 1024));

                                    var liveChats = new HashSet<Chat>(_settings.HeaderManager.GetMulticastChats());

                                    foreach (var section in _multicastChatLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveChats.Contains(section)) continue;

                                        _multicastChatLastAccessTimes.Remove(section);
                                    }
                                }
                            }

                            // Unicast
                            {
                                var trustSignature = new HashSet<string>(lockSignatures);

                                {
                                    var now = DateTime.UtcNow;

                                    var removeSignatureMessageHeaders = new HashSet<SignatureMessageHeader>();

                                    foreach (var targetSignature in _settings.HeaderManager.GetUnicastSignatures())
                                    {
                                        // SignatureMessage
                                        {
                                            var trustHeaders = new Dictionary<string, List<SignatureMessageHeader>>();
                                            var untrustHeaders = new Dictionary<string, List<SignatureMessageHeader>>();

                                            foreach (var header in _settings.HeaderManager.GetSignatureMessageHeaders(targetSignature))
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (trustSignature.Contains(signature))
                                                {
                                                    List<SignatureMessageHeader> list;

                                                    if (!trustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<SignatureMessageHeader>();
                                                        trustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                                else
                                                {
                                                    List<SignatureMessageHeader> list;

                                                    if (!untrustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<SignatureMessageHeader>();
                                                        untrustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                            }

                                            removeSignatureMessageHeaders.UnionWith(untrustHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in CollectionUtilities.Unite(trustHeaders.Values, untrustHeaders.Values))
                                            {
                                                if (list.Count <= 32) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeSignatureMessageHeaders.UnionWith(list.Take(list.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var header in removeSignatureMessageHeaders)
                                    {
                                        _settings.HeaderManager.RemoveHeader(header);
                                    }
                                }
                            }

                            // Multicast
                            {
                                var trustSignature = new HashSet<string>(lockSignatures);

                                {
                                    var now = DateTime.UtcNow;

                                    var removeWikiDocumentHeaders = new HashSet<WikiDocumentHeader>();

                                    foreach (var wiki in _settings.HeaderManager.GetMulticastWikis())
                                    {
                                        // WikiDocument
                                        {
                                            var trustHeaders = new Dictionary<string, List<WikiDocumentHeader>>();
                                            var untrustHeaders = new Dictionary<string, List<WikiDocumentHeader>>();

                                            foreach (var header in _settings.HeaderManager.GetWikiDocumentHeaders(wiki))
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (trustSignature.Contains(signature))
                                                {
                                                    List<WikiDocumentHeader> list;

                                                    if (!trustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<WikiDocumentHeader>();
                                                        trustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                                else
                                                {
                                                    List<WikiDocumentHeader> list;

                                                    if (!untrustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<WikiDocumentHeader>();
                                                        untrustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                            }

                                            removeWikiDocumentHeaders.UnionWith(untrustHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in CollectionUtilities.Unite(trustHeaders.Values, untrustHeaders.Values))
                                            {
                                                if (list.Count <= 32) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeWikiDocumentHeaders.UnionWith(list.Take(list.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var header in removeWikiDocumentHeaders)
                                    {
                                        _settings.HeaderManager.RemoveHeader(header);
                                    }
                                }

                                {
                                    var now = DateTime.UtcNow;

                                    var removeChatTopicHeaders = new HashSet<ChatTopicHeader>();
                                    var removeChatMessageHeaders = new HashSet<ChatMessageHeader>();

                                    foreach (var chat in _settings.HeaderManager.GetMulticastChats())
                                    {
                                        // ChatTopic
                                        {
                                            var untrustHeaders = new List<ChatTopicHeader>();

                                            foreach (var header in _settings.HeaderManager.GetChatTopicHeaders(chat))
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (!trustSignature.Contains(signature))
                                                {
                                                    untrustHeaders.Add(header);
                                                }
                                            }

                                            removeChatTopicHeaders.UnionWith(untrustHeaders.Randomize().Skip(32));
                                        }

                                        // ChatMessage
                                        {
                                            var trustHeaders = new Dictionary<string, List<ChatMessageHeader>>();
                                            var untrustHeaders = new Dictionary<string, List<ChatMessageHeader>>();

                                            foreach (var header in _settings.HeaderManager.GetChatMessageHeaders(chat))
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (trustSignature.Contains(signature))
                                                {
                                                    List<ChatMessageHeader> list;

                                                    if (!trustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<ChatMessageHeader>();
                                                        trustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                                else
                                                {
                                                    List<ChatMessageHeader> list;

                                                    if (!untrustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<ChatMessageHeader>();
                                                        untrustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                            }

                                            removeChatMessageHeaders.UnionWith(untrustHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in CollectionUtilities.Unite(trustHeaders.Values, untrustHeaders.Values))
                                            {
                                                if (list.Count <= 32) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeChatMessageHeaders.UnionWith(list.Take(list.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var header in removeChatTopicHeaders)
                                    {
                                        _settings.HeaderManager.RemoveHeader(header);
                                    }

                                    foreach (var header in removeChatMessageHeaders)
                                    {
                                        _settings.HeaderManager.RemoveHeader(header);
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

                // 拡散アップロード
                if (connectionCount > _diffusionConnectionCountLowerLimit
                    && pushBlockDiffusionStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushBlockDiffusionStopwatch.Restart();

                    // 拡散アップロードするブロック数を10000以下に抑える。
                    lock (this.ThisLock)
                    {
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
                    }

                    // 存在しないブロックのKeyをRemoveする。
                    lock (this.ThisLock)
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

                                // 自分より距離が遠いノードにもアップロードを試みる。
                                foreach (var node in Kademlia<Node>.Search(key.Hash, otherNodes, 3))
                                {
                                    requestNodes.Add(node);
                                }

                                if (requestNodes.Any(n => _messagesManager[n].StockBlocks.Contains(key)))
                                {
                                    _settings.UploadBlocksRequest.Remove(key);
                                    _settings.DiffusionBlocksRequest.Remove(key);

                                    continue;
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
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
                        }

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

                // Headerのアップロード
                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushHeaderUploadStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    pushHeaderUploadStopwatch.Restart();

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

                    // Broadcast
                    foreach (var signature in _settings.HeaderManager.GetBroadcastSignatures())
                    {
                        try
                        {
                            var requestNodes = new List<Node>();

                            foreach (var node in Kademlia<Node>.Search(Signature.GetHash(signature), baseNode.Id, otherNodes, 2))
                            {
                                requestNodes.Add(node);
                            }

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                messageManagers[requestNodes[i]].PullBroadcastSignaturesRequest.Add(signature);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    // Unicast
                    foreach (var signature in _settings.HeaderManager.GetUnicastSignatures())
                    {
                        try
                        {
                            var requestNodes = new List<Node>();

                            foreach (var node in Kademlia<Node>.Search(Signature.GetHash(signature), baseNode.Id, otherNodes, 2))
                            {
                                requestNodes.Add(node);
                            }

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                messageManagers[requestNodes[i]].PullUnicastSignaturesRequest.Add(signature);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    // Multicast
                    {
                        foreach (var tag in _settings.HeaderManager.GetMulticastWikis())
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(tag.Id, baseNode.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    messageManagers[requestNodes[i]].PullMulticastWikisRequest.Add(tag);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        foreach (var tag in _settings.HeaderManager.GetMulticastChats())
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(tag.Id, baseNode.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    messageManagers[requestNodes[i]].PullMulticastChatsRequest.Add(tag);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }
                    }
                }

                // Headerのダウンロード
                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushHeaderDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushHeaderDownloadStopwatch.Restart();

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

                    var pushBroadcastSignaturesRequestList = new List<string>();
                    var pushUnicastSignaturesRequestList = new List<string>();
                    var pushMulticastWikisRequestList = new List<Wiki>();
                    var pushMulticastChatsRequestList = new List<Chat>();

                    // Broadcast
                    {
                        {
                            var array = _pushBroadcastSignaturesRequestList.ToArray();
                            _random.Shuffle(array);

                            int count = _maxHeaderRequestCount;

                            for (int i = 0; count > 0 && i < array.Length; i++)
                            {
                                if (!messageManagers.Values.Any(n => n.PushBroadcastSignaturesRequest.Contains(array[i])))
                                {
                                    pushBroadcastSignaturesRequestList.Add(array[i]);

                                    count--;
                                }
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullBroadcastSignaturesRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushBroadcastSignaturesRequest.Contains(array[i])))
                                    {
                                        pushBroadcastSignaturesRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }
                        }
                    }

                    // Unicast
                    {
                        {
                            var array = _pushUnicastSignaturesRequestList.ToArray();
                            _random.Shuffle(array);

                            int count = _maxHeaderRequestCount;

                            for (int i = 0; count > 0 && i < array.Length; i++)
                            {
                                if (!messageManagers.Values.Any(n => n.PushUnicastSignaturesRequest.Contains(array[i])))
                                {
                                    pushUnicastSignaturesRequestList.Add(array[i]);

                                    count--;
                                }
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullUnicastSignaturesRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushUnicastSignaturesRequest.Contains(array[i])))
                                    {
                                        pushUnicastSignaturesRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }
                        }
                    }

                    // Multicast
                    {
                        {
                            {
                                var array = _pushMulticastWikisRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushMulticastWikisRequest.Contains(array[i])))
                                    {
                                        pushMulticastWikisRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                var array = _pushMulticastChatsRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushMulticastChatsRequest.Contains(array[i])))
                                    {
                                        pushMulticastChatsRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullMulticastWikisRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushMulticastWikisRequest.Contains(array[i])))
                                    {
                                        pushMulticastWikisRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                var array = messageManager.PullMulticastChatsRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushMulticastChatsRequest.Contains(array[i])))
                                    {
                                        pushMulticastChatsRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }
                        }
                    }

                    _random.Shuffle(pushBroadcastSignaturesRequestList);
                    _random.Shuffle(pushUnicastSignaturesRequestList);
                    _random.Shuffle(pushMulticastWikisRequestList);
                    _random.Shuffle(pushMulticastChatsRequestList);

                    // Broadcast
                    {
                        var pushBroadcastSignaturesRequestDictionary = new Dictionary<Node, SortedSet<string>>();

                        foreach (var signature in pushBroadcastSignaturesRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(Signature.GetHash(signature), baseNode.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    SortedSet<string> collection;

                                    if (!pushBroadcastSignaturesRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new SortedSet<string>();
                                        pushBroadcastSignaturesRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxHeaderRequestCount)
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

                        lock (_pushBroadcastSignaturesRequestDictionary.ThisLock)
                        {
                            _pushBroadcastSignaturesRequestDictionary.Clear();

                            foreach (var pair in pushBroadcastSignaturesRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushBroadcastSignaturesRequestDictionary.Add(node, new List<string>(targets.Randomize()));
                            }
                        }
                    }

                    // Unicast
                    {
                        var pushUnicastSignaturesRequestDictionary = new Dictionary<Node, SortedSet<string>>();

                        foreach (var signature in pushUnicastSignaturesRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(Signature.GetHash(signature), baseNode.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    SortedSet<string> collection;

                                    if (!pushUnicastSignaturesRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new SortedSet<string>();
                                        pushUnicastSignaturesRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxHeaderRequestCount)
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

                        lock (_pushUnicastSignaturesRequestDictionary.ThisLock)
                        {
                            _pushUnicastSignaturesRequestDictionary.Clear();

                            foreach (var pair in pushUnicastSignaturesRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushUnicastSignaturesRequestDictionary.Add(node, new List<string>(targets.Randomize()));
                            }
                        }
                    }

                    // Multicast
                    {
                        var pushMulticastWikisRequestDictionary = new Dictionary<Node, HashSet<Wiki>>();
                        var pushMulticastChatsRequestDictionary = new Dictionary<Node, HashSet<Chat>>();

                        foreach (var tag in pushMulticastWikisRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(tag.Id, baseNode.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Wiki> collection;

                                    if (!pushMulticastWikisRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Wiki>();
                                        pushMulticastWikisRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxHeaderRequestCount)
                                    {
                                        collection.Add(tag);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        foreach (var tag in pushMulticastChatsRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(tag.Id, baseNode.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Chat> collection;

                                    if (!pushMulticastChatsRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Chat>();
                                        pushMulticastChatsRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxHeaderRequestCount)
                                    {
                                        collection.Add(tag);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushMulticastWikisRequestDictionary.ThisLock)
                        {
                            _pushMulticastWikisRequestDictionary.Clear();

                            foreach (var pair in pushMulticastWikisRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushMulticastWikisRequestDictionary.Add(node, new List<Wiki>(targets.Randomize()));
                            }
                        }

                        lock (_pushMulticastChatsRequestDictionary.ThisLock)
                        {
                            _pushMulticastChatsRequestDictionary.Clear();

                            foreach (var pair in pushMulticastChatsRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushMulticastChatsRequestDictionary.Add(node, new List<Chat>(targets.Randomize()));
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

                Stopwatch nodeUpdateTime = new Stopwatch();
                Stopwatch updateTime = new Stopwatch();
                updateTime.Start();
                Stopwatch blockDiffusionTime = new Stopwatch();
                blockDiffusionTime.Start();
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
                        connectionCount = _connectionManagers.Count;
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

                        // PushBroadcastHeadersRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            List<string> targetList = null;

                            lock (_pushBroadcastSignaturesRequestDictionary.ThisLock)
                            {
                                if (_pushBroadcastSignaturesRequestDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushBroadcastSignaturesRequestDictionary.Remove(connectionManager.Node);
                                    messageManager.PushBroadcastSignaturesRequest.AddRange(targetList);
                                }
                            }

                            if (targetList != null)
                            {
                                try
                                {
                                    connectionManager.PushBroadcastHeadersRequest(targetList);

                                    foreach (var item in targetList)
                                    {
                                        _pushBroadcastSignaturesRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BroadcastHeadersRequest ({0})", targetList.Count));
                                    _pushHeaderRequestCount.Add(targetList.Count);
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in targetList)
                                    {
                                        messageManager.PushBroadcastSignaturesRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }

                        // PushUnicastHeadersRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            List<string> targetList = null;

                            lock (_pushUnicastSignaturesRequestDictionary.ThisLock)
                            {
                                if (_pushUnicastSignaturesRequestDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushUnicastSignaturesRequestDictionary.Remove(connectionManager.Node);
                                    messageManager.PushUnicastSignaturesRequest.AddRange(targetList);
                                }
                            }

                            if (targetList != null)
                            {
                                try
                                {
                                    connectionManager.PushUnicastHeadersRequest(targetList);

                                    foreach (var item in targetList)
                                    {
                                        _pushUnicastSignaturesRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push UnicastHeadersRequest ({0})", targetList.Count));
                                    _pushHeaderRequestCount.Add(targetList.Count);
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in targetList)
                                    {
                                        messageManager.PushUnicastSignaturesRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }

                        // PushMulticastHeadersRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            {
                                List<Wiki> wikiList = null;
                                List<Chat> chatList = null;

                                lock (_pushMulticastWikisRequestDictionary.ThisLock)
                                {
                                    if (_pushMulticastWikisRequestDictionary.TryGetValue(connectionManager.Node, out wikiList))
                                    {
                                        _pushMulticastWikisRequestDictionary.Remove(connectionManager.Node);
                                        messageManager.PushMulticastWikisRequest.AddRange(wikiList);
                                    }
                                }

                                lock (_pushMulticastChatsRequestDictionary.ThisLock)
                                {
                                    if (_pushMulticastChatsRequestDictionary.TryGetValue(connectionManager.Node, out chatList))
                                    {
                                        _pushMulticastChatsRequestDictionary.Remove(connectionManager.Node);
                                        messageManager.PushMulticastChatsRequest.AddRange(chatList);
                                    }
                                }

                                if (wikiList != null || chatList != null)
                                {
                                    try
                                    {
                                        connectionManager.PushMulticastHeadersRequest(wikiList, chatList);

                                        int tagCount = 0;

                                        if (wikiList != null)
                                        {
                                            foreach (var item in wikiList)
                                            {
                                                _pushMulticastWikisRequestList.Remove(item);
                                            }

                                            tagCount += wikiList.Count;
                                        }

                                        if (chatList != null)
                                        {
                                            foreach (var item in chatList)
                                            {
                                                _pushMulticastChatsRequestList.Remove(item);
                                            }

                                            tagCount += chatList.Count;
                                        }

                                        Debug.WriteLine(string.Format("ConnectionManager: Push MulticastHeadersRequest ({0})", tagCount));
                                        _pushHeaderRequestCount.Add(tagCount);
                                    }
                                    catch (Exception e)
                                    {
                                        if (wikiList != null)
                                        {
                                            foreach (var item in wikiList)
                                            {
                                                messageManager.PushMulticastWikisRequest.Remove(item);
                                            }
                                        }

                                        if (chatList != null)
                                        {
                                            foreach (var item in chatList)
                                            {
                                                messageManager.PushMulticastChatsRequest.Remove(item);
                                            }
                                        }

                                        throw e;
                                    }
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
                            }
                        }
                    }

                    if (headerUpdateTime.Elapsed.TotalSeconds >= 60)
                    {
                        headerUpdateTime.Restart();

                        // PushBroadcastHeaders
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var signatures = messageManager.PullBroadcastSignaturesRequest.ToArray();

                                var profileHeaders = new List<ProfileHeader>();

                                _random.Shuffle(signatures);
                                foreach (var signature in signatures)
                                {
                                    var header = _settings.HeaderManager.GetProfileHeader(signature);
                                    if (header == null) continue;

                                    if (!messageManager.StockProfileHeaders.Contains(header.CreateHash(_hashAlgorithm)))
                                    {
                                        profileHeaders.Add(header);

                                        if (profileHeaders.Count >= _maxHeaderCount) break;
                                    }

                                    if (profileHeaders.Count >= _maxHeaderCount) break;
                                }

                                if (profileHeaders.Count > 0)
                                {
                                    connectionManager.PushBroadcastHeaders(
                                        profileHeaders);

                                    var headerCount =
                                        profileHeaders.Count;

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BroadcastHeaders ({0})", headerCount));
                                    _pushHeaderCount.Add(headerCount);

                                    foreach (var header in profileHeaders)
                                    {
                                        messageManager.StockProfileHeaders.Add(header.CreateHash(_hashAlgorithm));
                                    }
                                }
                            }
                        }

                        // PushUnicastHeaders
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var signatures = messageManager.PullUnicastSignaturesRequest.ToArray();

                                var signatureMessageHeaders = new List<SignatureMessageHeader>();

                                _random.Shuffle(signatures);
                                foreach (var signature in signatures)
                                {
                                    foreach (var header in _settings.HeaderManager.GetSignatureMessageHeaders(signature))
                                    {
                                        if (!messageManager.StockSignatureMessageHeaders.Contains(header.CreateHash(_hashAlgorithm)))
                                        {
                                            signatureMessageHeaders.Add(header);

                                            if (signatureMessageHeaders.Count >= _maxHeaderCount) break;
                                        }
                                    }

                                    if (signatureMessageHeaders.Count >= _maxHeaderCount) break;
                                }

                                if (signatureMessageHeaders.Count > 0)
                                {
                                    connectionManager.PushUnicastHeaders(
                                        signatureMessageHeaders);

                                    var headerCount =
                                        signatureMessageHeaders.Count;

                                    Debug.WriteLine(string.Format("ConnectionManager: Push UnicastHeaders ({0})", headerCount));
                                    _pushHeaderCount.Add(headerCount);

                                    foreach (var header in signatureMessageHeaders)
                                    {
                                        messageManager.StockSignatureMessageHeaders.Add(header.CreateHash(_hashAlgorithm));
                                    }
                                }
                            }
                        }

                        // PushMulticastHeaders
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var wikis = messageManager.PullMulticastWikisRequest.ToArray();
                                var chats = messageManager.PullMulticastChatsRequest.ToArray();

                                var wikiDocumentHeaders = new List<WikiDocumentHeader>();
                                var chatTopicHeaders = new List<ChatTopicHeader>();
                                var chatMessageHeaders = new List<ChatMessageHeader>();

                                _random.Shuffle(wikis);
                                foreach (var tag in wikis)
                                {
                                    foreach (var header in _settings.HeaderManager.GetWikiDocumentHeaders(tag))
                                    {
                                        if (!messageManager.StockWikiDocumentHeaders.Contains(header.CreateHash(_hashAlgorithm)))
                                        {
                                            wikiDocumentHeaders.Add(header);

                                            if (wikiDocumentHeaders.Count >= _maxHeaderCount) break;
                                        }
                                    }

                                    if (wikiDocumentHeaders.Count >= _maxHeaderCount) break;
                                }

                                _random.Shuffle(chats);
                                foreach (var tag in chats)
                                {
                                    foreach (var header in _settings.HeaderManager.GetChatTopicHeaders(tag))
                                    {
                                        if (!messageManager.StockChatTopicHeaders.Contains(header.CreateHash(_hashAlgorithm)))
                                        {
                                            chatTopicHeaders.Add(header);

                                            if (chatTopicHeaders.Count >= _maxHeaderCount) break;
                                        }
                                    }

                                    foreach (var header in _settings.HeaderManager.GetChatMessageHeaders(tag))
                                    {
                                        if (!messageManager.StockChatMessageHeaders.Contains(header.CreateHash(_hashAlgorithm)))
                                        {
                                            chatMessageHeaders.Add(header);

                                            if (chatMessageHeaders.Count >= _maxHeaderCount) break;
                                        }
                                    }

                                    if (chatTopicHeaders.Count >= _maxHeaderCount) break;
                                    if (chatMessageHeaders.Count >= _maxHeaderCount) break;
                                }

                                if (wikiDocumentHeaders.Count > 0
                                    || chatTopicHeaders.Count > 0
                                    || chatMessageHeaders.Count > 0)
                                {
                                    connectionManager.PushMulticastHeaders(
                                        wikiDocumentHeaders,
                                        chatTopicHeaders,
                                        chatMessageHeaders);

                                    var headerCount =
                                        wikiDocumentHeaders.Count
                                        + chatTopicHeaders.Count
                                        + chatMessageHeaders.Count;

                                    Debug.WriteLine(string.Format("ConnectionManager: Push MulticastHeaders ({0})", headerCount));
                                    _pushHeaderCount.Add(headerCount);

                                    foreach (var header in wikiDocumentHeaders)
                                    {
                                        messageManager.StockWikiDocumentHeaders.Add(header.CreateHash(_hashAlgorithm));
                                    }

                                    foreach (var header in chatTopicHeaders)
                                    {
                                        messageManager.StockChatTopicHeaders.Add(header.CreateHash(_hashAlgorithm));
                                    }

                                    foreach (var header in chatMessageHeaders)
                                    {
                                        messageManager.StockChatMessageHeaders.Add(header.CreateHash(_hashAlgorithm));
                                    }
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

        private void connectionManager_PullBroadcastHeadersRequestEvent(object sender, PullBroadcastHeadersRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullBroadcastSignaturesRequest.Count > _maxHeaderRequestCount * messageManager.PullBroadcastSignaturesRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BroadcastHeadersRequest ({0})",
                e.Signatures.Count()));

            foreach (var signature in e.Signatures.Take(_maxHeaderRequestCount))
            {
                if (!Signature.Check(signature)) continue;

                messageManager.PullBroadcastSignaturesRequest.Add(signature);
                _pullHeaderRequestCount.Increment();

                _broadcastSignatureLastAccessTimes[signature] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullBroadcastHeadersEvent(object sender, PullBroadcastHeadersEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockProfileHeaders.Count > _maxHeaderCount * messageManager.StockProfileHeaders.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BroadcastHeaders ({0})",
                e.ProfileHeaders.Count()));

            foreach (var header in e.ProfileHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockProfileHeaders.Add(header.CreateHash(_hashAlgorithm));

                    var signature = header.Certificate.ToString();

                    _broadcastSignatureLastAccessTimes[signature] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
            }
        }

        private void connectionManager_PullUnicastHeadersRequestEvent(object sender, PullUnicastHeadersRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullUnicastSignaturesRequest.Count > _maxHeaderRequestCount * messageManager.PullUnicastSignaturesRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull UnicastHeadersRequest ({0})",
                e.Signatures.Count()));

            foreach (var signature in e.Signatures.Take(_maxHeaderRequestCount))
            {
                if (!Signature.Check(signature)) continue;

                messageManager.PullUnicastSignaturesRequest.Add(signature);
                _pullHeaderRequestCount.Increment();

                _unicastSignatureLastAccessTimes[signature] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullUnicastHeadersEvent(object sender, PullUnicastHeadersEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockSignatureMessageHeaders.Count > _maxHeaderCount * messageManager.StockSignatureMessageHeaders.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull UnicastHeaders ({0})",
                e.SignatureMessageHeaders.Count()));

            foreach (var header in e.SignatureMessageHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockSignatureMessageHeaders.Add(header.CreateHash(_hashAlgorithm));

                    _unicastSignatureLastAccessTimes[header.Signature] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
            }
        }

        private void connectionManager_PullMulticastHeadersRequestEvent(object sender, PullMulticastHeadersRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullMulticastWikisRequest.Count > _maxHeaderRequestCount * messageManager.PullMulticastWikisRequest.SurvivalTime.TotalMinutes) return;
            if (messageManager.PullMulticastChatsRequest.Count > _maxHeaderRequestCount * messageManager.PullMulticastChatsRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull MulticastHeadersRequest ({0})",
                e.Wikis.Count()
                + e.Chats.Count()));

            foreach (var tag in e.Wikis.Take(_maxHeaderRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullMulticastWikisRequest.Add(tag);
                _pullHeaderRequestCount.Increment();

                _multicastWikiLastAccessTimes[tag] = DateTime.UtcNow;
            }

            foreach (var tag in e.Chats.Take(_maxHeaderRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullMulticastChatsRequest.Add(tag);
                _pullHeaderRequestCount.Increment();

                _multicastChatLastAccessTimes[tag] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullMulticastHeadersEvent(object sender, PullMulticastHeadersEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockWikiDocumentHeaders.Count > _maxHeaderCount * messageManager.StockWikiDocumentHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockChatTopicHeaders.Count > _maxHeaderCount * messageManager.StockChatTopicHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockChatMessageHeaders.Count > _maxHeaderCount * messageManager.StockChatMessageHeaders.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull MulticastHeaders ({0})",
                e.WikiDocumentHeaders.Count()
                + e.ChatTopicHeaders.Count()
                + e.ChatMessageHeaders.Count()));

            foreach (var header in e.WikiDocumentHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockWikiDocumentHeaders.Add(header.CreateHash(_hashAlgorithm));

                    _multicastWikiLastAccessTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
            }

            foreach (var header in e.ChatTopicHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockChatTopicHeaders.Add(header.CreateHash(_hashAlgorithm));

                    _multicastChatLastAccessTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
            }

            foreach (var header in e.ChatMessageHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockChatMessageHeaders.Add(header.CreateHash(_hashAlgorithm));

                    _multicastChatLastAccessTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
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
                    this.RemoveNode(connectionManager.Node);
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

        public ProfileHeader GetProfileHeader(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushBroadcastSignaturesRequestList.Add(signature);

                return _settings.HeaderManager.GetProfileHeader(signature);
            }
        }

        public IEnumerable<SignatureMessageHeader> GetSignatureMessageHeaders(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushUnicastSignaturesRequestList.Add(signature);

                return _settings.HeaderManager.GetSignatureMessageHeaders(signature);
            }
        }

        public IEnumerable<WikiDocumentHeader> GetWikiDocumentHeaders(Wiki tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushMulticastWikisRequestList.Add(tag);

                return _settings.HeaderManager.GetWikiDocumentHeaders(tag);
            }
        }

        public IEnumerable<ChatTopicHeader> GetChatTopicHeaders(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushMulticastChatsRequestList.Add(tag);

                return _settings.HeaderManager.GetChatTopicHeaders(tag);
            }
        }

        public IEnumerable<ChatMessageHeader> GetChatMessageHeaders(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushMulticastChatsRequestList.Add(tag);

                return _settings.HeaderManager.GetChatMessageHeaders(tag);
            }
        }

        public void Upload(ProfileHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.HeaderManager.SetHeader(header);
            }
        }

        public void Upload(SignatureMessageHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.HeaderManager.SetHeader(header);
            }
        }

        public void Upload(WikiDocumentHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.HeaderManager.SetHeader(header);
            }
        }

        public void Upload(ChatTopicHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.HeaderManager.SetHeader(header);
            }
        }

        public void Upload(ChatMessageHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.HeaderManager.SetHeader(header);
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

            private HeaderManager _headerManager = new HeaderManager();

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<Node>() { Name = "BaseNode", Value = new Node(new byte[0], null)},
                    new Library.Configuration.SettingContent<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingContent<int>() { Name = "ConnectionCountLimit", Value = 32 },
                    new Library.Configuration.SettingContent<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "DiffusionBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "UploadBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<List<ProfileHeader>>() { Name = "ProfileHeaders", Value = new List<ProfileHeader>() },
                    new Library.Configuration.SettingContent<List<SignatureMessageHeader>>() { Name = "SignatureMessageHeaders", Value = new List<SignatureMessageHeader>() },
                    new Library.Configuration.SettingContent<List<WikiDocumentHeader>>() { Name = "WikiDocumentHeaders", Value = new List<WikiDocumentHeader>() },
                    new Library.Configuration.SettingContent<List<ChatTopicHeader>>() { Name = "ChatTopicHeaders", Value = new List<ChatTopicHeader>() },
                    new Library.Configuration.SettingContent<List<ChatMessageHeader>>() { Name = "ChatMessageHeaders", Value = new List<ChatMessageHeader>() },
                })
            {
                _thisLock = lockObject;
            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);

                    foreach (var header in this.ProfileHeaders)
                    {
                        _headerManager.SetHeader(header);
                    }

                    foreach (var header in this.SignatureMessageHeaders)
                    {
                        _headerManager.SetHeader(header);
                    }

                    foreach (var header in this.WikiDocumentHeaders)
                    {
                        _headerManager.SetHeader(header);
                    }

                    foreach (var header in this.ChatTopicHeaders)
                    {
                        _headerManager.SetHeader(header);
                    }

                    foreach (var header in this.ChatMessageHeaders)
                    {
                        _headerManager.SetHeader(header);
                    }

                    this.ProfileHeaders.Clear();
                    this.SignatureMessageHeaders.Clear();
                    this.WikiDocumentHeaders.Clear();
                    this.ChatTopicHeaders.Clear();
                    this.ChatMessageHeaders.Clear();
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    this.ProfileHeaders.AddRange(_headerManager.GetProfileHeaders());
                    this.SignatureMessageHeaders.AddRange(_headerManager.GetSignatureMessageHeaders());
                    this.WikiDocumentHeaders.AddRange(_headerManager.GetWikiDocumentHeaders());
                    this.ChatTopicHeaders.AddRange(_headerManager.GetChatTopicHeaders());
                    this.ChatMessageHeaders.AddRange(_headerManager.GetChatMessageHeaders());

                    base.Save(directoryPath);

                    this.ProfileHeaders.Clear();
                    this.SignatureMessageHeaders.Clear();
                    this.WikiDocumentHeaders.Clear();
                    this.ChatTopicHeaders.Clear();
                    this.ChatMessageHeaders.Clear();
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

            public HeaderManager HeaderManager
            {
                get
                {
                    lock (_thisLock)
                    {
                        return _headerManager;
                    }
                }
            }

            private List<ProfileHeader> ProfileHeaders
            {
                get
                {
                    return (List<ProfileHeader>)this["ProfileHeaders"];
                }
            }

            private List<SignatureMessageHeader> SignatureMessageHeaders
            {
                get
                {
                    return (List<SignatureMessageHeader>)this["SignatureMessageHeaders"];
                }
            }

            private List<WikiDocumentHeader> WikiDocumentHeaders
            {
                get
                {
                    return (List<WikiDocumentHeader>)this["WikiDocumentHeaders"];
                }
            }

            private List<ChatTopicHeader> ChatTopicHeaders
            {
                get
                {
                    return (List<ChatTopicHeader>)this["ChatTopicHeaders"];
                }
            }

            private List<ChatMessageHeader> ChatMessageHeaders
            {
                get
                {
                    return (List<ChatMessageHeader>)this["ChatMessageHeaders"];
                }
            }
        }

        public class HeaderManager
        {
            private Dictionary<string, ProfileHeader> _profileHeaders = new Dictionary<string, ProfileHeader>();
            private Dictionary<string, HashSet<SignatureMessageHeader>> _signatureMessageHeaders = new Dictionary<string, HashSet<SignatureMessageHeader>>();
            private Dictionary<Wiki, Dictionary<string, HashSet<WikiDocumentHeader>>> _wikiDocumentHeaders = new Dictionary<Wiki, Dictionary<string, HashSet<WikiDocumentHeader>>>();
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

                        count += _profileHeaders.Count;
                        count += _signatureMessageHeaders.Values.Sum(n => n.Count);
                        count += _wikiDocumentHeaders.Values.Sum(n => n.Values.Sum(m => m.Count));
                        count += _chatTopicHeaders.Values.Sum(n => n.Values.Count);
                        count += _chatMessageHeaders.Values.Sum(n => n.Values.Sum(m => m.Count));

                        return count;
                    }
                }
            }

            public IEnumerable<string> GetBroadcastSignatures()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<string>();

                    hashset.UnionWith(_profileHeaders.Keys);

                    return hashset;
                }
            }

            public IEnumerable<string> GetUnicastSignatures()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<string>();

                    hashset.UnionWith(_signatureMessageHeaders.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Wiki> GetMulticastWikis()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<Wiki>();

                    hashset.UnionWith(_wikiDocumentHeaders.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Chat> GetMulticastChats()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<Chat>();

                    hashset.UnionWith(_chatTopicHeaders.Keys);
                    hashset.UnionWith(_chatMessageHeaders.Keys);

                    return hashset;
                }
            }

            public void RemoveProfileSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        _profileHeaders.Remove(signature);
                    }
                }
            }

            public void RemoveUnicastSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        _signatureMessageHeaders.Remove(signature);
                    }
                }
            }

            public void RemoveMulticastTags(IEnumerable<Wiki> tags)
            {
                lock (_thisLock)
                {
                    foreach (var wiki in tags)
                    {
                        _wikiDocumentHeaders.Remove(wiki);
                    }
                }
            }

            public void RemoveMulticastTags(IEnumerable<Chat> tags)
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

            public IEnumerable<ProfileHeader> GetProfileHeaders()
            {
                lock (_thisLock)
                {
                    return _profileHeaders.Values.ToArray();
                }
            }

            public ProfileHeader GetProfileHeader(string signature)
            {
                lock (_thisLock)
                {
                    ProfileHeader header;

                    if (_profileHeaders.TryGetValue(signature, out header))
                    {
                        return header;
                    }

                    return null;
                }
            }

            public IEnumerable<SignatureMessageHeader> GetSignatureMessageHeaders()
            {
                lock (_thisLock)
                {
                    return _signatureMessageHeaders.Values.Extract().ToArray();
                }
            }

            public IEnumerable<SignatureMessageHeader> GetSignatureMessageHeaders(string signature)
            {
                lock (_thisLock)
                {
                    HashSet<SignatureMessageHeader> hashset;

                    if (_signatureMessageHeaders.TryGetValue(signature, out hashset))
                    {
                        return hashset.ToArray();
                    }

                    return new SignatureMessageHeader[0];
                }
            }

            public IEnumerable<WikiDocumentHeader> GetWikiDocumentHeaders()
            {
                lock (_thisLock)
                {
                    return _wikiDocumentHeaders.Values.SelectMany(n => n.Values.Extract()).ToArray();
                }
            }

            public IEnumerable<WikiDocumentHeader> GetWikiDocumentHeaders(Wiki tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<WikiDocumentHeader>> dic = null;

                    if (_wikiDocumentHeaders.TryGetValue(tag, out dic))
                    {
                        return dic.Values.Extract().ToArray();
                    }

                    return new WikiDocumentHeader[0];
                }
            }

            public IEnumerable<ChatTopicHeader> GetChatTopicHeaders()
            {
                lock (_thisLock)
                {
                    return _chatTopicHeaders.Values.SelectMany(n => n.Values).ToArray();
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
                    return _chatMessageHeaders.Values.SelectMany(n => n.Values.Extract()).ToArray();
                }
            }

            public IEnumerable<ChatMessageHeader> GetChatMessageHeaders(Chat tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<ChatMessageHeader>> dic = null;

                    if (_chatMessageHeaders.TryGetValue(tag, out dic))
                    {
                        return dic.Values.Extract().ToArray();
                    }

                    return new ChatMessageHeader[0];
                }
            }

            public bool SetHeader(ProfileHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return false;

                    var signature = header.Certificate.ToString();

                    ProfileHeader tempHeader;

                    if (!_profileHeaders.TryGetValue(signature, out tempHeader)
                        || header.CreationTime > tempHeader.CreationTime)
                    {
                        if (!header.VerifyCertificate()) throw new CertificateException();

                        _profileHeaders[signature] = header;
                    }

                    return (tempHeader == null || header.CreationTime >= tempHeader.CreationTime);
                }
            }

            public bool SetHeader(SignatureMessageHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || !Signature.Check(header.Signature)
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return false;

                    HashSet<SignatureMessageHeader> hashset;

                    if (!_signatureMessageHeaders.TryGetValue(header.Signature, out hashset))
                    {
                        hashset = new HashSet<SignatureMessageHeader>();
                        _signatureMessageHeaders[header.Signature] = hashset;
                    }

                    if (!hashset.Contains(header))
                    {
                        if (!header.VerifyCertificate()) throw new CertificateException();

                        hashset.Add(header);
                    }

                    return true;
                }
            }

            public bool SetHeader(WikiDocumentHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || header.Tag == null
                            || header.Tag.Id == null || header.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(header.Tag.Name)
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return false;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, HashSet<WikiDocumentHeader>> dic;

                    if (!_wikiDocumentHeaders.TryGetValue(header.Tag, out dic))
                    {
                        dic = new Dictionary<string, HashSet<WikiDocumentHeader>>();
                        _wikiDocumentHeaders[header.Tag] = dic;
                    }

                    HashSet<WikiDocumentHeader> hashset;

                    if (!dic.TryGetValue(signature, out hashset))
                    {
                        hashset = new HashSet<WikiDocumentHeader>();
                        dic[signature] = hashset;
                    }

                    if (!hashset.Contains(header))
                    {
                        if (!header.VerifyCertificate()) throw new CertificateException();

                        hashset.Add(header);
                    }

                    return true;
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
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return false;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, ChatTopicHeader> dic;

                    if (!_chatTopicHeaders.TryGetValue(header.Tag, out dic))
                    {
                        dic = new Dictionary<string, ChatTopicHeader>();
                        _chatTopicHeaders[header.Tag] = dic;
                    }

                    ChatTopicHeader tempHeader;

                    if (!dic.TryGetValue(signature, out tempHeader)
                        || header.CreationTime > tempHeader.CreationTime)
                    {
                        if (!header.VerifyCertificate()) throw new CertificateException();

                        dic[signature] = header;
                    }

                    return (tempHeader == null || header.CreationTime >= tempHeader.CreationTime);
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
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return false;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, HashSet<ChatMessageHeader>> dic;

                    if (!_chatMessageHeaders.TryGetValue(header.Tag, out dic))
                    {
                        dic = new Dictionary<string, HashSet<ChatMessageHeader>>();
                        _chatMessageHeaders[header.Tag] = dic;
                    }

                    HashSet<ChatMessageHeader> hashset;

                    if (!dic.TryGetValue(signature, out hashset))
                    {
                        hashset = new HashSet<ChatMessageHeader>();
                        dic[signature] = hashset;
                    }

                    if (!hashset.Contains(header))
                    {
                        if (!header.VerifyCertificate()) throw new CertificateException();

                        hashset.Add(header);
                    }

                    return true;
                }
            }

            public void RemoveHeader(ProfileHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return;

                    var signature = header.Certificate.ToString();

                    _profileHeaders.Remove(signature);
                }
            }

            public void RemoveHeader(SignatureMessageHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || !Signature.Check(header.Signature)
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return;

                    HashSet<SignatureMessageHeader> hashset;
                    if (!_signatureMessageHeaders.TryGetValue(header.Signature, out hashset)) return;

                    hashset.Remove(header);

                    if (hashset.Count == 0)
                    {
                        _signatureMessageHeaders.Remove(header.Signature);
                    }
                }
            }

            public void RemoveHeader(WikiDocumentHeader header)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (header == null
                        || header.Tag == null
                            || header.Tag.Id == null || header.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(header.Tag.Name)
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, HashSet<WikiDocumentHeader>> dic;
                    if (!_wikiDocumentHeaders.TryGetValue(header.Tag, out dic)) return;

                    HashSet<WikiDocumentHeader> hashset;
                    if (!dic.TryGetValue(signature, out hashset)) return;

                    hashset.Remove(header);

                    if (hashset.Count == 0)
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            _wikiDocumentHeaders.Remove(header.Tag);
                        }
                    }
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
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, ChatTopicHeader> dic;
                    if (!_chatTopicHeaders.TryGetValue(header.Tag, out dic)) return;

                    ChatTopicHeader tempHeader;
                    if (!dic.TryGetValue(signature, out tempHeader)) return;

                    if (header == tempHeader)
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            _chatTopicHeaders.Remove(header.Tag);
                        }
                    }
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
                        || (header.CreationTime - now).Minutes > 30
                        || header.Certificate == null) return;

                    var signature = header.Certificate.ToString();

                    Dictionary<string, HashSet<ChatMessageHeader>> dic;
                    if (!_chatMessageHeaders.TryGetValue(header.Tag, out dic)) return;

                    HashSet<ChatMessageHeader> hashset;
                    if (!dic.TryGetValue(signature, out hashset)) return;

                    hashset.Remove(header);

                    if (hashset.Count == 0)
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            _chatMessageHeaders.Remove(header.Tag);
                        }
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
