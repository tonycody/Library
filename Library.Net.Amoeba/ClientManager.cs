using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Library.Net.Connection;
using Library.Net.Proxy;

namespace Library.Net.Amoeba
{
    class ClientManager : ManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;

        private Settings _settings;

        private volatile bool _disposed = false;
        private object _thisLock = new object();

        private const int _maxReceiveCount = 1024 * 1024 * 32;

        public ClientManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);
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

        private static IEnumerable<KeyValuePair<string, string>> DecodeCommand(string option)
        {
            try
            {
                Dictionary<string, string> pair = new Dictionary<string, string>();
                List<char> kl = new List<char>();
                List<char> vl = new List<char>();
                bool keyFlag = true;
                bool wordFlag = false;

                for (int i = 0; i < option.Length; i++)
                {
                    char w1;
                    char? w2 = null;

                    w1 = option[i];
                    if (option.Length > i + 1) w2 = option[i + 1];

                    if (keyFlag)
                    {
                        if (w1 == '=')
                        {
                            keyFlag = false;
                        }
                        else
                        {
                            kl.Add(w1);
                        }
                    }
                    else
                    {
                        if (w1 == '\\' && w2.HasValue)
                        {
                            if (w2.Value == '\"' || w2.Value == '\\')
                            {
                                vl.Add(w2.Value);
                                i++;
                            }
                        }
                        else
                        {
                            if (wordFlag)
                            {
                                if (w1 == '\"')
                                {
                                    wordFlag = false;
                                }
                                else
                                {
                                    vl.Add(w1);
                                }
                            }
                            else
                            {
                                if (w1 == '\"')
                                {
                                    wordFlag = true;
                                }
                                else if (w1 == ' ')
                                {
                                    var key = new string(kl.ToArray());
                                    var value = new string(vl.ToArray());

                                    if (!string.IsNullOrWhiteSpace(key))
                                    {
                                        pair[key.Trim()] = value;
                                    }

                                    kl.Clear();
                                    vl.Clear();

                                    keyFlag = true;
                                }
                                else
                                {
                                    vl.Add(w1);
                                }
                            }
                        }
                    }
                }

                if (!keyFlag)
                {
                    var key = new string(kl.ToArray());
                    var value = new string(vl.ToArray());

                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        pair[key.Trim()] = value;
                    }
                }

                return pair;
            }
            catch (Exception)
            {
                return null;
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
                if (socket != null) socket.Dispose();
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

        public ConnectionBase CreateConnection(string uri, BandwidthLimit bandwidthLimit)
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

                IList<KeyValuePair<string, string>> options = null;

                if (!string.IsNullOrWhiteSpace(connectionFilter.Option))
                {
                    options = ClientManager.DecodeCommand(connectionFilter.Option).OfType<KeyValuePair<string, string>>().ToList();
                }

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
                    connection = new TcpConnection(socket, bandwidthLimit, _maxReceiveCount, _bufferManager);
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
                        var user = (options != null) ? options.Where(n => n.Key.ToLower().StartsWith("user")).Select(n => n.Value).FirstOrDefault() : null;

                        proxy = new Socks4ProxyClient(socket, user, host, port);
                    }
                    else if (connectionFilter.ConnectionType == ConnectionType.Socks4aProxy)
                    {
                        var user = (options != null) ? options.Where(n => n.Key.ToLower().StartsWith("user")).Select(n => n.Value).FirstOrDefault() : null;

                        proxy = new Socks4aProxyClient(socket, user, host, port);
                    }
                    else if (connectionFilter.ConnectionType == ConnectionType.Socks5Proxy)
                    {
                        var user = (options != null) ? options.Where(n => n.Key.ToLower().StartsWith("user")).Select(n => n.Value).FirstOrDefault() : null;
                        var pass = (options != null) ? options.Where(n => n.Key.ToLower().StartsWith("pass")).Select(n => n.Value).FirstOrDefault() : null;

                        proxy = new Socks5ProxyClient(socket, user, pass, host, port);
                    }
                    else if (connectionFilter.ConnectionType == ConnectionType.HttpProxy)
                    {
                        proxy = new HttpProxyClient(socket, host, port);
                    }

                    connection = new TcpConnection(proxy.CreateConnection(new TimeSpan(0, 0, 30)), bandwidthLimit, _maxReceiveCount, _bufferManager);
                }

                var secureConnection = new SecureConnection(SecureConnectionType.Client, SecureConnectionVersion.Version1 | SecureConnectionVersion.Version2, connection, null, _bufferManager);

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
                if (socket != null) socket.Dispose();
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

        private class Settings : Library.Configuration.SettingsBase
        {
            private object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<ConnectionFilterCollection>() { Name = "ConnectionFilters", Value = new ConnectionFilterCollection() },
                 })
            {
                _thisLock = lockObject;
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
                    lock (_thisLock)
                    {
                        return (ConnectionFilterCollection)this["ConnectionFilters"];
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
