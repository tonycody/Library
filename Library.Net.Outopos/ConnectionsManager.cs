using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Library.Collections;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Outopos
{
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
        private readonly SafeInteger _pushMetadataRequestCount = new SafeInteger();
        private readonly SafeInteger _pushMetadataCount = new SafeInteger();

        private readonly SafeInteger _pullNodeCount = new SafeInteger();
        private readonly SafeInteger _pullBlockLinkCount = new SafeInteger();
        private readonly SafeInteger _pullBlockRequestCount = new SafeInteger();
        private readonly SafeInteger _pullBlockCount = new SafeInteger();
        private readonly SafeInteger _pullMetadataRequestCount = new SafeInteger();
        private readonly SafeInteger _pullMetadataCount = new SafeInteger();

        private VolatileHashSet<Key> _relayBlocks;
        private readonly SafeInteger _relayBlockCount = new SafeInteger();

        private readonly SafeInteger _connectConnectionCount = new SafeInteger();
        private readonly SafeInteger _acceptConnectionCount = new SafeInteger();

        private GetWikisEventHandler _getLockWikisEvent;
        private GetChatsEventHandler _getLockChatsEvent;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxNodeCount = 128;
        private const int _maxBlockLinkCount = 8192;
        private const int _maxBlockRequestCount = 8192;
        private const int _maxMetadataRequestCount = 1024;
        private const int _maxMetadataCount = 1024;

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

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.TrustSignatures.ToArray();
                }
            }
        }

        public void SetTrustSignatures(IEnumerable<string> signatures)
        {
            lock (this.ThisLock)
            {
                lock (_settings.TrustSignatures.ThisLock)
                {
                    _settings.TrustSignatures.Clear();

                    foreach (var signature in signatures)
                    {
                        if (signature == null || !Signature.Check(signature)) continue;

                        _settings.TrustSignatures.Add(signature);
                    }
                }
            }
        }

        public bool ContainsTrustSignature(string signature)
        {
            lock (this.ThisLock)
            {
                lock (_settings.TrustSignatures.ThisLock)
                {
                    return _settings.TrustSignatures.Contains(signature);
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
                    contexts.Add(new InformationContext("PushMetadataRequestCount", (long)_pushMetadataRequestCount));
                    contexts.Add(new InformationContext("PushMetadataCount", (long)_pushMetadataCount));

                    contexts.Add(new InformationContext("PullNodeCount", (long)_pullNodeCount));
                    contexts.Add(new InformationContext("PullBlockLinkCount", (long)_pullBlockLinkCount));
                    contexts.Add(new InformationContext("PullBlockRequestCount", (long)_pullBlockRequestCount));
                    contexts.Add(new InformationContext("PullBlockCount", (long)_pullBlockCount));
                    contexts.Add(new InformationContext("PullMetadataRequestCount", (long)_pullMetadataRequestCount));
                    contexts.Add(new InformationContext("PullMetadataCount", (long)_pullMetadataCount));

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

                    contexts.Add(new InformationContext("MetadataCount", _settings.MetadataManager.Count));

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
                connectionManager.PullBroadcastMetadatasRequestEvent += this.connectionManager_PullBroadcastMetadatasRequestEvent;
                connectionManager.PullBroadcastMetadatasEvent += this.connectionManager_PullBroadcastMetadatasEvent;
                connectionManager.PullUnicastMetadatasRequestEvent += this.connectionManager_PullUnicastMetadatasRequestEvent;
                connectionManager.PullUnicastMetadatasEvent += this.connectionManager_PullUnicastMetadatasEvent;
                connectionManager.PullMulticastMetadatasRequestEvent += this.connectionManager_PullMulticastMetadatasRequestEvent;
                connectionManager.PullMulticastMetadatasEvent += this.connectionManager_PullMulticastMetadatasEvent;
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

            Stopwatch pushMetadataUploadStopwatch = new Stopwatch();
            pushMetadataUploadStopwatch.Start();
            Stopwatch pushMetadataDownloadStopwatch = new Stopwatch();
            pushMetadataDownloadStopwatch.Start();

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

                    // トラストにより必要なMetadataを選択し、不要なMetadataを削除する。
                    ThreadPool.QueueUserWorkItem((object wstate) =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            var trustSignature = new HashSet<string>(this.TrustSignatures);

                            var lockWikis = this.OnLockWikisEvent();
                            if (lockWikis == null) return;

                            var lockChats = this.OnLockChatsEvent();
                            if (lockChats == null) return;

                            // Broadcast
                            {
                                // Signature
                                {
                                    var removeSignatures = new SortedSet<string>();
                                    removeSignatures.UnionWith(_settings.MetadataManager.GetBroadcastSignatures());
                                    removeSignatures.ExceptWith(trustSignature);

                                    var sortList = removeSignatures
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _broadcastSignatureLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.MetadataManager.RemoveProfileSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new SortedSet<string>(_settings.MetadataManager.GetBroadcastSignatures());

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
                                    removeSignatures.UnionWith(_settings.MetadataManager.GetUnicastSignatures());
                                    removeSignatures.ExceptWith(trustSignature);

                                    var sortList = removeSignatures
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _unicastSignatureLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.MetadataManager.RemoveUnicastSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new SortedSet<string>(_settings.MetadataManager.GetUnicastSignatures());

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
                                    removeWikis.UnionWith(_settings.MetadataManager.GetMulticastWikis());
                                    removeWikis.ExceptWith(lockWikis);

                                    var sortList = removeWikis
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _multicastWikiLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.MetadataManager.RemoveMulticastTags(sortList.Take(sortList.Count - 1024));

                                    var liveWikis = new HashSet<Wiki>(_settings.MetadataManager.GetMulticastWikis());

                                    foreach (var section in _multicastWikiLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveWikis.Contains(section)) continue;

                                        _multicastWikiLastAccessTimes.Remove(section);
                                    }
                                }

                                // Chat
                                {
                                    var removeChats = new HashSet<Chat>();
                                    removeChats.UnionWith(_settings.MetadataManager.GetMulticastChats());
                                    removeChats.ExceptWith(lockChats);

                                    var sortList = removeChats
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _multicastChatLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.MetadataManager.RemoveMulticastTags(sortList.Take(sortList.Count - 1024));

                                    var liveChats = new HashSet<Chat>(_settings.MetadataManager.GetMulticastChats());

                                    foreach (var section in _multicastChatLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveChats.Contains(section)) continue;

                                        _multicastChatLastAccessTimes.Remove(section);
                                    }
                                }
                            }

                            // Unicast
                            {
                                {
                                    var now = DateTime.UtcNow;

                                    var removeSignatureMessageMetadatas = new HashSet<SignatureMessageMetadata>();

                                    foreach (var targetSignature in _settings.MetadataManager.GetUnicastSignatures())
                                    {
                                        // SignatureMessage
                                        {
                                            var trustMetadatas = new Dictionary<string, List<SignatureMessageMetadata>>();
                                            var untrustMetadatas = new Dictionary<string, List<SignatureMessageMetadata>>();

                                            foreach (var metadata in _settings.MetadataManager.GetSignatureMessageMetadatas(targetSignature))
                                            {
                                                var signature = metadata.Certificate.ToString();

                                                if (trustSignature.Contains(signature))
                                                {
                                                    List<SignatureMessageMetadata> list;

                                                    if (!trustMetadatas.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<SignatureMessageMetadata>();
                                                        trustMetadatas[signature] = list;
                                                    }

                                                    list.Add(metadata);
                                                }
                                                else
                                                {
                                                    List<SignatureMessageMetadata> list;

                                                    if (!untrustMetadatas.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<SignatureMessageMetadata>();
                                                        untrustMetadatas[signature] = list;
                                                    }

                                                    list.Add(metadata);
                                                }
                                            }

                                            removeSignatureMessageMetadatas.UnionWith(untrustMetadatas.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in CollectionUtilities.Unite(trustMetadatas.Values, untrustMetadatas.Values))
                                            {
                                                if (list.Count <= 32) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeSignatureMessageMetadatas.UnionWith(list.Take(list.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var metadata in removeSignatureMessageMetadatas)
                                    {
                                        _settings.MetadataManager.RemoveMetadata(metadata);
                                    }
                                }
                            }

                            // Multicast
                            {
                                {
                                    var now = DateTime.UtcNow;

                                    var removeWikiDocumentMetadatas = new HashSet<WikiDocumentMetadata>();

                                    foreach (var wiki in _settings.MetadataManager.GetMulticastWikis())
                                    {
                                        // WikiDocument
                                        {
                                            var trustMetadatas = new Dictionary<string, List<WikiDocumentMetadata>>();
                                            var untrustMetadatas = new Dictionary<string, List<WikiDocumentMetadata>>();

                                            foreach (var metadata in _settings.MetadataManager.GetWikiDocumentMetadatas(wiki))
                                            {
                                                var signature = metadata.Certificate.ToString();

                                                if (trustSignature.Contains(signature))
                                                {
                                                    List<WikiDocumentMetadata> list;

                                                    if (!trustMetadatas.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<WikiDocumentMetadata>();
                                                        trustMetadatas[signature] = list;
                                                    }

                                                    list.Add(metadata);
                                                }
                                                else
                                                {
                                                    List<WikiDocumentMetadata> list;

                                                    if (!untrustMetadatas.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<WikiDocumentMetadata>();
                                                        untrustMetadatas[signature] = list;
                                                    }

                                                    list.Add(metadata);
                                                }
                                            }

                                            removeWikiDocumentMetadatas.UnionWith(untrustMetadatas.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in CollectionUtilities.Unite(trustMetadatas.Values, untrustMetadatas.Values))
                                            {
                                                if (list.Count <= 32) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeWikiDocumentMetadatas.UnionWith(list.Take(list.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var metadata in removeWikiDocumentMetadatas)
                                    {
                                        _settings.MetadataManager.RemoveMetadata(metadata);
                                    }
                                }

                                {
                                    var now = DateTime.UtcNow;

                                    var removeChatTopicMetadatas = new HashSet<ChatTopicMetadata>();
                                    var removeChatMessageMetadatas = new HashSet<ChatMessageMetadata>();

                                    foreach (var chat in _settings.MetadataManager.GetMulticastChats())
                                    {
                                        // ChatTopic
                                        {
                                            var untrustMetadatas = new List<ChatTopicMetadata>();

                                            foreach (var metadata in _settings.MetadataManager.GetChatTopicMetadatas(chat))
                                            {
                                                var signature = metadata.Certificate.ToString();

                                                if (!trustSignature.Contains(signature))
                                                {
                                                    untrustMetadatas.Add(metadata);
                                                }
                                            }

                                            removeChatTopicMetadatas.UnionWith(untrustMetadatas.Randomize().Skip(32));
                                        }

                                        // ChatMessage
                                        {
                                            var trustMetadatas = new Dictionary<string, List<ChatMessageMetadata>>();
                                            var untrustMetadatas = new Dictionary<string, List<ChatMessageMetadata>>();

                                            foreach (var metadata in _settings.MetadataManager.GetChatMessageMetadatas(chat))
                                            {
                                                var signature = metadata.Certificate.ToString();

                                                if (trustSignature.Contains(signature))
                                                {
                                                    List<ChatMessageMetadata> list;

                                                    if (!trustMetadatas.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<ChatMessageMetadata>();
                                                        trustMetadatas[signature] = list;
                                                    }

                                                    list.Add(metadata);
                                                }
                                                else
                                                {
                                                    List<ChatMessageMetadata> list;

                                                    if (!untrustMetadatas.TryGetValue(signature, out list))
                                                    {
                                                        list = new List<ChatMessageMetadata>();
                                                        untrustMetadatas[signature] = list;
                                                    }

                                                    list.Add(metadata);
                                                }
                                            }

                                            removeChatMessageMetadatas.UnionWith(untrustMetadatas.Randomize().Skip(32).SelectMany(n => n.Value));

                                            foreach (var list in CollectionUtilities.Unite(trustMetadatas.Values, untrustMetadatas.Values))
                                            {
                                                if (list.Count <= 32) continue;

                                                list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                                removeChatMessageMetadatas.UnionWith(list.Take(list.Count - 32));
                                            }
                                        }
                                    }

                                    foreach (var metadata in removeChatTopicMetadatas)
                                    {
                                        _settings.MetadataManager.RemoveMetadata(metadata);
                                    }

                                    foreach (var metadata in removeChatMessageMetadatas)
                                    {
                                        _settings.MetadataManager.RemoveMetadata(metadata);
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

                // Metadataのアップロード
                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushMetadataUploadStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    pushMetadataUploadStopwatch.Restart();

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
                    foreach (var signature in _settings.MetadataManager.GetBroadcastSignatures())
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
                    foreach (var signature in _settings.MetadataManager.GetUnicastSignatures())
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
                        foreach (var tag in _settings.MetadataManager.GetMulticastWikis())
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

                        foreach (var tag in _settings.MetadataManager.GetMulticastChats())
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

                // Metadataのダウンロード
                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushMetadataDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushMetadataDownloadStopwatch.Restart();

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

                            int count = _maxMetadataRequestCount;

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

                                int count = _maxMetadataRequestCount;

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

                            int count = _maxMetadataRequestCount;

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

                                int count = _maxMetadataRequestCount;

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

                                int count = _maxMetadataRequestCount;

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

                                int count = _maxMetadataRequestCount;

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

                                int count = _maxMetadataRequestCount;

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

                                int count = _maxMetadataRequestCount;

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

                                    if (collection.Count < _maxMetadataRequestCount)
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

                                    if (collection.Count < _maxMetadataRequestCount)
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

                                    if (collection.Count < _maxMetadataRequestCount)
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

                                    if (collection.Count < _maxMetadataRequestCount)
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
                Stopwatch metadataUpdateTime = new Stopwatch();
                metadataUpdateTime.Start();

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

                        // PushBroadcastMetadatasRequest
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
                                    connectionManager.PushBroadcastMetadatasRequest(targetList);

                                    foreach (var item in targetList)
                                    {
                                        _pushBroadcastSignaturesRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BroadcastMetadatasRequest ({0})", targetList.Count));
                                    _pushMetadataRequestCount.Add(targetList.Count);
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

                        // PushUnicastMetadatasRequest
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
                                    connectionManager.PushUnicastMetadatasRequest(targetList);

                                    foreach (var item in targetList)
                                    {
                                        _pushUnicastSignaturesRequestList.Remove(item);
                                    }

                                    Debug.WriteLine(string.Format("ConnectionManager: Push UnicastMetadatasRequest ({0})", targetList.Count));
                                    _pushMetadataRequestCount.Add(targetList.Count);
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

                        // PushMulticastMetadatasRequest
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
                                        connectionManager.PushMulticastMetadatasRequest(wikiList, chatList);

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

                                        Debug.WriteLine(string.Format("ConnectionManager: Push MulticastMetadatasRequest ({0})", tagCount));
                                        _pushMetadataRequestCount.Add(tagCount);
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

                    if (metadataUpdateTime.Elapsed.TotalSeconds >= 60)
                    {
                        metadataUpdateTime.Restart();

                        // PushBroadcastMetadatas
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var signatures = messageManager.PullBroadcastSignaturesRequest.ToArray();

                                var profileMetadatas = new List<ProfileMetadata>();

                                _random.Shuffle(signatures);
                                foreach (var signature in signatures)
                                {
                                    var metadata = _settings.MetadataManager.GetProfileMetadata(signature);
                                    if (metadata == null) continue;

                                    if (!messageManager.StockProfileMetadatas.Contains(metadata.CreateHash(_hashAlgorithm)))
                                    {
                                        profileMetadatas.Add(metadata);

                                        if (profileMetadatas.Count >= _maxMetadataCount) break;
                                    }

                                    if (profileMetadatas.Count >= _maxMetadataCount) break;
                                }

                                if (profileMetadatas.Count > 0)
                                {
                                    connectionManager.PushBroadcastMetadatas(
                                        profileMetadatas);

                                    var metadataCount =
                                        profileMetadatas.Count;

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BroadcastMetadatas ({0})", metadataCount));
                                    _pushMetadataCount.Add(metadataCount);

                                    foreach (var metadata in profileMetadatas)
                                    {
                                        messageManager.StockProfileMetadatas.Add(metadata.CreateHash(_hashAlgorithm));
                                    }
                                }
                            }
                        }

                        // PushUnicastMetadatas
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var signatures = messageManager.PullUnicastSignaturesRequest.ToArray();

                                var signatureMessageMetadatas = new List<SignatureMessageMetadata>();

                                _random.Shuffle(signatures);
                                foreach (var signature in signatures)
                                {
                                    foreach (var metadata in _settings.MetadataManager.GetSignatureMessageMetadatas(signature))
                                    {
                                        if (!messageManager.StockSignatureMessageMetadatas.Contains(metadata.CreateHash(_hashAlgorithm)))
                                        {
                                            signatureMessageMetadatas.Add(metadata);

                                            if (signatureMessageMetadatas.Count >= _maxMetadataCount) break;
                                        }
                                    }

                                    if (signatureMessageMetadatas.Count >= _maxMetadataCount) break;
                                }

                                if (signatureMessageMetadatas.Count > 0)
                                {
                                    connectionManager.PushUnicastMetadatas(
                                        signatureMessageMetadatas);

                                    var metadataCount =
                                        signatureMessageMetadatas.Count;

                                    Debug.WriteLine(string.Format("ConnectionManager: Push UnicastMetadatas ({0})", metadataCount));
                                    _pushMetadataCount.Add(metadataCount);

                                    foreach (var metadata in signatureMessageMetadatas)
                                    {
                                        messageManager.StockSignatureMessageMetadatas.Add(metadata.CreateHash(_hashAlgorithm));
                                    }
                                }
                            }
                        }

                        // PushMulticastMetadatas
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var wikis = messageManager.PullMulticastWikisRequest.ToArray();
                                var chats = messageManager.PullMulticastChatsRequest.ToArray();

                                var wikiDocumentMetadatas = new List<WikiDocumentMetadata>();
                                var chatTopicMetadatas = new List<ChatTopicMetadata>();
                                var chatMessageMetadatas = new List<ChatMessageMetadata>();

                                _random.Shuffle(wikis);
                                foreach (var tag in wikis)
                                {
                                    foreach (var metadata in _settings.MetadataManager.GetWikiDocumentMetadatas(tag))
                                    {
                                        if (!messageManager.StockWikiDocumentMetadatas.Contains(metadata.CreateHash(_hashAlgorithm)))
                                        {
                                            wikiDocumentMetadatas.Add(metadata);

                                            if (wikiDocumentMetadatas.Count >= _maxMetadataCount) break;
                                        }
                                    }

                                    if (wikiDocumentMetadatas.Count >= _maxMetadataCount) break;
                                }

                                _random.Shuffle(chats);
                                foreach (var tag in chats)
                                {
                                    foreach (var metadata in _settings.MetadataManager.GetChatTopicMetadatas(tag))
                                    {
                                        if (!messageManager.StockChatTopicMetadatas.Contains(metadata.CreateHash(_hashAlgorithm)))
                                        {
                                            chatTopicMetadatas.Add(metadata);

                                            if (chatTopicMetadatas.Count >= _maxMetadataCount) break;
                                        }
                                    }

                                    foreach (var metadata in _settings.MetadataManager.GetChatMessageMetadatas(tag))
                                    {
                                        if (!messageManager.StockChatMessageMetadatas.Contains(metadata.CreateHash(_hashAlgorithm)))
                                        {
                                            chatMessageMetadatas.Add(metadata);

                                            if (chatMessageMetadatas.Count >= _maxMetadataCount) break;
                                        }
                                    }

                                    if (chatTopicMetadatas.Count >= _maxMetadataCount) break;
                                    if (chatMessageMetadatas.Count >= _maxMetadataCount) break;
                                }

                                if (wikiDocumentMetadatas.Count > 0
                                    || chatTopicMetadatas.Count > 0
                                    || chatMessageMetadatas.Count > 0)
                                {
                                    connectionManager.PushMulticastMetadatas(
                                        wikiDocumentMetadatas,
                                        chatTopicMetadatas,
                                        chatMessageMetadatas);

                                    var metadataCount =
                                        wikiDocumentMetadatas.Count
                                        + chatTopicMetadatas.Count
                                        + chatMessageMetadatas.Count;

                                    Debug.WriteLine(string.Format("ConnectionManager: Push MulticastMetadatas ({0})", metadataCount));
                                    _pushMetadataCount.Add(metadataCount);

                                    foreach (var metadata in wikiDocumentMetadatas)
                                    {
                                        messageManager.StockWikiDocumentMetadatas.Add(metadata.CreateHash(_hashAlgorithm));
                                    }

                                    foreach (var metadata in chatTopicMetadatas)
                                    {
                                        messageManager.StockChatTopicMetadatas.Add(metadata.CreateHash(_hashAlgorithm));
                                    }

                                    foreach (var metadata in chatMessageMetadatas)
                                    {
                                        messageManager.StockChatMessageMetadatas.Add(metadata.CreateHash(_hashAlgorithm));
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

        private void connectionManager_PullBroadcastMetadatasRequestEvent(object sender, PullBroadcastMetadatasRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullBroadcastSignaturesRequest.Count > _maxMetadataRequestCount * messageManager.PullBroadcastSignaturesRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BroadcastMetadatasRequest ({0})",
                e.Signatures.Count()));

            foreach (var signature in e.Signatures.Take(_maxMetadataRequestCount))
            {
                if (!Signature.Check(signature)) continue;

                messageManager.PullBroadcastSignaturesRequest.Add(signature);
                _pullMetadataRequestCount.Increment();

                _broadcastSignatureLastAccessTimes[signature] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullBroadcastMetadatasEvent(object sender, PullBroadcastMetadatasEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockProfileMetadatas.Count > _maxMetadataCount * messageManager.StockProfileMetadatas.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BroadcastMetadatas ({0})",
                e.ProfileMetadatas.Count()));

            foreach (var metadata in e.ProfileMetadatas.Take(_maxMetadataCount))
            {
                if (_settings.MetadataManager.SetMetadata(metadata))
                {
                    messageManager.StockProfileMetadatas.Add(metadata.CreateHash(_hashAlgorithm));

                    var signature = metadata.Certificate.ToString();

                    _broadcastSignatureLastAccessTimes[signature] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
            }
        }

        private void connectionManager_PullUnicastMetadatasRequestEvent(object sender, PullUnicastMetadatasRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullUnicastSignaturesRequest.Count > _maxMetadataRequestCount * messageManager.PullUnicastSignaturesRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull UnicastMetadatasRequest ({0})",
                e.Signatures.Count()));

            foreach (var signature in e.Signatures.Take(_maxMetadataRequestCount))
            {
                if (!Signature.Check(signature)) continue;

                messageManager.PullUnicastSignaturesRequest.Add(signature);
                _pullMetadataRequestCount.Increment();

                _unicastSignatureLastAccessTimes[signature] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullUnicastMetadatasEvent(object sender, PullUnicastMetadatasEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockSignatureMessageMetadatas.Count > _maxMetadataCount * messageManager.StockSignatureMessageMetadatas.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull UnicastMetadatas ({0})",
                e.SignatureMessageMetadatas.Count()));

            foreach (var metadata in e.SignatureMessageMetadatas.Take(_maxMetadataCount))
            {
                if (_settings.MetadataManager.SetMetadata(metadata))
                {
                    messageManager.StockSignatureMessageMetadatas.Add(metadata.CreateHash(_hashAlgorithm));

                    _unicastSignatureLastAccessTimes[metadata.Signature] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
            }
        }

        private void connectionManager_PullMulticastMetadatasRequestEvent(object sender, PullMulticastMetadatasRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullMulticastWikisRequest.Count > _maxMetadataRequestCount * messageManager.PullMulticastWikisRequest.SurvivalTime.TotalMinutes) return;
            if (messageManager.PullMulticastChatsRequest.Count > _maxMetadataRequestCount * messageManager.PullMulticastChatsRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull MulticastMetadatasRequest ({0})",
                e.Wikis.Count()
                + e.Chats.Count()));

            foreach (var tag in e.Wikis.Take(_maxMetadataRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullMulticastWikisRequest.Add(tag);
                _pullMetadataRequestCount.Increment();

                _multicastWikiLastAccessTimes[tag] = DateTime.UtcNow;
            }

            foreach (var tag in e.Chats.Take(_maxMetadataRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullMulticastChatsRequest.Add(tag);
                _pullMetadataRequestCount.Increment();

                _multicastChatLastAccessTimes[tag] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullMulticastMetadatasEvent(object sender, PullMulticastMetadatasEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockWikiDocumentMetadatas.Count > _maxMetadataCount * messageManager.StockWikiDocumentMetadatas.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockChatTopicMetadatas.Count > _maxMetadataCount * messageManager.StockChatTopicMetadatas.SurvivalTime.TotalMinutes) return;
            if (messageManager.StockChatMessageMetadatas.Count > _maxMetadataCount * messageManager.StockChatMessageMetadatas.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull MulticastMetadatas ({0})",
                e.WikiDocumentMetadatas.Count()
                + e.ChatTopicMetadatas.Count()
                + e.ChatMessageMetadatas.Count()));

            foreach (var metadata in e.WikiDocumentMetadatas.Take(_maxMetadataCount))
            {
                if (_settings.MetadataManager.SetMetadata(metadata))
                {
                    messageManager.StockWikiDocumentMetadatas.Add(metadata.CreateHash(_hashAlgorithm));

                    _multicastWikiLastAccessTimes[metadata.Tag] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
            }

            foreach (var metadata in e.ChatTopicMetadatas.Take(_maxMetadataCount))
            {
                if (_settings.MetadataManager.SetMetadata(metadata))
                {
                    messageManager.StockChatTopicMetadatas.Add(metadata.CreateHash(_hashAlgorithm));

                    _multicastChatLastAccessTimes[metadata.Tag] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
            }

            foreach (var metadata in e.ChatMessageMetadatas.Take(_maxMetadataCount))
            {
                if (_settings.MetadataManager.SetMetadata(metadata))
                {
                    messageManager.StockChatMessageMetadatas.Add(metadata.CreateHash(_hashAlgorithm));

                    _multicastChatLastAccessTimes[metadata.Tag] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
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

        public ProfileMetadata GetProfileMetadata(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushBroadcastSignaturesRequestList.Add(signature);

                return _settings.MetadataManager.GetProfileMetadata(signature);
            }
        }

        public IEnumerable<SignatureMessageMetadata> GetSignatureMessageMetadatas(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushUnicastSignaturesRequestList.Add(signature);

                return _settings.MetadataManager.GetSignatureMessageMetadatas(signature);
            }
        }

        public IEnumerable<WikiDocumentMetadata> GetWikiDocumentMetadatas(Wiki tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushMulticastWikisRequestList.Add(tag);

                return _settings.MetadataManager.GetWikiDocumentMetadatas(tag);
            }
        }

        public IEnumerable<ChatTopicMetadata> GetChatTopicMetadatas(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushMulticastChatsRequestList.Add(tag);

                return _settings.MetadataManager.GetChatTopicMetadatas(tag);
            }
        }

        public IEnumerable<ChatMessageMetadata> GetChatMessageMetadatas(Chat tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushMulticastChatsRequestList.Add(tag);

                return _settings.MetadataManager.GetChatMessageMetadatas(tag);
            }
        }

        public void Upload(ProfileMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.MetadataManager.SetMetadata(metadata);
            }
        }

        public void Upload(SignatureMessageMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.MetadataManager.SetMetadata(metadata);
            }
        }

        public void Upload(WikiDocumentMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.MetadataManager.SetMetadata(metadata);
            }
        }

        public void Upload(ChatTopicMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.MetadataManager.SetMetadata(metadata);
            }
        }

        public void Upload(ChatMessageMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.MetadataManager.SetMetadata(metadata);
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

            private MetadataManager _metadataManager = new MetadataManager();

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<Node>() { Name = "BaseNode", Value = new Node(new byte[0], null)},
                    new Library.Configuration.SettingContent<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingContent<int>() { Name = "ConnectionCountLimit", Value = 32 },
                    new Library.Configuration.SettingContent<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "DiffusionBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "UploadBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<LockedHashSet<string>>() { Name = "TrustSignatures", Value = new LockedHashSet<string>() },
                    new Library.Configuration.SettingContent<List<ProfileMetadata>>() { Name = "ProfileMetadatas", Value = new List<ProfileMetadata>() },
                    new Library.Configuration.SettingContent<List<SignatureMessageMetadata>>() { Name = "SignatureMessageMetadatas", Value = new List<SignatureMessageMetadata>() },
                    new Library.Configuration.SettingContent<List<WikiDocumentMetadata>>() { Name = "WikiDocumentMetadatas", Value = new List<WikiDocumentMetadata>() },
                    new Library.Configuration.SettingContent<List<ChatTopicMetadata>>() { Name = "ChatTopicMetadatas", Value = new List<ChatTopicMetadata>() },
                    new Library.Configuration.SettingContent<List<ChatMessageMetadata>>() { Name = "ChatMessageMetadatas", Value = new List<ChatMessageMetadata>() },
                })
            {
                _thisLock = lockObject;
            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);

                    foreach (var metadata in this.ProfileMetadatas)
                    {
                        _metadataManager.SetMetadata(metadata);
                    }

                    foreach (var metadata in this.SignatureMessageMetadatas)
                    {
                        _metadataManager.SetMetadata(metadata);
                    }

                    foreach (var metadata in this.WikiDocumentMetadatas)
                    {
                        _metadataManager.SetMetadata(metadata);
                    }

                    foreach (var metadata in this.ChatTopicMetadatas)
                    {
                        _metadataManager.SetMetadata(metadata);
                    }

                    foreach (var metadata in this.ChatMessageMetadatas)
                    {
                        _metadataManager.SetMetadata(metadata);
                    }

                    this.ProfileMetadatas.Clear();
                    this.SignatureMessageMetadatas.Clear();
                    this.WikiDocumentMetadatas.Clear();
                    this.ChatTopicMetadatas.Clear();
                    this.ChatMessageMetadatas.Clear();
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    this.ProfileMetadatas.AddRange(_metadataManager.GetProfileMetadatas());
                    this.SignatureMessageMetadatas.AddRange(_metadataManager.GetSignatureMessageMetadatas());
                    this.WikiDocumentMetadatas.AddRange(_metadataManager.GetWikiDocumentMetadatas());
                    this.ChatTopicMetadatas.AddRange(_metadataManager.GetChatTopicMetadatas());
                    this.ChatMessageMetadatas.AddRange(_metadataManager.GetChatMessageMetadatas());

                    base.Save(directoryPath);

                    this.ProfileMetadatas.Clear();
                    this.SignatureMessageMetadatas.Clear();
                    this.WikiDocumentMetadatas.Clear();
                    this.ChatTopicMetadatas.Clear();
                    this.ChatMessageMetadatas.Clear();
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

            public LockedHashSet<string> TrustSignatures
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<string>)this["TrustSignatures"];
                    }
                }
            }

            public MetadataManager MetadataManager
            {
                get
                {
                    lock (_thisLock)
                    {
                        return _metadataManager;
                    }
                }
            }

            private List<ProfileMetadata> ProfileMetadatas
            {
                get
                {
                    return (List<ProfileMetadata>)this["ProfileMetadatas"];
                }
            }

            private List<SignatureMessageMetadata> SignatureMessageMetadatas
            {
                get
                {
                    return (List<SignatureMessageMetadata>)this["SignatureMessageMetadatas"];
                }
            }

            private List<WikiDocumentMetadata> WikiDocumentMetadatas
            {
                get
                {
                    return (List<WikiDocumentMetadata>)this["WikiDocumentMetadatas"];
                }
            }

            private List<ChatTopicMetadata> ChatTopicMetadatas
            {
                get
                {
                    return (List<ChatTopicMetadata>)this["ChatTopicMetadatas"];
                }
            }

            private List<ChatMessageMetadata> ChatMessageMetadatas
            {
                get
                {
                    return (List<ChatMessageMetadata>)this["ChatMessageMetadatas"];
                }
            }
        }

        public class MetadataManager
        {
            private Dictionary<string, ProfileMetadata> _profileMetadatas = new Dictionary<string, ProfileMetadata>();
            private Dictionary<string, HashSet<SignatureMessageMetadata>> _signatureMessageMetadatas = new Dictionary<string, HashSet<SignatureMessageMetadata>>();
            private Dictionary<Wiki, Dictionary<string, HashSet<WikiDocumentMetadata>>> _wikiDocumentMetadatas = new Dictionary<Wiki, Dictionary<string, HashSet<WikiDocumentMetadata>>>();
            private Dictionary<Chat, Dictionary<string, ChatTopicMetadata>> _chatTopicMetadatas = new Dictionary<Chat, Dictionary<string, ChatTopicMetadata>>();
            private Dictionary<Chat, Dictionary<string, HashSet<ChatMessageMetadata>>> _chatMessageMetadatas = new Dictionary<Chat, Dictionary<string, HashSet<ChatMessageMetadata>>>();

            private readonly object _thisLock = new object();

            public MetadataManager()
            {

            }

            public int Count
            {
                get
                {
                    lock (_thisLock)
                    {
                        int count = 0;

                        count += _profileMetadatas.Count;
                        count += _signatureMessageMetadatas.Values.Sum(n => n.Count);
                        count += _wikiDocumentMetadatas.Values.Sum(n => n.Values.Sum(m => m.Count));
                        count += _chatTopicMetadatas.Values.Sum(n => n.Values.Count);
                        count += _chatMessageMetadatas.Values.Sum(n => n.Values.Sum(m => m.Count));

                        return count;
                    }
                }
            }

            public IEnumerable<string> GetBroadcastSignatures()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<string>();

                    hashset.UnionWith(_profileMetadatas.Keys);

                    return hashset;
                }
            }

            public IEnumerable<string> GetUnicastSignatures()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<string>();

                    hashset.UnionWith(_signatureMessageMetadatas.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Wiki> GetMulticastWikis()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<Wiki>();

                    hashset.UnionWith(_wikiDocumentMetadatas.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Chat> GetMulticastChats()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<Chat>();

                    hashset.UnionWith(_chatTopicMetadatas.Keys);
                    hashset.UnionWith(_chatMessageMetadatas.Keys);

                    return hashset;
                }
            }

            public void RemoveProfileSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        _profileMetadatas.Remove(signature);
                    }
                }
            }

            public void RemoveUnicastSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        _signatureMessageMetadatas.Remove(signature);
                    }
                }
            }

            public void RemoveMulticastTags(IEnumerable<Wiki> tags)
            {
                lock (_thisLock)
                {
                    foreach (var wiki in tags)
                    {
                        _wikiDocumentMetadatas.Remove(wiki);
                    }
                }
            }

            public void RemoveMulticastTags(IEnumerable<Chat> tags)
            {
                lock (_thisLock)
                {
                    foreach (var chat in tags)
                    {
                        _chatTopicMetadatas.Remove(chat);
                        _chatMessageMetadatas.Remove(chat);
                    }
                }
            }

            public IEnumerable<ProfileMetadata> GetProfileMetadatas()
            {
                lock (_thisLock)
                {
                    return _profileMetadatas.Values.ToArray();
                }
            }

            public ProfileMetadata GetProfileMetadata(string signature)
            {
                lock (_thisLock)
                {
                    ProfileMetadata metadata;

                    if (_profileMetadatas.TryGetValue(signature, out metadata))
                    {
                        return metadata;
                    }

                    return null;
                }
            }

            public IEnumerable<SignatureMessageMetadata> GetSignatureMessageMetadatas()
            {
                lock (_thisLock)
                {
                    return _signatureMessageMetadatas.Values.Extract().ToArray();
                }
            }

            public IEnumerable<SignatureMessageMetadata> GetSignatureMessageMetadatas(string signature)
            {
                lock (_thisLock)
                {
                    HashSet<SignatureMessageMetadata> hashset;

                    if (_signatureMessageMetadatas.TryGetValue(signature, out hashset))
                    {
                        return hashset.ToArray();
                    }

                    return new SignatureMessageMetadata[0];
                }
            }

            public IEnumerable<WikiDocumentMetadata> GetWikiDocumentMetadatas()
            {
                lock (_thisLock)
                {
                    return _wikiDocumentMetadatas.Values.SelectMany(n => n.Values.Extract()).ToArray();
                }
            }

            public IEnumerable<WikiDocumentMetadata> GetWikiDocumentMetadatas(Wiki tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<WikiDocumentMetadata>> dic = null;

                    if (_wikiDocumentMetadatas.TryGetValue(tag, out dic))
                    {
                        return dic.Values.Extract().ToArray();
                    }

                    return new WikiDocumentMetadata[0];
                }
            }

            public IEnumerable<ChatTopicMetadata> GetChatTopicMetadatas()
            {
                lock (_thisLock)
                {
                    return _chatTopicMetadatas.Values.SelectMany(n => n.Values).ToArray();
                }
            }

            public IEnumerable<ChatTopicMetadata> GetChatTopicMetadatas(Chat chat)
            {
                lock (_thisLock)
                {
                    Dictionary<string, ChatTopicMetadata> dic = null;

                    if (_chatTopicMetadatas.TryGetValue(chat, out dic))
                    {
                        return dic.Values.ToArray();
                    }

                    return new ChatTopicMetadata[0];
                }
            }

            public IEnumerable<ChatMessageMetadata> GetChatMessageMetadatas()
            {
                lock (_thisLock)
                {
                    return _chatMessageMetadatas.Values.SelectMany(n => n.Values.Extract()).ToArray();
                }
            }

            public IEnumerable<ChatMessageMetadata> GetChatMessageMetadatas(Chat tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<ChatMessageMetadata>> dic = null;

                    if (_chatMessageMetadatas.TryGetValue(tag, out dic))
                    {
                        return dic.Values.Extract().ToArray();
                    }

                    return new ChatMessageMetadata[0];
                }
            }

            public bool SetMetadata(ProfileMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return false;

                    var signature = metadata.Certificate.ToString();

                    ProfileMetadata tempMetadata;

                    if (!_profileMetadatas.TryGetValue(signature, out tempMetadata)
                        || metadata.CreationTime > tempMetadata.CreationTime)
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        _profileMetadatas[signature] = metadata;
                    }

                    return (tempMetadata == null || metadata.CreationTime >= tempMetadata.CreationTime);
                }
            }

            public bool SetMetadata(SignatureMessageMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || !Signature.Check(metadata.Signature)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return false;

                    HashSet<SignatureMessageMetadata> hashset;

                    if (!_signatureMessageMetadatas.TryGetValue(metadata.Signature, out hashset))
                    {
                        hashset = new HashSet<SignatureMessageMetadata>();
                        _signatureMessageMetadatas[metadata.Signature] = hashset;
                    }

                    if (!hashset.Contains(metadata))
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        hashset.Add(metadata);
                    }

                    return true;
                }
            }

            public bool SetMetadata(WikiDocumentMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || metadata.Tag == null
                            || metadata.Tag.Id == null || metadata.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(metadata.Tag.Name)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return false;

                    var signature = metadata.Certificate.ToString();

                    Dictionary<string, HashSet<WikiDocumentMetadata>> dic;

                    if (!_wikiDocumentMetadatas.TryGetValue(metadata.Tag, out dic))
                    {
                        dic = new Dictionary<string, HashSet<WikiDocumentMetadata>>();
                        _wikiDocumentMetadatas[metadata.Tag] = dic;
                    }

                    HashSet<WikiDocumentMetadata> hashset;

                    if (!dic.TryGetValue(signature, out hashset))
                    {
                        hashset = new HashSet<WikiDocumentMetadata>();
                        dic[signature] = hashset;
                    }

                    if (!hashset.Contains(metadata))
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        hashset.Add(metadata);
                    }

                    return true;
                }
            }

            public bool SetMetadata(ChatTopicMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || metadata.Tag == null
                            || metadata.Tag.Id == null || metadata.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(metadata.Tag.Name)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return false;

                    var signature = metadata.Certificate.ToString();

                    Dictionary<string, ChatTopicMetadata> dic;

                    if (!_chatTopicMetadatas.TryGetValue(metadata.Tag, out dic))
                    {
                        dic = new Dictionary<string, ChatTopicMetadata>();
                        _chatTopicMetadatas[metadata.Tag] = dic;
                    }

                    ChatTopicMetadata tempMetadata;

                    if (!dic.TryGetValue(signature, out tempMetadata)
                        || metadata.CreationTime > tempMetadata.CreationTime)
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        dic[signature] = metadata;
                    }

                    return (tempMetadata == null || metadata.CreationTime >= tempMetadata.CreationTime);
                }
            }

            public bool SetMetadata(ChatMessageMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || metadata.Tag == null
                            || metadata.Tag.Id == null || metadata.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(metadata.Tag.Name)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return false;

                    var signature = metadata.Certificate.ToString();

                    Dictionary<string, HashSet<ChatMessageMetadata>> dic;

                    if (!_chatMessageMetadatas.TryGetValue(metadata.Tag, out dic))
                    {
                        dic = new Dictionary<string, HashSet<ChatMessageMetadata>>();
                        _chatMessageMetadatas[metadata.Tag] = dic;
                    }

                    HashSet<ChatMessageMetadata> hashset;

                    if (!dic.TryGetValue(signature, out hashset))
                    {
                        hashset = new HashSet<ChatMessageMetadata>();
                        dic[signature] = hashset;
                    }

                    if (!hashset.Contains(metadata))
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        hashset.Add(metadata);
                    }

                    return true;
                }
            }

            public void RemoveMetadata(ProfileMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return;

                    var signature = metadata.Certificate.ToString();

                    _profileMetadatas.Remove(signature);
                }
            }

            public void RemoveMetadata(SignatureMessageMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || !Signature.Check(metadata.Signature)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return;

                    HashSet<SignatureMessageMetadata> hashset;
                    if (!_signatureMessageMetadatas.TryGetValue(metadata.Signature, out hashset)) return;

                    hashset.Remove(metadata);

                    if (hashset.Count == 0)
                    {
                        _signatureMessageMetadatas.Remove(metadata.Signature);
                    }
                }
            }

            public void RemoveMetadata(WikiDocumentMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || metadata.Tag == null
                            || metadata.Tag.Id == null || metadata.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(metadata.Tag.Name)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return;

                    var signature = metadata.Certificate.ToString();

                    Dictionary<string, HashSet<WikiDocumentMetadata>> dic;
                    if (!_wikiDocumentMetadatas.TryGetValue(metadata.Tag, out dic)) return;

                    HashSet<WikiDocumentMetadata> hashset;
                    if (!dic.TryGetValue(signature, out hashset)) return;

                    hashset.Remove(metadata);

                    if (hashset.Count == 0)
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            _wikiDocumentMetadatas.Remove(metadata.Tag);
                        }
                    }
                }
            }

            public void RemoveMetadata(ChatTopicMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || metadata.Tag == null
                            || metadata.Tag.Id == null || metadata.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(metadata.Tag.Name)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return;

                    var signature = metadata.Certificate.ToString();

                    Dictionary<string, ChatTopicMetadata> dic;
                    if (!_chatTopicMetadatas.TryGetValue(metadata.Tag, out dic)) return;

                    ChatTopicMetadata tempMetadata;
                    if (!dic.TryGetValue(signature, out tempMetadata)) return;

                    if (metadata == tempMetadata)
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            _chatTopicMetadatas.Remove(metadata.Tag);
                        }
                    }
                }
            }

            public void RemoveMetadata(ChatMessageMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || metadata.Tag == null
                            || metadata.Tag.Id == null || metadata.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(metadata.Tag.Name)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return;

                    var signature = metadata.Certificate.ToString();

                    Dictionary<string, HashSet<ChatMessageMetadata>> dic;
                    if (!_chatMessageMetadatas.TryGetValue(metadata.Tag, out dic)) return;

                    HashSet<ChatMessageMetadata> hashset;
                    if (!dic.TryGetValue(signature, out hashset)) return;

                    hashset.Remove(metadata);

                    if (hashset.Count == 0)
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            _chatMessageMetadatas.Remove(metadata.Tag);
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
