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

namespace Library.Net.Amoeba
{
    class PullNodesEventArgs : EventArgs
    {
        public IEnumerable<Node> Nodes { get; set; }
    }

    class PullSeedsLinkEventArgs : EventArgs
    {
        public IEnumerable<Keyword> Keywords { get; set; }
    }

    class PullSeedsRequestEventArgs : EventArgs
    {
        public IEnumerable<Keyword> Keywords { get; set; }
    }

    class PullSeedsEventArgs : EventArgs
    {
        public Keyword Keyword { get; set; }
        public IEnumerable<Seed> Seeds { get; set; }
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

    delegate void PullNodesRequestEventHandler(object sender, EventArgs e);
    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);
    delegate void PullSeedsLinkEventHandler(object sender, PullSeedsLinkEventArgs e);
    delegate void PullSeedsRequestEventHandler(object sender, PullSeedsRequestEventArgs e);
    delegate void PullSeedsEventHandler(object sender, PullSeedsEventArgs e);
    delegate void PullBlocksLinkEventHandler(object sender, PullBlocksLinkEventArgs e);
    delegate void PullBlocksRequestEventHandler(object sender, PullBlocksRequestEventArgs e);
    delegate void PullBlockEventHandler(object sender, PullBlockEventArgs e);
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
            NodesRequest = 4,
            Nodes = 5,
            SeedsLink = 6,
            SeedsRequest = 7,
            Seeds = 8,
            BlocksLink = 9,
            BlocksRequest = 10,
            Block = 11,
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

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 3, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 6, 0);

        private object _thisLock = new object();
        private bool _disposed = false;

        public event PullNodesRequestEventHandler PullNodesRequestEvent;
        public event PullNodesEventHandler PullNodesEvent;
        public event PullSeedsLinkEventHandler PullSeedsLinkEvent;
        public event PullSeedsRequestEventHandler PullSeedsRequestEvent;
        public event PullSeedsEventHandler PullSeedsEvent;
        public event PullBlocksLinkEventHandler PullBlocksLinkEvent;
        public event PullBlocksRequestEventHandler PullBlocksRequestEvent;
        public event PullBlockEventHandler PullBlockEvent;
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

                using (DeadlockMonitor.Lock(this.ThisLock))
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

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _otherNode;
                }
            }
        }

        public ProtocolVersion ProtocolVersion
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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

            using (DeadlockMonitor.Lock(this.ThisLock))
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

                        xml.WriteStartElement("Configuration");

                        if (_myProtocolVersion == ProtocolVersion.Version1)
                        {
                            xml.WriteStartElement("Protocol");
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
                                if (xml.LocalName == "Protocol")
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
                        ThreadPool.QueueUserWorkItem(new WaitCallback(this.AliveTimer));
                        //ThreadPool.QueueUserWorkItem(new WaitCallback(this.PingTimer));

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

            using (DeadlockMonitor.Lock(this.ThisLock))
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

        private void AliveTimer(object state)
        {
            Thread.CurrentThread.Name = "AliveTimer";

            try
            {
                for (; ; )
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    while ((DateTime.UtcNow - _sendUpdateTime) < _sendTimeSpan)
                    {
                        Thread.Sleep(new TimeSpan(0, 0, 1));
                    }

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
                    this.OnClose(new EventArgs());
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
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        private void Pull(object state)
        {
            Thread.CurrentThread.Name = "Pull";

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
                                else if (type == (byte)SerializeId.NodesRequest)
                                {
                                    this.OnPullNodesRequest(new EventArgs());
                                }
                                else if (type == (byte)SerializeId.Nodes)
                                {
                                    var message = NodesMessage.Import(stream2, _bufferManager);
                                    this.OnPullNodes(new PullNodesEventArgs() { Nodes = message.Nodes });
                                }
                                else if (type == (byte)SerializeId.SeedsLink)
                                {
                                    var message = SeedLinkMessage.Import(stream2, _bufferManager);
                                    this.OnPullSeedsLink(new PullSeedsLinkEventArgs() { Keywords = message.Keywords });
                                }
                                else if (type == (byte)SerializeId.SeedsRequest)
                                {
                                    var message = SeedRequestMessage.Import(stream2, _bufferManager);
                                    this.OnPullSeedsRequest(new PullSeedsRequestEventArgs() { Keywords = message.Keywords });
                                }
                                else if (type == (byte)SerializeId.Seeds)
                                {
                                    var message = SeedMessage.Import(stream2, _bufferManager);
                                    this.OnPullSeeds(new PullSeedsEventArgs() { Keyword = message.Keyword, Seeds = message.Seeds });
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
                //Log.Warning(ex);

                if (!_disposed)
                {
                    this.OnClose(new EventArgs());
                }
            }
        }

        protected virtual void OnPullNodesRequest(EventArgs e)
        {
            if (this.PullNodesRequestEvent != null)
            {
                this.PullNodesRequestEvent(this, e);
            }
        }

        protected virtual void OnPullNodes(PullNodesEventArgs e)
        {
            if (this.PullNodesEvent != null)
            {
                this.PullNodesEvent(this, e);
            }
        }

        protected virtual void OnPullSeedsLink(PullSeedsLinkEventArgs e)
        {
            if (this.PullSeedsLinkEvent != null)
            {
                this.PullSeedsLinkEvent(this, e);
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

        public void PushNodesRequest()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.NodesRequest);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _sendUpdateTime = DateTime.UtcNow;
                    }
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());
                }
            }
            else
            {
                throw new ConnectionManagerException();
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

                    stream = new AddStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());
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

        public void PushSeedsLink(IEnumerable<Keyword> keywords)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.SeedsLink);
                    stream.Flush();

                    var message = new SeedLinkMessage();
                    message.Keywords.AddRange(keywords);

                    stream = new AddStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());
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

        public void PushSeedsRequest(IEnumerable<Keyword> keywords)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.SeedsRequest);
                    stream.Flush();

                    var message = new SeedRequestMessage();
                    message.Keywords.AddRange(keywords);

                    stream = new AddStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());
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

        public void PushSeeds(Keyword keyword, IEnumerable<Seed> seeds)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Seeds);
                    stream.Flush();

                    var message = new SeedMessage();
                    message.Keyword = keyword;
                    message.Seeds.AddRange(seeds);

                    stream = new AddStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());
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

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BlocksLink);
                    stream.Flush();

                    var message = new BlocksLinkMessage();
                    message.Keys.AddRange(keys);

                    stream = new AddStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());
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

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BlocksRequest);
                    stream.Flush();

                    var message = new BlocksRequestMessage();
                    message.Keys.AddRange(keys);

                    stream = new AddStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());
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

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Block);
                    stream.Flush();

                    var message = new BlockMessage();
                    message.Key = key;
                    message.Value = value;

                    stream = new AddStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _sendUpdateTime = DateTime.UtcNow;
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());
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
                }
            }
            else
            {
                throw new ConnectionManagerException();
            }
        }

        #region Message

        [DataContract(Name = "NodesMessage", Namespace = "http://Library/Net/Amoeba/ConnectionManager")]
        private class NodesMessage : ItemBase<NodesMessage>, IThisLock
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
                using (DeadlockMonitor.Lock(this.ThisLock))
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

                    streams.Add(new AddStream(bufferStream, exportStream));
                }

                return new AddStream(streams);
            }

            public override NodesMessage DeepClone()
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        if (_nodes == null)
                            _nodes = new NodeCollection();

                        return _nodes;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
                    {
                        if (_thisLock == null) 
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
        }

        [DataContract(Name = "SeedLinkMessage", Namespace = "http://Library/Net/Amoeba/ConnectionManager")]
        private class SeedLinkMessage : ItemBase<SeedLinkMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Keyword = 0,
            }

            private KeywordCollection _keywords;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                            if (id == (byte)SerializeId.Keyword)
                            {
                                this.Keywords.Add(Keyword.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<Stream> streams = new List<Stream>();

                    // Keywords
                    foreach (var k in this.Keywords)
                    {
                        Stream exportStream = k.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Keyword);

                        streams.Add(new AddStream(bufferStream, exportStream));
                    }

                    return new AddStream(streams);
                }
            }

            public override SeedLinkMessage DeepClone()
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return SeedLinkMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "Keywords")]
            public KeywordCollection Keywords
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        if (_keywords == null)
                            _keywords = new KeywordCollection();

                        return _keywords;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
                    {
                        if (_thisLock == null) 
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
        }

        [DataContract(Name = "SeedRequestMessage", Namespace = "http://Library/Net/Amoeba/ConnectionManager")]
        private class SeedRequestMessage : ItemBase<SeedRequestMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Keyword = 0,
            }

            private KeywordCollection _keywords;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                            if (id == (byte)SerializeId.Keyword)
                            {
                                this.Keywords.Add(Keyword.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<Stream> streams = new List<Stream>();

                    // Keywords
                    foreach (var k in this.Keywords)
                    {
                        Stream exportStream = k.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Keyword);

                        streams.Add(new AddStream(bufferStream, exportStream));
                    }

                    return new AddStream(streams);
                }
            }

            public override SeedRequestMessage DeepClone()
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return SeedRequestMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "Keywords")]
            public KeywordCollection Keywords
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        if (_keywords == null)
                            _keywords = new KeywordCollection();

                        return _keywords;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
                    {
                        if (_thisLock == null)
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
        }

        [DataContract(Name = "SeedMessage", Namespace = "http://Library/Net/Amoeba/ConnectionManager")]
        private class SeedMessage : ItemBase<SeedMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Keyword = 0,
                Seed = 1,
            }

            private Keyword _keyword;
            private SeedCollection _seeds;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                            if (id == (byte)SerializeId.Keyword)
                            {
                                this.Keyword = Keyword.Import(rangeStream, bufferManager);
                            }
                            else if (id == (byte)SerializeId.Seed)
                            {
                                this.Seeds.Add(Seed.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<Stream> streams = new List<Stream>();
                    Encoding encoding = new UTF8Encoding(false);

                    // Keyword
                    if (this.Keyword != null)
                    {
                        Stream exportStream = this.Keyword.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Keyword);

                        streams.Add(new AddStream(bufferStream, exportStream));
                    }
                    // Seeds
                    foreach (var s in this.Seeds)
                    {
                        Stream exportStream = s.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Seed);

                        streams.Add(new AddStream(bufferStream, exportStream));
                    }

                    return new AddStream(streams);
                }
            }

            public override SeedMessage DeepClone()
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return SeedMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "Keyword")]
            public Keyword Keyword
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _keyword;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        _keyword = value;
                    }
                }
            }

            [DataMember(Name = "Seeds")]
            public SeedCollection Seeds
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        if (_seeds == null)
                            _seeds = new SeedCollection();

                        return _seeds;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
                    {
                        if (_thisLock == null) 
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
        }

        [DataContract(Name = "BlocksLinkMessage", Namespace = "http://Library/Net/Amoeba/ConnectionManager")]
        private class BlocksLinkMessage : ItemBase<BlocksLinkMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Key = 0,
            }

            private KeyCollection _keys;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                                this.Keys.Add(Key.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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

                        streams.Add(new AddStream(bufferStream, exportStream));
                    }

                    return new AddStream(streams);
                }
            }

            public override BlocksLinkMessage DeepClone()
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return BlocksLinkMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "Keys")]
            public KeyCollection Keys
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        if (_keys == null)
                            _keys = new KeyCollection();

                        return _keys;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
                    {
                        if (_thisLock == null) 
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
        }

        [DataContract(Name = "BlocksRequestMessage", Namespace = "http://Library/Net/Amoeba/ConnectionManager")]
        private class BlocksRequestMessage : ItemBase<BlocksRequestMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Key = 0,
            }

            private KeyCollection _keys;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                                this.Keys.Add(Key.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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

                        streams.Add(new AddStream(bufferStream, exportStream));
                    }

                    return new AddStream(streams);
                }
            }

            public override BlocksRequestMessage DeepClone()
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return BlocksRequestMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "Keys")]
            public KeyCollection Keys
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        if (_keys == null)
                            _keys = new KeyCollection();

                        return _keys;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
                    {
                        if (_thisLock == null) 
                            _thisLock = new object();

                        return _thisLock;
                    }
                }
            }

            #endregion
        }

        [DataContract(Name = "BlockMessage", Namespace = "http://Library/Net/Amoeba/ConnectionManager")]
        private class BlockMessage : ItemBase<BlockMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Key = 0,
                Value = 1,
            }

            private Key _key;
            private ArraySegment<byte> _value;
            private object _thisLock;
            private static object _thisStaticLock = new object();

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                                this.Key = Key.Import(rangeStream, bufferManager);
                            }
                            else if (id == (byte)SerializeId.Value)
                            {
                                byte[] buff = new byte[(int)rangeStream.Length];
                                rangeStream.Read(buff, 0, buff.Length);

                                this.Value = new ArraySegment<byte>(buff, 0, (int)rangeStream.Length);
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    List<Stream> streams = new List<Stream>();

                    // Key
                    if (this.Key != null)
                    {
                        Stream exportStream = this.Key.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Key);

                        streams.Add(new AddStream(bufferStream, exportStream));
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

                    return new AddStream(streams);
                }
            }

            public override BlockMessage DeepClone()
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return BlockMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "Key")]
            public Key Key
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _key;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        _key = value;
                    }
                }
            }

            [DataMember(Name = "Value")]
            public ArraySegment<byte> Value
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return _value;
                    }
                }
                set
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        _value = value;
                    }
                }
            }

            #region IThisLock メンバ

            public object ThisLock
            {
                get
                {
                    using (DeadlockMonitor.Lock(_thisStaticLock))
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
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
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

                    _disposed = true;
                }
            }
        }

        #region IThisLock メンバ

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
