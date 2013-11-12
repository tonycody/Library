using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Library.Net.Proxy
{
    /// <summary>
    /// Socks4 connection proxy class.  This class implements the Socks4 standard proxy protocol.
    /// </summary>
    /// <remarks>
    /// This class implements the Socks4 proxy protocol standard for TCP communciations.
    /// </remarks>
    public class Socks4ProxyClient : ProxyClientBase, IThisLock
    {
        private const int WAIT_FOR_DATA_INTERVAL = 50;   // 50 ms
        private const int WAIT_FOR_DATA_TIMEOUT = 15000; // 15 seconds

        private string _proxyUserId;
        private TcpClient _tcpClient;
        private string _destinationHost;
        private int _destinationPort;
        private object _thisLock = new object();

        /// <summary>
        /// Default Socks4 proxy port.
        /// </summary>
        protected internal const int SOCKS_PROXY_DEFAULT_PORT = 1080;

        /// <summary>
        /// Socks4 version number.
        /// </summary>
        protected internal const byte SOCKS4_VERSION_NUMBER = 4;

        /// <summary>
        /// Socks4 connection command value.
        /// </summary>
        protected internal const byte SOCKS4_CMD_CONNECT = 0x01;

        /// <summary>
        /// Socks4 bind command value.
        /// </summary>
        protected internal const byte SOCKS4_CMD_BIND = 0x02;

        /// <summary>
        /// Socks4 reply request grant response value.
        /// </summary>
        protected internal const byte SOCKS4_CMD_REPLY_REQUEST_GRANTED = 90;

        /// <summary>
        /// Socks4 reply request rejected or failed response value.
        /// </summary>
        protected internal const byte SOCKS4_CMD_REPLY_REQUEST_REJECTED_OR_FAILED = 91;

        /// <summary>
        /// Socks4 reply request rejected becauase the proxy server can not connect to the IDENTD server value.
        /// </summary>
        protected internal const byte SOCKS4_CMD_REPLY_REQUEST_REJECTED_CANNOT_CONNECT_TO_IDENTD = 92;

        /// <summary>
        /// Socks4 reply request rejected because of a different IDENTD server.
        /// </summary>
        protected internal const byte SOCKS4_CMD_REPLY_REQUEST_REJECTED_DIFFERENT_IDENTD = 93;

        private Socks4ProxyClient(string destinationHost, int destinationPort)
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

        public Socks4ProxyClient(Socket socket, string destinationHost, int destinationPort)
            : this(destinationHost, destinationPort)
        {
            if (socket == null)
            {
                throw new ArgumentNullException("socket");
            }

            _tcpClient = new TcpClient();
            _tcpClient.Client = socket;
        }

        public Socks4ProxyClient(Socket socket, string proxyUserId, string destinationHost, int destinationPort)
            : this(destinationHost, destinationPort)
        {
            if (socket == null)
            {
                throw new ArgumentNullException("socket");
            }

            _tcpClient = new TcpClient();
            _tcpClient.Client = socket;
            _proxyUserId = proxyUserId;
        }

        /// <summary>
        /// Gets or sets proxy user identification information.
        /// </summary>
        public string ProxyUserId
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _proxyUserId;
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
        /// Creates a TCP connection to the destination host through the proxy server
        /// host.
        /// </summary>
        /// <param name="destinationHost">Destination host name or IP address of the destination server.</param>
        /// <param name="destinationPort">Port number to connect to on the destination server.</param>
        /// <returns>
        /// Returns an open TcpClient object that can be used normally to communicate
        /// with the destination server
        /// </returns>
        /// <remarks>
        /// This method creates a connection to the proxy server and instructs the proxy server
        /// to make a pass through connection to the specified destination host on the specified
        /// port.  
        /// </remarks>
        public override Socket Create(TimeSpan timeout)
        {
            try
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                // send connection command to proxy host for the specified destination host and port
                SendCommand(_tcpClient.GetStream(), SOCKS4_CMD_CONNECT, _destinationHost, _destinationPort, _proxyUserId);

                // return the open proxied tcp client object to the caller for normal use
                return _tcpClient.Client;
            }
            catch (Exception ex)
            {
                throw new ProxyClientException(String.Format(CultureInfo.InvariantCulture, "Connection to proxy host {0} on port {1} failed.", ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)_tcpClient.Client.RemoteEndPoint).Port.ToString()), ex);
            }
        }

        /// <summary>
        /// Sends a command to the proxy server.
        /// </summary>
        /// <param name="proxy">Proxy server data stream.</param>
        /// <param name="command">Proxy byte command to execute.</param>
        /// <param name="destinationHost">Destination host name or IP address.</param>
        /// <param name="destinationPort">Destination port number</param>
        /// <param name="userId">IDENTD user ID value.</param>
        protected virtual void SendCommand(NetworkStream proxy, byte command, string destinationHost, int destinationPort, string userId)
        {
            // PROXY SERVER REQUEST
            // The client connects to the SOCKS server and sends a CONNECT request when
            // it wants to establish a connection to an application server. The client
            // includes in the request packet the IP address and the port number of the
            // destination host, and userid, in the following format.
            //
            //        +----+----+----+----+----+----+----+----+----+----+....+----+
            //        | VN | CD | DSTPORT |      DSTIP        | USERID       |NULL|
            //        +----+----+----+----+----+----+----+----+----+----+....+----+
            // # of bytes:   1    1      2              4           variable       1
            //
            // VN is the SOCKS protocol version number and should be 4. CD is the
            // SOCKS command code and should be 1 for CONNECT request. NULL is a byte
            // of all zero bits.         

            // userId needs to be a zero length string so that the GetBytes method
            // works properly
            if (userId == null)
            {
                userId = "";
            }

            byte[] destIp = GetIPAddressBytes(destinationHost);
            byte[] destPort = GetDestinationPortBytes(destinationPort);
            byte[] userIdBytes = ASCIIEncoding.ASCII.GetBytes(userId);
            byte[] request = new byte[9 + userIdBytes.Length];

            // set the bits on the request byte array
            request[0] = SOCKS4_VERSION_NUMBER;
            request[1] = command;
            destPort.CopyTo(request, 2);
            destIp.CopyTo(request, 4);
            userIdBytes.CopyTo(request, 8);
            request[8 + userIdBytes.Length] = 0x00;  // null (byte with all zeros) terminator for userId

            // send the connect request
            proxy.Write(request, 0, request.Length);

            // wait for the proxy server to respond
            WaitForData(proxy);

            // PROXY SERVER RESPONSE
            // The SOCKS server checks to see whether such a request should be granted
            // based on any combination of source IP address, destination IP address,
            // destination port number, the userid, and information it may obtain by
            // consulting IDENT, cf. RFC 1413.  If the request is granted, the SOCKS
            // server makes a connection to the specified port of the destination host.
            // A reply packet is sent to the client when this connection is established,
            // or when the request is rejected or the operation fails. 
            //
            //        +----+----+----+----+----+----+----+----+
            //        | VN | CD | DSTPORT |      DSTIP        |
            //        +----+----+----+----+----+----+----+----+
            // # of bytes:    1    1      2              4
            //
            // VN is the version of the reply code and should be 0. CD is the result
            // code with one of the following values:
            //
            //    90: request granted
            //    91: request rejected or failed
            //    92: request rejected becuase SOCKS server cannot connect to
            //        identd on the client
            //    93: request rejected because the client program and identd
            //        report different user-ids
            //
            // The remaining fields are ignored.
            //
            // The SOCKS server closes its connection immediately after notifying
            // the client of a failed or rejected request. For a successful request,
            // the SOCKS server gets ready to relay traffic on both directions. This
            // enables the client to do I/O on its connection as if it were directly
            // connected to the application server.

            // create an 8 byte response array  
            byte[] response = new byte[8];

            // read the resonse from the network stream
            proxy.Read(response, 0, 8);

            // evaluate the reply code for an error condition
            if (response[1] != SOCKS4_CMD_REPLY_REQUEST_GRANTED)
            {
                HandleProxyCommandError(response, destinationHost, destinationPort);
            }
        }

        /// <summary>
        /// Translate the host name or IP address to a byte array.
        /// </summary>
        /// <param name="destinationHost">Host name or IP address.</param>
        /// <returns>Byte array representing IP address in bytes.</returns>
        protected byte[] GetIPAddressBytes(string destinationHost)
        {
            IPAddress ipAddr = null;

            // if the address doesn't parse then try to resolve with dns
            if (!IPAddress.TryParse(destinationHost, out ipAddr))
            {
                try
                {
                    ipAddr = Dns.GetHostEntry(destinationHost).AddressList[0];
                }
                catch (Exception ex)
                {
                    throw new ProxyClientException(String.Format(CultureInfo.InvariantCulture, "A error occurred while attempting to DNS resolve the host name {0}.", destinationHost), ex);
                }
            }

            // return address bytes
            return ipAddr.GetAddressBytes();
        }

        /// <summary>
        /// Translate the destination port value to a byte array.
        /// </summary>
        /// <param name="value">Destination port.</param>
        /// <returns>Byte array representing an 16 bit port number as two bytes.</returns>
        protected byte[] GetDestinationPortBytes(int value)
        {
            byte[] array = new byte[2];
            array[0] = System.Convert.ToByte(value / 256);
            array[1] = System.Convert.ToByte(value % 256);
            return array;
        }

        /// <summary>
        /// Receive a byte array from the proxy server and determine and handle and errors that may have occurred.
        /// </summary>
        /// <param name="response">Proxy server command response as a byte array.</param>
        /// <param name="destinationHost">Destination host.</param>
        /// <param name="destinationPort">Destination port number.</param>
        protected void HandleProxyCommandError(byte[] response, string destinationHost, int destinationPort)
        {
            if (response == null)
            {
                throw new ArgumentNullException("response");
            }

            // extract the reply code
            byte replyCode = response[1];

            // extract the ip v4 address (4 bytes)
            byte[] ipBytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                ipBytes[i] = response[i + 4];
            }

            // convert the ip address to an IPAddress object
            IPAddress ipAddr = new IPAddress(ipBytes);

            // extract the port number big endian (2 bytes)
            byte[] portBytes = new byte[2];
            portBytes[0] = response[3];
            portBytes[1] = response[2];
            Int16 port = BitConverter.ToInt16(portBytes, 0);

            // translate the reply code error number to human readable text
            string proxyErrorText;
            switch (replyCode)
            {
                case SOCKS4_CMD_REPLY_REQUEST_REJECTED_OR_FAILED:
                    proxyErrorText = "connection request was rejected or failed";
                    break;
                case SOCKS4_CMD_REPLY_REQUEST_REJECTED_CANNOT_CONNECT_TO_IDENTD:
                    proxyErrorText = "connection request was rejected because SOCKS destination cannot connect to identd on the client";
                    break;
                case SOCKS4_CMD_REPLY_REQUEST_REJECTED_DIFFERENT_IDENTD:
                    proxyErrorText = "connection request rejected because the client program and identd report different user-ids";
                    break;
                default:
                    proxyErrorText = String.Format(CultureInfo.InvariantCulture, "proxy client received an unknown reply with the code value '{0}' from the proxy destination", replyCode.ToString(CultureInfo.InvariantCulture));
                    break;
            }

            // build the exeception message string
            string exceptionMsg = String.Format(CultureInfo.InvariantCulture, "The {0} concerning destination host {1} port number {2}.  The destination reported the host as {3} port {4}.", proxyErrorText, destinationHost, destinationPort, ipAddr.ToString(), port.ToString(CultureInfo.InvariantCulture));

            // throw a new application exception 
            throw new ProxyClientException(exceptionMsg);
        }

        protected void WaitForData(NetworkStream stream)
        {
            int sleepTime = 0;
            while (!stream.DataAvailable)
            {
                Thread.Sleep(WAIT_FOR_DATA_INTERVAL);
                sleepTime += WAIT_FOR_DATA_INTERVAL;

                if (sleepTime > WAIT_FOR_DATA_TIMEOUT)
                {
                    throw new ProxyClientException("A timeout while waiting for the proxy destination to respond.");
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
}
