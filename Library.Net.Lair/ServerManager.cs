using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using Library.Net.Connection;

namespace Library.Net.Lair
{
    class ServerManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;
        private Settings _settings;

        private Dictionary<string, TcpListener> _tcpListeners = new Dictionary<string, TcpListener>();
        private List<string> _urisHistory = new List<string>();

        private System.Threading.Timer _watchTimer;

        private ManagerState _state = ManagerState.Stop;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        private const int _maxReceiveCount = 1024 * 1024 * 16;

        public ServerManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings();

            _watchTimer = new Timer(new TimerCallback(this.WatchTimer), null, Timeout.Infinite, Timeout.Infinite);
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

        public ConnectionBase AcceptConnection(out string uri, BandwidthLimit bandwidthLimit)
        {
            uri = null;
            ConnectionBase connection = null;

            try
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return null;

                    foreach (var item in _tcpListeners)
                    {
                        if (item.Value.Pending())
                        {
                            var socket = item.Value.AcceptTcpClient().Client;

                            uri = item.Key;
                            connection = new TcpConnection(socket, bandwidthLimit, _maxReceiveCount, _bufferManager);
                            break;
                        }
                    }
                }

                if (connection == null) return null;

                var secureConnection = new SecureConnection(SecureConnectionType.Server, SecureConnectionVersion.Version2, connection, null, _bufferManager);

                try
                {
                    secureConnection.Connect(new TimeSpan(0, 0, 30));
                }
                catch (Exception)
                {
                    secureConnection.Dispose();

                    throw;
                }

                var compressConnection = new CompressConnection(secureConnection, _maxReceiveCount, _bufferManager);

                try
                {
                    compressConnection.Connect(new TimeSpan(0, 0, 10));
                }
                catch (Exception)
                {
                    compressConnection.Dispose();

                    throw;
                }

                return compressConnection;
            }
            catch (Exception)
            {
                if (connection != null) connection.Dispose();
            }

            return null;
        }

        private void WatchTimer(object state)
        {
            lock (this.ThisLock)
            {
                if (!Collection.Equals(_urisHistory, this.ListenUris))
                {
                    // Stop
                    {
                        foreach (var tcpListener in _tcpListeners.Values)
                        {
                            tcpListener.Stop();
                        }

                        _tcpListeners.Clear();
                    }

                    // Start
                    {
                        Regex regex = new Regex(@"(.*?):(.*):(\d*)");

                        foreach (var uri in this.ListenUris)
                        {
                            var match = regex.Match(uri);
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
                    }

                    _urisHistory.Clear();
                    _urisHistory.AddRange(this.ListenUris);
                }
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
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _watchTimer.Change(1000 * 10, 1000 * 10);
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _watchTimer.Change(Timeout.Infinite, Timeout.Infinite);
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

        private class Settings : Library.Configuration.SettingsBase, IThisLock
        {
            private object _thisLock = new object();

            public Settings()
                : base(new List<Library.Configuration.ISettingsContext>() { 
                    new Library.Configuration.SettingsContext<UriCollection>() { Name = "ListenUris", Value = new UriCollection() },
                })
            {

            }

            public override void Load(string directoryPath)
            {
                lock (this.ThisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (this.ThisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public UriCollection ListenUris
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return (UriCollection)this["ListenUris"];
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

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

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
