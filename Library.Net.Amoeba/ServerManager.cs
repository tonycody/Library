﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Library.Net.Connection;
using System.Text.RegularExpressions;

namespace Library.Net.Amoeba
{
    class ServerManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;

        private Settings _settings;

        private List<TcpListener> _listeners;
        private ManagerState _state = ManagerState.Stop;
        private bool _disposed = false;
        private object _thisLock = new object();

        private const int MaxReceiveCount = 1 * 1024 * 1024;

        public ServerManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings();
            
            _listeners = new List<TcpListener>();
        }

        public UriCollection ListenUris
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.ListenUris;
                }
            }
        }

        public ConnectionBase AcceptConnection(out string uri)
        {
            uri = null;

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Stop) return null;

                try
                {
                    ConnectionBase connection = null;

                    for (int i = 0; i < _listeners.Count; i++)
                    {
                        if (_listeners[i].Pending())
                        {
                            var socket = _listeners[i].AcceptTcpClient().Client;

                            IPEndPoint ipEndPoing = (IPEndPoint)socket.RemoteEndPoint;

                            if (ipEndPoing.AddressFamily == AddressFamily.InterNetwork)
                            {
                                uri = string.Format("tcp:{0}:{1}", ipEndPoing.Address.ToString(), ipEndPoing.Port);
                            }
                            else if (ipEndPoing.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                uri = string.Format("tcp:[{0}]:{1}", ipEndPoing.Address.ToString(), ipEndPoing.Port);
                            }

                            connection = new TcpConnection(socket, ServerManager.MaxReceiveCount, _bufferManager);
                            break;
                        }
                    }

                    if (connection != null)
                    {
                        var secureConnection = new SecureServerConnection(connection, null, _bufferManager);
                        secureConnection.Connect(new TimeSpan(0, 1, 0));

                        return new CompressConnection(secureConnection, ServerManager.MaxReceiveCount, _bufferManager);
                    }
                }
                catch (Exception)
                {

                }

                return null;
            }
        }

        public override ManagerState State
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _state;
                }
            }
        }

        public override void Start()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

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
        }

        public override void Stop()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                for (int i = 0; i < _listeners.Count; i++)
                {
                    _listeners[i].Stop();
                }

                _listeners.Clear();
            }
        }

        #region ISettings メンバ

        public void Load(string directoryPath)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _settings.Load(directoryPath);
            }
        }

        public void Save(string directoryPath)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    base.Save(directoryPath);
                }
            }

            public UriCollection ListenUris
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (UriCollection)this["ListenUris"];
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

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_listeners != null)
                    {
                        for (int i = 0; i < _listeners.Count; i++)
                        {
                            try
                            {
                                _listeners[i].Stop();
                            }
                            catch (Exception)
                            {

                            }
                        }

                        _listeners = null;
                    }
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

    [Serializable]
    class ServerManagerException : StateManagerException
    {
        public ServerManagerException() : base() { }
        public ServerManagerException(string message) : base(message) { }
        public ServerManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
