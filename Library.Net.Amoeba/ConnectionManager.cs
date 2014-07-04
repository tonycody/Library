using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using Library.Io;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Amoeba
{
    class PullNodesEventArgs : EventArgs
    {
        public IEnumerable<Node> Nodes { get; set; }
    }

    class PullBlocksLinkEventArgs : EventArgs
    {
        public IEnumerable<Key> Keys { get; set; }
    }

    class PullBlocksRequestEventArgs : EventArgs
    {
        public IEnumerable<Key> Keys { get; set; }
    }

    class PullBlockEventArgs : EventArgs
    {
        public Key Key { get; set; }
        public ArraySegment<byte> Value { get; set; }
    }

    class PullSeedsRequestEventArgs : EventArgs
    {
        public IEnumerable<string> Signatures { get; set; }
    }

    class PullSeedsEventArgs : EventArgs
    {
        public IEnumerable<Seed> Seeds { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullBlocksLinkEventHandler(object sender, PullBlocksLinkEventArgs e);
    delegate void PullBlocksRequestEventHandler(object sender, PullBlocksRequestEventArgs e);
    delegate void PullBlockEventHandler(object sender, PullBlockEventArgs e);

    delegate void PullSeedsRequestEventHandler(object sender, PullSeedsRequestEventArgs e);
    delegate void PullSeedsEventHandler(object sender, PullSeedsEventArgs e);

    delegate void PullCancelEventHandler(object sender, EventArgs e);

    delegate void CloseEventHandler(object sender, EventArgs e);

    [DataContract(Name = "ConnectDirection", Namespace = "http://Library/Net/Amoeba")]
    public enum ConnectDirection
    {
        [EnumMember(Value = "In")]
        In = 0,

        [EnumMember(Value = "Out")]
        Out = 1,
    }

    class ConnectionManager : ManagerBase, IThisLock
    {
        private enum SerializeId : byte
        {
            Alive = 0,
            Cancel = 1,

            Ping = 2,
            Pong = 3,

            Nodes = 4,

            BlocksLink = 5,
            BlocksRequest = 6,
            Block = 7,

            SeedsRequest = 8,
            Seeds = 9,
        }

        private byte[] _mySessionId;
        private byte[] _otherSessionId;
        private Connection _connection;
        private ProtocolVersion _protocolVersion;
        private ProtocolVersion _myProtocolVersion;
        private ProtocolVersion _otherProtocolVersion;
        private Node _baseNode;
        private Node _otherNode;
        private BufferManager _bufferManager;

        private ConnectDirection _direction;

        private bool _onClose;

        private byte[] _pingHash;
        private Stopwatch _responseStopwatch = new Stopwatch();

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 3, 0);

        private WatchTimer _aliveTimer;
        private Stopwatch _aliveStopwatch = new Stopwatch();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxNodeCount = 1024;
        private const int _maxBlockLinkCount = 8192;
        private const int _maxBlockRequestCount = 8192;
        private const int _maxSeedRequestCount = 1024;
        private const int _maxSeedCount = 1024;

        public event PullNodesEventHandler PullNodesEvent;

        public event PullBlocksLinkEventHandler PullBlocksLinkEvent;
        public event PullBlocksRequestEventHandler PullBlocksRequestEvent;
        public event PullBlockEventHandler PullBlockEvent;

        public event PullSeedsRequestEventHandler PullSeedsRequestEvent;
        public event PullSeedsEventHandler PullSeedsEvent;

        public event PullCancelEventHandler PullCancelEvent;

        public event CloseEventHandler CloseEvent;

        public ConnectionManager(Connection connection, byte[] mySessionId, Node baseNode, ConnectDirection direction, BufferManager bufferManager)
        {
            _connection = connection;
            _mySessionId = mySessionId;
            _baseNode = baseNode;
            _direction = direction;
            _bufferManager = bufferManager;

            _myProtocolVersion = ProtocolVersion.Version1;
        }

        public byte[] SesstionId
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _otherSessionId;
                }
            }
        }

        public Node Node
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _otherNode;
                }
            }
        }

        public ConnectDirection Direction
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _direction;
                }
            }
        }

        public Connection Connection
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connection;
                }
            }
        }

        public ProtocolVersion ProtocolVersion
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _protocolVersion;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.ReceivedByteCount;
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.SentByteCount;
            }
        }

        public TimeSpan ResponseTime
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _responseStopwatch.Elapsed;
            }
        }

        public void Connect()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    TimeSpan timeout = new TimeSpan(0, 0, 30);

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    using (BufferStream stream = new BufferStream(_bufferManager))
                    using (XmlTextWriter xml = new XmlTextWriter(stream, new UTF8Encoding(false)))
                    {
                        xml.WriteStartDocument();

                        xml.WriteStartElement("Protocol");

                        if (_myProtocolVersion.HasFlag(ProtocolVersion.Version1))
                        {
                            xml.WriteStartElement("Amoeba");
                            xml.WriteAttributeString("Version", "1");
                            xml.WriteEndElement(); //Protocol
                        }

                        xml.WriteEndElement(); //Configuration

                        xml.WriteEndDocument();
                        xml.Flush();
                        stream.Flush();

                        stream.Seek(0, SeekOrigin.Begin);
                        _connection.Send(stream, timeout - stopwatch.Elapsed);
                    }

                    using (Stream stream = _connection.Receive(timeout - stopwatch.Elapsed))
                    using (XmlTextReader xml = new XmlTextReader(stream))
                    {
                        while (xml.Read())
                        {
                            if (xml.NodeType == XmlNodeType.Element)
                            {
                                if (xml.LocalName == "Amoeba")
                                {
                                    var version = xml.GetAttribute("Version");

                                    if (version == "1")
                                    {
                                        _otherProtocolVersion |= ProtocolVersion.Version1;
                                    }
                                }
                            }
                        }
                    }

                    _protocolVersion = _myProtocolVersion & _otherProtocolVersion;

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
                    {
                        using (Stream stream = new MemoryStream(_mySessionId))
                        {
                            _connection.Send(stream, timeout - stopwatch.Elapsed);
                        }

                        using (Stream stream = _connection.Receive(timeout - stopwatch.Elapsed))
                        {
                            if (stream.Length > 64) throw new ConnectionManagerException();

                            _otherSessionId = new byte[stream.Length];
                            stream.Read(_otherSessionId, 0, _otherSessionId.Length);
                        }

                        using (Stream stream = _baseNode.Export(_bufferManager))
                        {
                            _connection.Send(stream, timeout - stopwatch.Elapsed);
                        }

                        using (Stream stream = _connection.Receive(timeout - stopwatch.Elapsed))
                        {
                            _otherNode = Node.Import(stream, _bufferManager);
                        }

                        _aliveStopwatch.Restart();

                        _pingHash = new byte[64];

                        using (var rng = RandomNumberGenerator.Create())
                        {
                            rng.GetBytes(_pingHash);
                        }

                        _responseStopwatch.Start();
                        this.Ping(_pingHash);

                        ThreadPool.QueueUserWorkItem(this.Pull);
                        _aliveTimer = new WatchTimer(this.AliveTimer, new TimeSpan(0, 0, 30));
                    }
                    else
                    {
                        throw new ConnectionManagerException();
                    }
                }
                catch (Exception ex)
                {
                    throw new ConnectionManagerException(ex.Message, ex);
                }
            }
        }

        public void Close()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    _connection.Close(new TimeSpan(0, 0, 30));

                    this.OnClose(new EventArgs());
                }
                catch (Exception ex)
                {
                    throw new ConnectionManagerException(ex.Message, ex);
                }
            }
        }

        private void AliveTimer()
        {
            if (_disposed) return;

            Thread.CurrentThread.Name = "ConnectionManager_AliveTimer";

            try
            {
                if (_aliveStopwatch.Elapsed > _aliveTimeSpan)
                {
                    this.Alive();
                }
            }
            catch (Exception)
            {
                this.OnClose(new EventArgs());
            }
        }

        private void Alive()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Alive);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _aliveStopwatch.Restart();
                    }
                }
                catch (ConnectionException)
                {
                    if (!_disposed)
                    {
                        this.OnClose(new EventArgs());
                    }

                    throw;
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        private void Ping(byte[] value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Ping);
                        stream.Write(value, 0, value.Length);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _aliveStopwatch.Restart();
                    }
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        private void Pong(byte[] value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Pong);
                        stream.Write(value, 0, value.Length);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _aliveStopwatch.Restart();
                    }
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        private void Pull(object state)
        {
            Thread.CurrentThread.Name = "ConnectionManager_Pull";
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            try
            {
                Stopwatch sw = new Stopwatch();

                for (; ; )
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    sw.Restart();

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
                    {
                        using (Stream stream = _connection.Receive(_receiveTimeSpan))
                        {
                            if (stream.Length == 0) continue;

                            byte type = (byte)stream.ReadByte();

                            using (Stream stream2 = new RangeStream(stream, 1, stream.Length - 1, true))
                            {
                                try
                                {
                                    if (type == (byte)SerializeId.Alive)
                                    {

                                    }
                                    else if (type == (byte)SerializeId.Cancel)
                                    {
                                        this.OnPullCancel(new EventArgs());
                                    }
                                    else if (type == (byte)SerializeId.Ping)
                                    {
                                        if (stream2.Length > 64) continue;

                                        var buffer = new byte[stream2.Length];
                                        stream2.Read(buffer, 0, buffer.Length);

                                        this.Pong(buffer);
                                    }
                                    else if (type == (byte)SerializeId.Pong)
                                    {
                                        if (stream2.Length > 64) continue;

                                        var buffer = new byte[stream2.Length];
                                        stream2.Read(buffer, 0, buffer.Length);

                                        if (!CollectionUtilities.Equals(buffer, _pingHash)) continue;

                                        _responseStopwatch.Stop();
                                    }
                                    else if (type == (byte)SerializeId.Nodes)
                                    {
                                        var message = NodesMessage.Import(stream2, _bufferManager);
                                        this.OnPullNodes(new PullNodesEventArgs() { Nodes = message.Nodes });
                                    }
                                    else if (type == (byte)SerializeId.BlocksLink)
                                    {
                                        var message = BlocksLinkMessage.Import(stream2, _bufferManager);
                                        this.OnPullBlocksLink(new PullBlocksLinkEventArgs() { Keys = message.Keys });
                                    }
                                    else if (type == (byte)SerializeId.BlocksRequest)
                                    {
                                        var message = BlocksRequestMessage.Import(stream2, _bufferManager);
                                        this.OnPullBlocksRequest(new PullBlocksRequestEventArgs() { Keys = message.Keys });
                                    }
                                    else if (type == (byte)SerializeId.Block)
                                    {
                                        var message = BlockMessage.Import(stream2, _bufferManager);
                                        this.OnPullBlock(new PullBlockEventArgs() { Key = message.Key, Value = message.Value });
                                    }
                                    else if (type == (byte)SerializeId.SeedsRequest)
                                    {
                                        var message = SeedsRequestMessage.Import(stream2, _bufferManager);
                                        this.OnPullSeedsRequest(new PullSeedsRequestEventArgs() { Signatures = message.Signatures });
                                    }
                                    else if (type == (byte)SerializeId.Seeds)
                                    {
                                        var message = SeedsMessage.Import(stream2, _bufferManager);
                                        this.OnPullSeeds(new PullSeedsEventArgs() { Seeds = message.Seeds });
                                    }
                                }
                                catch (Exception)
                                {

                                }
                            }
                        }
                    }
                    else
                    {
                        throw new ConnectionManagerException();
                    }

                    sw.Stop();

                    if (300 > sw.ElapsedMilliseconds) Thread.Sleep(300 - (int)sw.ElapsedMilliseconds);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);

                if (!_disposed)
                {
                    this.OnClose(new EventArgs());
                }
            }
        }

        protected virtual void OnPullNodes(PullNodesEventArgs e)
        {
            if (this.PullNodesEvent != null)
            {
                this.PullNodesEvent(this, e);
            }
        }

        protected virtual void OnPullBlocksLink(PullBlocksLinkEventArgs e)
        {
            if (this.PullBlocksLinkEvent != null)
            {
                this.PullBlocksLinkEvent(this, e);
            }
        }

        protected virtual void OnPullBlocksRequest(PullBlocksRequestEventArgs e)
        {
            if (this.PullBlocksRequestEvent != null)
            {
                this.PullBlocksRequestEvent(this, e);
            }
        }

        protected virtual void OnPullBlock(PullBlockEventArgs e)
        {
            if (this.PullBlockEvent != null)
            {
                this.PullBlockEvent(this, e);
            }
        }

        protected virtual void OnPullSeedsRequest(PullSeedsRequestEventArgs e)
        {
            if (this.PullSeedsRequestEvent != null)
            {
                this.PullSeedsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullSeeds(PullSeedsEventArgs e)
        {
            if (this.PullSeedsEvent != null)
            {
                this.PullSeedsEvent(this, e);
            }
        }

        protected virtual void OnPullCancel(EventArgs e)
        {
            if (this.PullCancelEvent != null)
            {
                this.PullCancelEvent(this, e);
            }
        }

        protected virtual void OnClose(EventArgs e)
        {
            if (_onClose) return;
            _onClose = true;

            if (this.CloseEvent != null)
            {
                this.CloseEvent(this, e);
            }
        }

        public void PushNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new NodesMessage(nodes);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        public void PushBlocksLink(IEnumerable<Key> keys)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BlocksLink);
                    stream.Flush();

                    var message = new BlocksLinkMessage(keys);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        public void PushBlocksRequest(IEnumerable<Key> keys)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BlocksRequest);
                    stream.Flush();

                    var message = new BlocksRequestMessage(keys);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        public void PushBlock(Key key, ArraySegment<byte> value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Block);
                    stream.Flush();

                    var message = new BlockMessage(key, value);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    var contexts = new List<InformationContext>();
                    contexts.Add(new InformationContext("IsCompress", false));

                    _connection.Send(stream, _sendTimeSpan, new Information(contexts));
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        public void PushSeedsRequest(IEnumerable<string> signatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.SeedsRequest);
                    stream.Flush();

                    var message = new SeedsRequestMessage(signatures);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        public void PushSeeds(IEnumerable<Seed> seeds)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Seeds);
                    stream.Flush();

                    var message = new SeedsMessage(seeds);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        public void PushCancel()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Cancel);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _aliveStopwatch.Restart();
                    }
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        #region Message

        private sealed class NodesMessage : ItemBase<NodesMessage>
        {
            private enum SerializeId : byte
            {
                Node = 0,
            }

            private NodeCollection _nodes;

            public NodesMessage(IEnumerable<Node> nodes)
            {
                if (nodes != null) this.ProtectedNodes.AddRange(nodes);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Node)
                        {
                            this.ProtectedNodes.Add(Node.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Nodes
                foreach (var value in this.Nodes)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Node, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public NodesMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return NodesMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<Node> _readOnlyNodes;

            public IEnumerable<Node> Nodes
            {
                get
                {
                    if (_readOnlyNodes == null)
                        _readOnlyNodes = new ReadOnlyCollection<Node>(this.ProtectedNodes.ToArray());

                    return _readOnlyNodes;
                }
            }

            [DataMember(Name = "Nodes")]
            private NodeCollection ProtectedNodes
            {
                get
                {
                    if (_nodes == null)
                        _nodes = new NodeCollection(_maxNodeCount);

                    return _nodes;
                }
            }
        }

        private sealed class BlocksLinkMessage : ItemBase<BlocksLinkMessage>
        {
            private enum SerializeId : byte
            {
                Key = 0,
            }

            private KeyCollection _keys;

            public BlocksLinkMessage(IEnumerable<Key> keys)
            {
                if (keys != null) this.ProtectedKeys.AddRange(keys);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Key)
                        {
                            this.ProtectedKeys.Add(Key.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Keys
                foreach (var value in this.Keys)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public BlocksLinkMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return BlocksLinkMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<Key> _readOnlyKeys;

            public IEnumerable<Key> Keys
            {
                get
                {
                    if (_readOnlyKeys == null)
                        _readOnlyKeys = new ReadOnlyCollection<Key>(this.ProtectedKeys.ToArray());

                    return _readOnlyKeys;
                }
            }

            [DataMember(Name = "Keys")]
            private KeyCollection ProtectedKeys
            {
                get
                {
                    if (_keys == null)
                        _keys = new KeyCollection(_maxBlockLinkCount);

                    return _keys;
                }
            }
        }

        private sealed class BlocksRequestMessage : ItemBase<BlocksRequestMessage>
        {
            private enum SerializeId : byte
            {
                Key = 0,
            }

            private KeyCollection _keys;

            public BlocksRequestMessage(IEnumerable<Key> keys)
            {
                if (keys != null) this.ProtectedKeys.AddRange(keys);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Key)
                        {
                            this.ProtectedKeys.Add(Key.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Keys
                foreach (var value in this.Keys)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public BlocksRequestMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return BlocksRequestMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<Key> _readOnlyKeys;

            public IEnumerable<Key> Keys
            {
                get
                {
                    if (_readOnlyKeys == null)
                        _readOnlyKeys = new ReadOnlyCollection<Key>(this.ProtectedKeys.ToArray());

                    return _readOnlyKeys;
                }
            }

            [DataMember(Name = "Keys")]
            private KeyCollection ProtectedKeys
            {
                get
                {
                    if (_keys == null)
                        _keys = new KeyCollection(_maxBlockRequestCount);

                    return _keys;
                }
            }
        }

        private sealed class BlockMessage : ItemBase<BlockMessage>
        {
            private enum SerializeId : byte
            {
                Key = 0,
                Value = 1,
            }

            private Key _key;
            private ArraySegment<byte> _value;

            public BlockMessage(Key key, ArraySegment<byte> value)
            {
                this.Key = key;
                this.Value = value;
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Key)
                        {
                            this.Key = Key.Import(rangeStream, bufferManager);
                        }
                        else if (id == (byte)SerializeId.Value)
                        {
                            if (this.Value.Array != null)
                            {
                                bufferManager.ReturnBuffer(this.Value.Array);
                            }

                            byte[] buffer = null;

                            try
                            {
                                buffer = bufferManager.TakeBuffer((int)rangeStream.Length);
                                rangeStream.Read(buffer, 0, (int)rangeStream.Length);
                            }
                            catch (Exception e)
                            {
                                if (buffer != null)
                                {
                                    bufferManager.ReturnBuffer(buffer);
                                }

                                throw e;
                            }

                            this.Value = new ArraySegment<byte>(buffer, 0, (int)rangeStream.Length);
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Key
                if (this.Key != null)
                {
                    using (var stream = this.Key.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                    }
                }
                // Value
                if (this.Value.Array != null)
                {
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Value.Count), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Value);
                    bufferStream.Write(this.Value.Array, this.Value.Offset, this.Value.Count);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public BlockMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return BlockMessage.Import(stream, BufferManager.Instance);
                }
            }

            public Key Key
            {
                get
                {
                    return _key;
                }
                private set
                {
                    _key = value;
                }
            }

            public ArraySegment<byte> Value
            {
                get
                {
                    return _value;
                }
                private set
                {
                    _value = value;
                }
            }
        }

        private sealed class SeedsRequestMessage : ItemBase<SeedsRequestMessage>
        {
            private enum SerializeId : byte
            {
                Signature = 0,
            }

            private SignatureCollection _signatures;

            public SeedsRequestMessage(IEnumerable<string> signatures)
            {
                if (signatures != null) this.ProtectedSignatures.AddRange(signatures);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Signature)
                        {
                            this.ProtectedSignatures.Add(ItemUtilities.GetString(rangeStream));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Signatures
                foreach (var value in this.Signatures)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Signature, value);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public SeedsRequestMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return SeedsRequestMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<string> _readOnlySignatures;

            public IEnumerable<string> Signatures
            {
                get
                {
                    if (_readOnlySignatures == null)
                        _readOnlySignatures = new ReadOnlyCollection<string>(this.ProtectedSignatures.ToArray());

                    return _readOnlySignatures;
                }
            }

            [DataMember(Name = "Signatures")]
            private SignatureCollection ProtectedSignatures
            {
                get
                {
                    if (_signatures == null)
                        _signatures = new SignatureCollection(_maxSeedRequestCount);

                    return _signatures;
                }
            }
        }

        private sealed class SeedsMessage : ItemBase<SeedsMessage>
        {
            private enum SerializeId : byte
            {
                Seed = 0,
            }

            private SeedCollection _seeds;

            public SeedsMessage(IEnumerable<Seed> seeds)
            {
                if (seeds != null) this.ProtectedSeeds.AddRange(seeds);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Seed)
                        {
                            this.ProtectedSeeds.Add(Seed.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Seeds
                foreach (var value in this.Seeds)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Seed, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public SeedsMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return SeedsMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<Seed> _readOnlySeeds;

            public IEnumerable<Seed> Seeds
            {
                get
                {
                    if (_readOnlySeeds == null)
                        _readOnlySeeds = new ReadOnlyCollection<Seed>(this.ProtectedSeeds.ToArray());

                    return _readOnlySeeds;
                }
            }

            [DataMember(Name = "Seeds")]
            private SeedCollection ProtectedSeeds
            {
                get
                {
                    if (_seeds == null)
                        _seeds = new SeedCollection(_maxSeedCount);

                    return _seeds;
                }
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_aliveTimer != null)
                {
                    try
                    {
                        _aliveTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _aliveTimer = null;
                }

                if (_connection != null)
                {
                    try
                    {
                        _connection.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _connection = null;
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
    class ConnectionManagerException : ManagerException
    {
        public ConnectionManagerException() : base() { }
        public ConnectionManagerException(string message) : base(message) { }
        public ConnectionManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
