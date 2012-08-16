using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;
using System.IO;

namespace Library.Net.Lair
{
    public class LairManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private string _cachePath;
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private ConnectionsManager _connectionsManager;

        private ManagerState _state = ManagerState.Stop;

        public event UnlockChannelsEventHandler UnlockChannelsEvent;
        public event UnlockMessagesEventHandler UnlockMessagesEvent;
        public event UnlockFiltersEventHandler UnlockFiltersEvent;

        private bool _disposed = false;
        private object _thisLock = new object();

        public LairManager(string cachePath, BufferManager bufferManager)
        {
            _cachePath = cachePath;

            _bufferManager = bufferManager;
            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _bufferManager);

            _connectionsManager.UnlockChannelsEvent += (object sender, ref IList<Channel> channels) =>
            {
                if (this.UnlockChannelsEvent != null)
                {
                    this.UnlockChannelsEvent(this, ref channels);
                }
            };

            _connectionsManager.UnlockMessagesEvent += (object sender, Channel channel, ref IList<Message> messages) =>
            {
                if (this.UnlockMessagesEvent != null)
                {
                    this.UnlockMessagesEvent(this, channel, ref messages);
                }
            };

            _connectionsManager.UnlockFiltersEvent += (object sender, Channel channel, ref IList<Filter> filters) =>
            {
                if (this.UnlockFiltersEvent != null)
                {
                    this.UnlockFiltersEvent(this, channel, ref filters);
                }
            };
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _clientManager.Filters;
                }
            }
        }

        public UriCollection ListenUris
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _serverManager.ListenUris;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.BaseNode;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _connectionsManager.BaseNode = value;
                }
            }
        }

        public IEnumerable<Node> OtherNodes
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.OtherNodes;
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.ConnectionCountLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _connectionsManager.ConnectionCountLimit = value;
                }
            }
        }

        public int DownloadingConnectionCountLowerLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.DownloadingConnectionCountLowerLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _connectionsManager.DownloadingConnectionCountLowerLimit = value;
                }
            }
        }

        public int UploadingConnectionCountLowerLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.UploadingConnectionCountLowerLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _connectionsManager.UploadingConnectionCountLowerLimit = value;
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.ConnectionInformation;
                }
            }
        }

        public Information Information
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    List<InformationContext> contexts = new List<InformationContext>();
                    contexts.AddRange(_connectionsManager.Information);

                    return new Information(contexts);
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.ReceivedByteCount;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.SentByteCount;
                }
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.SetOtherNodes(nodes);
            }
        }

        public IEnumerable<Channel> GetChannels()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connectionsManager.GetChannels();
            }
        }

        public void GetChannelInfomation(Channel channel, out IList<Message> messages, out IList<Filter> filetrs)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.GetChannelInfomation(channel, out messages, out filetrs);
            }
        }

        public void Upload(Message message)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.Upload(message);
            }
        }

        public void Upload(Filter filter)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.Upload(filter);
            }
        }

        public override ManagerState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
            }
        }

        public override void Start()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _connectionsManager.Start();
            }
        }

        public override void Stop()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _connectionsManager.Stop();
            }
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                this.Stop();

                _clientManager.Load(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
            }
        }

        public void Save(string directoryPath)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.Save(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _serverManager.Save(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _clientManager.Save(System.IO.Path.Combine(directoryPath, "ClientManager"));
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            lock (this.ThisLock)
            {
                if (_disposed) return;

                if (disposing)
                {
                    this.Stop();

                    _connectionsManager.Dispose();
                    _serverManager.Dispose();
                    _clientManager.Dispose();
                }

                _disposed = true;
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
}
