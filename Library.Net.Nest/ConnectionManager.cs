using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using Library.Io;
using Library.Net.Connection;

namespace Library.Net.Nest
{
    public class MessagesEventArgs : EventArgs
    {
        public IEnumerable<CommandMessage> Messages
        {
            get;
            set;
        }
    }

    public delegate void PullMessagesEventHandler(object sender, MessagesEventArgs e);
    public delegate void CloseEventHandler(object sender, EventArgs e);

    public class ConnectionManager : ManagerBase, IThisLock
    {
        private enum SerializeId : byte
        {
            Alive = 0,
            Messages = 1,
        }

        private ConnectionBase _connection;
        private ProtocolVersion _protocolVersion;
        private ProtocolVersion _myProtocolVersion;
        private ProtocolVersion _otherProtocolVersion;
        private BufferManager _bufferManager;

        private DateTime _sendUpdateTime;
        private bool _onClose = false;

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 3, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 6, 0);

        private object _thisLock = new object();
        private bool _disposed = false;

        public event PullMessagesEventHandler PullMessagesEvent;
        public event CloseEventHandler CloseEvent;

        public ConnectionManager(ConnectionBase connection, BufferManager bufferManager)
        {
            _connection = connection;
            _bufferManager = bufferManager;

            _myProtocolVersion = ProtocolVersion.Version1;
        }

        public ProtocolVersion ProtocolVersion
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (_disposed)
                        throw new ObjectDisposedException(this.GetType().FullName);

                    return _protocolVersion;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.ReceivedByteCount;
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.SentByteCount;
            }
        }

        public void Connect()
        {
            if (_disposed)
                throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                try
                {
                    TimeSpan timeout = new TimeSpan(0, 30, 0);

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    using (BufferStream stream = new BufferStream(_bufferManager))
                    using (XmlTextWriter writer = new XmlTextWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.WriteStartDocument();

                        writer.WriteStartElement("Configuration");

                        if (_myProtocolVersion == ProtocolVersion.Version1)
                        {
                            writer.WriteStartElement("Protocol");
                            writer.WriteAttributeString("Version", "1");

                            writer.WriteEndElement(); //Protocol
                        }

                        writer.WriteEndElement(); //Configuration

                        writer.WriteEndDocument();
                        writer.Flush();
                        stream.Flush();

                        stream.Seek(0, SeekOrigin.Begin);
                        _connection.Send(stream, timeout - stopwatch.Elapsed);
                    }

                    using (Stream stream = _connection.Receive(timeout - stopwatch.Elapsed))
                    using (XmlTextReader reader = new XmlTextReader(stream))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                if (reader.LocalName == "Protocol")
                                {
                                    var version = reader.GetAttribute("Version");

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
                        _sendUpdateTime = DateTime.UtcNow;

                        ThreadPool.QueueUserWorkItem(new WaitCallback(this.Pull));
                        ThreadPool.QueueUserWorkItem(new WaitCallback(this.AliveTimer));
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
            if (_disposed)
                throw new ObjectDisposedException(this.GetType().FullName);

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
                    if (_disposed)
                        throw new ObjectDisposedException(this.GetType().FullName);

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
            if (_disposed)
                throw new ObjectDisposedException(this.GetType().FullName);

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

        private void Pull(object state)
        {
            Thread.CurrentThread.Name = "Pull";

            try
            {
                for (; ; )
                {
                    if (_disposed)
                        throw new ObjectDisposedException(this.GetType().FullName);

                    if (_protocolVersion == ProtocolVersion.Version1)
                    {
                        using (Stream stream = _connection.Receive(_receiveTimeSpan))
                        {
                            if (stream.Length == 0)
                                continue;

                            byte type = (byte)stream.ReadByte();

                            using (Stream stream2 = new RangeStream(stream, 1, stream.Length - 1, true))
                            {
                                if (type == (byte)SerializeId.Alive)
                                {

                                }
                                else if (type == (byte)SerializeId.Messages)
                                {
                                    var message = CommandsMessage.Import(stream2, _bufferManager);
                                    this.OnPullMessagesEvent(new MessagesEventArgs()
                                    {
                                        Messages = message.CommandMessages
                                    });
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

        protected virtual void OnPullMessagesEvent(MessagesEventArgs e)
        {
            if (this.PullMessagesEvent != null)
            {
                this.PullMessagesEvent(this, e);
            }
        }

        protected virtual void OnClose(EventArgs e)
        {
            if (_onClose)
                return;
            _onClose = true;

            if (this.CloseEvent != null)
            {
                this.CloseEvent(this, e);
            }
        }

        public void PushMessages(IEnumerable<CommandMessage> messages)
        {
            if (_disposed)
                throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion == ProtocolVersion.Version1)
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Messages);
                    stream.Flush();

                    var message = new CommandsMessage();
                    message.CommandMessages.AddRange(messages);

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

        [DataContract(Name = "CommandsMessage", Namespace = "http://Library/Net/Nest/ConnectionManager")]
        private class CommandsMessage : ItemBase<CommandsMessage>, IThisLock
        {
            private enum SerializeId : byte
            {
                CommandMessage = 0,
            }

            private List<CommandMessage> _commandMessages;
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
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length)
                            return;
                        int length = NetworkConverter.ToInt32(lengthBuffer);
                        byte id = (byte)stream.ReadByte();

                        using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                        {
                            if (id == (byte)SerializeId.CommandMessage)
                            {
                                this.CommandMessages.Add(CommandMessage.Import(rangeStream, bufferManager));
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

                    // CommandMessages
                    foreach (var m in this.CommandMessages)
                    {
                        Stream exportStream = m.Export(bufferManager);

                        BufferStream bufferStream = new BufferStream(bufferManager);
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.CommandMessage);

                        streams.Add(new AddStream(bufferStream, exportStream));
                    }

                    return new AddStream(streams);
                }
            }

            public override CommandsMessage DeepClone()
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    using (var bufferManager = new BufferManager())
                    using (var stream = this.Export(bufferManager))
                    {
                        return CommandsMessage.Import(stream, bufferManager);
                    }
                }
            }

            [DataMember(Name = "CommandMessages")]
            public List<CommandMessage> CommandMessages
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        if (_commandMessages == null)
                            _commandMessages = new List<CommandMessage>();

                        return _commandMessages;
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

        protected override void Dispose(bool disposing)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (!_disposed)
                {
                    if (disposing)
                    {

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
    public class ConnectionManagerException : ManagerException
    {
        public ConnectionManagerException() : base()
        {
        }
        public ConnectionManagerException(string message) : base(message)
        {
        }
        public ConnectionManagerException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
