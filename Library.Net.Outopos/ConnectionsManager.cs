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
    public delegate IEnumerable<Section> GetSectionsEventHandler(object sender);
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
        private LockedHashDictionary<Node, List<Section>> _pushSectionsRequestDictionary = new LockedHashDictionary<Node, List<Section>>();
        private LockedHashDictionary<Node, List<Wiki>> _pushWikisRequestDictionary = new LockedHashDictionary<Node, List<Wiki>>();
        private LockedHashDictionary<Node, List<Chat>> _pushChatsRequestDictionary = new LockedHashDictionary<Node, List<Chat>>();
        private LockedHashDictionary<Node, List<string>> _pushSignaturesRequestDictionary = new LockedHashDictionary<Node, List<string>>();

        private LockedHashDictionary<Node, Queue<Key>> _diffusionBlocksDictionary = new LockedHashDictionary<Node, Queue<Key>>();
        private LockedHashDictionary<Node, Queue<Key>> _uploadBlocksDictionary = new LockedHashDictionary<Node, Queue<Key>>();

        private WatchTimer _refreshTimer;

        private LockedList<Node> _creatingNodes;

        private VolatileHashSet<Node> _waitingNodes;
        private VolatileHashSet<Node> _cuttingNodes;
        private VolatileHashSet<Node> _removeNodes;

        private VolatileHashSet<string> _succeededUris;

        private VolatileHashSet<Section> _pushSectionsRequestList;
        private VolatileHashSet<Wiki> _pushWikisRequestList;
        private VolatileHashSet<Chat> _pushChatsRequestList;
        private VolatileHashSet<string> _pushSignaturesRequestList;
        private VolatileHashSet<Key> _downloadBlocks;

        private LockedHashDictionary<Section, DateTime> _lastUsedSectionTimes = new LockedHashDictionary<Section, DateTime>();
        private LockedHashDictionary<Wiki, DateTime> _lastUsedWikiTimes = new LockedHashDictionary<Wiki, DateTime>();
        private LockedHashDictionary<Chat, DateTime> _lastUsedChatTimes = new LockedHashDictionary<Chat, DateTime>();

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
        private GetSectionsEventHandler _getLockSectionsEvent;
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
            _pushSectionsRequestList = new VolatileHashSet<Section>(new TimeSpan(0, 3, 0));
            _pushWikisRequestList = new VolatileHashSet<Wiki>(new TimeSpan(0, 3, 0));
            _pushChatsRequestList = new VolatileHashSet<Chat>(new TimeSpan(0, 3, 0));
            _pushSignaturesRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));

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
            _pushSectionsRequestList.TrimExcess();
            _pushWikisRequestList.TrimExcess();
            _pushChatsRequestList.TrimExcess();
            _pushSignaturesRequestList.TrimExcess();

            _relayBlocks.TrimExcess();
        }

        public GetSectionsEventHandler GetLockSectionsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockSectionsEvent = value;
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

        protected virtual IEnumerable<Section> OnLockSectionsEvent()
        {
            if (_getLockSignaturesEvent != null)
            {
                return _getLockSectionsEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Wiki> OnLockWikisEvent()
        {
            if (_getLockSignaturesEvent != null)
            {
                return _getLockWikisEvent(this);
            }

            return null;
        }

        protected virtual IEnumerable<Chat> OnLockChatsEvent()
        {
            if (_getLockSignaturesEvent != null)
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

        private static bool Check(Section section)
        {
            return !(section == null
                || section.Name == null
                || section.Id == null || section.Id.Length == 0);
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
                connectionManager.PullHeadersRequestEvent += this.connectionManager_HeadersRequestEvent;
                connectionManager.PullHeadersEvent += this.connectionManager_HeadersEvent;
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

                            var lockSections = this.OnLockSectionsEvent();
                            if (lockSections == null) return;

                            var lockWikis = this.OnLockWikisEvent();
                            if (lockWikis == null) return;

                            var lockChats = this.OnLockChatsEvent();
                            if (lockChats == null) return;

                            {
                                // Section
                                {
                                    var removeSections = new HashSet<Section>();
                                    removeSections.UnionWith(_settings.HeaderManager.GetSections());
                                    removeSections.ExceptWith(lockSections);

                                    var sortList = removeSections.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedSectionTimes.TryGetValue(x, out tx);
                                        _lastUsedSectionTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.HeaderManager.RemoveTags(sortList.Take(sortList.Count - 1024));

                                    var liveSections = new HashSet<Section>(_settings.HeaderManager.GetSections());

                                    foreach (var section in _lastUsedSectionTimes.Keys.ToArray())
                                    {
                                        if (liveSections.Contains(section)) continue;

                                        _lastUsedSectionTimes.Remove(section);
                                    }
                                }

                                // Wiki
                                {
                                    var removeWikis = new HashSet<Wiki>();
                                    removeWikis.UnionWith(_settings.HeaderManager.GetWikis());
                                    removeWikis.ExceptWith(lockWikis);

                                    var sortList = removeWikis.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedWikiTimes.TryGetValue(x, out tx);
                                        _lastUsedWikiTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.HeaderManager.RemoveTags(sortList.Take(sortList.Count - 1024));

                                    var liveWikis = new HashSet<Wiki>(_settings.HeaderManager.GetWikis());

                                    foreach (var section in _lastUsedWikiTimes.Keys.ToArray())
                                    {
                                        if (liveWikis.Contains(section)) continue;

                                        _lastUsedWikiTimes.Remove(section);
                                    }
                                }

                                // Chat
                                {
                                    var removeChats = new HashSet<Chat>();
                                    removeChats.UnionWith(_settings.HeaderManager.GetChats());
                                    removeChats.ExceptWith(lockChats);

                                    var sortList = removeChats.ToList();

                                    sortList.Sort((x, y) =>
                                    {
                                        DateTime tx;
                                        DateTime ty;

                                        _lastUsedChatTimes.TryGetValue(x, out tx);
                                        _lastUsedChatTimes.TryGetValue(y, out ty);

                                        return tx.CompareTo(ty);
                                    });

                                    _settings.HeaderManager.RemoveTags(sortList.Take(sortList.Count - 1024));

                                    var liveChats = new HashSet<Chat>(_settings.HeaderManager.GetChats());

                                    foreach (var section in _lastUsedChatTimes.Keys.ToArray())
                                    {
                                        if (liveChats.Contains(section)) continue;

                                        _lastUsedChatTimes.Remove(section);
                                    }
                                }
                            }

                            {
                                var trustSignature = new HashSet<string>(lockSignatures);

                                {
                                    var now = DateTime.UtcNow;

                                    var removeSectionProfileHeaders = new HashSet<SectionProfileHeader>();
                                    var removeSectionMessageHeaders = new HashSet<SectionMessageHeader>();

                                    foreach (var section in _settings.HeaderManager.GetSections())
                                    {
                                        // SectionProfile
                                        {
                                            var untrustHeaders = new List<SectionProfileHeader>();

                                            foreach (var header in _settings.HeaderManager.GetSectionProfileHeaders(section))
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (!trustSignature.Contains(signature))
                                                {
                                                    untrustHeaders.Add(header);
                                                }
                                            }

                                            removeSectionProfileHeaders.UnionWith(untrustHeaders.Randomize().Skip(32));
                                        }

                                        // SectionMessage
                                        {
                                            var trustHeaders = new Dictionary<string, List<SectionMessageHeader>>();
                                            var untrustHeaders = new Dictionary<string, List<SectionMessageHeader>>();

                                            foreach (var header in _settings.HeaderManager.GetSectionMessageHeaders(section))
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (trustSignature.Contains(signature))
                                                {
                                                    List<SectionMessageHeader> list;

                                                    if (!trustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<SectionMessageHeader>();
                                                        trustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                                else
                                                {
                                                    List<SectionMessageHeader> list;

                                                    if (!untrustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<SectionMessageHeader>();
                                                        untrustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                            }

                                            removeSectionMessageHeaders.UnionWith(untrustHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in trustHeaders.Values.Concat(untrustHeaders.Values))
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

                                                if (tempList.Count <= 32) continue;

                                                tempList.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeSectionMessageHeaders.UnionWith(tempList.Take(tempList.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var header in removeSectionProfileHeaders)
                                    {
                                        _settings.HeaderManager.RemoveHeader(header);
                                    }

                                    foreach (var header in removeSectionMessageHeaders)
                                    {
                                        _settings.HeaderManager.RemoveHeader(header);
                                    }
                                }

                                {
                                    var now = DateTime.UtcNow;

                                    var removeWikiPageHeaders = new HashSet<WikiPageHeader>();

                                    foreach (var wiki in _settings.HeaderManager.GetWikis())
                                    {
                                        // WikiPage
                                        {
                                            var trustHeaders = new Dictionary<string, List<WikiPageHeader>>();
                                            var untrustHeaders = new Dictionary<string, List<WikiPageHeader>>();

                                            foreach (var header in _settings.HeaderManager.GetWikiPageHeaders(wiki))
                                            {
                                                var signature = header.Certificate.ToString();

                                                if (trustSignature.Contains(signature))
                                                {
                                                    List<WikiPageHeader> list;

                                                    if (!trustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<WikiPageHeader>();
                                                        trustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                                else
                                                {
                                                    List<WikiPageHeader> list;

                                                    if (!untrustHeaders.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<WikiPageHeader>();
                                                        untrustHeaders[signature] = list;
                                                    }

                                                    list.Add(header);
                                                }
                                            }

                                            removeWikiPageHeaders.UnionWith(untrustHeaders.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in trustHeaders.Values.Concat(untrustHeaders.Values))
                                            {
                                                if (list.Count <= 32) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeWikiPageHeaders.UnionWith(list.Take(list.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var header in removeWikiPageHeaders)
                                    {
                                        _settings.HeaderManager.RemoveHeader(header);
                                    }
                                }

                                {
                                    var now = DateTime.UtcNow;

                                    var removeChatTopicHeaders = new HashSet<ChatTopicHeader>();
                                    var removeChatMessageHeaders = new HashSet<ChatMessageHeader>();

                                    foreach (var chat in _settings.HeaderManager.GetChats())
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

                                            foreach (var list in trustHeaders.Values.Concat(untrustHeaders.Values))
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

                                                if (tempList.Count <= 32) continue;

                                                tempList.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeChatMessageHeaders.UnionWith(tempList.Take(tempList.Count - 32));
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

                    foreach (var tag in _settings.HeaderManager.GetSections())
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
                                messageManagers[requestNodes[i]].PullSectionsRequest.Add(tag);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var tag in _settings.HeaderManager.GetWikis())
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
                                messageManagers[requestNodes[i]].PullWikisRequest.Add(tag);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var tag in _settings.HeaderManager.GetChats())
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
                                messageManagers[requestNodes[i]].PullChatsRequest.Add(tag);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
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

                    var pushSectionsRequestList = new List<Section>();
                    var pushWikisRequestList = new List<Wiki>();
                    var pushChatsRequestList = new List<Chat>();

                    {
                        {
                            {
                                var array = _pushSectionsRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushSectionsRequest.Contains(array[i])))
                                    {
                                        pushSectionsRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                var array = _pushWikisRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushWikisRequest.Contains(array[i])))
                                    {
                                        pushWikisRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                var array = _pushChatsRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushChatsRequest.Contains(array[i])))
                                    {
                                        pushChatsRequestList.Add(array[i]);

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
                                var array = messageManager.PullSectionsRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushSectionsRequest.Contains(array[i])))
                                    {
                                        pushSectionsRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                var array = messageManager.PullWikisRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushWikisRequest.Contains(array[i])))
                                    {
                                        pushWikisRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }

                            {
                                var array = messageManager.PullChatsRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxHeaderRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    if (!messageManagers.Values.Any(n => n.PushChatsRequest.Contains(array[i])))
                                    {
                                        pushChatsRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }
                        }
                    }

                    _random.Shuffle(pushSectionsRequestList);
                    _random.Shuffle(pushWikisRequestList);
                    _random.Shuffle(pushChatsRequestList);

                    {
                        var pushSectionsRequestDictionary = new Dictionary<Node, HashSet<Section>>();
                        var pushWikisRequestDictionary = new Dictionary<Node, HashSet<Wiki>>();
                        var pushChatsRequestDictionary = new Dictionary<Node, HashSet<Chat>>();

                        foreach (var tag in pushSectionsRequestList)
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
                                    HashSet<Section> collection;

                                    if (!pushSectionsRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Section>();
                                        pushSectionsRequestDictionary[requestNodes[i]] = collection;
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

                        foreach (var tag in pushWikisRequestList)
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

                                    if (!pushWikisRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Wiki>();
                                        pushWikisRequestDictionary[requestNodes[i]] = collection;
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

                        foreach (var tag in pushChatsRequestList)
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

                                    if (!pushChatsRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Chat>();
                                        pushChatsRequestDictionary[requestNodes[i]] = collection;
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

                        lock (_pushSectionsRequestDictionary.ThisLock)
                        {
                            _pushSectionsRequestDictionary.Clear();

                            foreach (var pair in pushSectionsRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushSectionsRequestDictionary.Add(node, new List<Section>(targets.Randomize()));
                            }
                        }

                        lock (_pushWikisRequestDictionary.ThisLock)
                        {
                            _pushWikisRequestDictionary.Clear();

                            foreach (var pair in pushWikisRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushWikisRequestDictionary.Add(node, new List<Wiki>(targets.Randomize()));
                            }
                        }

                        lock (_pushChatsRequestDictionary.ThisLock)
                        {
                            _pushChatsRequestDictionary.Clear();

                            foreach (var pair in pushChatsRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushChatsRequestDictionary.Add(node, new List<Chat>(targets.Randomize()));
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

                    // Check
                    if (messageManager.Priority < 0 && checkTime.Elapsed.TotalSeconds >= 5)
                    {
                        checkTime.Restart();

                        if ((DateTime.UtcNow - messageManager.LastPullTime).TotalMinutes >= 10)
                        {
                            lock (this.ThisLock)
                            {
                                this.RemoveNode(connectionManager.Node);
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

                        // PushHeadersRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            {
                                List<Section> sectionList = null;
                                List<Wiki> wikiList = null;
                                List<Chat> chatList = null;

                                lock (_pushSectionsRequestDictionary.ThisLock)
                                {
                                    if (_pushSectionsRequestDictionary.TryGetValue(connectionManager.Node, out sectionList))
                                    {
                                        _pushSectionsRequestDictionary.Remove(connectionManager.Node);
                                        messageManager.PushSectionsRequest.AddRange(sectionList);
                                    }
                                }

                                lock (_pushWikisRequestDictionary.ThisLock)
                                {
                                    if (_pushWikisRequestDictionary.TryGetValue(connectionManager.Node, out wikiList))
                                    {
                                        _pushWikisRequestDictionary.Remove(connectionManager.Node);
                                        messageManager.PushWikisRequest.AddRange(wikiList);
                                    }
                                }

                                lock (_pushChatsRequestDictionary.ThisLock)
                                {
                                    if (_pushChatsRequestDictionary.TryGetValue(connectionManager.Node, out chatList))
                                    {
                                        _pushChatsRequestDictionary.Remove(connectionManager.Node);
                                        messageManager.PushChatsRequest.AddRange(chatList);
                                    }
                                }

                                if (sectionList != null || wikiList != null || chatList != null)
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

                                        _pushHeaderRequestCount.Add(sectionList.Count);
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

                        // PushHeaders
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var sections = messageManager.PullSectionsRequest.ToArray();
                                var wikis = messageManager.PullWikisRequest.ToArray();
                                var chats = messageManager.PullChatsRequest.ToArray();

                                var sectionProfileHeaders = new List<SectionProfileHeader>();
                                var sectionMessageHeaders = new List<SectionMessageHeader>();
                                var wikiPageHeaders = new List<WikiPageHeader>();
                                var chatTopicHeaders = new List<ChatTopicHeader>();
                                var chatMessageHeaders = new List<ChatMessageHeader>();

                                _random.Shuffle(sections);
                                foreach (var tag in sections)
                                {
                                    foreach (var header in _settings.HeaderManager.GetSectionProfileHeaders(tag))
                                    {
                                        if (!messageManager.StockSectionProfileHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                        {
                                            sectionProfileHeaders.Add(header);

                                            if (sectionProfileHeaders.Count >= _maxHeaderCount) break;
                                        }
                                    }

                                    foreach (var header in _settings.HeaderManager.GetSectionMessageHeaders(tag))
                                    {
                                        if (!messageManager.StockSectionMessageHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                        {
                                            sectionMessageHeaders.Add(header);

                                            if (sectionMessageHeaders.Count >= _maxHeaderCount) break;
                                        }
                                    }

                                    if (sectionProfileHeaders.Count >= _maxHeaderCount) break;
                                    if (sectionMessageHeaders.Count >= _maxHeaderCount) break;
                                }

                                _random.Shuffle(wikis);
                                foreach (var tag in wikis)
                                {
                                    foreach (var header in _settings.HeaderManager.GetWikiPageHeaders(tag))
                                    {
                                        if (!messageManager.StockWikiPageHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                        {
                                            wikiPageHeaders.Add(header);

                                            if (wikiPageHeaders.Count >= _maxHeaderCount) break;
                                        }
                                    }

                                    if (wikiPageHeaders.Count >= _maxHeaderCount) break;
                                }

                                _random.Shuffle(chats);
                                foreach (var tag in chats)
                                {
                                    foreach (var header in _settings.HeaderManager.GetChatTopicHeaders(tag))
                                    {
                                        if (!messageManager.StockChatTopicHeaders.Contains(header.GetHash(_hashAlgorithm)))
                                        {
                                            chatTopicHeaders.Add(header);

                                            if (chatTopicHeaders.Count >= _maxHeaderCount) break;
                                        }
                                    }

                                    foreach (var header in _settings.HeaderManager.GetChatMessageHeaders(tag))
                                    {
                                        if (!messageManager.StockChatMessageHeaders.Contains(header.GetHash(_hashAlgorithm)))
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
                                    || chatTopicHeaders.Count > 0
                                    || chatMessageHeaders.Count > 0)
                                {
                                    connectionManager.PushHeaders(
                                        sectionProfileHeaders,
                                        sectionMessageHeaders,
                                        wikiPageHeaders,
                                        chatTopicHeaders,
                                        chatMessageHeaders);

                                    var headerCount =
                                        sectionProfileHeaders.Count
                                        + sectionMessageHeaders.Count
                                        + wikiPageHeaders.Count
                                        + chatTopicHeaders.Count
                                        + chatMessageHeaders.Count;

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Headers ({0})", headerCount));
                                    _pushHeaderCount.Add(headerCount);

                                    foreach (var header in sectionProfileHeaders)
                                    {
                                        messageManager.StockSectionProfileHeaders.Add(header.GetHash(_hashAlgorithm));
                                    }

                                    foreach (var header in sectionMessageHeaders)
                                    {
                                        messageManager.StockSectionMessageHeaders.Add(header.GetHash(_hashAlgorithm));
                                    }

                                    foreach (var header in wikiPageHeaders)
                                    {
                                        messageManager.StockWikiPageHeaders.Add(header.GetHash(_hashAlgorithm));
                                    }

                                    foreach (var header in chatTopicHeaders)
                                    {
                                        messageManager.StockChatTopicHeaders.Add(header.GetHash(_hashAlgorithm));
                                    }

                                    foreach (var header in chatMessageHeaders)
                                    {
                                        messageManager.StockChatMessageHeaders.Add(header.GetHash(_hashAlgorithm));
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
                _pullHeaderRequestCount.Increment();

                _lastUsedSectionTimes[tag] = DateTime.UtcNow;
            }

            foreach (var tag in e.Wikis.Take(_maxHeaderRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullWikisRequest.Add(tag);
                _pullHeaderRequestCount.Increment();

                _lastUsedWikiTimes[tag] = DateTime.UtcNow;
            }

            foreach (var tag in e.Chats.Take(_maxHeaderRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullChatsRequest.Add(tag);
                _pullHeaderRequestCount.Increment();

                _lastUsedChatTimes[tag] = DateTime.UtcNow;
            }
        }

        private void connectionManager_HeadersEvent(object sender, PullHeadersEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockSectionProfileHeaders.Count > _maxHeaderCount * messageManager.StockSectionProfileHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockSectionMessageHeaders.Count > _maxHeaderCount * messageManager.StockSectionMessageHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockWikiPageHeaders.Count > _maxHeaderCount * messageManager.StockWikiPageHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockChatTopicHeaders.Count > _maxHeaderCount * messageManager.StockChatTopicHeaders.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockChatMessageHeaders.Count > _maxHeaderCount * messageManager.StockChatMessageHeaders.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Headers ({0})",
                e.SectionProfileHeaders.Count()
                + e.SectionMessageHeaders.Count()
                + e.WikiPageHeaders.Count()
                + e.ChatTopicHeaders.Count()
                + e.ChatMessageHeaders.Count()));

            foreach (var header in e.SectionProfileHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockSectionProfileHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedSectionTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
            }

            foreach (var header in e.SectionMessageHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockSectionMessageHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedSectionTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
            }

            foreach (var header in e.WikiPageHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockWikiPageHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedWikiTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
            }

            foreach (var header in e.ChatTopicHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockChatTopicHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedChatTimes[header.Tag] = DateTime.UtcNow;
                }

                _pullHeaderCount.Increment();
            }

            foreach (var header in e.ChatMessageHeaders.Take(_maxHeaderCount))
            {
                if (_settings.HeaderManager.SetHeader(header))
                {
                    messageManager.StockChatMessageHeaders.Add(header.GetHash(_hashAlgorithm));

                    _lastUsedChatTimes[header.Tag] = DateTime.UtcNow;
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
            if (ConnectionsManager.Check(baseNode)) throw new ArgumentException("baseNode");

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

        public void SendHeadersRequest(Section tag)
        {
            lock (this.ThisLock)
            {
                _pushSectionsRequestList.Add(tag);
            }
        }

        public void SendHeadersRequest(Wiki tag)
        {
            lock (this.ThisLock)
            {
                _pushWikisRequestList.Add(tag);
            }
        }

        public void SendHeadersRequest(Chat tag)
        {
            lock (this.ThisLock)
            {
                _pushChatsRequestList.Add(tag);
            }
        }

        public IEnumerable<SectionProfileHeader> GetSectionProfileHeaders(Section tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushSectionsRequestList.Add(tag);

                return _settings.HeaderManager.GetSectionProfileHeaders(tag);
            }
        }

        public IEnumerable<SectionMessageHeader> GetSectionMessageHeaders(Section tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushSectionsRequestList.Add(tag);

                return _settings.HeaderManager.GetSectionMessageHeaders(tag);
            }
        }

        public IEnumerable<WikiPageHeader> GetWikiPageHeaders(Wiki tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _settings.HeaderManager.GetWikiPageHeaders(tag);
            }
        }

        public IEnumerable<ChatTopicHeader> GetChatTopicHeaders(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushChatsRequestList.Add(tag);

                return _settings.HeaderManager.GetChatTopicHeaders(tag);
            }
        }

        public IEnumerable<ChatMessageHeader> GetChatMessageHeaders(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushChatsRequestList.Add(tag);

                return _settings.HeaderManager.GetChatMessageHeaders(tag);
            }
        }

        public void Upload(SectionProfileHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.HeaderManager.SetHeader(header);
            }
        }

        public void Upload(SectionMessageHeader header)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.HeaderManager.SetHeader(header);
            }
        }

        public void Upload(WikiPageHeader header)
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

                lock (this.ThisLock)
                {
                    return _state;
                }
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
                    new Library.Configuration.SettingContent<List<SectionProfileHeader>>() { Name = "SectionProfileHeaders", Value = new List<SectionProfileHeader>() },
                    new Library.Configuration.SettingContent<List<SectionMessageHeader>>() { Name = "SectionMessageHeaders", Value = new List<SectionMessageHeader>() },
                    new Library.Configuration.SettingContent<List<WikiPageHeader>>() { Name = "WikiPageHeaders", Value = new List<WikiPageHeader>() },
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

                    foreach (var header in this.SectionProfileHeaders)
                    {
                        _headerManager.SetHeader(header);
                    }

                    foreach (var header in this.SectionMessageHeaders)
                    {
                        _headerManager.SetHeader(header);
                    }

                    foreach (var header in this.WikiPageHeaders)
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

                    this.SectionProfileHeaders.Clear();
                    this.SectionMessageHeaders.Clear();
                    this.WikiPageHeaders.Clear();
                    this.ChatTopicHeaders.Clear();
                    this.ChatMessageHeaders.Clear();
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    this.SectionProfileHeaders.AddRange(_headerManager.GetSectionProfileHeaders());
                    this.SectionMessageHeaders.AddRange(_headerManager.GetSectionMessageHeaders());
                    this.WikiPageHeaders.AddRange(_headerManager.GetWikiPageHeaders());
                    this.ChatTopicHeaders.AddRange(_headerManager.GetChatTopicHeaders());
                    this.ChatMessageHeaders.AddRange(_headerManager.GetChatMessageHeaders());

                    base.Save(directoryPath);

                    this.SectionProfileHeaders.Clear();
                    this.SectionMessageHeaders.Clear();
                    this.WikiPageHeaders.Clear();
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

            private List<SectionProfileHeader> SectionProfileHeaders
            {
                get
                {
                    return (List<SectionProfileHeader>)this["SectionProfileHeaders"];
                }
            }

            private List<SectionMessageHeader> SectionMessageHeaders
            {
                get
                {
                    return (List<SectionMessageHeader>)this["SectionMessageHeaders"];
                }
            }

            private List<WikiPageHeader> WikiPageHeaders
            {
                get
                {
                    return (List<WikiPageHeader>)this["WikiPageHeaders"];
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
            private Dictionary<Section, Dictionary<string, SectionProfileHeader>> _sectionProfileHeaders = new Dictionary<Section, Dictionary<string, SectionProfileHeader>>();
            private Dictionary<Section, Dictionary<string, HashSet<SectionMessageHeader>>> _sectionMessageHeaders = new Dictionary<Section, Dictionary<string, HashSet<SectionMessageHeader>>>();
            private Dictionary<Wiki, Dictionary<string, HashSet<WikiPageHeader>>> _wikiPageHeaders = new Dictionary<Wiki, Dictionary<string, HashSet<WikiPageHeader>>>();
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
                    return _sectionMessageHeaders.Values.SelectMany(n => n.Values.Extract());
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
