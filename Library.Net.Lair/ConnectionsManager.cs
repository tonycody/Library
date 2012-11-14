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

namespace Library.Net.Lair
{
    public delegate void UnlockChannelsEventHandler(object sender, ref IList<Channel> channels);
    public delegate void UnlockMessagesEventHandler(object sender, Channel channel, ref IList<Message> messages);
    public delegate void UnlockFiltersEventHandler(object sender, Channel channel, ref IList<Filter> filters);

    class ConnectionsManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Kademlia<Node> _routeTable;
        private static Random _random = new Random();

        private byte[] _mySessionId;

        private LockedList<ConnectionManager> _connectionManagers;
        private MessagesManager _messagesManager;

        private LockedDictionary<Node, LockedHashSet<Channel>> _pushChannelsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Channel>>();

        private LockedList<Node> _creatingNodes;
        private LockedHashSet<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private LockedDictionary<Node, int> _nodesStatus;

        private LockedHashSet<Channel> _pushChannelsRequestList = new LockedHashSet<Channel>();

        private readonly HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

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
        private volatile int _pushChannelRequestCount;
        private volatile int _pushMessageCount;
        private volatile int _pushFilterCount;

        private volatile int _pullNodeCount;
        private volatile int _pullChannelRequestCount;
        private volatile int _pullMessageCount;
        private volatile int _pullFilterCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        public event UnlockChannelsEventHandler UnlockChannelsEvent;
        public event UnlockMessagesEventHandler UnlockMessagesEvent;
        public event UnlockFiltersEventHandler UnlockFiltersEvent;
        
        private bool _disposed = false;
        private object _thisLock = new object();

        private readonly int _maxNodeCount = 128;
        private readonly int _maxRequestCount = 128;

        private readonly int _downloadingConnectionCountLowerLimit = 3;
        private readonly int _uploadingConnectionCountLowerLimit = 3;

        private int _threadCount = 2;
        
        public ConnectionsManager(ClientManager clientManager, ServerManager serverManager, BufferManager bufferManager)
        {
            _clientManager = clientManager;
            _serverManager = serverManager;
            _bufferManager = bufferManager;

            _settings = new Settings();

            _routeTable = new Kademlia<Node>(512, 30);

            _connectionManagers = new LockedList<ConnectionManager>();
            _messagesManager = new MessagesManager();

            _creatingNodes = new LockedList<Node>();
            _cuttingNodes = new LockedHashSet<Node>();
            _removeNodes = new CirculationCollection<Node>(new TimeSpan(0, 10, 0));
            _nodesStatus = new LockedDictionary<Node, int>();

            this.UpdateSessionId();

#if !MONO
            {
                SYSTEM_INFO info = new SYSTEM_INFO();
                NativeMethods.GetSystemInfo(ref info);

                _threadCount = Math.Max(1, Math.Min(info.dwNumberOfProcessors, 32) / 2);
            }
#endif
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
                    contexts.Add(new InformationContext("PushChannelRequestCount", _pushChannelRequestCount));
                    contexts.Add(new InformationContext("PushMessageCount", _pushMessageCount));
                    contexts.Add(new InformationContext("PushFilterCount", _pushFilterCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PullChannelRequestCount", _pullChannelRequestCount));
                    contexts.Add(new InformationContext("PullMessageCount", _pullMessageCount));
                    contexts.Add(new InformationContext("PullFilterCount", _pullFilterCount));

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

                    contexts.Add(new InformationContext("MessageCount", _settings.Messages.Values.Sum(n => n.Count)));
                    contexts.Add(new InformationContext("FilterCount", _settings.Filters.Values.Sum(n => n.Count)));

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

        protected virtual void OnUnlockChannelsEvent(ref IList<Channel> channels)
        {
            if (this.UnlockChannelsEvent != null)
            {
                this.UnlockChannelsEvent(this, ref channels);
            }
        }

        protected virtual void OnUnlockMessagesEvent(Channel channel, ref IList<Message> messages)
        {
            if (this.UnlockMessagesEvent != null)
            {
                this.UnlockMessagesEvent(this, channel, ref messages);
            }
        }

        protected virtual void OnUnlockFiltersEvent(Channel channel, ref IList<Filter> filters)
        {
            if (this.UnlockFiltersEvent != null)
            {
                this.UnlockFiltersEvent(this, channel, ref filters);
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
                if (!_searchNodeStopwatch.IsRunning || _searchNodeStopwatch.Elapsed.TotalSeconds > 10)
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
                var requestNodes = Kademlia<Node>.Sort(this.BaseNode, id, _searchNodes).ToList();
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
                        var list = _connectionsNodes.Where(n => _messagesManager[n].SurroundingNodes.Contains(item)).ToList();

                        list.Sort(new Comparison<Node>((Node x, Node y) =>
                        {
                            return _responseTimeDic[x].CompareTo(_responseTimeDic[y]);
                        }));

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

                if (_connectionManagers.Count > this.ConnectionCountLimit)
                {
                    // PushNodes
                    try
                    {
                        List<Node> nodes = new List<Node>();

                        lock (this.ThisLock)
                        {
                            var clist = _connectionManagers.ToList();
                            clist.Remove(connectionManager);

                            clist.Sort(new Comparison<ConnectionManager>((ConnectionManager x, ConnectionManager y) =>
                            {
                                return x.ResponseTime.CompareTo(y.ResponseTime);
                            }));

                            nodes.AddRange(clist.Take(12).Select(n => n.Node));
                        }

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
                connectionManager.PullChannelsRequestEvent += new PullChannelsRequestEventHandler(connectionManager_PullChannelsRequestEvent);
                connectionManager.PullMessageEvent += new PullMessageEventHandler(connectionManager_PullMessageEvent);
                connectionManager.PullFilterEvent += new PullFilterEventHandler(connectionManager_PullFilterEvent);
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
                        HashSet<string> uris = new HashSet<string>();
                        uris.UnionWith(node.Uris.Take(12).Where(n => _clientManager.CheckUri(n)));

                        foreach (var uri in uris)
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

                                if (_nodesStatus[node] >= 3)
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

                        if (connectionManager.Node.Uris.Count != 0) _routeTable.Live(connectionManager.Node);

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
            Stopwatch uploadStopwatch = new Stopwatch();
            uploadStopwatch.Start();
            Stopwatch pushStopwatch = new Stopwatch();
            pushStopwatch.Start();
            Stopwatch removeStopwatch = new Stopwatch();
            removeStopwatch.Start();
            Stopwatch refreshStopwatch = new Stopwatch();

            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalMinutes >= 60)
                {
                    refreshStopwatch.Restart();

                    var now = DateTime.UtcNow;

                    lock (this.ThisLock)
                    {
                        lock (_settings.ThisLock)
                        {
                            foreach (var c in _settings.Messages.Keys.ToArray())
                            {
                                var list = _settings.Messages[c];

                                foreach (var m in list.ToArray())
                                {
                                    if ((now - m.CreationTime) > new TimeSpan(64, 0, 0, 0))
                                    {
                                        list.Remove(m);
                                    }
                                }

                                if (list.Count == 0) _settings.Messages.Remove(c);
                            }

                            foreach (var c in _settings.Filters.Keys.ToArray())
                            {
                                var list = _settings.Filters[c];

                                foreach (var f in list.ToArray())
                                {
                                    if ((now - f.CreationTime) > new TimeSpan(6, 0, 0, 0))
                                    {
                                        list.Remove(f);
                                    }
                                }

                                if (list.Count == 0) _settings.Filters.Remove(c);
                            }
                        }
                    }
                }

                if (removeStopwatch.Elapsed.TotalSeconds >= 30)
                {
                    removeStopwatch.Restart();

                    {
                        var channels = this.GetChannels().ToList();

                        if (channels.Count > 128)
                        {
                            IList<Channel> unlockChannels = new List<Channel>();

                            this.OnUnlockChannelsEvent(ref unlockChannels);

                            var removeChannels = unlockChannels.Take(channels.Count - 128);

                            lock (this.ThisLock)
                            {
                                lock (_settings.ThisLock)
                                {
                                    foreach (var channel in removeChannels)
                                    {
                                        _settings.Messages.Remove(channel);
                                        _settings.Filters.Remove(channel);
                                    }
                                }
                            }
                        }
                    }

                    {
                        List<Channel> channels = new List<Channel>();

                        lock (this.ThisLock)
                        {
                            lock (_settings.ThisLock)
                            {
                                foreach (var item in _settings.Messages.ToArray())
                                {
                                    if (item.Value.Count > 1024)
                                    {
                                        channels.Add(item.Key);
                                    }
                                }
                            }
                        }

                        Dictionary<Channel, IList<Message>> unlockMessagesDic = new Dictionary<Channel, IList<Message>>();

                        foreach (var c in channels)
                        {
                            IList<Message> unlockMessages = new List<Message>();
                            this.OnUnlockMessagesEvent(c, ref unlockMessages);

                            unlockMessagesDic.Add(c, unlockMessages);
                        }

                        lock (this.ThisLock)
                        {
                            lock (_settings.ThisLock)
                            {
                                foreach (var c in channels)
                                {
                                    if (!_settings.Messages.ContainsKey(c)) continue;

                                    var list = _settings.Messages[c];
                                    var unlockMessages = unlockMessagesDic[c];

                                    foreach (var m in unlockMessages.Take(list.Count - 1024))
                                    {
                                        list.Remove(m);
                                    }
                                }
                            }
                        }
                    }

                    {
                        List<Channel> channels = new List<Channel>();

                        lock (this.ThisLock)
                        {
                            lock (_settings.ThisLock)
                            {
                                foreach (var item in _settings.Filters.ToArray())
                                {
                                    if (item.Value.Count > 32)
                                    {
                                        channels.Add(item.Key);
                                    }
                                }
                            }
                        }

                        Dictionary<Channel, IList<Filter>> unlockFiltersDic = new Dictionary<Channel, IList<Filter>>();

                        foreach (var c in channels)
                        {
                            IList<Filter> unlockFilters = new List<Filter>();
                            this.OnUnlockFiltersEvent(c, ref unlockFilters);

                            unlockFiltersDic.Add(c, unlockFilters);
                        }

                        lock (this.ThisLock)
                        {
                            lock (_settings.ThisLock)
                            {
                                foreach (var c in channels)
                                {
                                    if (!_settings.Filters.ContainsKey(c)) continue;

                                    var list = _settings.Filters[c];
                                    var unlockFilters = unlockFiltersDic[c];

                                    foreach (var m in unlockFilters.Take(list.Count - 32))
                                    {
                                        list.Remove(m);
                                    }
                                }
                            }
                        }
                    }
                }

                if (_connectionManagers.Count >= _uploadingConnectionCountLowerLimit && uploadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    uploadStopwatch.Restart();

                    Parallel.ForEach(this.GetChannels(), new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, item =>
                    {
                        try
                        {
                            List<Node> requestNodes = new List<Node>();
                            requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                var messageManager = _messagesManager[requestNodes[i]];

                                messageManager.PullChannelsRequest.Add(item);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    });
                }

                if (_connectionManagers.Count >= _downloadingConnectionCountLowerLimit && pushStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushStopwatch.Restart();

                    HashSet<Channel> pushChannelsRequestList = new HashSet<Channel>();
                    List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node));

                    {
                        {
                            var list = _pushChannelsRequestList
                                .OrderBy(n => _random.Next()).ToList();

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushChannelsRequestList.Add(list[i]);
                            }
                        }
                    }

                    {
                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullChannelsRequest.OrderBy(n => _random.Next()).ToList();

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushChannelsRequestList.Add(list[i]);
                            }
                        }
                    }

                    {
                        LockedDictionary<Node, LockedHashSet<Channel>> pushChannelsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Channel>>();

                        Parallel.ForEach(pushChannelsRequestList.OrderBy(n => _random.Next()), new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, item =>
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    lock (pushChannelsRequestDictionary.ThisLock)
                                    {
                                        if (!pushChannelsRequestDictionary.ContainsKey(requestNodes[i]))
                                            pushChannelsRequestDictionary[requestNodes[i]] = new LockedHashSet<Channel>();

                                        pushChannelsRequestDictionary[requestNodes[i]].Add(item);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        });

                        lock (this.ThisLock)
                        {
                            lock (_pushChannelsRequestDictionary.ThisLock)
                            {
                                _pushChannelsRequestDictionary.Clear();

                                foreach (var item in pushChannelsRequestDictionary)
                                {
                                    _pushChannelsRequestDictionary.Add(item.Key, item.Value);
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
                updateTime.Start();
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

                        List<Node> nodes = new List<Node>();

                        lock (this.ThisLock)
                        {
                            var clist = _connectionManagers.ToList();
                            clist.Remove(connectionManager);

                            clist.Sort(new Comparison<ConnectionManager>((ConnectionManager x, ConnectionManager y) =>
                            {
                                return x.ResponseTime.CompareTo(y.ResponseTime);
                            }));

                            nodes.AddRange(clist.Take(12).Select(n => n.Node));
                        }

                        if (nodes.Count > 0)
                        {
                            connectionManager.PushNodes(nodes);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodeCount += nodes.Count;
                        }
                    }

                    if (updateTime.Elapsed.TotalSeconds > 60)
                    {
                        updateTime.Restart();

                        // PushChannelsRequest
                        if (_connectionManagers.Count >= _downloadingConnectionCountLowerLimit)
                        {
                            ChannelCollection tempList = null;
                            int count = (int)(128 * this.ResponseTimePriority(connectionManager.Node));

                            lock (this.ThisLock)
                            {
                                lock (_pushChannelsRequestDictionary.ThisLock)
                                {
                                    if (_pushChannelsRequestDictionary.ContainsKey(connectionManager.Node))
                                    {
                                        tempList = new ChannelCollection(_pushChannelsRequestDictionary[connectionManager.Node]
                                            .OrderBy(n => _random.Next()).Take(count));

                                        _pushChannelsRequestDictionary[connectionManager.Node].ExceptWith(tempList);
                                        _messagesManager[connectionManager.Node].PushChannelsRequest.AddRange(tempList);
                                    }
                                }
                            }

                            if (tempList != null && tempList.Count != 0)
                            {
                                try
                                {
                                    connectionManager.PushChannelsRequest(tempList);

                                    _pushChannelsRequestList.ExceptWith(tempList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push ChannelsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                    _pushChannelRequestCount += tempList.Count;
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        _messagesManager[connectionManager.Node].PushChannelsRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }
                    }

                    // Upload (Message)
                    if (_connectionManagers.Count >= _uploadingConnectionCountLowerLimit)
                    {
                        List<Channel> channels = new List<Channel>();
                        channels.AddRange(messageManager.PullChannelsRequest);

                        HashSet<Message> messages = new HashSet<Message>();

                        lock (this.ThisLock)
                        {
                            lock (_settings.ThisLock)
                            {
                                foreach (var channel in channels.OrderBy(n => _random.Next()))
                                {
                                    if (_settings.Messages.ContainsKey(channel))
                                    {
                                        foreach (var m in _settings.Messages[channel].OrderBy(n => _random.Next()))
                                        {
                                            if (!messageManager.PushMessages.Contains(m.GetHash(_hashAlgorithm)))
                                            {
                                                messages.Add(m);

                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (messages.Count != 0)
                        {
                            var message = messages.First();
                            connectionManager.PushMessage(message);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Message ({0})", message.Channel.Name));
                            _pushMessageCount++;

                            messageManager.PushMessages.Add(message.GetHash(_hashAlgorithm));
                            messageManager.Priority--;
                        }
                    }

                    // Upload (Filter)
                    if (_connectionManagers.Count >= _uploadingConnectionCountLowerLimit)
                    {
                        List<Channel> channels = new List<Channel>();
                        channels.AddRange(messageManager.PullChannelsRequest);

                        HashSet<Filter> filters = new HashSet<Filter>();

                        lock (this.ThisLock)
                        {
                            lock (_settings.ThisLock)
                            {
                                foreach (var channel in channels.OrderBy(n => _random.Next()))
                                {
                                    if (_settings.Filters.ContainsKey(channel))
                                    {
                                        foreach (var f in _settings.Filters[channel].OrderBy(n => _random.Next()))
                                        {
                                            if (!messageManager.PushFilters.Contains(f.GetHash(_hashAlgorithm)))
                                            {
                                                filters.Add(f);

                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (filters.Count != 0)
                        {
                            var filter = filters.First();
                            connectionManager.PushFilter(filter);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Filter ({0})", filter.Channel.Name));
                            _pushFilterCount++;

                            messageManager.PushFilters.Add(filter.GetHash(_hashAlgorithm));
                            messageManager.Priority--;
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
                if (node == null || node.Id == null || node.Uris.Where(n => _clientManager.CheckUri(n)).Count() == 0 || _removeNodes.Contains(node)) continue;

                _routeTable.Add(node);
                _pullNodeCount++;
            }

            lock (this.ThisLock)
            {
                lock (_messagesManager.ThisLock)
                {
                    _messagesManager[connectionManager.Node].SurroundingNodes.Clear();
                    _messagesManager[connectionManager.Node].SurroundingNodes
                        .UnionWith(e.Nodes.OrderBy(n => _random.Next()).Take(12).Where(n => n != null && n.Id != null));
                }
            }
        }

        private void connectionManager_PullChannelsRequestEvent(object sender, PullChannelsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Channels == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull ChannelsRequest {0} ({1})", String.Join(", ", e.Channels), e.Channels.Count()));

            foreach (var c in e.Channels.Take(_maxRequestCount))
            {
                if (c == null || c.Id == null || string.IsNullOrWhiteSpace(c.Name)) continue;

                _messagesManager[connectionManager.Node].PullChannelsRequest.Add(c);
                _pullChannelRequestCount++;
            }
        }

        private void connectionManager_PullMessageEvent(object sender, PullMessageEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var now = DateTime.UtcNow;

            if (e.Message == null || e.Message.Channel.Id == null || string.IsNullOrWhiteSpace(e.Message.Channel.Name)
                || string.IsNullOrWhiteSpace(e.Message.Content)
                || (now - e.Message.CreationTime) > new TimeSpan(64, 0, 0, 0)
                || (e.Message.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                || !e.Message.VerifyCertificate()) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Message ({0})", e.Message.Channel.Name));

            lock (this.ThisLock)
            {
                lock (_settings.ThisLock)
                {
                    if (!_settings.Messages.ContainsKey(e.Message.Channel))
                        _settings.Messages[e.Message.Channel] = new LockedHashSet<Message>();

                    _settings.Messages[e.Message.Channel].Add(e.Message);
                }
            }

            _messagesManager[connectionManager.Node].PushMessages.Add(e.Message.GetHash(_hashAlgorithm));
            _messagesManager[connectionManager.Node].Priority++;

            _pullMessageCount++;
        
            if (connectionManager.Node.Uris.Count != 0) _routeTable.Live(connectionManager.Node);
        }

        private void connectionManager_PullFilterEvent(object sender, PullFilterEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var now = DateTime.UtcNow;

            if (e.Filter == null || e.Filter.Channel.Id == null || string.IsNullOrWhiteSpace(e.Filter.Channel.Name)
                || (now - e.Filter.CreationTime) > new TimeSpan(6, 0, 0, 0)
                || (e.Filter.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                || !e.Filter.VerifyCertificate()) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Filter ({0})", e.Filter.Channel.Name));

            lock (this.ThisLock)
            {
                lock (_settings.ThisLock)
                {
                    if (!_settings.Filters.ContainsKey(e.Filter.Channel))
                        _settings.Filters[e.Filter.Channel] = new LockedHashSet<Filter>();

                    _settings.Filters[e.Filter.Channel].Add(e.Filter);

                    Dictionary<byte[], Filter> dic = new Dictionary<byte[], Filter>(new BytesEqualityComparer());

                    foreach (var f2 in _settings.Filters[e.Filter.Channel])
                    {
                        if (!dic.ContainsKey(f2.Certificate.PublicKey))
                        {
                            dic[f2.Certificate.PublicKey] = f2;
                        }
                        else
                        {
                            var f3 = dic[f2.Certificate.PublicKey];

                            if (f2.CreationTime > f3.CreationTime) dic[f2.Certificate.PublicKey] = f2;
                        }
                    }

                    _settings.Filters[e.Filter.Channel].Clear();
                    _settings.Filters[e.Filter.Channel].UnionWith(dic.Values);
                }
            }

            _messagesManager[connectionManager.Node].PushFilters.Add(e.Filter.GetHash(_hashAlgorithm));
            _messagesManager[connectionManager.Node].Priority++;

            _pullFilterCount++;

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

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                foreach (var node in nodes)
                {
                    if (node == null || node.Id == null || node.Uris.Where(n => _clientManager.CheckUri(n)).Count() == 0 || _removeNodes.Contains(node)) continue;

                    _routeTable.Live(node);
                }
            }
        }

        public IEnumerable<Channel> GetChannels()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                var tc = new HashSet<Channel>();

                tc.UnionWith(_settings.Messages.Keys);
                tc.UnionWith(_settings.Filters.Keys);

                return tc;
            }
        }

        public void GetChannelInfomation(Channel channel, out IList<Message> messages, out IList<Filter> filetrs)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _pushChannelsRequestList.Add(channel);

                var tm = new List<Message>();
                var tf = new List<Filter>();

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        if (_settings.Messages.ContainsKey(channel))
                        {
                            tm.AddRange(_settings.Messages[channel]);
                        }
                    }
                }

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        if (_settings.Filters.ContainsKey(channel))
                        {
                            tf.AddRange(_settings.Filters[channel]);
                        }
                    }
                }

                messages = tm;
                filetrs = tf;
            }
        }

        public void Upload(Message message)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                var now = DateTime.UtcNow;

                if (message == null || message.Channel.Id == null || string.IsNullOrWhiteSpace(message.Channel.Name)
                    || string.IsNullOrWhiteSpace(message.Content)
                    || (now - message.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || (message.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || !message.VerifyCertificate()) return;

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        if (!_settings.Messages.ContainsKey(message.Channel))
                            _settings.Messages[message.Channel] = new LockedHashSet<Message>();

                        _settings.Messages[message.Channel].Add(message);
                    }
                }
            }
        }

        public void Upload(Filter filter)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                var now = DateTime.UtcNow;

                if (filter == null || filter.Channel.Id == null || string.IsNullOrWhiteSpace(filter.Channel.Name)
                    || (now - filter.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || (filter.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || !filter.VerifyCertificate()) return;

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        if (!_settings.Filters.ContainsKey(filter.Channel))
                            _settings.Filters[filter.Channel] = new LockedHashSet<Filter>();

                        _settings.Filters[filter.Channel].Add(filter);

                        Dictionary<byte[], Filter> dic = new Dictionary<byte[], Filter>(new BytesEqualityComparer());

                        foreach (var f2 in _settings.Filters[filter.Channel])
                        {
                            if (!dic.ContainsKey(f2.Certificate.PublicKey))
                            {
                                dic[f2.Certificate.PublicKey] = f2;
                            }
                            else
                            {
                                var f3 = dic[f2.Certificate.PublicKey];

                                if (f2.CreationTime > f3.CreationTime) dic[f2.Certificate.PublicKey] = f2;
                            }
                        }

                        _settings.Filters[filter.Channel].Clear();
                        _settings.Filters[filter.Channel].UnionWith(dic.Values);
                    }
                }
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
                    if (node == null || node.Id == null || node.Uris.Where(n => _clientManager.CheckUri(n)).Count() == 0) continue;

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
                    new Library.Configuration.SettingsContext<LockedDictionary<Channel, LockedHashSet<Message>>>() { Name = "Messages", Value = new LockedDictionary<Channel, LockedHashSet<Message>>() },
                    new Library.Configuration.SettingsContext<LockedDictionary<Channel, LockedHashSet<Filter>>>() { Name = "Filters", Value = new LockedDictionary<Channel, LockedHashSet<Filter>>() },
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

            public LockedDictionary<Channel, LockedHashSet<Message>> Messages
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedDictionary<Channel, LockedHashSet<Message>>)this["Messages"];
                    }
                }
            }

            public LockedDictionary<Channel, LockedHashSet<Filter>> Filters
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedDictionary<Channel, LockedHashSet<Filter>>)this["Filters"];
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
            if (_disposed) return;

            if (disposing)
            {

            }

            _disposed = true;
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
