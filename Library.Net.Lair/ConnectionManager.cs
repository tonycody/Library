using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using Library.Collections;
using Library.Io;
using Library.Net.Connection;

namespace Library.Net.Lair
{
    class PullNodesEventArgs : EventArgs
    {
        public IEnumerable<Node> Nodes { get; set; }
    }

    class PullChannelsRequestEventArgs : EventArgs
    {
        public IEnumerable<Channel> Channels { get; set; }
    }

    class PullMessageEventArgs : EventArgs
    {
        public Message Message { get; set; }
    }

    class PullFilterEventArgs : EventArgs
    {
        public Filter Filter { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullChannelsRequestEventHandler(object sender, PullChannelsRequestEventArgs e);
    delegate void PullMessageEventHandler(object sender, PullMessageEventArgs e);
    delegate void PullFilterEventHandler(object sender, PullFilterEventArgs e);

    delegate void PullCancelEventHandler(object sender, EventArgs e);

    delegate void CloseEventHandler(object sender, EventArgs e);

    class ConnectionManager : ManagerBase, IThisLock
    {
        private enum SerializeId : byte
        {
            Alive = 0,
            Cancel = 1,

            Ping = 2,
            Pong = 3,

            Nodes = 4,

            ChannelsRequest = 5,
            Message = 6,
            Filter = 7,
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

        private DateTime _sendUpdateTime;
        private DateTime _pingTime = DateTime.UtcNow;
        private byte[] _pingHash;
        private TimeSpan _responseTime = TimeSpan.MaxValue;
        private bool _onClose = false;

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 12, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 12, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 6, 0);

        private System.Threading.Timer _aliveTimer;
        
        private object _thisLock = new object();
        private volatile bool _disposed = false;

        public event PullNodesEventHandler PullNodesEvent;

        public event PullChannelsRequestEventHandler PullChannelsRequestEvent;
        public event PullMessageEventHandler PullMessageEvent;
        public event PullFilterEventHandler PullFilterEvent;

        public event PullCancelEventHandler PullCancelEvent;

        public event CloseEventHandler CloseEvent;

        public ConnectionManager(ConnectionBase connection, byte[] mySessionId, Node baseNode, BufferManager bufferManager)
        {
            _connection = connection;
            _myProtocolVersion = ProtocolVersion.Version1;
            _baseNode = baseNode;
            _mySessionId = mySessionId;
            _bufferManager = bufferManager;
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
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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

                        if (_myProtocolVersion == ProtocolVersion.Version1)
                        {
                            xml.WriteStartElement("Lair");
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
                                if (xml.LocalName == "Lair")
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

                    if (_protocolVersion == ProtocolVersion.Version1)
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

                        ThreadPool.QueueUserWorkItem(new WaitCallback(this.Pull));
                        _aliveTimer = new Timer(new TimerCallback(this.AliveTimer), null, 1000 * 60, 1000 * 60);

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

        private bool _aliveSending = false;

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

            if (_protocolVersion == ProtocolVersion.Version1)
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

            if (_protocolVersion == ProtocolVersion.Version1)
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

            if (_protocolVersion == ProtocolVersion.Version1)
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
                for (; ; )
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    if (_protocolVersion == ProtocolVersion.Version1)
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
                                else if (type == (byte)SerializeId.ChannelsRequest)
                                {
                                    var message = ChannelsRequestMessage.Import(stream2, _bufferManager);
                                    this.OnPullChannelsRequest(new PullChannelsRequestEventArgs() { Channels = message.Channels });
                                }
                                else if (type == (byte)SerializeId.Message)
                                {
                                    var message = Message.Import(stream2, _bufferManager);
                                    this.OnPullMessage(new PullMessageEventArgs() { Message = message });
                                }
                                else if (type == (byte)SerializeId.Filter)
                                {
                                    var filter = Filter.Import(stream2, _bufferManager);
                                    this.OnPullFilter(new PullFilterEventArgs() { Filter = filter });
                                }
                            }
                        }
                    }
                    else
                    {
                        throw new ConnectionManagerException();
                    }
                }
            }
            catch (Exception)
            {
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

        protected virtual void OnPullChannelsRequest(PullChannelsRequestEventArgs e)
        {
            if (this.PullChannelsRequestEvent != null)
            {
                this.PullChannelsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullMessage(PullMessageEventArgs e)
        {
            if (this.PullMessageEvent != null)
            {
                this.PullMessageEvent(this, e);
            }
        }

        protected virtual void OnPullFilter(PullFilterEventArgs e)
        {
            if (this.PullFilterEvent != null)
            {
                this.PullFilterEvent(this, e);
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

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new NodesMessage();
                    message.Nodes.AddRange(nodes);

                    stream = new JoinStream(stream, message.Export(_bufferManager));

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

        public void PushChannelsRequest(IEnumerable<Channel> channels)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.ChannelsRequest);
                    stream.Flush();

                    var message = new ChannelsRequestMessage();
                    message.Channels.AddRange(channels);

                    stream = new JoinStream(stream, message.Export(_bufferManager));

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

        public void PushMessage(Message message)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Message);
                    stream.Flush();

                    stream = new JoinStream(stream, message.Export(_bufferManager));

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

        public void PushFilter(Filter filter)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Filter);
                    stream.Flush();

                    stream = new JoinStream(stream, filter.Export(_bufferManager));

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

            if (_protocolVersion == ProtocolVersion.Version1)
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

        [DataContract(Name = "NodesMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class NodesMessage : ItemBase<NodesMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Node = 0,
            }

            private NodeCollection _nodes;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
            {
                lock (this.ThisLock)
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
                                this.Nodes.Add(Node.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
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

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }

            public override NodesMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return NodesMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "Nodes")]
            public NodeCollection Nodes
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_nodes == null)
                            _nodes = new NodeCollection(128);

                        return _nodes;
                    }
                }
            }

            #region IThisLock

            public object ThisLock
            {
                get
                {
                    lock (_thisStaticLock)
                    {
                        if (_thisLock == null)
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
        }

        [DataContract(Name = "ChannelsRequestMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class ChannelsRequestMessage : ItemBase<ChannelsRequestMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Channel = 0,
            }

            private ChannelCollection _channels;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
            {
                lock (this.ThisLock)
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
                            if (id == (byte)SerializeId.Channel)
                            {
                                this.Channels.Add(Channel.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                lock (this.ThisLock)
                {
                    List<Stream> streams = new List<Stream>();
                    Encoding encoding = new UTF8Encoding(false);

                    // Channels
                    foreach (var c in this.Channels)
                    {
                        Stream exportStream = c.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Channel);

                        streams.Add(new JoinStream(bufferStream, exportStream));
                    }

                    return new JoinStream(streams);
                }
            }

            public override ChannelsRequestMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return ChannelsRequestMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "Channels")]
            public ChannelCollection Channels
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_channels == null)
                            _channels = new ChannelCollection(128);

                        return _channels;
                    }
                }
            }

            #region IThisLock

            public object ThisLock
            {
                get
                {
                    lock (_thisStaticLock)
                    {
                        if (_thisLock == null)
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_connection != null)
                    {
                        try
                        {
                            _aliveTimer.Dispose();
                        }
                        catch (Exception)
                        {

                        }

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

    [Serializable]
    class ConnectionManagerException : ManagerException
    {
        public ConnectionManagerException() : base() { }
        public ConnectionManagerException(string message) : base(message) { }
        public ConnectionManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
