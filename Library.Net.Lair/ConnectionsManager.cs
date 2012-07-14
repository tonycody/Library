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
    class ConnectionsManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Kademlia<Node> _routeTable;
        private Random _random = new Random();

        private byte[] _mySessionId;

        private LockedList<ConnectionManager> _connectionManagers;
        private MessagesManager _messagesManager;

        private LockedDictionary<Node, LockedHashSet<Channel>> _pushChannelsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Channel>>();

        private LockedList<Node> _creatingNodes;
        private LockedHashSet<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private LockedDictionary<Node, int> _nodesStatus;

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
        private volatile int _pushMessageRequestCount;
        private volatile int _pushMessageCount;
        private volatile int _pushFilterCount;

        private volatile int _pullNodeCount;
        private volatile int _pullMessageRequestCount;
        private volatile int _pullMessageCount;
        private volatile int _pullFilterCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        private bool _disposed = false;
        private object _thisLock = new object();

        private readonly int _maxNodeCount = 128;
        private readonly int _maxRequestCount = 8192;

        public ConnectionsManager(ClientManager clientManager, ServerManager serverManager, BufferManager bufferManager)
        {
            _clientManager = clientManager;
            _serverManager = serverManager;
            _bufferManager = bufferManager;

            _settings = new Settings();

            _routeTable = new Kademlia<Node>(512, 20);

            _connectionManagers = new LockedList<ConnectionManager>();
            _messagesManager = new MessagesManager();

            _creatingNodes = new LockedList<Node>();
            _cuttingNodes = new LockedHashSet<Node>();
            _removeNodes = new CirculationCollection<Node>(new TimeSpan(0, 10, 0));
            _nodesStatus = new LockedDictionary<Node, int>();

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

        public ChannelCollection Channels
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _settings.Channels;
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
                    contexts.Add(new InformationContext("PushMessageRequestCount", _pushMessageRequestCount));
                    contexts.Add(new InformationContext("PushMessageCount", _pushMessageCount));
                    contexts.Add(new InformationContext("PushFilterCount", _pushFilterCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PushMessageRequestCount", _pullMessageRequestCount));
                    contexts.Add(new InformationContext("PushMessageCount", _pullMessageCount));
                    contexts.Add(new InformationContext("PushFilterCount", _pullFilterCount));

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
                connectionManager.PullChannelsRequestEvent += new PullChannelsRequestEventHandler(connectionManager_PullChannelsRequestEvent);
                connectionManager.PullMessagesEvent += new PullMessagesEventHandler(connectionManager_PullMessagesEvent);
                connectionManager.PullFiltersEvent += new PullFiltersEventHandler(connectionManager_PullFiltersEvent);
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
            Stopwatch uploadStopwatch = new Stopwatch();
            uploadStopwatch.Start();
            Stopwatch pushStopwatch = new Stopwatch();
            pushStopwatch.Start();
            Stopwatch seedRemoveStopwatch = new Stopwatch();
            seedRemoveStopwatch.Start();

            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                if (seedRemoveStopwatch.Elapsed.Minutes >= 60)
                {
                    seedRemoveStopwatch.Restart();

                    var now = DateTime.UtcNow;

                    foreach (var m in _settings.Messages.ToArray())
                    {
                        if ((now - m.CreationTime) > new TimeSpan(64, 0, 0, 0))
                        {
                            _settings.Messages.Remove(m);
                        }
                    }
                }

                if (_connectionManagers.Count >= this.UploadingConnectionCountLowerLimit && uploadStopwatch.Elapsed.TotalSeconds > 180)
                {
                    uploadStopwatch.Restart();

                    HashSet<Channel> channels = new HashSet<Channel>();

                    foreach (var m in _settings.Messages)
                    {
                        channels.Add(m.Channel);
                    }

                    foreach (var f in _settings.Filters)
                    {
                        channels.Add(f.Channel);
                    }

                    foreach (var c in channels)
                    {
                        var node = this.GetSearchNode(c.Id, 1).FirstOrDefault();

                        if (node != null)
                        {
                            var messageManager = _messagesManager[node];

                            messageManager.PullChannelsRequest.Add(c);
                        }
                    }
                }

                if (_connectionManagers.Count >= this.DownloadingConnectionCountLowerLimit && pushStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushStopwatch.Restart();

                    HashSet<Channel> pushChannelsRequestList = new HashSet<Channel>();
                    List<Node> nodes = new List<Node>(_connectionManagers.Select(n => n.Node));

                    {
                        {
                            var list = _settings.Messages.Select(n => n.Channel)
                                .OrderBy(n => _random.Next()).ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                pushChannelsRequestList.Add(list[i]);
                                j++;
                            }
                        }
                    }

                    {
                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PushChannelsRequest.OrderBy(n => _random.Next()).ToList();

                            for (int i = 0, j = 0; j < 1024 && i < list.Count; i++)
                            {
                                pushChannelsRequestList.Add(list[i]);
                                j++;
                            }
                        }
                    }

                    {
                        LockedDictionary<Node, LockedHashSet<Channel>> pushChannelsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Channel>>();
                        LockedDictionary<Node, LockedHashSet<Channel>> pushFiltersRequestDictionary = new LockedDictionary<Node, LockedHashSet<Channel>>();

                        Parallel.ForEach(pushChannelsRequestList, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, item =>
                        {
                            List<Node> requestNodes = new List<Node>();

                            var node = this.GetSearchNode(item.Id, 1).Where(n => !requestNodes.Contains(n)).FirstOrDefault();
                            if (node != null) requestNodes.Add(node);

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                lock (pushChannelsRequestDictionary.ThisLock)
                                {
                                    if (!pushChannelsRequestDictionary.ContainsKey(requestNodes[i]))
                                        pushChannelsRequestDictionary[requestNodes[i]] = new LockedHashSet<Channel>();

                                    pushChannelsRequestDictionary[requestNodes[i]].Add(item);
                                }
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

                        // PushChannelsRequest
                        if (_connectionManagers.Count >= this.DownloadingConnectionCountLowerLimit)
                        {
                            ChannelCollection tempList = null;
                            int count = (int)(1024 * this.ResponseTimePriority(connectionManager.Node));

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

                                    Debug.WriteLine(string.Format("ConnectionManager: Push ChannelsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                    _pushMessageRequestCount += tempList.Count;
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

                        // Upload
                        if (_connectionManagers.Count >= this.UploadingConnectionCountLowerLimit)
                        {
                            List<Channel> channels = new List<Channel>();
                            channels.AddRange(messageManager.PullChannelsRequest);
                            channels = channels.OrderBy(n => _random.Next()).ToList();

                            HashSet<Message> messages = new HashSet<Message>();
                            HashSet<Filter> filters = new HashSet<Filter>();

                            foreach (var m in _settings.Messages)
                            {
                                if (channels.Contains(m.Channel) && !messageManager.PushMessages.Contains(m)
                                    && messages.Count < 8192)
                                {
                                    messages.Add(m);
                                }
                            }

                            foreach (var f in _settings.Filters)
                            {
                                if (channels.Contains(f.Channel) && !messageManager.PushFilters.Contains(f)
                                    && filters.Count < 8192)
                                {
                                    filters.Add(f);
                                }
                            }

                            connectionManager.PushMessages(messages);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Messages ({0})", messages.Count));
                            _pushMessageCount += messages.Count;

                            connectionManager.PushFilters(filters);

                            Debug.WriteLine(string.Format("ConnectionManager: Push Filters ({0})", filters.Count));
                            _pushFilterCount += filters.Count;

                            messageManager.PullChannelsRequest.Clear();
                            messageManager.PushMessages.AddRange(messages);
                            messageManager.Priority -= messages.Count;
                            messageManager.PushFilters.AddRange(filters);
                            messageManager.Priority -= filters.Count;
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

        private void connectionManager_PullChannelsRequestEvent(object sender, PullChannelsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Channels == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull ChannelsRequest ({0})", e.Channels.Count()));

            foreach (var c in e.Channels.Take(_maxRequestCount))
            {
                if (c == null || c.Id == null || string.IsNullOrWhiteSpace(c.Name)) continue;

                _messagesManager[connectionManager.Node].PullChannelsRequest.Add(c);
                _pullMessageRequestCount++;
            }
        }

        private void connectionManager_PullMessagesEvent(object sender, PullMessagesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Messages == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Messages ({0})", e.Messages.Count()));

            var now = DateTime.UtcNow;

            foreach (var m in e.Messages.Take(_maxRequestCount))
            {
                if (m == null || m.Channel.Id == null || string.IsNullOrWhiteSpace(m.Channel.Name)
                    || string.IsNullOrWhiteSpace(m.Content)
                    || (now - m.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || !m.VerifyCertificate()) continue;

                _settings.Messages.Add(m);
                _messagesManager[connectionManager.Node].Priority++;

                _pullMessageCount++;
            }
        }

        private void connectionManager_PullFiltersEvent(object sender, PullFiltersEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Filters == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Filters ({0})", e.Filters.Count()));

            var now = DateTime.UtcNow;

            foreach (var f in e.Filters.Take(_maxRequestCount))
            {
                if (f == null || f.Channel.Id == null || string.IsNullOrWhiteSpace(f.Channel.Name)
                    || f.Keys.Count == 0
                    || (now - f.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || !f.VerifyCertificate()) continue;

                _settings.Filters.Add(f);
                _messagesManager[connectionManager.Node].Priority++;

                _pullFilterCount++;
            }
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
                    if (node == null || node.Id == null || node.Uris.Count == 0 || _removeNodes.Contains(node)) continue;

                    _routeTable.Add(node);
                }
            }
        }

        public void GetChannelInfomation(Channel channel, out IList<Message> messages, out IList<Filter> filetrs)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                messages = _settings.Messages.Where(n => n.Channel == channel).ToList();
                filetrs = _settings.Filters.Where(n => n.Channel == channel).ToList();
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
                    || !message.VerifyCertificate()) return;

                _settings.Messages.Add(message);
            }
        }

        public void Upload(Filter filter)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                
                var now = DateTime.UtcNow;

                if (filter == null || filter.Channel.Id == null || string.IsNullOrWhiteSpace(filter.Channel.Name)
                    || filter.Keys.Count == 0
                    || (now - filter.CreationTime) > new TimeSpan(64, 0, 0, 0)
                    || !filter.VerifyCertificate()) return;

                _settings.Filters.Add(filter);
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
                    new Library.Configuration.SettingsContext<ChannelCollection>() { Name = "Channels", Value = new ChannelCollection() },
                    new Library.Configuration.SettingsContext<LockedHashSet<Message>>() { Name = "Messages", Value = new LockedHashSet<Message>() },
                    new Library.Configuration.SettingsContext<LockedHashSet<Filter>>() { Name = "Filters", Value = new LockedHashSet<Filter>() },
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

            public ChannelCollection Channels
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (ChannelCollection)this["Channels"];
                    }
                }
            }

            public LockedHashSet<Message> Messages
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedHashSet<Message>)this["Messages"];
                    }
                }
            }

            public LockedHashSet<Filter> Filters
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedHashSet<Filter>)this["Filters"];
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
