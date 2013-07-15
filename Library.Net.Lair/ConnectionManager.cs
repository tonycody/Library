using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using Library.Io;
using Library.Net.Connection;

namespace Library.Net.Lair
{
    class PullNodesEventArgs : EventArgs
    {
        public IEnumerable<Node> Nodes { get; set; }
    }

    class PullSectionsRequestEventArgs : EventArgs
    {
        public IEnumerable<Section> Sections { get; set; }
    }

    class PullSectionContentsEventArgs : EventArgs
    {
        public IEnumerable<Profile> Profiles { get; set; }
        public IEnumerable<Mail> Mails { get; set; }
    }

    class PullChannelsRequestEventArgs : EventArgs
    {
        public IEnumerable<Channel> Channels { get; set; }
    }

    class PullChannelContentsEventArgs : EventArgs
    {
        public IEnumerable<Topic> Topics { get; set; }
        public IEnumerable<Message> Messages { get; set; }
    }

    class PullArchivesRequestEventArgs : EventArgs
    {
        public IEnumerable<Archive> Archives { get; set; }
    }

    class PullArchiveContentsEventArgs : EventArgs
    {
        public IEnumerable<Document> Documents { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullSectionsRequestEventHandler(object sender, PullSectionsRequestEventArgs e);
    delegate void PullSectionContentsEventHandler(object sender, PullSectionContentsEventArgs e);

    delegate void PullChannelsRequestEventHandler(object sender, PullChannelsRequestEventArgs e);
    delegate void PullChannelContentsEventHandler(object sender, PullChannelContentsEventArgs e);

    delegate void PullArchivesRequestEventHandler(object sender, PullArchivesRequestEventArgs e);
    delegate void PullArchiveContentsEventHandler(object sender, PullArchiveContentsEventArgs e);

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

            SectionsRequest = 5,
            SectionContents = 6,

            ChannelsRequest = 7,
            ChannelContents = 8,

            ArchivesRequest = 9,
            ArchiveContents = 10,
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
        private bool _onClose = false;

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 12, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 12, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 6, 0);

        private System.Threading.Timer _aliveTimer;

        private object _thisLock = new object();
        private volatile bool _disposed = false;

        public event PullNodesEventHandler PullNodesEvent;

        public event PullSectionsRequestEventHandler PullSectionsRequestEvent;
        public event PullSectionContentsEventHandler PullSectionContentsEvent;

        public event PullChannelsRequestEventHandler PullChannelsRequestEvent;
        public event PullChannelContentsEventHandler PullChannelContentsEvent;

        public event PullArchivesRequestEventHandler PullArchivesRequestEvent;
        public event PullArchiveContentsEventHandler PullArchiveContentsEvent;

        public event PullCancelEventHandler PullCancelEvent;

        public event CloseEventHandler CloseEvent;

        public ConnectionManager(ConnectionBase connection, byte[] mySessionId, Node baseNode, ConnectionManagerType type, BufferManager bufferManager)
        {
            _connection = connection;
            _myProtocolVersion = ProtocolVersion.Version3;
            _baseNode = baseNode;
            _mySessionId = mySessionId;
            _bufferManager = bufferManager;
            _type = type;
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

                        if (_myProtocolVersion == ProtocolVersion.Version3)
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

                    if (_protocolVersion == ProtocolVersion.Version3)
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

            if (_protocolVersion == ProtocolVersion.Version3)
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

            if (_protocolVersion == ProtocolVersion.Version3)
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

            if (_protocolVersion == ProtocolVersion.Version3)
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

                    if (_protocolVersion == ProtocolVersion.Version3)
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
                                else if (type == (byte)SerializeId.SectionsRequest)
                                {
                                    var message = SectionsRequestMessage.Import(stream2, _bufferManager);
                                    this.OnPullSectionsRequest(new PullSectionsRequestEventArgs() { Sections = message.Sections });
                                }
                                else if (type == (byte)SerializeId.SectionContents)
                                {
                                    var message = SectionContentsMessage.Import(stream2, _bufferManager);
                                    this.OnPullSectionContents(new PullSectionContentsEventArgs() { Profiles = message.Profiles, Mails = message.Mails });
                                }
                                else if (type == (byte)SerializeId.ChannelsRequest)
                                {
                                    var message = ChannelsRequestMessage.Import(stream2, _bufferManager);
                                    this.OnPullChannelsRequest(new PullChannelsRequestEventArgs() { Channels = message.Channels });
                                }
                                else if (type == (byte)SerializeId.ChannelContents)
                                {
                                    var message = ChannelContentsMessage.Import(stream2, _bufferManager);
                                    this.OnPullChannelContents(new PullChannelContentsEventArgs() { Topics = message.Topics, Messages = message.Messages });
                                }
                                else if (type == (byte)SerializeId.ArchivesRequest)
                                {
                                    var message = ArchivesRequestMessage.Import(stream2, _bufferManager);
                                    this.OnPullArchivesRequest(new PullArchivesRequestEventArgs() { Archives = message.Archives });
                                }
                                else if (type == (byte)SerializeId.ArchiveContents)
                                {
                                    var message = ArchiveContentsMessage.Import(stream2, _bufferManager);
                                    this.OnPullArchiveContents(new PullArchiveContentsEventArgs() { Documents = message.Documents });
                                }
                            }
                        }
                    }
                    else
                    {
                        throw new ConnectionManagerException();
                    }

                    sw.Stop();

                    if (sw.ElapsedMilliseconds < 1000) Thread.Sleep(1000 - (int)sw.ElapsedMilliseconds);
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

        protected virtual void OnPullSectionsRequest(PullSectionsRequestEventArgs e)
        {
            if (this.PullSectionsRequestEvent != null)
            {
                this.PullSectionsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullSectionContents(PullSectionContentsEventArgs e)
        {
            if (this.PullSectionContentsEvent != null)
            {
                this.PullSectionContentsEvent(this, e);
            }
        }

        protected virtual void OnPullChannelsRequest(PullChannelsRequestEventArgs e)
        {
            if (this.PullChannelsRequestEvent != null)
            {
                this.PullChannelsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullChannelContents(PullChannelContentsEventArgs e)
        {
            if (this.PullChannelContentsEvent != null)
            {
                this.PullChannelContentsEvent(this, e);
            }
        }

        protected virtual void OnPullArchivesRequest(PullArchivesRequestEventArgs e)
        {
            if (this.PullArchivesRequestEvent != null)
            {
                this.PullArchivesRequestEvent(this, e);
            }
        }

        protected virtual void OnPullArchiveContents(PullArchiveContentsEventArgs e)
        {
            if (this.PullArchiveContentsEvent != null)
            {
                this.PullArchiveContentsEvent(this, e);
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

            if (_protocolVersion == ProtocolVersion.Version3)
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

        public void PushSectionsRequest(IEnumerable<Section> sections)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.SectionsRequest);
                    stream.Flush();

                    var message = new SectionsRequestMessage();
                    message.Sections.AddRange(sections);

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

        public void PushSectionContents(IEnumerable<Profile> profiles, IEnumerable<Mail> mails)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.SectionsRequest);
                    stream.Flush();

                    var message = new SectionContentsMessage();
                    message.Profiles.AddRange(profiles);
                    message.Mails.AddRange(mails);

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

            if (_protocolVersion == ProtocolVersion.Version3)
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

        public void PushChannelContents(IEnumerable<Topic> topics, IEnumerable<Message> messages)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.ChannelsRequest);
                    stream.Flush();

                    var message = new ChannelContentsMessage();
                    message.Topics.AddRange(topics);
                    message.Messages.AddRange(messages);

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

        public void PushArchivesRequest(IEnumerable<Archive> archives)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.ArchivesRequest);
                    stream.Flush();

                    var message = new ArchivesRequestMessage();
                    message.Archives.AddRange(archives);

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

        public void PushArchiveContents(IEnumerable<Document> documents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.ArchivesRequest);
                    stream.Flush();

                    var message = new ArchiveContentsMessage();
                    message.Documents.AddRange(documents);

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

        public void PushCancel()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
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
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return NodesMessage.Import(stream, BufferManager.Instance);
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

        [DataContract(Name = "SectionsRequestMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class SectionsRequestMessage : ItemBase<SectionsRequestMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Section = 0,
            }

            private SectionCollection _channels;

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
                            if (id == (byte)SerializeId.Section)
                            {
                                this.Sections.Add(Section.Import(rangeStream, bufferManager));
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

                    // Sections
                    foreach (var c in this.Sections)
                    {
                        Stream exportStream = c.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Section);

                        streams.Add(new JoinStream(bufferStream, exportStream));
                    }

                    return new JoinStream(streams);
                }
            }

            public override SectionsRequestMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return SectionsRequestMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "Sections")]
            public SectionCollection Sections
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_channels == null)
                            _channels = new SectionCollection(128);

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

        [DataContract(Name = "SectionContentsMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class SectionContentsMessage : ItemBase<SectionContentsMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Profile = 0,
                Mail = 1,
            }

            private ProfileCollection _profiles;
            private MailCollection _mails;

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
                            if (id == (byte)SerializeId.Profile)
                            {
                                this.Profiles.Add(Profile.Import(rangeStream, bufferManager));
                            }
                            else if (id == (byte)SerializeId.Mail)
                            {
                                this.Mails.Add(Mail.Import(rangeStream, bufferManager));
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

                    // Profile
                    foreach (var p in this.Profiles)
                    {
                        Stream exportStream = p.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Profile);

                        streams.Add(new JoinStream(bufferStream, exportStream));
                    }
                    // Mail
                    foreach (var m in this.Mails)
                    {
                        Stream exportStream = m.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Profile);

                        streams.Add(new JoinStream(bufferStream, exportStream));
                    }

                    return new JoinStream(streams);
                }
            }

            public override SectionContentsMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return SectionContentsMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "Profiles")]
            public ProfileCollection Profiles
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_profiles == null)
                            _profiles = new ProfileCollection();

                        return _profiles;
                    }
                }
            }

            [DataMember(Name = "Mails")]
            public MailCollection Mails
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_mails == null)
                            _mails = new MailCollection();

                        return _mails;
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
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return ChannelsRequestMessage.Import(stream, BufferManager.Instance);
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

        [DataContract(Name = "ChannelContentsMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class ChannelContentsMessage : ItemBase<ChannelContentsMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Topic = 0,
                Message = 1,
            }

            private TopicCollection _topics;
            private MessageCollection _messages;

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
                            if (id == (byte)SerializeId.Topic)
                            {
                                this.Topics.Add(Topic.Import(rangeStream, bufferManager));
                            }
                            else if (id == (byte)SerializeId.Message)
                            {
                                this.Messages.Add(Message.Import(rangeStream, bufferManager));
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

                    // Topic
                    foreach (var t in this.Topics)
                    {
                        Stream exportStream = t.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Topic);

                        streams.Add(new JoinStream(bufferStream, exportStream));
                    }
                    // Message
                    foreach (var m in this.Messages)
                    {
                        Stream exportStream = m.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Topic);

                        streams.Add(new JoinStream(bufferStream, exportStream));
                    }

                    return new JoinStream(streams);
                }
            }

            public override ChannelContentsMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return ChannelContentsMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "Topics")]
            public TopicCollection Topics
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_topics == null)
                            _topics = new TopicCollection();

                        return _topics;
                    }
                }
            }

            [DataMember(Name = "Messages")]
            public MessageCollection Messages
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_messages == null)
                            _messages = new MessageCollection();

                        return _messages;
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

        [DataContract(Name = "ArchivesRequestMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class ArchivesRequestMessage : ItemBase<ArchivesRequestMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Archive = 0,
            }

            private ArchiveCollection _archives;

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
                            if (id == (byte)SerializeId.Archive)
                            {
                                this.Archives.Add(Archive.Import(rangeStream, bufferManager));
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

                    // Archives
                    foreach (var c in this.Archives)
                    {
                        Stream exportStream = c.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Archive);

                        streams.Add(new JoinStream(bufferStream, exportStream));
                    }

                    return new JoinStream(streams);
                }
            }

            public override ArchivesRequestMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return ArchivesRequestMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "Archives")]
            public ArchiveCollection Archives
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_archives == null)
                            _archives = new ArchiveCollection(128);

                        return _archives;
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

        [DataContract(Name = "ArchiveContentsMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class ArchiveContentsMessage : ItemBase<ArchiveContentsMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                Document = 0,
            }

            private DocumentCollection _documents;

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
                            if (id == (byte)SerializeId.Document)
                            {
                                this.Documents.Add(Document.Import(rangeStream, bufferManager));
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

                    // Document
                    foreach (var d in this.Documents)
                    {
                        Stream exportStream = d.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Document);

                        streams.Add(new JoinStream(bufferStream, exportStream));
                    }

                    return new JoinStream(streams);
                }
            }

            public override ArchiveContentsMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return ArchiveContentsMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "Documents")]
            public DocumentCollection Documents
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_documents == null)
                            _documents = new DocumentCollection(32);

                        return _documents;
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
