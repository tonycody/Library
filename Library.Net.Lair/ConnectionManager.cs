using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using Library.Io;
using Library.Net.Connections;
using System.Collections.ObjectModel;
using Library.Collections;

namespace Library.Net.Lair
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
        public IEnumerable<WikiVoteHeader> WikiVoteHeaders { get; set; }
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

    enum ConnectionManagerType
    {
        Client,
        Server,
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
        private ConnectionBase _connection;
        private ProtocolVersion _protocolVersion;
        private ProtocolVersion _myProtocolVersion;
        private ProtocolVersion _otherProtocolVersion;
        private Node _baseNode;
        private Node _otherNode;
        private BufferManager _bufferManager;

        private ConnectionManagerType _type;

        private DateTime _sendUpdateTime;
        private DateTime _pingTime = DateTime.UtcNow;
        private byte[] _pingHash;
        private TimeSpan _responseTime = TimeSpan.MaxValue;
        private bool _onClose;

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 12, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 12, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 6, 0);

        private System.Threading.Timer _aliveTimer;

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

        public ConnectionManager(ConnectionBase connection, byte[] mySessionId, Node baseNode, ConnectionManagerType type, BufferManager bufferManager)
        {
            _connection = connection;
            _mySessionId = mySessionId;
            _baseNode = baseNode;
            _type = type;
            _bufferManager = bufferManager;

            _myProtocolVersion = ProtocolVersion.Version3;
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

        public ConnectionManagerType Type
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _type;
                }
            }
        }

        public ConnectionBase Connection
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

                return _responseTime;
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

                        if (_myProtocolVersion.HasFlag(ProtocolVersion.Version3))
                        {
                            xml.WriteStartElement("Lair");
                            xml.WriteAttributeString("Version", "3");
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
                                if (xml.LocalName == "Lair")
                                {
                                    var version = xml.GetAttribute("Version");

                                    if (version == "3")
                                    {
                                        _otherProtocolVersion |= ProtocolVersion.Version3;
                                    }
                                }
                            }
                        }
                    }

                    _protocolVersion = _myProtocolVersion & _otherProtocolVersion;

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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

                        _sendUpdateTime = DateTime.UtcNow;

                        ThreadPool.QueueUserWorkItem(this.Pull);
                        _aliveTimer = new Timer(this.AliveTimer, null, 1000 * 60, 1000 * 60);

                        _pingTime = DateTime.UtcNow;
                        _pingHash = new byte[64];
                        (new System.Security.Cryptography.RNGCryptoServiceProvider()).GetBytes(_pingHash);
                        this.Ping(_pingHash);
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

        private bool _aliveSending;

        private void AliveTimer(object state)
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                if (_aliveSending) return;
                _aliveSending = true;

                Thread.CurrentThread.Name = "ConnectionManager_AliveTimer";

                try
                {
                    if ((DateTime.UtcNow - _sendUpdateTime) > _aliveTimeSpan)
                    {
                        this.Alive();
                    }
                }
                catch (Exception)
                {
                    this.OnClose(new EventArgs());
                }
                finally
                {
                    _aliveSending = false;
                }
            }
            catch (Exception)
            {

            }
        }

        private void Alive()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Alive);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _sendUpdateTime = DateTime.UtcNow;
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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
                        _sendUpdateTime = DateTime.UtcNow;
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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
                        _sendUpdateTime = DateTime.UtcNow;
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

            try
            {
                Stopwatch sw = new Stopwatch();

                for (; ; )
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    sw.Restart();

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
                    {
                        using (Stream stream = _connection.Receive(_receiveTimeSpan))
                        {
                            if (stream.Length == 0) continue;

                            byte type = (byte)stream.ReadByte();

                            using (Stream stream2 = new RangeStream(stream, 1, stream.Length - 1, true))
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
                                    if (stream2.Length > 64) throw new ConnectionManagerException();

                                    var buffer = new byte[stream2.Length];
                                    stream2.Read(buffer, 0, buffer.Length);

                                    this.Pong(buffer);
                                }
                                else if (type == (byte)SerializeId.Pong)
                                {
                                    if (stream2.Length > 64) throw new ConnectionManagerException();

                                    var buffer = new byte[stream2.Length];
                                    stream2.Read(buffer, 0, buffer.Length);

                                    if (!Collection.Equals(buffer, _pingHash)) throw new ConnectionManagerException();

                                    _responseTime = DateTime.UtcNow - _pingTime;
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
                                        SectionMessageHeaders = message.SectionMessageHeaders,
                                        WikiPageHeaders = message.WikiPageHeaders,
                                        WikiVoteHeaders = message.WikiVoteHeaders,
                                        ChatTopicHeaders = message.ChatTopicHeaders,
                                        ChatMessageHeaders = message.ChatMessageHeaders,
                                    });
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
#if DEBUG
            catch (Exception e)
            {
                Log.Information(e);

                if (!_disposed)
                {
                    this.OnClose(new EventArgs());
                }
            }
#else
            catch (Exception)
            {
                if (!_disposed)
                {
                    this.OnClose(new EventArgs());
                }
            }
#endif
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new NodesMessage(nodes);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BlocksLink);
                    stream.Flush();

                    var message = new BlocksLinkMessage(keys);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BlocksRequest);
                    stream.Flush();

                    var message = new BlocksRequestMessage(keys);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Block);
                    stream.Flush();

                    var message = new BlockMessage(key, value);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
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

        public void PushHeadersRequest(IEnumerable<Section> sections,
                IEnumerable<Wiki> wikis,
                IEnumerable<Chat> chats)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.HeadersRequest);
                    stream.Flush();

                    var message = new HeadersRequestMessage(sections, wikis, chats);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
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

        public void PushHeaders(IEnumerable<SectionProfileHeader> sectionProfileHeaders,
                IEnumerable<SectionMessageHeader> sectionMessageHeaders,
                IEnumerable<WikiPageHeader> wikiPageHeaders,
                IEnumerable<WikiVoteHeader> wikiVoteHeaders,
                IEnumerable<ChatTopicHeader> chatTopicHeaders,
                IEnumerable<ChatMessageHeader> chatMessageHeaders)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Headers);
                    stream.Flush();

                    var message = new HeadersMessage(sectionProfileHeaders,
                        sectionMessageHeaders,
                        wikiPageHeaders,
                        wikiVoteHeaders,
                        chatTopicHeaders,
                        chatMessageHeaders);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Cancel);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _sendUpdateTime = DateTime.UtcNow;
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

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                Encoding encoding = new UTF8Encoding(false);
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
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Nodes
                foreach (var n in this.Nodes)
                {
                    Stream exportStream = n.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Node);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }

                return new UniteStream(streams);
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

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                Encoding encoding = new UTF8Encoding(false);
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
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Keys
                foreach (var k in this.Keys)
                {
                    Stream exportStream = k.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Key);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }

                return new UniteStream(streams);
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

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                Encoding encoding = new UTF8Encoding(false);
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
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Keys
                foreach (var k in this.Keys)
                {
                    Stream exportStream = k.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Key);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }

                return new UniteStream(streams);
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
                _key = key;
                _value = value;
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                try
                {
                    Encoding encoding = new UTF8Encoding(false);
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
                                _key = Key.Import(rangeStream, bufferManager);
                            }
                            else if (id == (byte)SerializeId.Value)
                            {
                                byte[] buff = bufferManager.TakeBuffer((int)rangeStream.Length);
                                rangeStream.Read(buff, 0, (int)rangeStream.Length);

                                _value = new ArraySegment<byte>(buff, 0, (int)rangeStream.Length);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    if (_value.Array != null)
                    {
                        bufferManager.ReturnBuffer(_value.Array);
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                List<Stream> streams = new List<Stream>();

                // Key
                if (this.Key != null)
                {
                    Stream exportStream = this.Key.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Key);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }
                // Value
                if (this.Value.Array != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Value.Count), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Value);
                    bufferStream.Write(this.Value.Array, this.Value.Offset, this.Value.Count);

                    streams.Add(bufferStream);
                }

                return new UniteStream(streams);
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
            }

            public ArraySegment<byte> Value
            {
                get
                {
                    return _value;
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

            public HeadersRequestMessage(IEnumerable<Section> sections,
                IEnumerable<Wiki> wikis,
                IEnumerable<Chat> chats)
            {
                if (sections != null) this.ProtectedSections.AddRange(sections);
                if (wikis != null) this.ProtectedWikis.AddRange(wikis);
                if (chats != null) this.ProtectedChats.AddRange(chats);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                Encoding encoding = new UTF8Encoding(false);
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
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Sections
                foreach (var s in this.Sections)
                {
                    Stream exportStream = s.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Section);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }
                // Wikis
                foreach (var a in this.Wikis)
                {
                    Stream exportStream = a.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Wiki);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }
                // Chats
                foreach (var c in this.Chats)
                {
                    Stream exportStream = c.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Chat);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }

                return new UniteStream(streams);
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
                        _sections = new SectionCollection(_maxHeaderCount);

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
                        _wikis = new WikiCollection(_maxHeaderCount);

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
                        _chats = new ChatCollection(_maxHeaderCount);

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
                WikiVoteHeader = 3,
                ChatTopicHeader = 4,
                ChatMessageHeader = 5,
            }

            private LockedList<SectionProfileHeader> _sectionProfileHeaders;
            private LockedList<SectionMessageHeader> _sectionMessageHeaders;
            private LockedList<WikiPageHeader> _wikiPageHeaders;
            private LockedList<WikiVoteHeader> _wikiVoteHeaders;
            private LockedList<ChatTopicHeader> _chatTopicHeaders;
            private LockedList<ChatMessageHeader> _chatMessageHeaders;

            public HeadersMessage(IEnumerable<SectionProfileHeader> sectionProfileHeaders,
                IEnumerable<SectionMessageHeader> sectionMessageHeaders,
                IEnumerable<WikiPageHeader> wikiPageHeaders,
                IEnumerable<WikiVoteHeader> wikiVoteHeaders,
                IEnumerable<ChatTopicHeader> chatTopicHeaders,
                IEnumerable<ChatMessageHeader> chatMessageHeaders)
            {
                if (sectionProfileHeaders != null) this.ProtectedSectionProfileHeaders.AddRange(sectionProfileHeaders);
                if (sectionMessageHeaders != null) this.ProtectedSectionMessageHeaders.AddRange(sectionMessageHeaders);
                if (wikiPageHeaders != null) this.ProtectedWikiPageHeaders.AddRange(wikiPageHeaders);
                if (wikiVoteHeaders != null) this.ProtectedWikiVoteHeaders.AddRange(wikiVoteHeaders);
                if (chatTopicHeaders != null) this.ProtectedChatTopicHeaders.AddRange(chatTopicHeaders);
                if (chatMessageHeaders != null) this.ProtectedChatMessageHeaders.AddRange(chatMessageHeaders);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                Encoding encoding = new UTF8Encoding(false);
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
                        else if (id == (byte)SerializeId.WikiVoteHeader)
                        {
                            this.ProtectedWikiVoteHeaders.Add(WikiVoteHeader.Import(rangeStream, bufferManager));
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
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // SectionProfileHeaders
                foreach (var h in this.SectionProfileHeaders)
                {
                    Stream exportStream = h.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.SectionProfileHeader);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }
                // SectionMessageHeaders
                foreach (var h in this.SectionMessageHeaders)
                {
                    Stream exportStream = h.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.SectionMessageHeader);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }
                // WikiPageHeaders
                foreach (var h in this.WikiPageHeaders)
                {
                    Stream exportStream = h.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.WikiPageHeader);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }
                // WikiVoteHeaders
                foreach (var h in this.WikiVoteHeaders)
                {
                    Stream exportStream = h.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.WikiVoteHeader);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }
                // ChatTopicHeaders
                foreach (var h in this.ChatTopicHeaders)
                {
                    Stream exportStream = h.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.ChatTopicHeader);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }
                // ChatMessageHeaders
                foreach (var h in this.ChatMessageHeaders)
                {
                    Stream exportStream = h.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.ChatMessageHeader);

                    streams.Add(new UniteStream(bufferStream, exportStream));
                }

                return new UniteStream(streams);
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

            private volatile ReadOnlyCollection<WikiVoteHeader> _readOnlyWikiVoteHeaders;

            public IEnumerable<WikiVoteHeader> WikiVoteHeaders
            {
                get
                {
                    if (_readOnlyWikiVoteHeaders == null)
                        _readOnlyWikiVoteHeaders = new ReadOnlyCollection<WikiVoteHeader>(this.ProtectedWikiVoteHeaders);

                    return _readOnlyWikiVoteHeaders;
                }
            }

            [DataMember(Name = "WikiVoteHeaders")]
            private LockedList<WikiVoteHeader> ProtectedWikiVoteHeaders
            {
                get
                {
                    if (_wikiVoteHeaders == null)
                        _wikiVoteHeaders = new LockedList<WikiVoteHeader>(_maxHeaderCount);

                    return _wikiVoteHeaders;
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
