using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    public sealed class LairManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private ConnectionsManager _connectionsManager;

        private ManagerState _state = ManagerState.Stop;

        private RemoveSectionsEventHandler _removeSectionsEvent;
        private RemoveLeadersEventHandler _removeLeadersEvent;
        private RemoveCreatorsEventHandler _removeCreatorsEvent;
        private RemoveManagersEventHandler _removeManagersEvent;

        private RemoveChannelsEventHandler _removeChannelsEvent;
        private RemoveTopicsEventHandler _removeTopicsEvent;
        private RemoveMessagesEventHandler _removeMessagesEvent;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        public LairManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;
            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _bufferManager);

            _connectionsManager.RemoveSectionsEvent = (object sender) =>
            {
                if (_removeSectionsEvent != null)
                {
                    return _removeSectionsEvent(this);
                }

                return null;
            };

            _connectionsManager.RemoveLeadersEvent = (object sender, Section section) =>
            {
                if (_removeLeadersEvent != null)
                {
                    return _removeLeadersEvent(this, section);
                }

                return null;
            };

            _connectionsManager.RemoveManagersEvent = (object sender, Section section) =>
            {
                if (_removeManagersEvent != null)
                {
                    return _removeManagersEvent(this, section);
                }

                return null;
            };

            _connectionsManager.RemoveCreatorsEvent = (object sender, Section section) =>
            {
                if (_removeCreatorsEvent != null)
                {
                    return _removeCreatorsEvent(this, section);
                }

                return null;
            };

            _connectionsManager.RemoveChannelsEvent = (object sender) =>
            {
                if (_removeChannelsEvent != null)
                {
                    return _removeChannelsEvent(this);
                }

                return null;
            };

            _connectionsManager.RemoveTopicsEvent = (object sender, Channel channel) =>
            {
                if (_removeTopicsEvent != null)
                {
                    return _removeTopicsEvent(this, channel);
                }

                return null;
            };

            _connectionsManager.RemoveMessagesEvent = (object sender, Channel channel) =>
            {
                if (_removeMessagesEvent != null)
                {
                    return _removeMessagesEvent(this, channel);
                }

                return null;
            };
        }

        public RemoveSectionsEventHandler RemoveSectionsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeSectionsEvent = value;
                }
            }
        }

        public RemoveLeadersEventHandler RemoveLeadersEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeLeadersEvent = value;
                }
            }
        }

        public RemoveCreatorsEventHandler RemoveCreatorsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeCreatorsEvent = value;
                }
            }
        }

        public RemoveManagersEventHandler RemoveManagersEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeManagersEvent = value;
                }
            }
        }

        public RemoveChannelsEventHandler RemoveChannelsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeChannelsEvent = value;
                }
            }
        }

        public RemoveTopicsEventHandler RemoveTopicsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeTopicsEvent = value;
                }
            }
        }

        public RemoveMessagesEventHandler RemoveMessagesEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _removeMessagesEvent = value;
                }
            }
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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.BaseNode;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.BaseNode = value;
                }
            }
        }

        public IEnumerable<Node> OtherNodes
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.OtherNodes;
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionCountLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.ConnectionCountLimit = value;
                }
            }
        }

        public long BandWidthLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.BandWidthLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _connectionsManager.BandWidthLimit = value;
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionInformation;
                }
            }
        }

        public Information Information
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.ReceivedByteCount;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connectionsManager.SentByteCount;
                }
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.SetOtherNodes(nodes);
            }
        }

        public void SendRequest(Section section)
        {
            lock (this.ThisLock)
            {
                _connectionsManager.SendRequest(section);
            }
        }

        public void SendRequest(Channel channel)
        {
            lock (this.ThisLock)
            {
                _connectionsManager.SendRequest(channel);
            }
        }

        public IEnumerable<Section> GetSections()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetSections();
            }
        }

        public IEnumerable<Leader> GetLeaders(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetLeaders(section);
            }
        }

        public IEnumerable<Creator> GetCreators(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetCreators(section);
            }
        }

        public IEnumerable<Manager> GetManagers(Section section)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetManagers(section);
            }
        }

        public void Upload(Leader leader)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.Upload(leader);
            }
        }

        public void Upload(Manager manager)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.Upload(manager);
            }
        }

        public void Upload(Creator creator)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.Upload(creator);
            }
        }

        public IEnumerable<Channel> GetChannels()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetChannels();
            }
        }

        public IEnumerable<Topic> GetTopics(Channel channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetTopics(channel);
            }
        }

        public IEnumerable<Message> GetMessages(Channel channel)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _connectionsManager.GetMessages(channel);
            }
        }

        public void Upload(Topic topic)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.Upload(topic);
            }
        }

        public void Upload(Message message)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
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
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                this.Stop();

                _clientManager.Load(System.IO.Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _connectionsManager.Load(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _connectionsManager.Save(System.IO.Path.Combine(directoryPath, "ConnectionManager"));
                _serverManager.Save(System.IO.Path.Combine(directoryPath, "ServerManager"));
                _clientManager.Save(System.IO.Path.Combine(directoryPath, "ClientManager"));
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _connectionsManager.Dispose();
                _serverManager.Dispose();
                _clientManager.Dispose();
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
