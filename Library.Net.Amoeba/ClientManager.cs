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

        private const int MaxReceiveCount = 1024 * 1024 * 16;
        
        public ClientManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings();
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.ConnectionFilters;
                }
            }
        }

        private static IPAddress GetIpAddress(string host)
        {
            IPAddress remoteIP = null;

            if (!IPAddress.TryParse(host, out remoteIP))
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(host);

                if (hostEntry.AddressList.Length > 0)
                {
                    remoteIP = hostEntry.AddressList[0];
                }
                else
                {
                    return null;
                }
            }

            return remoteIP;
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

        public bool CheckUri(string uri)
        {
            if (uri == null) return false;

            ConnectionFilter connectionFilter = null;

            lock (this.ThisLock)
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

            return !(connectionFilter == null || connectionFilter.ConnectionType == ConnectionType.None);
        }

        public ConnectionBase CreateConnection(string uri)
        {
            Socket socket = null;
            ConnectionBase connection = null;

            try
            {
                ConnectionFilter connectionFilter = null;

                lock (this.ThisLock)
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

                string scheme = null;
                string host = null;
                int port = -1;

                {
                    Regex regex = new Regex(@"(.*?):(.*):(\d*)");
                    var match = regex.Match(uri);

                    if (match.Success)
                    {
                        scheme = match.Groups[1].Value;
                        host = match.Groups[2].Value;
                        port = int.Parse(match.Groups[3].Value);
                    }
                    else
                    {
                        Regex regex2 = new Regex(@"(.*?):(.*)");
                        var match2 = regex2.Match(uri);

                        if (match2.Success)
                        {
                            scheme = match2.Groups[1].Value;
                            host = match2.Groups[2].Value;
                            port = 4050;
                        }
                    }
                }

                if (host == null) return null;

                if (connectionFilter.ConnectionType == ConnectionType.Tcp)
                {
#if !DEBUG
                    {
                        Uri url = new Uri(string.Format("{0}://{1}:{2}", scheme, host, port));

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
                    socket = ClientManager.Connect(new IPEndPoint(ClientManager.GetIpAddress(host), port), new TimeSpan(0, 0, 10));
                    connection = new TcpConnection(socket, ClientManager.MaxReceiveCount, _bufferManager);
                }
                else
                {
                    string proxyScheme = null;
                    string proxyHost = null;
                    int proxyPort = -1;

                    {
                        Regex regex = new Regex(@"(.*?):(.*):(\d*)");
                        var match = regex.Match(connectionFilter.ProxyUri);

                        if (match.Success)
                        {
                            proxyScheme = match.Groups[1].Value;
                            proxyHost = match.Groups[2].Value;
                            proxyPort = int.Parse(match.Groups[3].Value);
                        }
                        else
                        {
                            Regex regex2 = new Regex(@"(.*?):(.*)");
                            var match2 = regex2.Match(connectionFilter.ProxyUri);

                            if (match2.Success)
                            {
                                proxyScheme = match2.Groups[1].Value;
                                proxyHost = match2.Groups[2].Value;

                                if (connectionFilter.ConnectionType == ConnectionType.Socks4Proxy
                                    || connectionFilter.ConnectionType == ConnectionType.Socks4aProxy
                                    || connectionFilter.ConnectionType == ConnectionType.Socks5Proxy)
                                {
                                    proxyPort = 1080;
                                }
                                else if (connectionFilter.ConnectionType == ConnectionType.HttpProxy)
                                {
                                    proxyPort = 80;
                                }
                            }
                        }
                    }

                    if (proxyHost == null) return null;

                    socket = ClientManager.Connect(new IPEndPoint(ClientManager.GetIpAddress(proxyHost), proxyPort), new TimeSpan(0, 0, 10));
                    ProxyClientBase proxy = null;

                    if (connectionFilter.ConnectionType == ConnectionType.Socks4Proxy)
                    {
                        proxy = new Socks4ProxyClient(socket, host, port);
                    }
                    else if (connectionFilter.ConnectionType == ConnectionType.Socks4aProxy)
                    {
                        proxy = new Socks4aProxyClient(socket, host, port);
                    }
                    else if (connectionFilter.ConnectionType == ConnectionType.Socks5Proxy)
                    {
                        proxy = new Socks5ProxyClient(socket, host, port);
                    }
                    else if (connectionFilter.ConnectionType == ConnectionType.HttpProxy)
                    {
                        proxy = new HttpProxyClient(socket, host, port);
                    }

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
                    lock (this.ThisLock)
                    {
                        return (ConnectionFilterCollection)this["ConnectionFilters"];
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
            lock (this.ThisLock)
            {
                if (_disposed) return;

                if (disposing)
                {

                }

                _disposed = true;
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
    class ClientManagerException : ManagerException
    {
        public ClientManagerException() : base() { }
        public ClientManagerException(string message) : base(message) { }
        public ClientManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
