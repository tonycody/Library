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
using Library.Security;

namespace Library.Net.Lair
{
    public delegate void UnlockChannelsEventHandler(object sender, ref IList<Channel> channels);
    public delegate void UnlockMessagesEventHandler(object sender, Channel channel, ref IList<Message> messages);

    public delegate void UnlockSectionsEventHandler(object sender, ref IList<Section> sections);
    public delegate void UnlockLeadersEventHandler(object sender, Section section, ref IList<Leader> leaders);
    public delegate void UnlockManagersEventHandler(object sender, Section section, ref IList<Manager> managers);
    public delegate void UnlockCreatorsEventHandler(object sender, Section section, ref IList<Creator> creators);

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
        private LockedDictionary<Node, LockedHashSet<Section>> _pushSectionsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Section>>();

        private LockedList<Node> _creatingNodes;
        private CirculationCollection<Node> _cuttingNodes;
        private CirculationCollection<Node> _removeNodes;
        private LockedDictionary<Node, int> _nodesStatus;

        private LockedHashSet<Channel> _pushChannelsRequestList = new LockedHashSet<Channel>();
        private LockedHashSet<Section> _pushSectionsRequestList = new LockedHashSet<Section>();

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

        private BandwidthLimit _bandwidthLimit = new BandwidthLimit();

        private long _receivedByteCount = 0;
        private long _sentByteCount = 0;

        private volatile int _pushNodeCount;
        private volatile int _pushChannelRequestCount;
        private volatile int _pushMessageCount;
        private volatile int _pushSectionRequestCount;
        private volatile int _pushLeaderCount;
        private volatile int _pushManagerCount;
        private volatile int _pushCreatorCount;

        private volatile int _pullNodeCount;
        private volatile int _pullChannelRequestCount;
        private volatile int _pullMessageCount;
        private volatile int _pullSectionRequestCount;
        private volatile int _pullLeaderCount;
        private volatile int _pullManagerCount;
        private volatile int _pullCreatorCount;

        private volatile int _acceptConnectionCount;
        private volatile int _createConnectionCount;

        public event UnlockChannelsEventHandler UnlockChannelsEvent;
        public event UnlockMessagesEventHandler UnlockMessagesEvent;

        public event UnlockSectionsEventHandler UnlockSectionsEvent;
        public event UnlockLeadersEventHandler UnlockLeadersEvent;
        public event UnlockManagersEventHandler UnlockManagersEvent;
        public event UnlockCreatorsEventHandler UnlockCreatorsEvent;

        private volatile bool _disposed = false;
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
            _cuttingNodes = new CirculationCollection<Node>(new TimeSpan(0, 30, 0));
            _removeNodes = new CirculationCollection<Node>(new TimeSpan(0, 30, 0));
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

        public long BandWidthLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _settings.BandwidthLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                    contexts.Add(new InformationContext("PushSectionRequestCount", _pushSectionRequestCount));
                    contexts.Add(new InformationContext("PushLeaderCount", _pushLeaderCount));
                    contexts.Add(new InformationContext("PushCreatorCount", _pushCreatorCount));
                    contexts.Add(new InformationContext("PushManagerCount", _pushManagerCount));

                    contexts.Add(new InformationContext("PullNodeCount", _pullNodeCount));
                    contexts.Add(new InformationContext("PullChannelRequestCount", _pullChannelRequestCount));
                    contexts.Add(new InformationContext("PullMessageCount", _pullMessageCount));
                    contexts.Add(new InformationContext("PullSectionRequestCount", _pullSectionRequestCount));
                    contexts.Add(new InformationContext("PullLeaderCount", _pullLeaderCount));
                    contexts.Add(new InformationContext("PullCreatorCount", _pullCreatorCount));
                    contexts.Add(new InformationContext("PullManagerCount", _pullManagerCount));

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

        protected virtual void OnUnlockSectionsEvent(ref IList<Section> sections)
        {
            if (this.UnlockSectionsEvent != null)
            {
                this.UnlockSectionsEvent(this, ref sections);
            }
        }

        protected virtual void OnUnlockLeadersEvent(Section section, ref IList<Leader> leaders)
        {
            if (this.UnlockLeadersEvent != null)
            {
                this.UnlockLeadersEvent(this, section, ref leaders);
            }
        }

        protected virtual void OnUnlockManagersEvent(Section section, ref IList<Manager> managers)
        {
            if (this.UnlockManagersEvent != null)
            {
                this.UnlockManagersEvent(this, section, ref managers);
            }
        }

        protected virtual void OnUnlockCreatorsEvent(Section section, ref IList<Creator> creators)
        {
            if (this.UnlockCreatorsEvent != null)
            {
                this.UnlockCreatorsEvent(this, section, ref creators);
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
                List<KeyValuePair<Node, TimeSpan>> nodes = new List<KeyValuePair<Node, TimeSpan>>();

                foreach (var connectionManager in _connectionManagers)
                {
                    nodes.Add(new KeyValuePair<Node, TimeSpan>(connectionManager.Node, connectionManager.ResponseTime));
                }

                if (nodes.Count <= 1) return 0.5;

                nodes.Sort(new Comparison<KeyValuePair<Node, TimeSpan>>((KeyValuePair<Node, TimeSpan> x, KeyValuePair<Node, TimeSpan> y) =>
                {
                    return y.Value.CompareTo(x.Value);
                }));

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
                        var list = _connectionsNodes
                            .Where(n => _messagesManager[n].SurroundingNodes.Contains(item))
                            .ToList();

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

                {
                    bool flag = false;

                    if (connectionManager.Type == ConnectionManagerType.Server)
                    {
                        var connectionCount = 0;

                        lock (this.ThisLock)
                        {
                            connectionCount = _connectionManagers
                                .Where(n => n.Type == ConnectionManagerType.Server)
                                .Count();
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
                        ThreadPool.QueueUserWorkItem(new WaitCallback((object state) =>
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

                                    nodes.AddRange(clist
                                        .Select(n => n.Node)
                                        .Where(n => n.Uris.Count > 0)
                                        .Take(12));
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
                        }));

                        return;
                    }
                }

                Debug.WriteLine("ConnectionManager: Connect");

                connectionManager.PullNodesEvent += new PullNodesEventHandler(connectionManager_NodesEvent);
                connectionManager.PullChannelsRequestEvent += new PullChannelsRequestEventHandler(connectionManager_PullChannelsRequestEvent);
                connectionManager.PullMessageEvent += new PullMessageEventHandler(connectionManager_PullMessageEvent);
                connectionManager.PullSectionsRequestEvent += new PullSectionsRequestEventHandler(connectionManager_PullSectionsRequestEvent);
                connectionManager.PullLeaderEvent += new PullLeaderEventHandler(connectionManager_PullLeaderEvent);
                connectionManager.PullManagerEvent += new PullManagerEventHandler(connectionManager_PullManagerEvent);
                connectionManager.PullCreatorEvent += new PullCreatorEventHandler(connectionManager_PullCreatorEvent);
                connectionManager.PullCancelEvent += new PullCancelEventHandler(connectionManager_PullCancelEvent);
                connectionManager.CloseEvent += new CloseEventHandler(connectionManager_CloseEvent);

                var limit = connectionManager.Connection.GetLayers().OfType<IBandwidthLimit>().FirstOrDefault();

                if (limit != null)
                {
                    limit.BandwidthLimit = _bandwidthLimit;
                }

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
                        .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id)) && !_creatingNodes.Contains(n))
                        .OrderBy(n => _random.Next())
                        .FirstOrDefault();

                    if (node == null)
                    {
                        node = _routeTable
                            .ToArray()
                            .Where(n => !_connectionManagers.Any(m => Collection.Equals(m.Node.Id, n.Id)) && !_creatingNodes.Contains(n))
                            .OrderBy(n => _random.Next())
                            .FirstOrDefault();
                    }

                    if (node == null) continue;

                    _creatingNodes.Add(node);
                }

                try
                {
                    HashSet<string> uris = new HashSet<string>();
                    uris.UnionWith(node.Uris
                        .Take(12)
                        .Where(n => _clientManager.CheckUri(n))
                        .OrderBy(n => _random.Next()));

                    if (uris.Count == 0)
                    {
                        lock (this.ThisLock)
                        {
                            _nodesStatus.Remove(node);
                        }

                        _removeNodes.Remove(node);
                        _cuttingNodes.Remove(node);
                        _routeTable.Remove(node);

                        continue;
                    }

                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers
                            .Where(n => n.Type == ConnectionManagerType.Client)
                            .Count();
                    }

                    if (connectionCount > ((this.ConnectionCountLimit / 3) * 1))
                    {
                        continue;
                    }

                    foreach (var uri in uris)
                    {
                        if (this.State == ManagerState.Stop) return;

                        var connection = _clientManager.CreateConnection(uri);

                        if (connection != null)
                        {
                            var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectionManagerType.Client, _bufferManager);

                            try
                            {
                                connectionManager.Connect();
                                if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();

                                lock (this.ThisLock)
                                {
                                    _nodesStatus.Remove(node);
                                }

                                _cuttingNodes.Remove(node);

                                if (node != connectionManager.Node)
                                {
                                    _removeNodes.Add(node);
                                    _routeTable.Remove(node);
                                }

                                _routeTable.Live(connectionManager.Node);

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
                            bool flag = false;

                            lock (this.ThisLock)
                            {
                                if (!_nodesStatus.ContainsKey(node)) _nodesStatus[node] = 0;
                                _nodesStatus[node]++;

                                if (_nodesStatus[node] >= 3)
                                {
                                    flag = true;
                                }
                            }

                            if (flag)
                            {
                                _nodesStatus.Remove(node);
                                _removeNodes.Add(node);
                                _cuttingNodes.Remove(node);

                                if (_routeTable.Count > 100)
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
                    var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectionManagerType.Server, _bufferManager);

                    try
                    {
                        connectionManager.Connect();
                        if (connectionManager.Node == null || connectionManager.Node.Id == null) throw new ArgumentException();
                        if (_removeNodes.Contains(connectionManager.Node)) throw new ArgumentException();

                        if (connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                            _routeTable.Add(connectionManager.Node);

                        lock (this.ThisLock)
                        {
                            _nodesStatus.Remove(connectionManager.Node);
                        }

                        _cuttingNodes.Remove(connectionManager.Node);

                        if (connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
                            _routeTable.Add(connectionManager.Node);

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

        private class NodeSortItem
        {
            public Node Node { get; set; }
            public TimeSpan ResponseTime { get; set; }
            public DateTime LastPullTime { get; set; }
        }

        private void ConnectionsManagerThread()
        {
            Stopwatch connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();
            Stopwatch refreshStopwatch = new Stopwatch();
            Stopwatch removeStopwatch = new Stopwatch();
            removeStopwatch.Start();
            Stopwatch pushUploadStopwatch = new Stopwatch();
            pushUploadStopwatch.Start();
            Stopwatch pushDownloadStopwatch = new Stopwatch();
            pushDownloadStopwatch.Start();

            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                var connectionCount = 0;

                lock (this.ThisLock)
                {
                    connectionCount = _connectionManagers
                        .Where(n => n.Type == ConnectionManagerType.Client)
                        .Count();
                }

                if (connectionCount > ((this.ConnectionCountLimit / 3) * 1)
                    && connectionCheckStopwatch.Elapsed.TotalMinutes > 30)
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

                    nodeSortItems.Sort(new Comparison<NodeSortItem>((NodeSortItem x, NodeSortItem y) =>
                    {
                        int c = x.LastPullTime.CompareTo(y.LastPullTime);
                        if (c != 0) return c;

                        return y.ResponseTime.CompareTo(x.ResponseTime);
                    }));

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
                                        _nodesStatus.Remove(connectionManager.Node);
                                    }

                                    _removeNodes.Add(connectionManager.Node);
                                    _routeTable.Remove(connectionManager.Node);

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
                        }
                    }
                }

                if (removeStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    removeStopwatch.Restart();

                    ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
                    {
                        try
                        {
                            {
                                var channels = this.GetChannels().ToList();

                                if (channels.Count > 128)
                                {
                                    IList<Channel> unlockChannels = new List<Channel>();

                                    this.OnUnlockChannelsEvent(ref unlockChannels);

                                    var removeChannels = unlockChannels.Skip(32).Take(channels.Count - 128);

                                    lock (this.ThisLock)
                                    {
                                        lock (_settings.ThisLock)
                                        {
                                            foreach (var channel in removeChannels)
                                            {
                                                _settings.Messages.Remove(channel);
                                            }
                                        }
                                    }
                                }
                            }

                            {
                                List<KeyValuePair<Channel, int>> items = new List<KeyValuePair<Channel, int>>();

                                lock (this.ThisLock)
                                {
                                    lock (_settings.ThisLock)
                                    {
                                        foreach (var m in _settings.Messages.ToArray())
                                        {
                                            if (m.Value.Count > 256)
                                            {
                                                items.Add(new KeyValuePair<Channel, int>(m.Key, m.Value.Count));
                                            }
                                        }
                                    }
                                }

                                Dictionary<Channel, IList<Message>> unlockMessagesDic = new Dictionary<Channel, IList<Message>>();

                                foreach (var item in items)
                                {
                                    IList<Message> unlockMessages = new List<Message>();
                                    this.OnUnlockMessagesEvent(item.Key, ref unlockMessages);

                                    unlockMessagesDic.Add(item.Key, unlockMessages);
                                }

                                lock (this.ThisLock)
                                {
                                    lock (_settings.ThisLock)
                                    {
                                        foreach (var item in items)
                                        {
                                            if (!_settings.Messages.ContainsKey(item.Key)) continue;

                                            var list = _settings.Messages[item.Key];
                                            var unlockMessages = unlockMessagesDic[item.Key];

                                            foreach (var m in unlockMessages.Take(item.Value - 256))
                                            {
                                                list.Remove(m);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {

                        }
                    }));
                }

                if (connectionCount >= _uploadingConnectionCountLowerLimit && pushUploadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushUploadStopwatch.Restart();

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

                    Parallel.ForEach(this.GetSections(), new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, item =>
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
                    });
                }

                if (connectionCount >= _downloadingConnectionCountLowerLimit && pushDownloadStopwatch.Elapsed.TotalSeconds > 60)
                {
                    pushDownloadStopwatch.Restart();

                    HashSet<Channel> pushChannelsRequestList = new HashSet<Channel>();
                    HashSet<Section> pushSectionsRequestList = new HashSet<Section>();
                    List<Node> nodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        nodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    {
                        {
                            var list = _pushChannelsRequestList
                                .ToArray()
                                .OrderBy(n => _random.Next())
                                .ToList();

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
                            var list = messageManager.PullChannelsRequest
                                .ToArray()
                                .OrderBy(n => _random.Next())
                                .ToList();

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushChannelsRequestList.Add(list[i]);
                            }
                        }
                    }

                    {
                        {
                            var list = _pushSectionsRequestList
                                .ToArray()
                                .OrderBy(n => _random.Next())
                                .ToList();

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushSectionsRequestList.Add(list[i]);
                            }
                        }
                    }

                    {
                        foreach (var node in nodes)
                        {
                            var messageManager = _messagesManager[node];
                            var list = messageManager.PullSectionsRequest
                                .ToArray()
                                .OrderBy(n => _random.Next())
                                .ToList();

                            for (int i = 0; i < 128 && i < list.Count; i++)
                            {
                                pushSectionsRequestList.Add(list[i]);
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

                        lock (_pushChannelsRequestDictionary.ThisLock)
                        {
                            _pushChannelsRequestDictionary.Clear();

                            foreach (var item in pushChannelsRequestDictionary)
                            {
                                _pushChannelsRequestDictionary.Add(item.Key, item.Value);
                            }
                        }
                    }

                    {
                        LockedDictionary<Node, LockedHashSet<Section>> pushSectionsRequestDictionary = new LockedDictionary<Node, LockedHashSet<Section>>();

                        Parallel.ForEach(pushSectionsRequestList.OrderBy(n => _random.Next()), new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, item =>
                        {
                            try
                            {
                                List<Node> requestNodes = new List<Node>();
                                requestNodes.AddRange(this.GetSearchNode(item.Id, 2));

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    lock (pushSectionsRequestDictionary.ThisLock)
                                    {
                                        if (!pushSectionsRequestDictionary.ContainsKey(requestNodes[i]))
                                            pushSectionsRequestDictionary[requestNodes[i]] = new LockedHashSet<Section>();

                                        pushSectionsRequestDictionary[requestNodes[i]].Add(item);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        });

                        lock (_pushSectionsRequestDictionary.ThisLock)
                        {
                            _pushSectionsRequestDictionary.Clear();

                            foreach (var item in pushSectionsRequestDictionary)
                            {
                                _pushSectionsRequestDictionary.Add(item.Key, item.Value);
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

                for (; ; )
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;
                    if (!_connectionManagers.Contains(connectionManager)) return;

                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers
                            .Where(n => n.Type == ConnectionManagerType.Client)
                            .Count();
                    }

                    // Check
                    if (checkTime.Elapsed.TotalSeconds > 60)
                    {
                        checkTime.Restart();

                        if ((DateTime.UtcNow - messageManager.LastPullTime) > new TimeSpan(1, 0, 0))
                        {
                            lock (this.ThisLock)
                            {
                                _nodesStatus.Remove(connectionManager.Node);
                            }

                            _removeNodes.Add(connectionManager.Node);
                            _routeTable.Remove(connectionManager.Node);

                            connectionManager.PushCancel();

                            Debug.WriteLine("ConnectionManager: Push Cancel");
                            return;
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

                            nodes.AddRange(clist
                                .Select(n => n.Node)
                                .Where(n => n.Uris.Count > 0)
                                .Take(12));
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

                            lock (_pushChannelsRequestDictionary.ThisLock)
                            {
                                if (_pushChannelsRequestDictionary.ContainsKey(connectionManager.Node))
                                {
                                    tempList = new ChannelCollection(_pushChannelsRequestDictionary[connectionManager.Node]
                                        .ToArray()
                                        .OrderBy(n => _random.Next())
                                        .Take(count));

                                    _pushChannelsRequestDictionary[connectionManager.Node].ExceptWith(tempList);
                                    _messagesManager[connectionManager.Node].PushChannelsRequest.AddRange(tempList);
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

                        // PushSectionsRequest
                        if (_connectionManagers.Count >= _downloadingConnectionCountLowerLimit)
                        {
                            SectionCollection tempList = null;
                            int count = (int)(128 * this.ResponseTimePriority(connectionManager.Node));

                            lock (_pushSectionsRequestDictionary.ThisLock)
                            {
                                if (_pushSectionsRequestDictionary.ContainsKey(connectionManager.Node))
                                {
                                    tempList = new SectionCollection(_pushSectionsRequestDictionary[connectionManager.Node]
                                        .ToArray()
                                        .OrderBy(n => _random.Next())
                                        .Take(count));

                                    _pushSectionsRequestDictionary[connectionManager.Node].ExceptWith(tempList);
                                    _messagesManager[connectionManager.Node].PushSectionsRequest.AddRange(tempList);
                                }
                            }

                            if (tempList != null && tempList.Count != 0)
                            {
                                try
                                {
                                    connectionManager.PushSectionsRequest(tempList);

                                    _pushSectionsRequestList.ExceptWith(tempList);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push SectionsRequest {0} ({1})", String.Join(", ", tempList), tempList.Count));
                                    _pushSectionRequestCount += tempList.Count;
                                }
                                catch (Exception e)
                                {
                                    foreach (var item in tempList)
                                    {
                                        _messagesManager[connectionManager.Node].PushSectionsRequest.Remove(item);
                                    }

                                    throw e;
                                }
                            }
                        }
                    }

                    if (connectionCount >= _uploadingConnectionCountLowerLimit)
                    {
                        foreach (var s in new int[] { 0, 1, 2, 3, }.Randomize())
                        {
                            // Upload (Message)
                            if (s == 0)
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

                                break;
                            }

                            // Upload (Leader)
                            if (s == 1)
                            {
                                List<Section> channels = new List<Section>();
                                channels.AddRange(messageManager.PullSectionsRequest);

                                HashSet<Leader> leaders = new HashSet<Leader>();

                                lock (this.ThisLock)
                                {
                                    lock (_settings.ThisLock)
                                    {
                                        foreach (var channel in channels.OrderBy(n => _random.Next()))
                                        {
                                            if (_settings.Leaders.ContainsKey(channel))
                                            {
                                                foreach (var l in _settings.Leaders[channel].Values.OrderBy(n => _random.Next()))
                                                {
                                                    if (!messageManager.PushLeaders.Contains(l.GetHash(_hashAlgorithm)))
                                                    {
                                                        leaders.Add(l);

                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                if (leaders.Count != 0)
                                {
                                    var leader = leaders.First();
                                    connectionManager.PushLeader(leader);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Leader ({0})", leader.Section.Name));
                                    _pushLeaderCount++;

                                    messageManager.PushLeaders.Add(leader.GetHash(_hashAlgorithm));
                                    messageManager.Priority--;
                                }

                                break;
                            }

                            // Upload (Manager)
                            if (s == 2)
                            {
                                List<Section> channels = new List<Section>();
                                channels.AddRange(messageManager.PullSectionsRequest);

                                HashSet<Manager> managers = new HashSet<Manager>();

                                lock (this.ThisLock)
                                {
                                    lock (_settings.ThisLock)
                                    {
                                        foreach (var channel in channels.OrderBy(n => _random.Next()))
                                        {
                                            if (_settings.Managers.ContainsKey(channel))
                                            {
                                                foreach (var m in _settings.Managers[channel].Values.OrderBy(n => _random.Next()))
                                                {
                                                    if (!messageManager.PushManagers.Contains(m.GetHash(_hashAlgorithm)))
                                                    {
                                                        managers.Add(m);

                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                if (managers.Count != 0)
                                {
                                    var manager = managers.First();
                                    connectionManager.PushManager(manager);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Manager ({0})", manager.Section.Name));
                                    _pushManagerCount++;

                                    messageManager.PushManagers.Add(manager.GetHash(_hashAlgorithm));
                                    messageManager.Priority--;
                                }

                                break;
                            }

                            // Upload (Creator)
                            if (s == 3)
                            {
                                List<Section> channels = new List<Section>();
                                channels.AddRange(messageManager.PullSectionsRequest);

                                HashSet<Creator> creators = new HashSet<Creator>();

                                lock (this.ThisLock)
                                {
                                    lock (_settings.ThisLock)
                                    {
                                        foreach (var channel in channels.OrderBy(n => _random.Next()))
                                        {
                                            if (_settings.Creators.ContainsKey(channel))
                                            {
                                                foreach (var c in _settings.Creators[channel].Values.OrderBy(n => _random.Next()))
                                                {
                                                    if (!messageManager.PushCreators.Contains(c.GetHash(_hashAlgorithm)))
                                                    {
                                                        creators.Add(c);

                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                if (creators.Count != 0)
                                {
                                    var creator = creators.First();
                                    connectionManager.PushCreator(creator);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Creator ({0})", creator.Section.Name));
                                    _pushCreatorCount++;

                                    messageManager.PushCreators.Add(creator.GetHash(_hashAlgorithm));
                                    messageManager.Priority--;
                                }

                                break;
                            }
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
                    lock (_messagesManager[connectionManager.Node].ThisLock)
                    {
                        lock (_messagesManager[connectionManager.Node].SurroundingNodes.ThisLock)
                        {
                            _messagesManager[connectionManager.Node].SurroundingNodes.Clear();
                            _messagesManager[connectionManager.Node].SurroundingNodes.UnionWith(e.Nodes
                                .Where(n => n != null && n.Id != null)
                                .OrderBy(n => _random.Next())
                                .Take(12));
                        }
                    }
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

            if (e.Message == null || e.Message.Channel == null || e.Message.Channel.Id == null || string.IsNullOrWhiteSpace(e.Message.Channel.Name)
                || string.IsNullOrWhiteSpace(e.Message.Content)
                || (now - e.Message.CreationTime) > new TimeSpan(64, 0, 0, 0)
                || (e.Message.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                || e.Message.Certificate == null || !e.Message.VerifyCertificate()) return;

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
            _messagesManager[connectionManager.Node].LastPullTime = DateTime.UtcNow;
            _messagesManager[connectionManager.Node].Priority++;

            _pullMessageCount++;
        }

        private void connectionManager_PullSectionsRequestEvent(object sender, PullSectionsRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            if (e.Sections == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull SectionsRequest {0} ({1})", String.Join(", ", e.Sections), e.Sections.Count()));

            foreach (var c in e.Sections.Take(_maxRequestCount))
            {
                if (c == null || c.Id == null || string.IsNullOrWhiteSpace(c.Name)) continue;

                _messagesManager[connectionManager.Node].PullSectionsRequest.Add(c);
                _pullSectionRequestCount++;
            }
        }

        private void connectionManager_PullLeaderEvent(object sender, PullLeaderEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var now = DateTime.UtcNow;

            if (e.Leader == null || e.Leader.Section == null || e.Leader.Section.Id == null || string.IsNullOrWhiteSpace(e.Leader.Section.Name)
                || (e.Leader.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                || e.Leader.Certificate == null || !e.Leader.VerifyCertificate()) return;

            var signature = e.Leader.Certificate.ToString();

            Debug.WriteLine(string.Format("ConnectionManager: Pull Leader {0} ({1})", signature, e.Leader.Section.Name));

            lock (this.ThisLock)
            {
                lock (_settings.ThisLock)
                {
                    LockedDictionary<string, Leader> dic = null;

                    if (!_settings.Leaders.TryGetValue(e.Leader.Section, out dic))
                    {
                        dic = new LockedDictionary<string, Leader>();
                        _settings.Leaders[e.Leader.Section] = dic;
                    }

                    Leader tempLeader = null;

                    if (!dic.TryGetValue(signature, out tempLeader)
                        || e.Leader.CreationTime > tempLeader.CreationTime)
                    {
                        dic[signature] = e.Leader;
                    }
                }
            }

            _messagesManager[connectionManager.Node].PushLeaders.Add(e.Leader.GetHash(_hashAlgorithm));
            _messagesManager[connectionManager.Node].LastPullTime = DateTime.UtcNow;
            _messagesManager[connectionManager.Node].Priority++;

            _pullLeaderCount++;
        }

        private void connectionManager_PullManagerEvent(object sender, PullManagerEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var now = DateTime.UtcNow;

            if (e.Manager == null || e.Manager.Section == null || e.Manager.Section.Id == null || string.IsNullOrWhiteSpace(e.Manager.Section.Name)
                || (e.Manager.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                || e.Manager.Certificate == null || !e.Manager.VerifyCertificate()) return;

            var signature = e.Manager.Certificate.ToString();

            Debug.WriteLine(string.Format("ConnectionManager: Pull Manager {0} ({1})", signature, e.Manager.Section.Name));

            lock (this.ThisLock)
            {
                lock (_settings.ThisLock)
                {
                    LockedDictionary<string, Manager> dic = null;

                    if (!_settings.Managers.TryGetValue(e.Manager.Section, out dic))
                    {
                        dic = new LockedDictionary<string, Manager>();
                        _settings.Managers[e.Manager.Section] = dic;
                    }

                    Manager tempManager = null;

                    if (!dic.TryGetValue(signature, out tempManager)
                        || e.Manager.CreationTime > tempManager.CreationTime)
                    {
                        dic[signature] = e.Manager;
                    }
                }
            }

            _messagesManager[connectionManager.Node].PushManagers.Add(e.Manager.GetHash(_hashAlgorithm));
            _messagesManager[connectionManager.Node].LastPullTime = DateTime.UtcNow;
            _messagesManager[connectionManager.Node].Priority++;

            _pullManagerCount++;
        }

        private void connectionManager_PullCreatorEvent(object sender, PullCreatorEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var now = DateTime.UtcNow;

            if (e.Creator == null || e.Creator.Section == null || e.Creator.Section.Id == null || string.IsNullOrWhiteSpace(e.Creator.Section.Name)
                || (e.Creator.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                || e.Creator.Certificate == null || !e.Creator.VerifyCertificate()) return;

            var signature = e.Creator.Certificate.ToString();

            Debug.WriteLine(string.Format("ConnectionManager: Pull Creator {0} ({1})", signature, e.Creator.Section.Name));

            lock (this.ThisLock)
            {
                lock (_settings.ThisLock)
                {
                    LockedDictionary<string, Creator> dic = null;

                    if (!_settings.Creators.TryGetValue(e.Creator.Section, out dic))
                    {
                        dic = new LockedDictionary<string, Creator>();
                        _settings.Creators[e.Creator.Section] = dic;
                    }

                    Creator tempCreator = null;

                    if (!dic.TryGetValue(signature, out tempCreator)
                        || e.Creator.CreationTime > tempCreator.CreationTime)
                    {
                        dic[signature] = e.Creator;
                    }
                }
            }

            _messagesManager[connectionManager.Node].PushCreators.Add(e.Creator.GetHash(_hashAlgorithm));
            _messagesManager[connectionManager.Node].LastPullTime = DateTime.UtcNow;
            _messagesManager[connectionManager.Node].Priority++;

            _pullCreatorCount++;
        }
        
        private void connectionManager_PullCancelEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            Debug.WriteLine("ConnectionManager: Pull Cancel");

            try
            {
                _removeNodes.Add(connectionManager.Node);

                if (_routeTable.Count > 100)
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
                    if (connectionManager.Node.Uris.Any(n => _clientManager.CheckUri(n)))
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

                return tc;
            }
        }

        public IEnumerable<Section> GetSections()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                var ts = new HashSet<Section>();

                ts.UnionWith(_settings.Leaders.Keys);
                ts.UnionWith(_settings.Managers.Keys);
                ts.UnionWith(_settings.Creators.Keys);

                return ts;
            }
        }

        public void GetChannelInfomation(Channel channel, out IList<Message> messages)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _pushChannelsRequestList.Add(channel);

                var tm = new List<Message>();

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

                messages = tm;
            }
        }

        public void Upload(Message message)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                var now = DateTime.UtcNow;

                if (message == null || message.Channel == null || message.Channel.Id == null || string.IsNullOrWhiteSpace(message.Channel.Name)
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

        public void GetSectionInfomation(Section section, out IList<Leader> leaders, out IList<Manager> managers, out IList<Creator> creators)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _pushSectionsRequestList.Add(section);

                var tl = new List<Leader>();
                var tm = new List<Manager>();
                var tc = new List<Creator>();

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        if (_settings.Leaders.ContainsKey(section))
                        {
                            tl.AddRange(_settings.Leaders[section].Values);
                        }
                    }
                }

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        if (_settings.Managers.ContainsKey(section))
                        {
                            tm.AddRange(_settings.Managers[section].Values);
                        }
                    }
                }

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        if (_settings.Creators.ContainsKey(section))
                        {
                            tc.AddRange(_settings.Creators[section].Values);
                        }
                    }
                }

                leaders = tl;
                managers = tm;
                creators = tc;
            }
        }

        public void Upload(Leader leader)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                var now = DateTime.UtcNow;

                if (leader == null || leader.Section == null || leader.Section.Id == null || string.IsNullOrWhiteSpace(leader.Section.Name)
                    || (leader.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || leader.Certificate == null || !leader.VerifyCertificate()) return;

                var signature = leader.Certificate.ToString();

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        LockedDictionary<string, Leader> dic = null;

                        if (!_settings.Leaders.TryGetValue(leader.Section, out dic))
                        {
                            dic = new LockedDictionary<string, Leader>();
                            _settings.Leaders[leader.Section] = dic;
                        }

                        Leader tempLeader = null;

                        if (!dic.TryGetValue(signature, out tempLeader)
                            || leader.CreationTime > tempLeader.CreationTime)
                        {
                            dic[signature] = leader;
                        }
                    }
                }
            }
        }

        public void Upload(Manager manager)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                var now = DateTime.UtcNow;

                if (manager == null || manager.Section == null || manager.Section.Id == null || string.IsNullOrWhiteSpace(manager.Section.Name)
                    || (manager.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || manager.Certificate == null || !manager.VerifyCertificate()) return;

                var signature = manager.Certificate.ToString();

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        LockedDictionary<string, Manager> dic = null;

                        if (!_settings.Managers.TryGetValue(manager.Section, out dic))
                        {
                            dic = new LockedDictionary<string, Manager>();
                            _settings.Managers[manager.Section] = dic;
                        }

                        Manager tempManager = null;

                        if (!dic.TryGetValue(signature, out tempManager)
                            || manager.CreationTime > tempManager.CreationTime)
                        {
                            dic[signature] = manager;
                        }
                    }
                }
            }
        }

        public void Upload(Creator creator)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                var now = DateTime.UtcNow;

                if (creator == null || creator.Section == null || creator.Section.Id == null || string.IsNullOrWhiteSpace(creator.Section.Name)
                    || (creator.CreationTime - now) > new TimeSpan(0, 0, 30, 0)
                    || creator.Certificate == null || !creator.VerifyCertificate()) return;

                var signature = creator.Certificate.ToString();

                lock (this.ThisLock)
                {
                    lock (_settings.ThisLock)
                    {
                        LockedDictionary<string, Creator> dic = null;

                        if (!_settings.Creators.TryGetValue(creator.Section, out dic))
                        {
                            dic = new LockedDictionary<string, Creator>();
                            _settings.Creators[creator.Section] = dic;
                        }

                        Creator tempCreator = null;

                        if (!dic.TryGetValue(signature, out tempCreator)
                            || creator.CreationTime > tempCreator.CreationTime)
                        {
                            dic[signature] = creator;
                        }
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
                _connectionsManagerThread.Priority = ThreadPriority.Lowest;
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

                _bandwidthLimit.In = _settings.BandwidthLimit;
                _bandwidthLimit.Out = _settings.BandwidthLimit;
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
                    new Library.Configuration.SettingsContext<long>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingsContext<LockedDictionary<Section, LockedDictionary<string, Leader>>>() { Name = "Leaders", Value = new LockedDictionary<Section, LockedDictionary<string, Leader>>() },
                    new Library.Configuration.SettingsContext<LockedDictionary<Section, LockedDictionary<string, Manager>>>() { Name = "Managers", Value = new LockedDictionary<Section, LockedDictionary<string, Manager>>() },
                    new Library.Configuration.SettingsContext<LockedDictionary<Section, LockedDictionary<string, Creator>>>() { Name = "Creators", Value = new LockedDictionary<Section, LockedDictionary<string, Creator>>() },
                    new Library.Configuration.SettingsContext<LockedDictionary<Channel, LockedHashSet<Message>>>() { Name = "Messages", Value = new LockedDictionary<Channel, LockedHashSet<Message>>() },
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

            public long BandwidthLimit
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (long)this["BandwidthLimit"];
                    }
                }
                set
                {
                    lock (this.ThisLock)
                    {
                        this["BandwidthLimit"] = value;
                    }
                }
            }

            public LockedDictionary<Section, LockedDictionary<string, Leader>> Leaders
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedDictionary<Section, LockedDictionary<string, Leader>>)this["Leaders"];
                    }
                }
            }

            public LockedDictionary<Section, LockedDictionary<string, Manager>> Managers
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedDictionary<Section, LockedDictionary<string, Manager>>)this["Managers"];
                    }
                }
            }

            public LockedDictionary<Section, LockedDictionary<string, Creator>> Creators
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (LockedDictionary<Section, LockedDictionary<string, Creator>>)this["Creators"];
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
