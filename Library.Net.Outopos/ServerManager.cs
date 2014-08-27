using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using Library.Net.Connections;

namespace Library.Net.Outopos
{
    public delegate CapBase AcceptCapEventHandler(object sender, out string uri);

    class ServerManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;
        private Settings _settings;

        private Random _random = new Random();

        private Dictionary<string, TcpListener> _tcpListeners = new Dictionary<string, TcpListener>();
        private List<string> _oldListenUris = new List<string>();

        private Regex _regex = new Regex(@"(.*?):(.*):(\d*)");

        private WatchTimer _watchTimer;

        private volatile ManagerState _state = ManagerState.Stop;

        private AcceptCapEventHandler _acceptCapEvent;
        private CheckUriEventHandler _uriFilterEvent;

        private readonly SafeInteger _blockedCount = new SafeInteger();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxReceiveCount = 1024 * 1024 * 8;

        public ServerManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _watchTimer = new WatchTimer(this.WatchTimer, Timeout.Infinite);
        }

        public Information Information
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    List<InformationContext> contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("BlockedConnectionCount", (long)_blockedCount));

                    return new Information(contexts);
                }
            }
        }

        public AcceptCapEventHandler AcceptCapEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _acceptCapEvent = value;
                }
            }
        }

        public CheckUriEventHandler CheckUriEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _uriFilterEvent = value;
                }
            }
        }

        public UriCollection ListenUris
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.ListenUris;
                }
            }
        }

        protected virtual CapBase OnAcceptCapEvent(out string uri)
        {
            uri = null;

            if (_acceptCapEvent != null)
            {
                return _acceptCapEvent(this, out uri);
            }

            return null;
        }

        protected virtual bool OnCheckUriEvent(string uri)
        {
            if (_uriFilterEvent != null)
            {
                return _uriFilterEvent(this, uri);
            }

            return true;
        }

        public Connection AcceptConnection(out string uri, BandwidthLimit bandwidthLimit)
        {
            uri = null;
            List<IDisposable> garbages = new List<IDisposable>();

            try
            {
                Connection connection = null;

                foreach (var type in (new int[] { 0, 1 }).Randomize())
                {
                    if (this.State == ManagerState.Stop) return null;

                    if (type == 0)
                    {
                        lock (this.ThisLock)
                        {
                            foreach (var item in _tcpListeners)
                            {
                                if (item.Value.Pending())
                                {
                                    var socket = item.Value.AcceptTcpClient().Client;
                                    garbages.Add(socket);

                                    {
                                        var remoteEndPoint = (IPEndPoint)socket.RemoteEndPoint;

                                        uri = string.Format("tcp:{0}:{1}", remoteEndPoint.Address, remoteEndPoint.Port);
                                    }

                                    if (!this.OnCheckUriEvent(uri))
                                    {
                                        _blockedCount.Increment();

                                        continue;
                                    }

                                    var cap = new SocketCap(socket);
                                    garbages.Add(cap);

                                    connection = new BaseConnection(cap, bandwidthLimit, _maxReceiveCount, _bufferManager);
                                    garbages.Add(connection);
                                }
                            }
                        }
                    }
                    else if (type == 1)
                    {
                        // Overlay network
                        var cap = this.OnAcceptCapEvent(out uri);
                        if (cap == null) continue;

                        garbages.Add(cap);

                        connection = new BaseConnection(cap, bandwidthLimit, _maxReceiveCount, _bufferManager);
                        garbages.Add(connection);
                    }

                    if (connection != null) break;
                }

                if (connection == null) return null;

                var secureConnection = new SecureConnection(SecureConnectionVersion.Version3, SecureConnectionType.Accept, connection, null, _bufferManager);
                garbages.Add(secureConnection);

                secureConnection.Connect(new TimeSpan(0, 0, 30));

                var compressConnection = new CompressConnection(secureConnection, _maxReceiveCount, _bufferManager);
                garbages.Add(compressConnection);

                compressConnection.Connect(new TimeSpan(0, 0, 10));

                return compressConnection;
            }
            catch (Exception)
            {
                foreach (var item in garbages)
                {
                    item.Dispose();
                }
            }

            return null;
        }

        private void WatchTimer()
        {
            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;

                // 差分を更新。
                if (!CollectionUtilities.Equals(_oldListenUris, this.ListenUris))
                {
                    foreach (var item in _tcpListeners.ToArray())
                    {
                        if (this.ListenUris.Contains(item.Key)) continue;

                        item.Value.Stop();
                        _tcpListeners.Remove(item.Key);
                    }

                    foreach (var uri in this.ListenUris)
                    {
                        if (_tcpListeners.ContainsKey(uri)) continue;

                        var match = _regex.Match(uri);
                        if (!match.Success) continue;

                        if (match.Groups[1].Value == "tcp")
                        {
                            try
                            {
                                var listener = new TcpListener(IPAddress.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
                                listener.Start(3);
                                _tcpListeners[uri] = listener;
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }

                    _oldListenUris.Clear();
                    _oldListenUris.AddRange(this.ListenUris);
                }
            }
        }

        public override ManagerState State
        {
            get
            {
                return _state;
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    _watchTimer.Change(0, 1000 * 10);
                }
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;

                    _watchTimer.Change(Timeout.Infinite);

                    foreach (var tcpListener in _tcpListeners.Values)
                    {
                        tcpListener.Stop();
                    }

                    _tcpListeners.Clear();
                    _oldListenUris.Clear();
                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);
            }
        }

        public void Save(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Save(directoryPath);
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<UriCollection>() { Name = "ListenUris", Value = new UriCollection() },
                })
            {
                _thisLock = lockObject;
            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public UriCollection ListenUris
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (UriCollection)this["ListenUris"];
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_watchTimer != null)
                {
                    try
                    {
                        _watchTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _watchTimer = null;
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
    class ServerManagerException : StateManagerException
    {
        public ServerManagerException() : base() { }
        public ServerManagerException(string message) : base(message) { }
        public ServerManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
