using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Library.Net.Connection;
using Library.Net.Proxy.Sam;
using System.Threading;

namespace Library.Net.Lair
{
    class ServerManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;
        private Settings _settings;

        private List<object> _listeners = new List<object>();
        private List<string> _urisHistory = new List<string>();
        private volatile Thread _watchThread = null;

        private ManagerState _state = ManagerState.Stop;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        private const int MaxReceiveCount = 1024 * 1024 * 16;

        public delegate void NewBaseNodeEventHandler(string uri);
        NewBaseNodeEventHandler _newBaseNodeEvent;

        public ServerManager(BufferManager bufferManager, NewBaseNodeEventHandler newBaseNodeEvent)
        {
            _bufferManager = bufferManager;
            _newBaseNodeEvent = newBaseNodeEvent;

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

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return null;

                try
                {

                    for (int i = 0; i < _listeners.Count; i++)
                    {
                        if (_listeners[i] == null) continue;

                        if (_listeners[i] is TcpListener)
                        {
                            TcpListener listener = (TcpListener)_listeners[i];
                            if (listener.Pending())
                            {
                                var socket = listener.AcceptTcpClient().Client;

                                IPEndPoint ipEndPoint = (IPEndPoint)socket.RemoteEndPoint;

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
                        else if (_listeners[i] is SamListener)
                        {
                            SamListener listener = (SamListener)_listeners[i];
                            try
                            {
                                listener.Update();
                            }
                            catch (SamException)
                            {
                                continue;
                            }
                            if (listener.Pending())
                            {
                                SamV3StatefulAcceptor acceptor = listener.Dequeue();
                                try
                                {
                                    acceptor.AcceptComplete();
                                }
                                catch (SamException ex)
                                {
                                    Log.Error(ex);
                                    acceptor.Dispose();
                                    continue;
                                }
                                Socket socket = acceptor.BridgeSocket;
                                string base64Address = acceptor.DestinationBase64;
                                string base32Address = I2PEncoding.Base32Address.FromDestinationBase64(base64Address);
                                uri = "i2p:" + base32Address;
                                connection = new TcpConnection(socket, ServerManager.MaxReceiveCount, _bufferManager);
                                break;
                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            }

            if (connection != null)
            {
                try
                {
                    var secureConnection = new SecureServerConnection(connection, null, _bufferManager);
                    secureConnection.Connect(new TimeSpan(0, 1, 0));

                    var compressConnection = new CompressConnection(secureConnection, ServerManager.MaxReceiveCount, _bufferManager);
                    compressConnection.Connect(new TimeSpan(0, 1, 0));

                    return compressConnection;
                }
                catch (Exception)
                {

                }
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
                                if (_listeners[i] is TcpListener)
                                {
                                    TcpListener listener = (TcpListener)_listeners[i];
                                    listener.Stop();
                                }
                                else if (_listeners[i] is SamListener)
                                {
                                    SamListener listener = (SamListener)_listeners[i];
                                    listener.Dispose();
                                }
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
                                else if (match.Groups[1].Value == "samv3accept")
                                {
                                    SamListener listener = null;
                                    try
                                    {
                                        string caption = "Lair";
                                        string[] options = new string[]
                                        {
                                            "inbound.nickname=" + caption,
                                            "outbound.nickname=" + caption
                                        };
                                        string optionsString = string.Join(" ", options);

                                        listener = new SamListener(match.Groups[2].Value, int.Parse(match.Groups[3].Value), optionsString);
                                        _listeners.Add(listener);
                                        try
                                        {
                                            string base64Address = listener.Session.DestinationBase64;
                                            string base32Address = I2PEncoding.Base32Address.FromDestinationBase64(base64Address);
                                            Log.Information("New I2P BaseNode generated." + "\n" +
                                                    "i2p:" + base64Address + "\n" +
                                                    "i2p:" + base32Address);
                                            _newBaseNodeEvent("i2p:" + base32Address);
                                        }
                                        catch (Exception)
                                        {
                                        }
                                    }
                                    catch (SamException ex)
                                    {
                                        Log.Error(ex);
                                        if (listener != null)
                                            listener.Dispose();
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
