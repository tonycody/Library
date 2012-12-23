using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Library.Net.Connection;
using System.Threading;

namespace Library.Net.Amoeba
{
    class ServerManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;
        private Settings _settings;

        private List<TcpListener> _listeners = new List<TcpListener>();
        private List<string> _urisHistory = new List<string>();
        private volatile Thread _watchThread = null;

        private ManagerState _state = ManagerState.Stop;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        private const int MaxReceiveCount = 1024 * 1024 * 16;

        public ServerManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings();
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

        public ConnectionBase AcceptConnection(out string uri)
        {
            uri = null;
            ConnectionBase connection = null;

            try
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return null;

                    for (int i = 0; i < _listeners.Count; i++)
                    {
                        if (_listeners[i] == null) continue;

                        if (_listeners[i].Pending())
                        {
                            var socket = _listeners[i].AcceptTcpClient().Client;

                            IPEndPoint ipEndPoint = (IPEndPoint)socket.LocalEndPoint;

                            if (ipEndPoint.AddressFamily == AddressFamily.InterNetwork)
                            {
                                uri = string.Format("tcp:{0}:{1}", ipEndPoint.Address.ToString(), ipEndPoint.Port);
                            }
                            else if (ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                uri = string.Format("tcp:[{0}]:{1}", ipEndPoint.Address.ToString(), ipEndPoint.Port);
                            }

                            connection = new TcpConnection(socket, ServerManager.MaxReceiveCount, _bufferManager);
                            break;
                        }
                    }
                }

                if (connection == null) return null;

                var secureConnection = new SecureServerConnection(connection, null, _bufferManager);

                try
                {
                    secureConnection.Connect(new TimeSpan(0, 30, 0));
                }
                catch (Exception)
                {
                    secureConnection.Dispose();

                    throw;
                }

                var compressConnection = new CompressConnection(secureConnection, ServerManager.MaxReceiveCount, _bufferManager);

                try
                {
                    compressConnection.Connect(new TimeSpan(0, 10, 0));
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

        private void WatchThread()
        {
            for (; ; )
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                lock (this.ThisLock)
                {
                    if (!Collection.Equals(_urisHistory, this.ListenUris))
                    {
                        // Stop
                        {
                            for (int i = 0; i < _listeners.Count; i++)
                            {
                                _listeners[i].Stop();
                            }

                            _listeners.Clear();
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
                                        _listeners.Add(listener);
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

            while (_watchThread != null) Thread.Sleep(1000);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _watchThread = new Thread(this.WatchThread);
                _watchThread.Priority = ThreadPriority.Lowest;
                _watchThread.Name = "ServerManager_WatchThread";
                _watchThread.Start();
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;
            }

            _watchThread.Join();
            _watchThread = null;
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

            if (disposing)
            {

            }

            _disposed = true;
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
