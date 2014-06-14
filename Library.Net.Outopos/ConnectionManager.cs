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

    class PullBroadcastHeadersRequestEventArgs : EventArgs
    {
        public IEnumerable<string> Signatures { get; set; }
    }

    class PullBroadcastHeadersEventArgs : EventArgs
    {
        public IEnumerable<BroadcastProfileHeader> BroadcastProfileHeaders { get; set; }
    }

    class PullUnicastHeadersRequestEventArgs : EventArgs
    {
        public IEnumerable<string> Signatures { get; set; }
    }

    class PullUnicastHeadersEventArgs : EventArgs
    {
        public IEnumerable<UnicastMessageHeader> UnicastMessageHeaders { get; set; }
    }

    class PullMulticastHeadersRequestEventArgs : EventArgs
    {
        public IEnumerable<Wiki> Wikis { get; set; }
        public IEnumerable<Chat> Chats { get; set; }
    }

    class PullMulticastHeadersEventArgs : EventArgs
    {
        public IEnumerable<WikiPageHeader> WikiPageHeaders { get; set; }
        public IEnumerable<ChatTopicHeader> ChatTopicHeaders { get; set; }
        public IEnumerable<ChatMessageHeader> ChatMessageHeaders { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullBlocksLinkEventHandler(object sender, PullBlocksLinkEventArgs e);
    delegate void PullBlocksRequestEventHandler(object sender, PullBlocksRequestEventArgs e);
    delegate void PullBlockEventHandler(object sender, PullBlockEventArgs e);

    delegate void PullBroadcastHeadersRequestEventHandler(object sender, PullBroadcastHeadersRequestEventArgs e);
    delegate void PullBroadcastHeadersEventHandler(object sender, PullBroadcastHeadersEventArgs e);

    delegate void PullUnicastHeadersRequestEventHandler(object sender, PullUnicastHeadersRequestEventArgs e);
    delegate void PullUnicastHeadersEventHandler(object sender, PullUnicastHeadersEventArgs e);

    delegate void PullMulticastHeadersRequestEventHandler(object sender, PullMulticastHeadersRequestEventArgs e);
    delegate void PullMulticastHeadersEventHandler(object sender, PullMulticastHeadersEventArgs e);

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

            BroadcastHeadersRequest = 8,
            BroadcastHeaders = 9,

            UnicastHeadersRequest = 10,
            UnicastHeaders = 11,

            MulticastHeadersRequest = 12,
            MulticastHeaders = 13,
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

        public event PullBroadcastHeadersRequestEventHandler PullBroadcastHeadersRequestEvent;
        public event PullBroadcastHeadersEventHandler PullBroadcastHeadersEvent;

        public event PullUnicastHeadersRequestEventHandler PullUnicastHeadersRequestEvent;
        public event PullUnicastHeadersEventHandler PullUnicastHeadersEvent;

        public event PullMulticastHeadersRequestEventHandler PullMulticastHeadersRequestEvent;
        public event PullMulticastHeadersEventHandler PullMulticastHeadersEvent;

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
                                    else if (type == (byte)SerializeId.BroadcastHeadersRequest)
                                    {
                                        var message = BroadcastHeadersRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullBroadcastHeadersRequest(new PullBroadcastHeadersRequestEventArgs()
                                        {
                                            Signatures = message.Signatures,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.BroadcastHeaders)
                                    {
                                        var message = BroadcastHeadersMessage.Import(stream2, _bufferManager);

                                        this.OnPullBroadcastHeaders(new PullBroadcastHeadersEventArgs()
                                        {
                                            BroadcastProfileHeaders = message.BroadcastProfileHeaders,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.UnicastHeadersRequest)
                                    {
                                        var message = UnicastHeadersRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullUnicastHeadersRequest(new PullUnicastHeadersRequestEventArgs()
                                        {
                                            Signatures = message.Signatures,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.UnicastHeaders)
                                    {
                                        var message = UnicastHeadersMessage.Import(stream2, _bufferManager);

                                        this.OnPullUnicastHeaders(new PullUnicastHeadersEventArgs()
                                        {
                                            UnicastMessageHeaders = message.UnicastMessageHeaders,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.MulticastHeadersRequest)
                                    {
                                        var message = MulticastHeadersRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullMulticastHeadersRequest(new PullMulticastHeadersRequestEventArgs()
                                        {
                                            Wikis = message.Wikis,
                                            Chats = message.Chats,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.MulticastHeaders)
                                    {
                                        var message = MulticastHeadersMessage.Import(stream2, _bufferManager);

                                        this.OnPullMulticastHeaders(new PullMulticastHeadersEventArgs()
                                        {
                                            WikiPageHeaders = message.WikiPageHeaders,
                                            ChatTopicHeaders = message.ChatTopicHeaders,
                                            ChatMessageHeaders = message.ChatMessageHeaders,
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

        protected virtual void OnPullBroadcastHeadersRequest(PullBroadcastHeadersRequestEventArgs e)
        {
            if (this.PullBroadcastHeadersRequestEvent != null)
            {
                this.PullBroadcastHeadersRequestEvent(this, e);
            }
        }

        protected virtual void OnPullBroadcastHeaders(PullBroadcastHeadersEventArgs e)
        {
            if (this.PullBroadcastHeadersEvent != null)
            {
                this.PullBroadcastHeadersEvent(this, e);
            }
        }

        protected virtual void OnPullUnicastHeadersRequest(PullUnicastHeadersRequestEventArgs e)
        {
            if (this.PullUnicastHeadersRequestEvent != null)
            {
                this.PullUnicastHeadersRequestEvent(this, e);
            }
        }

        protected virtual void OnPullUnicastHeaders(PullUnicastHeadersEventArgs e)
        {
            if (this.PullUnicastHeadersEvent != null)
            {
                this.PullUnicastHeadersEvent(this, e);
            }
        }

        protected virtual void OnPullMulticastHeadersRequest(PullMulticastHeadersRequestEventArgs e)
        {
            if (this.PullMulticastHeadersRequestEvent != null)
            {
                this.PullMulticastHeadersRequestEvent(this, e);
            }
        }

        protected virtual void OnPullMulticastHeaders(PullMulticastHeadersEventArgs e)
        {
            if (this.PullMulticastHeadersEvent != null)
            {
                this.PullMulticastHeadersEvent(this, e);
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

        public void PushBroadcastHeadersRequest(IEnumerable<string> signatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BroadcastHeadersRequest);
                    stream.Flush();

                    var message = new BroadcastHeadersRequestMessage(signatures);

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

        public void PushBroadcastHeaders(
            IEnumerable<BroadcastProfileHeader> broadcastProfileHeaders)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BroadcastHeaders);
                    stream.Flush();

                    var message = new BroadcastHeadersMessage(
                        broadcastProfileHeaders);

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

        public void PushUnicastHeadersRequest(IEnumerable<string> signatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.UnicastHeadersRequest);
                    stream.Flush();

                    var message = new UnicastHeadersRequestMessage(signatures);

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

        public void PushUnicastHeaders(
            IEnumerable<UnicastMessageHeader> UnicastMessageHeaders)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.UnicastHeaders);
                    stream.Flush();

                    var message = new UnicastHeadersMessage(
                        UnicastMessageHeaders);

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

        public void PushMulticastHeadersRequest(
            IEnumerable<Wiki> wikis,
            IEnumerable<Chat> chats)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.MulticastHeadersRequest);
                    stream.Flush();

                    var message = new MulticastHeadersRequestMessage(
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

        public void PushMulticastHeaders(
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
                    stream.WriteByte((byte)SerializeId.MulticastHeaders);
                    stream.Flush();

                    var message = new MulticastHeadersMessage(
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

        private sealed class BroadcastHeadersRequestMessage : ItemBase<BroadcastHeadersRequestMessage>
        {
            private enum SerializeId : byte
            {
                Signature = 0,
            }

            private SignatureCollection _signatures;

            public BroadcastHeadersRequestMessage(IEnumerable<string> signatures)
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

            public BroadcastHeadersRequestMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return BroadcastHeadersRequestMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<string> _readOnlySignatures;

            public IEnumerable<string> Signatures
            {
                get
                {
                    if (_readOnlySignatures == null)
                        _readOnlySignatures = new ReadOnlyCollection<string>(this.ProtectedSignatures);

                    return _readOnlySignatures;
                }
            }

            [DataMember(Name = "Signatures")]
            private SignatureCollection ProtectedSignatures
            {
                get
                {
                    if (_signatures == null)
                        _signatures = new SignatureCollection(_maxHeaderRequestCount);

                    return _signatures;
                }
            }
        }

        private sealed class BroadcastHeadersMessage : ItemBase<BroadcastHeadersMessage>
        {
            private enum SerializeId : byte
            {
                BroadcastProfileHeader = 0,
            }

            private LockedList<BroadcastProfileHeader> _broadcastProfileHeaders;

            public BroadcastHeadersMessage(
                IEnumerable<BroadcastProfileHeader> broadcastProfileHeaders)
            {
                if (broadcastProfileHeaders != null) this.ProtectedBroadcastProfileHeaders.AddRange(broadcastProfileHeaders);
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
                        if (id == (byte)SerializeId.BroadcastProfileHeader)
                        {
                            this.ProtectedBroadcastProfileHeaders.Add(BroadcastProfileHeader.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // BroadcastProfileHeaders
                foreach (var value in this.BroadcastProfileHeaders)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.BroadcastProfileHeader, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public BroadcastHeadersMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return BroadcastHeadersMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<BroadcastProfileHeader> _readOnlyBroadcastProfileHeaders;

            public IEnumerable<BroadcastProfileHeader> BroadcastProfileHeaders
            {
                get
                {
                    if (_readOnlyBroadcastProfileHeaders == null)
                        _readOnlyBroadcastProfileHeaders = new ReadOnlyCollection<BroadcastProfileHeader>(this.ProtectedBroadcastProfileHeaders);

                    return _readOnlyBroadcastProfileHeaders;
                }
            }

            [DataMember(Name = "BroadcastProfileHeaders")]
            private LockedList<BroadcastProfileHeader> ProtectedBroadcastProfileHeaders
            {
                get
                {
                    if (_broadcastProfileHeaders == null)
                        _broadcastProfileHeaders = new LockedList<BroadcastProfileHeader>(_maxHeaderCount);

                    return _broadcastProfileHeaders;
                }
            }
        }

        private sealed class UnicastHeadersRequestMessage : ItemBase<UnicastHeadersRequestMessage>
        {
            private enum SerializeId : byte
            {
                Signature = 0,
            }

            private SignatureCollection _signatures;

            public UnicastHeadersRequestMessage(IEnumerable<string> signatures)
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

            public BroadcastHeadersRequestMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return BroadcastHeadersRequestMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<string> _readOnlySignatures;

            public IEnumerable<string> Signatures
            {
                get
                {
                    if (_readOnlySignatures == null)
                        _readOnlySignatures = new ReadOnlyCollection<string>(this.ProtectedSignatures);

                    return _readOnlySignatures;
                }
            }

            [DataMember(Name = "Signatures")]
            private SignatureCollection ProtectedSignatures
            {
                get
                {
                    if (_signatures == null)
                        _signatures = new SignatureCollection(_maxHeaderRequestCount);

                    return _signatures;
                }
            }
        }

        private sealed class UnicastHeadersMessage : ItemBase<UnicastHeadersMessage>
        {
            private enum SerializeId : byte
            {
                UnicastMessageHeader = 0,
            }

            private LockedList<UnicastMessageHeader> _unicastMessageHeaders;

            public UnicastHeadersMessage(
                IEnumerable<UnicastMessageHeader> unicastMessageHeaders)
            {
                if (unicastMessageHeaders != null) this.ProtectedUnicastMessageHeaders.AddRange(unicastMessageHeaders);
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
                        if (id == (byte)SerializeId.UnicastMessageHeader)
                        {
                            this.ProtectedUnicastMessageHeaders.Add(UnicastMessageHeader.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // UnicastMessageHeaders
                foreach (var value in this.UnicastMessageHeaders)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.UnicastMessageHeader, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public UnicastHeadersMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return UnicastHeadersMessage.Import(stream, BufferManager.Instance);
                }
            }

            private volatile ReadOnlyCollection<UnicastMessageHeader> _readOnlyUnicastMessageHeaders;

            public IEnumerable<UnicastMessageHeader> UnicastMessageHeaders
            {
                get
                {
                    if (_readOnlyUnicastMessageHeaders == null)
                        _readOnlyUnicastMessageHeaders = new ReadOnlyCollection<UnicastMessageHeader>(this.ProtectedUnicastMessageHeaders);

                    return _readOnlyUnicastMessageHeaders;
                }
            }

            [DataMember(Name = "UnicastMessageHeaders")]
            private LockedList<UnicastMessageHeader> ProtectedUnicastMessageHeaders
            {
                get
                {
                    if (_unicastMessageHeaders == null)
                        _unicastMessageHeaders = new LockedList<UnicastMessageHeader>(_maxHeaderCount);

                    return _unicastMessageHeaders;
                }
            }
        }

        private sealed class MulticastHeadersRequestMessage : ItemBase<MulticastHeadersRequestMessage>
        {
            private enum SerializeId : byte
            {
                Wiki = 0,
                Chat = 1,
            }

            private WikiCollection _wikis;
            private ChatCollection _chats;

            public MulticastHeadersRequestMessage(
                IEnumerable<Wiki> wikis,
                IEnumerable<Chat> chats)
            {
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
                        if (id == (byte)SerializeId.Wiki)
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

            public MulticastHeadersRequestMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return MulticastHeadersRequestMessage.Import(stream, BufferManager.Instance);
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

        private sealed class MulticastHeadersMessage : ItemBase<MulticastHeadersMessage>
        {
            private enum SerializeId : byte
            {
                WikiPageHeader = 0,
                ChatTopicHeader = 1,
                ChatMessageHeader = 2,
            }

            private LockedList<WikiPageHeader> _wikiPageHeaders;
            private LockedList<ChatTopicHeader> _chatTopicHeaders;
            private LockedList<ChatMessageHeader> _chatMessageHeaders;

            public MulticastHeadersMessage(
                IEnumerable<WikiPageHeader> wikiPageHeaders,
                IEnumerable<ChatTopicHeader> chatTopicHeaders,
                IEnumerable<ChatMessageHeader> chatMessageHeaders)
            {
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
                        if (id == (byte)SerializeId.WikiPageHeader)
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

            public MulticastHeadersMessage Clone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return MulticastHeadersMessage.Import(stream, BufferManager.Instance);
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
