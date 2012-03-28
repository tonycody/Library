using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using Library.Net.Connection;
using Library.Net.Proxy;

namespace Library.Net.Amoeba
{
    class ClientManager : ManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;

        private Settings _settings;
        
        private bool _disposed = false;
        private object _thisLock = new object();

        private const int MaxReceiveCount = 1 * 1024 * 1024;
        
        public ClientManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings();
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _settings.ConnectionFilters;
                }
            }
        }

        private static IPEndPoint GetIpEndPoint(string uri)
        {
            Regex regex = new Regex(@"(.*?):(.*):(\d*)");
            var match = regex.Match(uri);
            if (!match.Success) return null;

            IPAddress remoteIP = null;

            if (!IPAddress.TryParse(match.Groups[2].Value, out remoteIP))
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(match.Groups[2].Value);
                if (hostEntry.AddressList.Length > 0)
                {
                    remoteIP = hostEntry.AddressList[0];
                }
                else
                {
                    return null;
                }
            }

            return new IPEndPoint(remoteIP, int.Parse(match.Groups[3].Value));
        }

        private static Socket Connect(IPEndPoint remoteEndPoint, TimeSpan timeout)
        {
            Socket socket = null;

            try
            {
                socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.ReceiveTimeout = (int)timeout.TotalMilliseconds;
                socket.SendTimeout = (int)timeout.TotalMilliseconds;

                var asyncResult = socket.BeginConnect(remoteEndPoint, null, null);

                if (!asyncResult.IsCompleted && !asyncResult.CompletedSynchronously)
                {
                    if (!asyncResult.AsyncWaitHandle.WaitOne(timeout, false))
                    {
                        throw new ConnectionException();
                    }
                }

                socket.EndConnect(asyncResult);

                return socket;
            }
            catch (Exception)
            {
                if (socket != null) socket.Close();
            }

            throw new ClientManagerException();
        }

        public ConnectionBase CreateConnection(string uri)
        {
            Socket socket = null;
            ConnectionBase connection = null;

            try
            {
                ConnectionFilter connectionFilter = null;

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    foreach (var filter in this.Filters)
                    {
                        if (filter.UriCondition.IsMatch(uri))
                        {
                            connectionFilter = filter;
                            break;
                        }
                    }
                }

                if (connectionFilter == null || connectionFilter.ConnectionType == ConnectionType.None) return null;

                if (connectionFilter.ConnectionType == ConnectionType.Tcp)
                {
#if !DEBUG
                    {
                        Regex regex = new Regex(@"(.*?):(.*):(\d*)");
                        var match = regex.Match(uri);
                        Uri url = new Uri(string.Format("{0}://{1}:{2}", match.Groups[1], match.Groups[2], match.Groups[3]));

                        if (url.HostNameType == UriHostNameType.IPv4)
                        {
                            var myIpAddress = IPAddress.Parse(url.Host);

                            if (IPAddress.Any.ToString() == myIpAddress.ToString()
                                || IPAddress.Loopback.ToString() == myIpAddress.ToString()
                                || IPAddress.Broadcast.ToString() == myIpAddress.ToString())
                            {
                                return null;
                            }
                            if (Collection.Compare(myIpAddress.GetAddressBytes(), IPAddress.Parse("10.0.0.0").GetAddressBytes()) >= 0
                                && Collection.Compare(myIpAddress.GetAddressBytes(), IPAddress.Parse("10.255.255.255").GetAddressBytes()) <= 0)
                            {
                                return null;
                            }
                            if (Collection.Compare(myIpAddress.GetAddressBytes(), IPAddress.Parse("172.16.0.0").GetAddressBytes()) >= 0
                                && Collection.Compare(myIpAddress.GetAddressBytes(), IPAddress.Parse("172.31.255.255").GetAddressBytes()) <= 0)
                            {
                                return null;
                            }
                            if (Collection.Compare(myIpAddress.GetAddressBytes(), IPAddress.Parse("127.0.0.0").GetAddressBytes()) >= 0
                                && Collection.Compare(myIpAddress.GetAddressBytes(), IPAddress.Parse("127.255.255.255").GetAddressBytes()) <= 0)
                            {
                                return null;
                            }
                            if (Collection.Compare(myIpAddress.GetAddressBytes(), IPAddress.Parse("192.168.0.0").GetAddressBytes()) >= 0
                                && Collection.Compare(myIpAddress.GetAddressBytes(), IPAddress.Parse("192.168.255.255").GetAddressBytes()) <= 0)
                            {
                                return null;
                            }
                        }
                        else if (url.HostNameType == UriHostNameType.IPv6)
                        {
                            var myIpAddress = IPAddress.Parse(url.Host);

                            if (IPAddress.IPv6Any.ToString() == myIpAddress.ToString()
                                || IPAddress.IPv6Loopback.ToString() == myIpAddress.ToString()
                                || IPAddress.IPv6None.ToString() == myIpAddress.ToString())
                            {
                                return null;
                            }
                            if (myIpAddress.ToString().ToLower().StartsWith("fe80:"))
                            {
                                return null;
                            }
                        }
                    }
#endif

                    connection = new TcpConnection(ClientManager.Connect(ClientManager.GetIpEndPoint(uri), new TimeSpan(0, 0, 10)), ClientManager.MaxReceiveCount, _bufferManager);
                }
                else if (connectionFilter.ConnectionType == ConnectionType.Socks4Proxy)
                {
                    Regex regex = new Regex(@"(.*?):(.*):(\d*)");
                    var match = regex.Match(uri);
                    if (!match.Success) return null;

                    socket = ClientManager.Connect(ClientManager.GetIpEndPoint(connectionFilter.ProxyUri), new TimeSpan(0, 0, 10));
                    var proxy = new Socks4ProxyClient(socket, match.Groups[2].Value, int.Parse(match.Groups[3].Value));

                    connection = new TcpConnection(proxy.CreateConnection(new TimeSpan(0, 0, 30)), ClientManager.MaxReceiveCount, _bufferManager);
                }
                else if (connectionFilter.ConnectionType == ConnectionType.Socks4aProxy)
                {
                    Regex regex = new Regex(@"(.*?):(.*):(\d*)");
                    var match = regex.Match(uri);
                    if (!match.Success) return null;

                    socket = ClientManager.Connect(ClientManager.GetIpEndPoint(connectionFilter.ProxyUri), new TimeSpan(0, 0, 10));
                    var proxy = new Socks4aProxyClient(socket, match.Groups[2].Value, int.Parse(match.Groups[3].Value));

                    connection = new TcpConnection(proxy.CreateConnection(new TimeSpan(0, 0, 30)), ClientManager.MaxReceiveCount, _bufferManager);
                }
                else if (connectionFilter.ConnectionType == ConnectionType.Socks5Proxy)
                {
                    Regex regex = new Regex(@"(.*?):(.*):(\d*)");
                    var match = regex.Match(uri);
                    if (!match.Success) return null;

                    socket = ClientManager.Connect(ClientManager.GetIpEndPoint(connectionFilter.ProxyUri), new TimeSpan(0, 0, 10));
                    var proxy = new Socks5ProxyClient(socket, match.Groups[2].Value, int.Parse(match.Groups[3].Value));

                    connection = new TcpConnection(proxy.CreateConnection(new TimeSpan(0, 0, 30)), ClientManager.MaxReceiveCount, _bufferManager);
                }
                else if (connectionFilter.ConnectionType == ConnectionType.HttpProxy)
                {
                    Regex regex = new Regex(@"(.*?):(.*):(\d*)");
                    var match = regex.Match(uri);
                    if (!match.Success) return null;

                    socket = ClientManager.Connect(ClientManager.GetIpEndPoint(connectionFilter.ProxyUri), new TimeSpan(0, 0, 10));
                    var proxy = new HttpProxyClient(socket, match.Groups[2].Value, int.Parse(match.Groups[3].Value));

                    connection = new TcpConnection(proxy.CreateConnection(new TimeSpan(0, 0, 30)), ClientManager.MaxReceiveCount, _bufferManager);
                }

                var secureConnection = new SecureClientConnection(connection, null, _bufferManager);
                secureConnection.Connect(new TimeSpan(0, 1, 0));

                return new CompressConnection(secureConnection, ClientManager.MaxReceiveCount, _bufferManager);
            }
            catch (Exception)
            {
                if (socket != null) socket.Close();
                if (connection != null) connection.Dispose();
            }

            return null;
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
                    new Library.Configuration.SettingsContext<ConnectionFilterCollection>() { Name = "ConnectionFilters", Value = new ConnectionFilterCollection() },
                 })
            {

            }

            public override void Load(string path)
            {
                base.Load(path);
            }

            public override void Save(string path)
            {
                base.Save(path);
            }

            public ConnectionFilterCollection ConnectionFilters
            {
                get
                {
                    using (DeadlockMonitor.Lock(this.ThisLock))
                    {
                        return (ConnectionFilterCollection)this["ConnectionFilters"];
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
    class ClientManagerException : ManagerException
    {
        public ClientManagerException() : base() { }
        public ClientManagerException(string message) : base(message) { }
        public ClientManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
