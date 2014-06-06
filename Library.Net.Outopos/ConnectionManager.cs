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
using Library.Collections;
using Library.Io;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Outopos
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

    class PullHeadersRequestEventArgs : EventArgs
    {
        public IEnumerable<Section> Sections { get; set; }
        public IEnumerable<Wiki> Wikis { get; set; }
        public IEnumerable<Chat> Chats { get; set; }
    }

    class PullHeadersEventArgs : EventArgs
    {
        public IEnumerable<SectionProfileHeader> SectionProfileHeaders { get; set; }
        public IEnumerable<SectionMessageHeader> SectionMessageHeaders { get; set; }
        public IEnumerable<WikiPageHeader> WikiPageHeaders { get; set; }
        public IEnumerable<ChatTopicHeader> ChatTopicHeaders { get; set; }
        public IEnumerable<ChatMessageHeader> ChatMessageHeaders { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullBlocksLinkEventHandler(object sender, PullBlocksLinkEventArgs e);
    delegate void PullBlocksRequestEventHandler(object sender, PullBlocksRequestEventArgs e);
    delegate void PullBlockEventHandler(object sender, PullBlockEventArgs e);

    delegate void PullHeadersRequestEventHandler(object sender, PullHeadersRequestEventArgs e);
    delegate void PullHeadersEventHandler(object sender, PullHeadersEventArgs e);

    delegate void PullCancelEventHandler(object sender, EventArgs e);

    delegate void CloseEventHandler(object sender, EventArgs e);

    [DataContract(Name = "ConnectDirection", Namespace = "http://Library/Net/Outopos")]
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

            HeadersRequest = 8,
            Headers = 9,
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

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 12, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 12, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 6, 0);

        private WatchTimer _aliveTimer;
        private Stopwatch _aliveStopwatch = new Stopwatch();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxNodeCount = 1024;
        private const int _maxBlockLinkCount = 8192;
        private const int _maxBlockRequestCount = 8192;
        private const int _maxHeaderRequestCount = 1024;
        private const int _maxHeaderCount = 1024;

        public event PullNodesEventHandler PullNodesEvent;

        public event PullBlocksLinkEventHandler PullBlocksLinkEvent;
        public event PullBlocksRequestEventHandler PullBlocksRequestEvent;
        public event PullBlockEventHandler PullBlockEvent;

        public event PullHeadersRequestEventHandler PullHeadersRequestEvent;
        public event PullHeadersEventHandler PullHeadersEvent;

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
                            xml.WriteStartElement("Outopos");
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
                                if (xml.LocalName == "Outopos")
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
                                    else if (type == (byte)SerializeId.HeadersRequest)
                                    {
                                        var message = HeadersRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullHeadersRequest(new PullHeadersRequestEventArgs()
                                        {
                                            Sections = message.Sections,
                                            Wikis = message.Wikis,
                                            Chats = message.Chats,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.Headers)
                                    {
                                        var message = HeadersMessage.Import(stream2, _bufferManager);

                                        this.OnPullHeaders(new PullHeadersEventArgs()
                                        {
                                            SectionProfileHeaders = message.SectionProfileHeaders,
                                            WikiPageHeaders = message.WikiPageHeaders,
                                            ChatTopicHeaders = message.ChatTopicHeaders,
                                            ChatMessageHeaders = message.ChatMessageHeaders,
                                            SectionMessageHeaders = message.SectionMessageHeaders,
                                        });
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

        protected virtual void OnPullHeadersRequest(PullHeadersRequestEventArgs e)
        {
            if (this.PullHeadersRequestEvent != null)
            {
                this.PullHeadersRequestEvent(this, e);
            }
        }

        protected virtual void OnPullHeaders(PullHeadersEventArgs e)
        {
            if (this.PullHeadersEvent != null)
            {
                this.PullHeadersEvent(this, e);
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

        public void PushHeadersRequest(
            IEnumerable<Section> sections,
            IEnumerable<Wiki> wikis,
            IEnumerable<Chat> chats)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.HeadersRequest);
                    stream.Flush();

                    var message = new HeadersRequestMessage(
                        sections,
                        wikis,
                        chats);

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

        public void PushHeaders(
            IEnumerable<SectionProfileHeader> sectionProfileHeaders,
            IEnumerable<SectionMessageHeader> sectionMessageHeaders,
            IEnumerable<WikiPageHeader> wikiPageHeaders,
            IEnumerable<ChatTopicHeader> chatTopicHeaders,
            IEnumerable<ChatMessageHeader> chatMessageHeaders)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Headers);
                    stream.Flush();

                    var message = new HeadersMessage(
                        sectionProfileHeaders,
                        sectionMessageHeaders,
                        wikiPageHeaders,
                        chatTopicHeaders,
                        chatMessageHeaders);

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
                        _readOnlyNodes = new ReadOnlyCollection<Node>(this.ProtectedNodes);

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
                        _readOnlyKeys = new ReadOnlyCollection<Key>(this.ProtectedKeys);

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
                        _readOnlyKeys = new ReadOnlyCollection<Key>(this.ProtectedKeys);

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

        private sealed class HeadersRequestMessage : ItemBase<HeadersRequestMessage>
        {
            private enum SerializeId : byte
            {
                Section = 0,
                Wiki = 1,
                Chat = 2,
            }

            private SectionCollection _sections;
            private WikiCollection _wikis;
            private ChatCollection _chats;

            public HeadersRequestMessage(
                IEnumerable<Section> sections,
                IEnumerable<Wiki> wikis,
                IEnumerable<Chat> chats)
            {
                if (sections != null) this.ProtectedSections.AddRange(sections);
                if (wikis != null) this.ProtectedWikis.AddRange(wikis);
                if (chats != null) this.ProtectedChats.AddRange(chats);
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
                        if (id == (byte)SerializeId.Section)
                        {
                            this.ProtectedSections.Add(Section.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.Wiki)
                        {
                            this.ProtectedWikis.Add(Wiki.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.Chat)
                        {
                            this.ProtectedChats.Add(Chat.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Sections
                foreach (var value in this.Sections)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Section, stream);
                    }
                }
                // Wikis
                foreach (var value in this.Wikis)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Wiki, stream);
                    }
                }
                // Chats
                foreach (var value in this.Chats)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Chat, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public HeadersRequestMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return HeadersRequestMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<Section> _readOnlySections;

            public IEnumerable<Section> Sections
            {
                get
                {
                    if (_readOnlySections == null)
                        _readOnlySections = new ReadOnlyCollection<Section>(this.ProtectedSections);

                    return _readOnlySections;
                }
            }

            [DataMember(Name = "Sections")]
            private SectionCollection ProtectedSections
            {
                get
                {
                    if (_sections == null)
                        _sections = new SectionCollection(_maxHeaderRequestCount);

                    return _sections;
                }
            }

            private volatile ReadOnlyCollection<Wiki> _readOnlyWikis;

            public IEnumerable<Wiki> Wikis
            {
                get
                {
                    if (_readOnlyWikis == null)
                        _readOnlyWikis = new ReadOnlyCollection<Wiki>(this.ProtectedWikis);

                    return _readOnlyWikis;
                }
            }

            [DataMember(Name = "Wikis")]
            private WikiCollection ProtectedWikis
            {
                get
                {
                    if (_wikis == null)
                        _wikis = new WikiCollection(_maxHeaderRequestCount);

                    return _wikis;
                }
            }

            private volatile ReadOnlyCollection<Chat> _readOnlyChats;

            public IEnumerable<Chat> Chats
            {
                get
                {
                    if (_readOnlyChats == null)
                        _readOnlyChats = new ReadOnlyCollection<Chat>(this.ProtectedChats);

                    return _readOnlyChats;
                }
            }

            [DataMember(Name = "Chats")]
            private ChatCollection ProtectedChats
            {
                get
                {
                    if (_chats == null)
                        _chats = new ChatCollection(_maxHeaderRequestCount);

                    return _chats;
                }
            }
        }

        private sealed class HeadersMessage : ItemBase<HeadersMessage>
        {
            private enum SerializeId : byte
            {
                SectionProfileHeader = 0,
                SectionMessageHeader = 1,
                WikiPageHeader = 2,
                ChatTopicHeader = 3,
                ChatMessageHeader = 4,
            }

            private LockedList<SectionProfileHeader> _sectionProfileHeaders;
            private LockedList<SectionMessageHeader> _sectionMessageHeaders;
            private LockedList<WikiPageHeader> _wikiPageHeaders;
            private LockedList<ChatTopicHeader> _chatTopicHeaders;
            private LockedList<ChatMessageHeader> _chatMessageHeaders;

            public HeadersMessage(
                IEnumerable<SectionProfileHeader> sectionProfileHeaders,
                IEnumerable<SectionMessageHeader> sectionMessageHeaders,
                IEnumerable<WikiPageHeader> wikiPageHeaders,
                IEnumerable<ChatTopicHeader> chatTopicHeaders,
                IEnumerable<ChatMessageHeader> chatMessageHeaders)
            {
                if (sectionProfileHeaders != null) this.ProtectedSectionProfileHeaders.AddRange(sectionProfileHeaders);
                if (sectionMessageHeaders != null) this.ProtectedSectionMessageHeaders.AddRange(sectionMessageHeaders);
                if (wikiPageHeaders != null) this.ProtectedWikiPageHeaders.AddRange(wikiPageHeaders);
                if (chatTopicHeaders != null) this.ProtectedChatTopicHeaders.AddRange(chatTopicHeaders);
                if (chatMessageHeaders != null) this.ProtectedChatMessageHeaders.AddRange(chatMessageHeaders);
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
                        if (id == (byte)SerializeId.SectionProfileHeader)
                        {
                            this.ProtectedSectionProfileHeaders.Add(SectionProfileHeader.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.SectionMessageHeader)
                        {
                            this.ProtectedSectionMessageHeaders.Add(SectionMessageHeader.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.WikiPageHeader)
                        {
                            this.ProtectedWikiPageHeaders.Add(WikiPageHeader.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.ChatTopicHeader)
                        {
                            this.ProtectedChatTopicHeaders.Add(ChatTopicHeader.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.ChatMessageHeader)
                        {
                            this.ProtectedChatMessageHeaders.Add(ChatMessageHeader.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // SectionProfileHeaders
                foreach (var value in this.SectionProfileHeaders)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.SectionProfileHeader, stream);
                    }
                }
                // SectionMessageHeaders
                foreach (var value in this.SectionMessageHeaders)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.SectionMessageHeader, stream);
                    }
                }
                // WikiPageHeaders
                foreach (var value in this.WikiPageHeaders)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.WikiPageHeader, stream);
                    }
                }
                // ChatTopicHeaders
                foreach (var value in this.ChatTopicHeaders)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.ChatTopicHeader, stream);
                    }
                }
                // ChatMessageHeaders
                foreach (var value in this.ChatMessageHeaders)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.ChatMessageHeader, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public HeadersMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return HeadersMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<SectionProfileHeader> _readOnlySectionProfileHeaders;

            public IEnumerable<SectionProfileHeader> SectionProfileHeaders
            {
                get
                {
                    if (_readOnlySectionProfileHeaders == null)
                        _readOnlySectionProfileHeaders = new ReadOnlyCollection<SectionProfileHeader>(this.ProtectedSectionProfileHeaders);

                    return _readOnlySectionProfileHeaders;
                }
            }

            [DataMember(Name = "SectionProfileHeaders")]
            private LockedList<SectionProfileHeader> ProtectedSectionProfileHeaders
            {
                get
                {
                    if (_sectionProfileHeaders == null)
                        _sectionProfileHeaders = new LockedList<SectionProfileHeader>(_maxHeaderCount);

                    return _sectionProfileHeaders;
                }
            }

            private volatile ReadOnlyCollection<SectionMessageHeader> _readOnlySectionMessageHeaders;

            public IEnumerable<SectionMessageHeader> SectionMessageHeaders
            {
                get
                {
                    if (_readOnlySectionMessageHeaders == null)
                        _readOnlySectionMessageHeaders = new ReadOnlyCollection<SectionMessageHeader>(this.ProtectedSectionMessageHeaders);

                    return _readOnlySectionMessageHeaders;
                }
            }

            [DataMember(Name = "SectionMessageHeaders")]
            private LockedList<SectionMessageHeader> ProtectedSectionMessageHeaders
            {
                get
                {
                    if (_sectionMessageHeaders == null)
                        _sectionMessageHeaders = new LockedList<SectionMessageHeader>(_maxHeaderCount);

                    return _sectionMessageHeaders;
                }
            }

            private volatile ReadOnlyCollection<WikiPageHeader> _readOnlyWikiPageHeaders;

            public IEnumerable<WikiPageHeader> WikiPageHeaders
            {
                get
                {
                    if (_readOnlyWikiPageHeaders == null)
                        _readOnlyWikiPageHeaders = new ReadOnlyCollection<WikiPageHeader>(this.ProtectedWikiPageHeaders);

                    return _readOnlyWikiPageHeaders;
                }
            }

            [DataMember(Name = "WikiPageHeaders")]
            private LockedList<WikiPageHeader> ProtectedWikiPageHeaders
            {
                get
                {
                    if (_wikiPageHeaders == null)
                        _wikiPageHeaders = new LockedList<WikiPageHeader>(_maxHeaderCount);

                    return _wikiPageHeaders;
                }
            }

            private volatile ReadOnlyCollection<ChatTopicHeader> _readOnlyChatTopicHeaders;

            public IEnumerable<ChatTopicHeader> ChatTopicHeaders
            {
                get
                {
                    if (_readOnlyChatTopicHeaders == null)
                        _readOnlyChatTopicHeaders = new ReadOnlyCollection<ChatTopicHeader>(this.ProtectedChatTopicHeaders);

                    return _readOnlyChatTopicHeaders;
                }
            }

            [DataMember(Name = "ChatTopicHeaders")]
            private LockedList<ChatTopicHeader> ProtectedChatTopicHeaders
            {
                get
                {
                    if (_chatTopicHeaders == null)
                        _chatTopicHeaders = new LockedList<ChatTopicHeader>(_maxHeaderCount);

                    return _chatTopicHeaders;
                }
            }

            private volatile ReadOnlyCollection<ChatMessageHeader> _readOnlyChatMessageHeaders;

            public IEnumerable<ChatMessageHeader> ChatMessageHeaders
            {
                get
                {
                    if (_readOnlyChatMessageHeaders == null)
                        _readOnlyChatMessageHeaders = new ReadOnlyCollection<ChatMessageHeader>(this.ProtectedChatMessageHeaders);

                    return _readOnlyChatMessageHeaders;
                }
            }

            [DataMember(Name = "ChatMessageHeaders")]
            private LockedList<ChatMessageHeader> ProtectedChatMessageHeaders
            {
                get
                {
                    if (_chatMessageHeaders == null)
                        _chatMessageHeaders = new LockedList<ChatMessageHeader>(_maxHeaderCount);

                    return _chatMessageHeaders;
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
