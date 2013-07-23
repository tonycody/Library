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

    class PullMailsEventArgs : EventArgs
    {
        public IEnumerable<MailHeader> MailHeaders { get; set; }
        public IEnumerable<MailContent> MailContents { get; set; }
    }

    class PullSectionsRequestEventArgs : EventArgs
    {
        public IEnumerable<Section> Sections { get; set; }
    }

    class PullProfilesEventArgs : EventArgs
    {
        public IEnumerable<ProfileHeader> ProfileHeaders { get; set; }
        public IEnumerable<ProfileContent> ProfileContents { get; set; }
    }

    class PullDocumentsEventArgs : EventArgs
    {
        public IEnumerable<DocumentHeader> DocumentHeaders { get; set; }
        public IEnumerable<DocumentContent> DocumentContents { get; set; }
    }

    class PullChannelsRequestEventArgs : EventArgs
    {
        public IEnumerable<Channel> Channels { get; set; }
    }

    class PullTopicsEventArgs : EventArgs
    {
        public IEnumerable<TopicHeader> TopicHeaders { get; set; }
        public IEnumerable<TopicContent> TopicContents { get; set; }
    }

    class PullMessagesEventArgs : EventArgs
    {
        public IEnumerable<MessageHeader> MessageHeaders { get; set; }
        public IEnumerable<MessageContent> MessageContents { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullMailsEventHandler(object sender, PullMailsEventArgs e);

    delegate void PullSectionsRequestEventHandler(object sender, PullSectionsRequestEventArgs e);
    delegate void PullProfilesEventHandler(object sender, PullProfilesEventArgs e);
    delegate void PullDocumentsEventHandler(object sender, PullDocumentsEventArgs e);

    delegate void PullChannelsRequestEventHandler(object sender, PullChannelsRequestEventArgs e);
    delegate void PullTopicsEventHandler(object sender, PullTopicsEventArgs e);
    delegate void PullMessagesEventHandler(object sender, PullMessagesEventArgs e);

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

            Mails = 5,

            SectionsRequest = 6,
            Profiles = 7,
            Documents = 8,

            ChannelsRequest = 9,
            Topics = 10,
            Messages = 11,
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

        public event PullMailsEventHandler PullMailsEvent;

        public event PullSectionsRequestEventHandler PullSectionsRequestEvent;
        public event PullProfilesEventHandler PullProfilesEvent;
        public event PullDocumentsEventHandler PullDocumentsEvent;

        public event PullChannelsRequestEventHandler PullChannelsRequestEvent;
        public event PullTopicsEventHandler PullTopicsEvent;
        public event PullMessagesEventHandler PullMessagesEvent;

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
                                else if (type == (byte)SerializeId.Mails)
                                {
                                    var message = MailsMessage.Import(stream2, _bufferManager);
                                    this.OnPullMails(new PullMailsEventArgs() { MailHeaders = message.MailHeaders, MailContents = message.MailContents });
                                }
                                else if (type == (byte)SerializeId.SectionsRequest)
                                {
                                    var message = SectionsRequestMessage.Import(stream2, _bufferManager);
                                    this.OnPullSectionsRequest(new PullSectionsRequestEventArgs() { Sections = message.Sections });
                                }
                                else if (type == (byte)SerializeId.Profiles)
                                {
                                    var message = ProfilesMessage.Import(stream2, _bufferManager);
                                    this.OnPullProfiles(new PullProfilesEventArgs() { ProfileHeaders = message.ProfileHeaders, ProfileContents = message.ProfileContents });
                                }
                                else if (type == (byte)SerializeId.Documents)
                                {
                                    var message = DocumentsMessage.Import(stream2, _bufferManager);
                                    this.OnPullDocuments(new PullDocumentsEventArgs() { DocumentHeaders = message.DocumentHeaders, DocumentContents = message.DocumentContents });
                                }
                                else if (type == (byte)SerializeId.ChannelsRequest)
                                {
                                    var message = ChannelsRequestMessage.Import(stream2, _bufferManager);
                                    this.OnPullChannelsRequest(new PullChannelsRequestEventArgs() { Channels = message.Channels });
                                }
                                else if (type == (byte)SerializeId.Topics)
                                {
                                    var message = TopicsMessage.Import(stream2, _bufferManager);
                                    this.OnPullTopics(new PullTopicsEventArgs() { TopicHeaders = message.TopicHeaders, TopicContents = message.TopicContents });
                                }
                                else if (type == (byte)SerializeId.Messages)
                                {
                                    var message = MessagesMessage.Import(stream2, _bufferManager);
                                    this.OnPullMessages(new PullMessagesEventArgs() { MessageHeaders = message.MessageHeaders, MessageContents = message.MessageContents });
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

        protected virtual void OnPullMails(PullMailsEventArgs e)
        {
            if (this.PullMailsEvent != null)
            {
                this.PullMailsEvent(this, e);
            }
        }

        protected virtual void OnPullSectionsRequest(PullSectionsRequestEventArgs e)
        {
            if (this.PullSectionsRequestEvent != null)
            {
                this.PullSectionsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullProfiles(PullProfilesEventArgs e)
        {
            if (this.PullProfilesEvent != null)
            {
                this.PullProfilesEvent(this, e);
            }
        }

        protected virtual void OnPullDocuments(PullDocumentsEventArgs e)
        {
            if (this.PullDocumentsEvent != null)
            {
                this.PullDocumentsEvent(this, e);
            }
        }

        protected virtual void OnPullChannelsRequest(PullChannelsRequestEventArgs e)
        {
            if (this.PullChannelsRequestEvent != null)
            {
                this.PullChannelsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullTopics(PullTopicsEventArgs e)
        {
            if (this.PullTopicsEvent != null)
            {
                this.PullTopicsEvent(this, e);
            }
        }

        protected virtual void OnPullMessages(PullMessagesEventArgs e)
        {
            if (this.PullMessagesEvent != null)
            {
                this.PullMessagesEvent(this, e);
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

        public void PushMails(IEnumerable<MailHeader> mailHeaders , IEnumerable<MailContent> mailContents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new MailsMessage();
                    message.MailHeaders.AddRange(mailHeaders);
                    message.MailContents.AddRange(mailContents);

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

        public void PushProfiles(IEnumerable<ProfileHeader> profileHeaders, IEnumerable<ProfileContent> profileContents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new ProfilesMessage();
                    message.ProfileHeaders.AddRange(profileHeaders);
                    message.ProfileContents.AddRange(profileContents);

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
    
        public void PushDocuments(IEnumerable<DocumentHeader> documentHeaders, IEnumerable<DocumentContent> documentContents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new DocumentsMessage();
                    message.DocumentHeaders.AddRange(documentHeaders);
                    message.DocumentContents.AddRange(documentContents);

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

        public void PushTopics(IEnumerable<TopicHeader> topicHeaders, IEnumerable<TopicContent> topicContents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new TopicsMessage();
                    message.TopicHeaders.AddRange(topicHeaders);
                    message.TopicContents.AddRange(topicContents);

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
       
        public void PushMessages(IEnumerable<MessageHeader> messageHeaders, IEnumerable<MessageContent> messageContents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new MessagesMessage();
                    message.MessageHeaders.AddRange(messageHeaders);
                    message.MessageContents.AddRange(messageContents);

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

        [DataContract(Name = "MailsMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class MailsMessage : ItemBase<MailsMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                MailHeader = 0,
                MailContent = 1,
            }

            private MailHeaderCollection _mailHeaders;
            private MailContentCollection _mailContents;

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
                            if (id == (byte)SerializeId.MailHeader)
                            {
                                this.MailHeaders.Add(MailHeader.Import(rangeStream, bufferManager));
                            }
                            else if (id == (byte)SerializeId.MailContent)
                            {
                                this.MailContents.Add(MailContent.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // MailHeaders
                foreach (var m in this.MailContents)
                {
                    Stream exportStream = m.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.MailContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // MailContent
                foreach (var m in this.MailContents)
                {
                    Stream exportStream = m.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.MailContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }

            public override MailsMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return MailsMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "MailHeaders")]
            public MailHeaderCollection MailHeaders
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_mailHeaders == null)
                            _mailHeaders = new MailHeaderCollection();

                        return _mailHeaders;
                    }
                }
            }

            [DataMember(Name = "MailContents")]
            public MailContentCollection MailContents
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_mailContents == null)
                            _mailContents = new MailContentCollection();

                        return _mailContents;
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

            private SectionCollection _sections;

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
                    foreach (var s in this.Sections)
                    {
                        Stream exportStream = s.Export(bufferManager);

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
                        if (_sections == null)
                            _sections = new SectionCollection(128);

                        return _sections;
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

        [DataContract(Name = "ProfilesMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class ProfilesMessage : ItemBase<ProfilesMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                ProfileHeader = 0,
                ProfileContent = 1,
            }

            private ProfileHeaderCollection _profileHeaders;
            private ProfileContentCollection _profileContents;

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
                            if (id == (byte)SerializeId.ProfileHeader)
                            {
                                this.ProfileHeaders.Add(ProfileHeader.Import(rangeStream, bufferManager));
                            }
                            else if (id == (byte)SerializeId.ProfileContent)
                            {
                                this.ProfileContents.Add(ProfileContent.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // ProfileHeaders
                foreach (var p in this.ProfileContents)
                {
                    Stream exportStream = p.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.ProfileContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // ProfileContent
                foreach (var p in this.ProfileContents)
                {
                    Stream exportStream = p.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.ProfileContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }

            public override ProfilesMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return ProfilesMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "ProfileHeaders")]
            public ProfileHeaderCollection ProfileHeaders
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_profileHeaders == null)
                            _profileHeaders = new ProfileHeaderCollection();

                        return _profileHeaders;
                    }
                }
            }

            [DataMember(Name = "ProfileContents")]
            public ProfileContentCollection ProfileContents
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_profileContents == null)
                            _profileContents = new ProfileContentCollection();

                        return _profileContents;
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

        [DataContract(Name = "DocumentsMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class DocumentsMessage : ItemBase<DocumentsMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                DocumentHeader = 0,
                DocumentContent = 1,
            }

            private DocumentHeaderCollection _documentHeaders;
            private DocumentContentCollection _documentContents;

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
                            if (id == (byte)SerializeId.DocumentHeader)
                            {
                                this.DocumentHeaders.Add(DocumentHeader.Import(rangeStream, bufferManager));
                            }
                            else if (id == (byte)SerializeId.DocumentContent)
                            {
                                this.DocumentContents.Add(DocumentContent.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // DocumentHeaders
                foreach (var d in this.DocumentContents)
                {
                    Stream exportStream = d.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.DocumentContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // DocumentContent
                foreach (var d in this.DocumentContents)
                {
                    Stream exportStream = d.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.DocumentContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }

            public override DocumentsMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return DocumentsMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "DocumentHeaders")]
            public DocumentHeaderCollection DocumentHeaders
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_documentHeaders == null)
                            _documentHeaders = new DocumentHeaderCollection();

                        return _documentHeaders;
                    }
                }
            }

            [DataMember(Name = "DocumentContents")]
            public DocumentContentCollection DocumentContents
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_documentContents == null)
                            _documentContents = new DocumentContentCollection();

                        return _documentContents;
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

        [DataContract(Name = "TopicsMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class TopicsMessage : ItemBase<TopicsMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                TopicHeader = 0,
                TopicContent = 1,
            }

            private TopicHeaderCollection _topicHeaders;
            private TopicContentCollection _topicContents;

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
                            if (id == (byte)SerializeId.TopicHeader)
                            {
                                this.TopicHeaders.Add(TopicHeader.Import(rangeStream, bufferManager));
                            }
                            else if (id == (byte)SerializeId.TopicContent)
                            {
                                this.TopicContents.Add(TopicContent.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // TopicHeaders
                foreach (var t in this.TopicContents)
                {
                    Stream exportStream = t.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.TopicContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // TopicContent
                foreach (var t in this.TopicContents)
                {
                    Stream exportStream = t.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.TopicContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }

            public override TopicsMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return TopicsMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "TopicHeaders")]
            public TopicHeaderCollection TopicHeaders
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_topicHeaders == null)
                            _topicHeaders = new TopicHeaderCollection();

                        return _topicHeaders;
                    }
                }
            }

            [DataMember(Name = "TopicContents")]
            public TopicContentCollection TopicContents
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_topicContents == null)
                            _topicContents = new TopicContentCollection();

                        return _topicContents;
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

        [DataContract(Name = "MessagesMessage", Namespace = "http://Library/Net/Lair/ConnectionManager")]
        private sealed class MessagesMessage : ItemBase<MessagesMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                MessageHeader = 0,
                MessageContent = 1,
            }

            private MessageHeaderCollection _messageHeaders;
            private MessageContentCollection _messageContents;

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
                            if (id == (byte)SerializeId.MessageHeader)
                            {
                                this.MessageHeaders.Add(MessageHeader.Import(rangeStream, bufferManager));
                            }
                            else if (id == (byte)SerializeId.MessageContent)
                            {
                                this.MessageContents.Add(MessageContent.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // MessageHeaders
                foreach (var m in this.MessageContents)
                {
                    Stream exportStream = m.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.MessageContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // MessageContent
                foreach (var m in this.MessageContents)
                {
                    Stream exportStream = m.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.MessageContent);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }

            public override MessagesMessage DeepClone()
            {
                lock (this.ThisLock)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        return MessagesMessage.Import(stream, BufferManager.Instance);
                    }
                }
            }

            [DataMember(Name = "MessageHeaders")]
            public MessageHeaderCollection MessageHeaders
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_messageHeaders == null)
                            _messageHeaders = new MessageHeaderCollection();

                        return _messageHeaders;
                    }
                }
            }

            [DataMember(Name = "MessageContents")]
            public MessageContentCollection MessageContents
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        if (_messageContents == null)
                            _messageContents = new MessageContentCollection();

                        return _messageContents;
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
