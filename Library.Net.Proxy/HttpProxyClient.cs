using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Library.Net.Proxy
{
    public class HttpProxyClient : ProxyClientBase, IThisLock
    {
        private const int HTTP_PROXY_DEFAULT_PORT = 8080;
        private const string HTTP_PROXY_CONNECT_CMD = "CONNECT {0}:{1} HTTP/1.0 \r\nHOST {0}:{1}\r\n\r\n";
        private const int WAIT_FOR_DATA_INTERVAL = 50; // 50 ms
        private const int WAIT_FOR_DATA_TIMEOUT = 15000; // 15 seconds

        private string _proxyHost;
        private int _proxyPort;
        private HttpResponseCodes _respCode;
        private string _respText;
        private TcpClient _tcpClient;
        private string _destinationHost;
        private int _destinationPort;
        private object _thisLock = new object();

        private enum HttpResponseCodes
        {
            None = 0,
            Continue = 100,
            SwitchingProtocols = 101,
            OK = 200,
            Created = 201,
            Accepted = 202,
            NonAuthoritiveInformation = 203,
            NoContent = 204,
            ResetContent = 205,
            PartialContent = 206,
            MultipleChoices = 300,
            MovedPermanetly = 301,
            Found = 302,
            SeeOther = 303,
            NotModified = 304,
            UserProxy = 305,
            TemporaryRedirect = 307,
            BadRequest = 400,
            Unauthorized = 401,
            PaymentRequired = 402,
            Forbidden = 403,
            NotFound = 404,
            MethodNotAllowed = 405,
            NotAcceptable = 406,
            ProxyAuthenticantionRequired = 407,
            RequestTimeout = 408,
            Conflict = 409,
            Gone = 410,
            PreconditionFailed = 411,
            RequestEntityTooLarge = 413,
            RequestURITooLong = 414,
            UnsupportedMediaType = 415,
            RequestedRangeNotSatisfied = 416,
            ExpectationFailed = 417,
            InternalServerError = 500,
            NotImplemented = 501,
            BadGateway = 502,
            ServiceUnavailable = 503,
            GatewayTimeout = 504,
            HTTPVersionNotSupported = 505
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public HttpProxyClient(string destinationHost, int destinationPort)
        {
            if (String.IsNullOrEmpty(destinationHost))
            {
                throw new ArgumentNullException("destinationHost");
            }
            else if (destinationPort <= 0 || destinationPort > 65535)
            {
                throw new ArgumentOutOfRangeException("destinationPort", "port must be greater than zero and less than 65535");
            }

            _destinationHost = destinationHost;
            _destinationPort = destinationPort;
        }

        /// <summary>
        /// Creates a HTTP proxy client object using the supplied TcpClient object connection.
        /// </summary>
        /// <param name="tcpClient">A TcpClient connection object.</param>
        public HttpProxyClient(Socket socket, string destinationHost, int destinationPort)
            : this(destinationHost, destinationPort)
        {
            if (socket == null)
            {
                throw new ArgumentNullException("socket");
            }

            _tcpClient = new TcpClient();
            _tcpClient.Client = socket;
        }

        /// <summary>
        /// Constructor.  The default HTTP proxy port 8080 is used.
        /// </summary>
        /// <param name="proxyHost">Host name or IP address of the proxy.</param>
        public HttpProxyClient(string proxyHost, string destinationHost, int destinationPort)
            : this(destinationHost, destinationPort)
        {
            if (String.IsNullOrEmpty(proxyHost))
            {
                throw new ArgumentNullException("proxyHost");
            }

            _proxyHost = proxyHost;
            _proxyPort = HTTP_PROXY_DEFAULT_PORT;
        }

        /// <summary>
        /// Constructor.  
        /// </summary>
        /// <param name="proxyHost">Host name or IP address of the proxy server.</param>
        /// <param name="proxyPort">Port number for the proxy server.</param>
        public HttpProxyClient(string proxyHost, int proxyPort, string destinationHost, int destinationPort)
            : this(destinationHost, destinationPort)
        {
            if (String.IsNullOrEmpty(proxyHost))
            {
                throw new ArgumentNullException("proxyHost");
            }
            else if (proxyPort <= 0 || proxyPort > 65535)
            {
                throw new ArgumentOutOfRangeException("proxyPort", "port must be greater than zero and less than 65535");
            }

            _proxyHost = proxyHost;
            _proxyPort = proxyPort;
        }

        /// <summary>
        /// Gets or sets host name or IP address of the proxy server.
        /// </summary>
        public string ProxyHost
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _proxyHost;
                }
            }
        }

        /// <summary>
        /// Gets or sets port number for the proxy server.
        /// </summary>
        public int ProxyPort
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _proxyPort;
                }
            }
        }

        /// <summary>
        /// Gets or sets the TcpClient object. 
        /// This property can be set prior to executing CreateConnection to use an existing TcpClient connection.
        /// </summary>
        public Socket Socket
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _tcpClient.Client;
                }
            }
        }

        /// <summary>
        /// Creates a remote TCP connection through a proxy server to the destination host on the destination port.
        /// </summary>
        /// <param name="destinationHost">Destination host name or IP address.</param>
        /// <param name="destinationPort">Port number to connect to on the destination host.</param>
        /// <returns>
        /// Returns an open TcpClient object that can be used normally to communicate
        /// with the destination server
        /// </returns>
        /// <remarks>
        /// This method creates a connection to the proxy server and instructs the proxy server
        /// to make a pass through connection to the specified destination host on the specified
        /// port.  
        /// </remarks>
        public override Socket CreateConnection(TimeSpan timeout)
        {
            try
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                // if we have no connection, create one
                if (_tcpClient == null)
                {
                    if (String.IsNullOrEmpty(_proxyHost))
                    {
                        throw new ProxyClientException("ProxyHost property must contain a value.");
                    }
                    else if (_proxyPort <= 0 || _proxyPort > 65535)
                    {
                        throw new ProxyClientException("ProxyPort value must be greater than zero and less than 65535");
                    }

                    // create new tcp client object to the proxy server
                    _tcpClient = new TcpClient();

                    // attempt to open the connection
                    _tcpClient.Connect(_proxyHost, _proxyPort);
                    _tcpClient.Client.SendTimeout = (int)Socks4ProxyClient.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds;
                    _tcpClient.Client.ReceiveTimeout = (int)Socks4ProxyClient.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds;
                }

                // send connection command to proxy host for the specified destination host and port
                SendConnectionCommand(_destinationHost, _destinationPort);

                // return the open proxied tcp client object to the caller for normal use
                return _tcpClient.Client;
            }
            catch (SocketException ex)
            {
                throw new ProxyClientException(String.Format(CultureInfo.InvariantCulture, "Connection to proxy host {0} on port {1} failed.", ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Port.ToString()), ex);
            }
        }

        private void SendConnectionCommand(string host, int port)
        {
            NetworkStream stream = _tcpClient.GetStream();

            // PROXY SERVER REQUEST
            // =======================================================================
            // CONNECT starksoft.com:443 HTTP/1.0 <CR><LF>
            // HOST starksoft.com:443<CR><LF>
            // [... other HTTP header lines ending with <CR><LF> if required]>
            // <CR><LF>    // Last Empty Line

            string connectCmd = String.Format(CultureInfo.InvariantCulture, HTTP_PROXY_CONNECT_CMD, host, port.ToString(CultureInfo.InvariantCulture));
            byte[] request = ASCIIEncoding.ASCII.GetBytes(connectCmd);

            // send the connect request
            stream.Write(request, 0, request.Length);

            // wait for the proxy server to respond
            WaitForData(stream);

            // PROXY SERVER RESPONSE
            // =======================================================================
            // HTTP/1.0 200 Connection Established<CR><LF>
            // [.... other HTTP header lines ending with <CR><LF>..
            // ignore all of them]
            // <CR><LF>    // Last Empty Line

            // create an byte response array  
            byte[] response = new byte[_tcpClient.ReceiveBufferSize];
            StringBuilder sbuilder = new StringBuilder();
            int bytes = 0;
            long total = 0;

            do
            {
                bytes = stream.Read(response, 0, _tcpClient.ReceiveBufferSize);
                total += bytes;
                sbuilder.Append(System.Text.ASCIIEncoding.UTF8.GetString(response, 0, bytes));
            }
            while (stream.DataAvailable);

            ParseResponse(sbuilder.ToString());

            // evaluate the reply code for an error condition
            if (_respCode != HttpResponseCodes.OK)
            {
                HandleProxyCommandError(host, port);
            }
        }

        private void HandleProxyCommandError(string host, int port)
        {
            string msg;

            switch (_respCode)
            {
                case HttpResponseCodes.None:
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} failed to return a recognized HTTP response code.  Server response: {2}", ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Port.ToString(), _respText);
                    break;

                case HttpResponseCodes.BadGateway:
                    // HTTP/1.1 502 Proxy Error (The specified Secure Sockets Layer (SSL) port is not allowed. ISA Server is not configured to allow SSL requests from this port. Most Web browsers use port 443 for SSL requests.)
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} responded with a 502 code - Bad Gateway.  If you are connecting to a Microsoft ISA destination please refer to knowledge based article Q283284 for more information.  Server response: {2}", ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Port.ToString(), _respText);
                    break;

                default:
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} responded with a {2} code - {3}", ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Port.ToString(), ((int)_respCode).ToString(CultureInfo.InvariantCulture), _respText);
                    break;
            }

            // throw a new application exception 
            throw new ProxyClientException(msg);
        }

        private void WaitForData(NetworkStream stream)
        {
            int sleepTime = 0;
            while (!stream.DataAvailable)
            {
                Thread.Sleep(WAIT_FOR_DATA_INTERVAL);
                sleepTime += WAIT_FOR_DATA_INTERVAL;
              
                if (sleepTime > WAIT_FOR_DATA_TIMEOUT)
                {
                    throw new ProxyClientException(String.Format("A timeout while waiting for the proxy server at {0} on port {1} to respond.", ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Port.ToString()));
                }
            }
        }

        private void ParseResponse(string response)
        {
            string[] data = null;

            // get rid of the LF character if it exists and then split the string on all CR
            data = response.Replace('\n', ' ').Split('\r');

            ParseCodeAndText(data[0]);
        }

        private void ParseCodeAndText(string line)
        {
            int begin = 0;
            int end = 0;
            string val = null;

            if (line.IndexOf("HTTP") == -1)
            {
                throw new ProxyClientException(String.Format("No HTTP response received from proxy destination.  Server response: {0}.", line));
            }

            begin = line.IndexOf(" ") + 1;
            end = line.IndexOf(" ", begin);

            val = line.Substring(begin, end - begin);
            Int32 code = 0;

            if (!Int32.TryParse(val, out code))
            {
                throw new ProxyClientException(String.Format("An invalid response code was received from proxy destination.  Server response: {0}.", line));
            }

            _respCode = (HttpResponseCodes)code;
            _respText = line.Substring(end + 1).Trim();
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
}
