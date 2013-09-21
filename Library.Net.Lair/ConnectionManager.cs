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
using Library.Security;

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

    class PullSectionProfilesEventArgs : EventArgs
    {
        public IEnumerable<SectionProfile> SectionProfiles { get; set; }
        public IEnumerable<ArraySegment<byte>> Contents { get; set; }
    }

    class PullDocumentsRequestEventArgs : EventArgs
    {
        public IEnumerable<Document> Documents { get; set; }
    }

    class PullDocumentPagesEventArgs : EventArgs
    {
        public IEnumerable<DocumentPage> DocumentPages { get; set; }
        public IEnumerable<ArraySegment<byte>> Contents { get; set; }
    }

    class PullDocumentOpinionsEventArgs : EventArgs
    {
        public IEnumerable<DocumentOpinion> DocumentOpinions { get; set; }
        public IEnumerable<ArraySegment<byte>> Contents { get; set; }
    }

    class PullChatsRequestEventArgs : EventArgs
    {
        public IEnumerable<Chat> Chats { get; set; }
    }

    class PullChatTopicsEventArgs : EventArgs
    {
        public IEnumerable<ChatTopic> ChatTopics { get; set; }
        public IEnumerable<ArraySegment<byte>> Contents { get; set; }
    }

    class PullChatMessagesEventArgs : EventArgs
    {
        public IEnumerable<ChatMessage> ChatMessages { get; set; }
        public IEnumerable<ArraySegment<byte>> Contents { get; set; }
    }

    class PullWhispersRequestEventArgs : EventArgs
    {
        public IEnumerable<Whisper> Whispers { get; set; }
    }

    class PullWhisperMessagesEventArgs : EventArgs
    {
        public IEnumerable<WhisperMessage> WhisperMessages { get; set; }
        public IEnumerable<ArraySegment<byte>> Contents { get; set; }
    }

    class PullMailsRequestEventArgs : EventArgs
    {
        public IEnumerable<Mail> Mails { get; set; }
    }

    class PullMailMessagesEventArgs : EventArgs
    {
        public IEnumerable<MailMessage> MailMessages { get; set; }
        public IEnumerable<ArraySegment<byte>> Contents { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullSectionsRequestEventHandler(object sender, PullSectionsRequestEventArgs e);
    delegate void PullSectionProfilesEventHandler(object sender, PullSectionProfilesEventArgs e);

    delegate void PullDocumentsRequestEventHandler(object sender, PullDocumentsRequestEventArgs e);
    delegate void PullDocumentPagesEventHandler(object sender, PullDocumentPagesEventArgs e);
    delegate void PullDocumentOpinionsEventHandler(object sender, PullDocumentOpinionsEventArgs e);

    delegate void PullChatsRequestEventHandler(object sender, PullChatsRequestEventArgs e);
    delegate void PullChatTopicsEventHandler(object sender, PullChatTopicsEventArgs e);
    delegate void PullChatMessagesEventHandler(object sender, PullChatMessagesEventArgs e);

    delegate void PullWhispersRequestEventHandler(object sender, PullWhispersRequestEventArgs e);
    delegate void PullWhisperMessagesEventHandler(object sender, PullWhisperMessagesEventArgs e);

    delegate void PullMailsRequestEventHandler(object sender, PullMailsRequestEventArgs e);
    delegate void PullMailMessagesEventHandler(object sender, PullMailMessagesEventArgs e);

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
            SectionProfiles = 6,

            DocumentsRequest = 7,
            DocumentPages = 8,
            DocumentOpinions = 9,

            ChatsRequest = 10,
            ChatTopics = 11,
            ChatMessages = 12,

            WhispersRequest = 13,
            WhisperMessages = 14,

            MailsRequest = 15,
            MailMessages = 16,
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
        public event PullSectionProfilesEventHandler PullSectionProfilesEvent;

        public event PullDocumentsRequestEventHandler PullDocumentsRequestEvent;
        public event PullDocumentPagesEventHandler PullDocumentPagesEvent;
        public event PullDocumentOpinionsEventHandler PullDocumentOpinionsEvent;

        public event PullChatsRequestEventHandler PullChatsRequestEvent;
        public event PullChatTopicsEventHandler PullChatTopicsEvent;
        public event PullChatMessagesEventHandler PullChatMessagesEvent;

        public event PullWhispersRequestEventHandler PullWhispersRequestEvent;
        public event PullWhisperMessagesEventHandler PullWhisperMessagesEvent;

        public event PullMailsRequestEventHandler PullMailsRequestEvent;
        public event PullMailMessagesEventHandler PullMailMessagesEvent;

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
                                    var message = RequestMessage<Section>.Import(stream2, _bufferManager);
                                    this.OnPullSectionsRequest(new PullSectionsRequestEventArgs() { Sections = message.Tags });
                                }
                                else if (type == (byte)SerializeId.SectionProfiles)
                                {
                                    var message = ItemsMessage<SectionProfile>.Import(stream2, _bufferManager);
                                    this.OnPullSectionProfiles(new PullSectionProfilesEventArgs() { SectionProfiles = message.Items, Contents = message.Contents });
                                }
                                else if (type == (byte)SerializeId.DocumentsRequest)
                                {
                                    var message = RequestMessage<Document>.Import(stream2, _bufferManager);
                                    this.OnPullDocumentsRequest(new PullDocumentsRequestEventArgs() { Documents = message.Tags });
                                }
                                else if (type == (byte)SerializeId.DocumentPages)
                                {
                                    var message = ItemsMessage<DocumentPage>.Import(stream2, _bufferManager);
                                    this.OnPullDocumentPages(new PullDocumentPagesEventArgs() { DocumentPages = message.Items, Contents = message.Contents });
                                }
                                else if (type == (byte)SerializeId.DocumentOpinions)
                                {
                                    var message = ItemsMessage<DocumentOpinion>.Import(stream2, _bufferManager);
                                    this.OnPullDocumentOpinions(new PullDocumentOpinionsEventArgs() { DocumentOpinions = message.Items, Contents = message.Contents });
                                }
                                else if (type == (byte)SerializeId.ChatsRequest)
                                {
                                    var message = RequestMessage<Chat>.Import(stream2, _bufferManager);
                                    this.OnPullChatsRequest(new PullChatsRequestEventArgs() { Chats = message.Tags });
                                }
                                else if (type == (byte)SerializeId.ChatTopics)
                                {
                                    var message = ItemsMessage<ChatTopic>.Import(stream2, _bufferManager);
                                    this.OnPullChatTopics(new PullChatTopicsEventArgs() { ChatTopics = message.Items, Contents = message.Contents });
                                }
                                else if (type == (byte)SerializeId.ChatMessages)
                                {
                                    var message = ItemsMessage<ChatMessage>.Import(stream2, _bufferManager);
                                    this.OnPullChatMessages(new PullChatMessagesEventArgs() { ChatMessages = message.Items, Contents = message.Contents });
                                }
                                else if (type == (byte)SerializeId.WhispersRequest)
                                {
                                    var message = RequestMessage<Whisper>.Import(stream2, _bufferManager);
                                    this.OnPullWhispersRequest(new PullWhispersRequestEventArgs() { Whispers = message.Tags });
                                }
                                else if (type == (byte)SerializeId.WhisperMessages)
                                {
                                    var message = ItemsMessage<WhisperMessage>.Import(stream2, _bufferManager);
                                    this.OnPullWhisperMessages(new PullWhisperMessagesEventArgs() { WhisperMessages = message.Items, Contents = message.Contents });
                                }
                                else if (type == (byte)SerializeId.MailsRequest)
                                {
                                    var message = RequestMessage<Mail>.Import(stream2, _bufferManager);
                                    this.OnPullMailsRequest(new PullMailsRequestEventArgs() { Mails = message.Tags });
                                }
                                else if (type == (byte)SerializeId.MailMessages)
                                {
                                    var message = ItemsMessage<MailMessage>.Import(stream2, _bufferManager);
                                    this.OnPullMailMessages(new PullMailMessagesEventArgs() { MailMessages = message.Items, Contents = message.Contents });
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

        protected virtual void OnPullSectionProfiles(PullSectionProfilesEventArgs e)
        {
            if (this.PullSectionProfilesEvent != null)
            {
                this.PullSectionProfilesEvent(this, e);
            }
        }

        protected virtual void OnPullDocumentsRequest(PullDocumentsRequestEventArgs e)
        {
            if (this.PullDocumentsRequestEvent != null)
            {
                this.PullDocumentsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullDocumentPages(PullDocumentPagesEventArgs e)
        {
            if (this.PullDocumentPagesEvent != null)
            {
                this.PullDocumentPagesEvent(this, e);
            }
        }

        protected virtual void OnPullDocumentOpinions(PullDocumentOpinionsEventArgs e)
        {
            if (this.PullDocumentOpinionsEvent != null)
            {
                this.PullDocumentOpinionsEvent(this, e);
            }
        }

        protected virtual void OnPullChatsRequest(PullChatsRequestEventArgs e)
        {
            if (this.PullChatsRequestEvent != null)
            {
                this.PullChatsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullChatTopics(PullChatTopicsEventArgs e)
        {
            if (this.PullChatTopicsEvent != null)
            {
                this.PullChatTopicsEvent(this, e);
            }
        }

        protected virtual void OnPullChatMessages(PullChatMessagesEventArgs e)
        {
            if (this.PullChatMessagesEvent != null)
            {
                this.PullChatMessagesEvent(this, e);
            }
        }

        protected virtual void OnPullWhispersRequest(PullWhispersRequestEventArgs e)
        {
            if (this.PullWhispersRequestEvent != null)
            {
                this.PullWhispersRequestEvent(this, e);
            }
        }

        protected virtual void OnPullWhisperMessages(PullWhisperMessagesEventArgs e)
        {
            if (this.PullWhisperMessagesEvent != null)
            {
                this.PullWhisperMessagesEvent(this, e);
            }
        }

        protected virtual void OnPullMailsRequest(PullMailsRequestEventArgs e)
        {
            if (this.PullMailsRequestEvent != null)
            {
                this.PullMailsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullMailMessages(PullMailMessagesEventArgs e)
        {
            if (this.PullMailMessagesEvent != null)
            {
                this.PullMailMessagesEvent(this, e);
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

                    var message = new NodesMessage(nodes);

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

                    var message = new RequestMessage<Section>(sections);

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

        public void PushSectionProfiles(IEnumerable<SectionProfile> sectionProfiles, IEnumerable<ArraySegment<byte>> contents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.SectionProfiles);
                    stream.Flush();

                    var message = new ItemsMessage<SectionProfile>(sectionProfiles, contents);

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

        public void PushDocumentsRequest(IEnumerable<Document> documents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.DocumentsRequest);
                    stream.Flush();

                    var message = new RequestMessage<Document>(documents);

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

        public void PushDocumentPages(IEnumerable<DocumentPage> documentPages, IEnumerable<ArraySegment<byte>> contents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.DocumentPages);
                    stream.Flush();

                    var message = new ItemsMessage<DocumentPage>(documentPages, contents);

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

        public void PushDocumentOpinions(IEnumerable<DocumentOpinion> documentOpinions, IEnumerable<ArraySegment<byte>> contents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.DocumentOpinions);
                    stream.Flush();

                    var message = new ItemsMessage<DocumentOpinion>(documentOpinions, contents);

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

        public void PushChatsRequest(IEnumerable<Chat> chats)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.ChatsRequest);
                    stream.Flush();

                    var message = new RequestMessage<Chat>(chats);

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

        public void PushChatTopics(IEnumerable<ChatTopic> chatTopics, IEnumerable<ArraySegment<byte>> contents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.ChatTopics);
                    stream.Flush();

                    var message = new ItemsMessage<ChatTopic>(chatTopics, contents);

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

        public void PushChatMessages(IEnumerable<ChatMessage> chatMessages, IEnumerable<ArraySegment<byte>> contents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.ChatMessages);
                    stream.Flush();

                    var message = new ItemsMessage<ChatMessage>(chatMessages, contents);

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

        public void PushWhispersRequest(IEnumerable<Whisper> whispers)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.WhispersRequest);
                    stream.Flush();

                    var message = new RequestMessage<Whisper>(whispers);

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

        public void PushWhisperMessages(IEnumerable<WhisperMessage> whisperMessages, IEnumerable<ArraySegment<byte>> contents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.WhisperMessages);
                    stream.Flush();

                    var message = new ItemsMessage<WhisperMessage>(whisperMessages, contents);

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

        public void PushMailsRequest(IEnumerable<Mail> mails)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.MailsRequest);
                    stream.Flush();

                    var message = new RequestMessage<Mail>(mails);

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

        public void PushMailMessages(IEnumerable<MailMessage> mailMessages, IEnumerable<ArraySegment<byte>> contents)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version3)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.MailMessages);
                    stream.Flush();

                    var message = new ItemsMessage<MailMessage>(mailMessages, contents);

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

        private sealed class NodesMessage : ItemBase<NodesMessage>
        {
            private enum SerializeId : byte
            {
                Node = 0,
            }

            private NodeCollection _nodes = new NodeCollection();

            public NodesMessage(IEnumerable<Node> nodes)
            {
                _nodes.AddRange(nodes);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
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
                            _nodes.Add(Node.Import(rangeStream, bufferManager));
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
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return NodesMessage.Import(stream, BufferManager.Instance);
                }
            }

            public IEnumerable<Node> Nodes
            {
                get
                {
                    return _nodes;
                }
            }
        }

        private sealed class RequestMessage<T> : ItemBase<RequestMessage<T>>
            where T : ItemBase<T>, ITag
        {
            private enum SerializeId : byte
            {
                Tag = 0,
            }

            private List<T> _tags = new List<T>();

            public RequestMessage(IEnumerable<T> tags)
            {
                _tags.AddRange(tags);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
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
                        if (id == (byte)SerializeId.Tag)
                        {
                            _tags.Add(ItemBase<T>.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Tags
                foreach (var t in this.Tags)
                {
                    Stream exportStream = t.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Tag);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }

            public override RequestMessage<T> DeepClone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return RequestMessage<T>.Import(stream, BufferManager.Instance);
                }
            }

            public IEnumerable<T> Tags
            {
                get
                {
                    return _tags;
                }
            }
        }

        private sealed class ItemsMessage<T> : ItemBase<ItemsMessage<T>>
            where T : ItemBase<T>
        {
            private enum SerializeId : byte
            {
                Item = 0,
                Content = 1,
            }

            private List<T> _items = new List<T>();
            private List<ArraySegment<byte>> _contents = new List<ArraySegment<byte>>();

            public ItemsMessage(IEnumerable<T> items, IEnumerable<ArraySegment<byte>> contents)
            {
                _items.AddRange(items);
                _contents.AddRange(contents);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
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
                        if (id == (byte)SerializeId.Item)
                        {
                            _items.Add(ItemBase<T>.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.Content)
                        {
                            byte[] buff = bufferManager.TakeBuffer((int)rangeStream.Length);
                            rangeStream.Read(buff, 0, (int)rangeStream.Length);

                            _contents.Add(new ArraySegment<byte>(buff, 0, (int)rangeStream.Length));
                        }
                    }
                }
            }

            public override Stream Export(BufferManager bufferManager)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Items
                foreach (var i in this.Items)
                {
                    Stream exportStream = i.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Item);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // Contents
                foreach (var c in this.Contents)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)c.Count), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Content);
                    bufferStream.Write(c.Array, c.Offset, c.Count);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
            }

            public override ItemsMessage<T> DeepClone()
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return ItemsMessage<T>.Import(stream, BufferManager.Instance);
                }
            }

            public IEnumerable<T> Items
            {
                get
                {
                    return _items;
                }
            }

            public IEnumerable<ArraySegment<byte>> Contents
            {
                get
                {
                    return _contents;
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
