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
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private ConnectionsManager _connectionsManager;

        private ManagerState _state = ManagerState.Stop;

        public event UnlockSectionsEventHandler UnlockSectionsEvent;
        public event UnlockLeadersEventHandler UnlockLeadersEvent;
        public event UnlockManagersEventHandler UnlockManagersEvent;
        public event UnlockCreatorsEventHandler UnlockCreatorsEvent;

        public event UnlockChannelsEventHandler UnlockChannelsEvent;
        public event UnlockTopicsEventHandler UnlockTopicsEvent;
        public event UnlockMessagesEventHandler UnlockMessagesEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public LairManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;
            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _bufferManager);

            _connectionsManager.UnlockSectionsEvent += (object sender, ref IList<Section> channels) =>
            {
                if (this.UnlockSectionsEvent != null)
                {
                    this.UnlockSectionsEvent(this, ref channels);
                }
            };

            _connectionsManager.UnlockLeadersEvent += (object sender, Section section, ref IList<Leader> leaders) =>
            {
                if (this.UnlockLeadersEvent != null)
                {
                    this.UnlockLeadersEvent(this, section, ref leaders);
                }
            };

            _connectionsManager.UnlockManagersEvent += (object sender, Section section, ref IList<Manager> managers) =>
            {
                if (this.UnlockManagersEvent != null)
                {
                    this.UnlockManagersEvent(this, section, ref managers);
                }
            };

            _connectionsManager.UnlockCreatorsEvent += (object sender, Section section, ref IList<Creator> creators) =>
            {
                if (this.UnlockCreatorsEvent != null)
                {
                    this.UnlockCreatorsEvent(this, section, ref creators);
                }
            };

            _connectionsManager.UnlockChannelsEvent += (object sender, ref IList<Channel> channels) =>
            {
                if (this.UnlockChannelsEvent != null)
                {
                    this.UnlockChannelsEvent(this, ref channels);
                }
            };

            _connectionsManager.UnlockTopicsEvent += (object sender, Channel channel, ref IList<Topic> topics) =>
            {
                if (this.UnlockTopicsEvent != null)
                {
                    this.UnlockTopicsEvent(this, channel, ref topics);
                }
            };

            _connectionsManager.UnlockMessagesEvent += (object sender, Channel channel, ref IList<Message> messages) =>
            {
                if (this.UnlockMessagesEvent != null)
                {
                    this.UnlockMessagesEvent(this, channel, ref messages);
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

        public long BandWidthLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _connectionsManager.BandWidthLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    _connectionsManager.BandWidthLimit = value;
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

        public IEnumerable<Section> GetSections()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connectionsManager.GetSections();
            }
        }

        public IEnumerable<Leader> GetLeaders(Section section)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connectionsManager.GetLeaders(section);
            }
        }

        public IEnumerable<Creator> GetCreators(Section section)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connectionsManager.GetCreators(section);
            }
        }

        public IEnumerable<Manager> GetManagers(Section section)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connectionsManager.GetManagers(section);
            }
        }

        public void Upload(Leader leader)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.Upload(leader);
            }
        }

        public void Upload(Manager manager)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.Upload(manager);
            }
        }

        public void Upload(Creator creator)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.Upload(creator);
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

        public IEnumerable<Topic> GetTopics(Channel channel)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connectionsManager.GetTopics(channel);
            }
        }

        public IEnumerable<Message> GetMessages(Channel channel)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connectionsManager.GetMessages(channel);
            }
        }

        public void Upload(Topic topic)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _connectionsManager.Upload(topic);
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
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    this.Stop();
                }
                catch (Exception)
                {

                }

                _connectionsManager.Dispose();
                _serverManager.Dispose();
                _clientManager.Dispose();
            }

            _disposed = true;
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
