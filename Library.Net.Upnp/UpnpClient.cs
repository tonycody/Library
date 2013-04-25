using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Library.Net.Upnp
{
    public enum UpnpProtocolType
    {
        Tcp,
        Udp,
    }

    public class UpnpClient : ManagerBase, IThisLock
    {
        private string _services = null;
        private Uri _location;
        private readonly static Regex _deviceTypeRegex = new Regex(@"(\s*)<deviceType>(\s*)urn:schemas-upnp-org:device:InternetGatewayDevice:1(\s*)</deviceType>(\s*)");
        private readonly static Regex _controlUrlRegex = new Regex(@"<controlURL>(\s*)(?<url>.*?)(\s*)</controlURL>");

        private object _thisLock = new object();
        private volatile bool _disposed = false;

        public void Connect(TimeSpan timeout)
        {
            lock (this.ThisLock)
            {
                try
                {
                    _services = GetServicesFromDevice(out _location, IPAddress.Parse("239.255.255.250"), new TimeSpan(0, 0, 10));
                    if (_services != null) return;
                }
                catch (Exception)
                {

                }

                try
                {
                    _services = GetServicesFromDevice(out _location, IPAddress.Parse("255.255.255.255"), new TimeSpan(0, 0, 10));
                    if (_services != null) return;
                }
                catch (Exception)
                {

                }

#if !MONO
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up))
                {
                    var machineIp = nic.GetIPProperties().UnicastAddresses
                        .Select(n => n.Address)
                        .Where(n => n.AddressFamily == AddressFamily.InterNetwork)
                        .FirstOrDefault();
                    if (machineIp == null) continue;

                    var gatewayIp = nic.GetIPProperties().GatewayAddresses
                        .Select(n => n.Address)
                        .Where(n => n.AddressFamily == AddressFamily.InterNetwork)
                        .FirstOrDefault();
                    if (gatewayIp == null) continue;

                    try
                    {
                        _services = GetServicesFromDevice(out _location, gatewayIp, new TimeSpan(0, 0, 10));
                        if (_services != null) return;
                    }
                    catch (Exception)
                    {

                    }
                }
#else

#endif

                throw new TimeoutException();
            }
        }

        private static TimeSpan TimeoutCheck(TimeSpan elapsedTime, TimeSpan timeout)
        {
            var value = timeout - elapsedTime;

            if (value > TimeSpan.Zero)
            {
                return value;
            }
            else
            {
                throw new TimeoutException();
            }
        }

        private static string GetServicesFromDevice(out Uri location, IPAddress ip, TimeSpan timeout)
        {
            location = null;

            List<string> querys = new List<string>();

            querys.Add("M-SEARCH * HTTP/1.1\r\n" +
                    "Host: 239.255.255.250:1900\r\n" +
                    "Man: \"ssdp:discover\"\r\n" +
                    "ST: upnp:rootdevice\r\n" +
                    "MX: 3\r\n" +
                    "\r\n");

            querys.Add("M-SEARCH * HTTP/1.1\r\n" +
                    "Host: 239.255.255.250:1900\r\n" +
                    "Man: \"ssdp:discover\"\r\n" +
                    "ST: urn:schemas-upnp-org:service:WANIPConnection:1\r\n" +
                    "MX: 3\r\n" +
                    "\r\n");

            querys.Add("M-SEARCH * HTTP/1.1\r\n" +
                    "Host: 239.255.255.250:1900\r\n" +
                    "Man: \"ssdp:discover\"\r\n" +
                    "ST: urn:schemas-upnp-org:service:WANPPPConnection:1\r\n" +
                    "MX: 3\r\n" +
                    "\r\n");

            int queryCount = 0;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            for (; ; )
            {
                TimeoutCheck(stopwatch.Elapsed, timeout);

                try
                {
                    List<string> queryResponses = new List<string>();

                    try
                    {
                        using (Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                        {
                            client.ReceiveTimeout = 3000;
                            client.SendTimeout = 1000;

                            if (ip.ToString() == "255.255.255.255") client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

                            byte[] q = Encoding.ASCII.GetBytes(querys[queryCount++]);

                            IPEndPoint endPoint = new IPEndPoint(ip, 1900);
                            client.SendTo(q, q.Length, SocketFlags.None, endPoint);

                            for (int i = 0; i < 10; i++)
                            {
                                byte[] data = new byte[1024];
                                int dataLength = client.Receive(data);

                                var temp = Encoding.ASCII.GetString(data, 0, dataLength);
                                if (!string.IsNullOrWhiteSpace(temp)) queryResponses.Add(temp);
                            }
                        }
                    }
                    catch (Exception)
                    {

                    }

                    foreach (var queryResponse in queryResponses)
                    {
                        var regexLocation = Regex.Match(queryResponse.ToLower(), "^location.*?:(.*)", RegexOptions.Multiline).Groups[1].Value;
                        if (string.IsNullOrWhiteSpace(regexLocation)) continue;

                        Uri tempLocation = new Uri(regexLocation);

                        Debug.WriteLine("UPnP Router: " + ip.ToString());
                        Debug.WriteLine("UPnP Location: " + tempLocation.ToString());

                        string downloadString = null;

                        using (var webClient = new WebClient())
                        {
                            downloadString = webClient.DownloadString(tempLocation);
                        }

                        if (string.IsNullOrWhiteSpace(downloadString)) continue;
                        if (!_deviceTypeRegex.IsMatch(downloadString)) continue;

                        location = tempLocation;
                        return downloadString;
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private static string GetExternalIpAddressFromService(string services, string serviceType, string gatewayIp, int gatewayPort, TimeSpan timeout)
        {
            if (services == null || services == "" || !services.Contains(serviceType)) return null;

            try
            {
                services = services.Substring(services.IndexOf(serviceType));

                string controlUrl = _controlUrlRegex.Match(services).Groups["url"].Value;
                string soapBody =
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    " <s:Body>" +
                    "  <u:GetExternalIPAddress xmlns:u=\"" + serviceType + "\">" + "</u:GetExternalIPAddress>" +
                    " </s:Body>" +
                    "</s:Envelope>";
                byte[] body = System.Text.UTF8Encoding.ASCII.GetBytes(soapBody);

                Uri uri = new Uri(controlUrl, UriKind.RelativeOrAbsolute);

                if (!uri.IsAbsoluteUri)
                {
                    Uri baseUri = new Uri("http://" + gatewayIp + ":" + gatewayPort.ToString());
                    uri = new Uri(baseUri, uri);
                }

                System.Net.WebRequest wr = System.Net.WebRequest.Create(uri);

                wr.Method = "POST";
                wr.Headers.Add("SOAPAction", "\"" + serviceType + "#GetExternalIPAddress\"");
                wr.ContentType = "text/xml;charset=\"utf-8\"";
                wr.ContentLength = body.Length;
                wr.Timeout = (int)timeout.TotalMilliseconds;

                using (System.IO.Stream stream = wr.GetRequestStream())
                {
                    stream.Write(body, 0, body.Length);
                }

                string externalIpAddress = null;

                using (WebResponse wres = wr.GetResponse())
                {
                    if (((HttpWebResponse)wres).StatusCode == HttpStatusCode.OK)
                    {
                        using (StreamReader sr = new StreamReader(wres.GetResponseStream()))
                        {
                            externalIpAddress = Regex.Match(sr.ReadToEnd(), "<NewExternalIPAddress>(.*)</NewExternalIPAddress>").Groups[1].Value;
                        }
                    }
                }

                return externalIpAddress;
            }
            catch (Exception)
            {

            }

            return null;
        }

        private static bool OpenPortFromService(string services, string serviceType, string gatewayIp, int gatewayPort, UpnpProtocolType protocol, string machineIp, int externalPort, int internalPort, string description, TimeSpan timeout)
        {
            if (services == null || services == "" || !services.Contains(serviceType)) return false;

            try
            {
                services = services.Substring(services.IndexOf(serviceType));

                string controlUrl = _controlUrlRegex.Match(services).Groups["url"].Value;
                string protocolString = "";

                if (protocol == UpnpProtocolType.Tcp)
                {
                    protocolString = "TCP";
                }
                else if (protocol == UpnpProtocolType.Udp)
                {
                    protocolString = "UDP";
                }

                string soapBody =
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    " <s:Body>" +
                    "  <u:AddPortMapping xmlns:u=\"" + serviceType + "\">" +
                    "   <NewRemoteHost></NewRemoteHost>" +
                    "   <NewExternalPort>" + externalPort + "</NewExternalPort>" +
                    "   <NewProtocol>" + protocolString + "</NewProtocol>" +
                    "   <NewInternalPort>" + internalPort + "</NewInternalPort>" +
                    "   <NewInternalClient>" + machineIp + "</NewInternalClient>" +
                    "   <NewEnabled>1</NewEnabled>" +
                    "   <NewPortMappingDescription>" + description + "</NewPortMappingDescription>" +
                    "   <NewLeaseDuration>0</NewLeaseDuration>" +
                    "  </u:AddPortMapping>" +
                    " </s:Body>" +
                    "</s:Envelope>";
                byte[] body = System.Text.UTF8Encoding.ASCII.GetBytes(soapBody);

                Uri uri = new Uri(controlUrl, UriKind.RelativeOrAbsolute);

                if (!uri.IsAbsoluteUri)
                {
                    Uri baseUri = new Uri("http://" + gatewayIp + ":" + gatewayPort.ToString());
                    uri = new Uri(baseUri, uri);
                }

                System.Net.WebRequest wr = System.Net.WebRequest.Create(uri);

                wr.Method = "POST";
                wr.Headers.Add("SOAPAction", "\"" + serviceType + "#AddPortMapping\"");
                wr.ContentType = "text/xml;charset=\"utf-8\"";
                wr.ContentLength = body.Length;
                wr.Timeout = (int)timeout.TotalMilliseconds;

                using (System.IO.Stream stream = wr.GetRequestStream())
                {
                    stream.Write(body, 0, body.Length);
                }

                using (WebResponse wres = wr.GetResponse())
                {
                    if (((HttpWebResponse)wres).StatusCode == HttpStatusCode.OK)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {

            }

            return false;
        }

        private static bool ClosePortFromService(string services, string serviceType, string gatewayIp, int gatewayPort, UpnpProtocolType protocol, int externalPort, TimeSpan timeout)
        {
            if (services == null || services == "" || !services.Contains(serviceType)) return false;

            try
            {
                services = services.Substring(services.IndexOf(serviceType));

                string controlUrl = _controlUrlRegex.Match(services).Groups["url"].Value;
                string protocolString = "";

                if (protocol == UpnpProtocolType.Tcp)
                {
                    protocolString = "TCP";
                }
                else if (protocol == UpnpProtocolType.Udp)
                {
                    protocolString = "UDP";
                }

                string soapBody =
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    " <s:Body>" +
                    "  <u:DeletePortMapping xmlns:u=\"" + serviceType + "\">" +
                    "   <NewRemoteHost></NewRemoteHost>" +
                    "   <NewExternalPort>" + externalPort + "</NewExternalPort>" +
                    "   <NewProtocol>" + protocolString + "</NewProtocol>" +
                    "  </u:DeletePortMapping>" +
                    " </s:Body>" +
                    "</s:Envelope>";
                byte[] body = System.Text.UTF8Encoding.ASCII.GetBytes(soapBody);

                Uri uri = new Uri(controlUrl, UriKind.RelativeOrAbsolute);

                if (!uri.IsAbsoluteUri)
                {
                    Uri baseUri = new Uri("http://" + gatewayIp + ":" + gatewayPort.ToString());
                    uri = new Uri(baseUri, uri);
                }

                System.Net.WebRequest wr = System.Net.WebRequest.Create(uri);

                wr.Method = "POST";
                wr.Headers.Add("SOAPAction", "\"" + serviceType + "#DeletePortMapping\"");
                wr.ContentType = "text/xml;charset=\"utf-8\"";
                wr.ContentLength = body.Length;
                wr.Timeout = (int)timeout.TotalMilliseconds;

                using (System.IO.Stream stream = wr.GetRequestStream())
                {
                    stream.Write(body, 0, body.Length);
                }

                using (WebResponse wres = wr.GetResponse())
                {
                    if (((HttpWebResponse)wres).StatusCode == HttpStatusCode.OK)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {

            }

            return false;
        }

        private static string GetPortEntryFromService(string services, string serviceType, string gatewayIp, int gatewayPort, int index, TimeSpan timeout)
        {
            if (services == null || services == "" || !services.Contains(serviceType)) return null;

            try
            {
                services = services.Substring(services.IndexOf(serviceType));

                string controlUrl = _controlUrlRegex.Match(services).Groups["url"].Value;
                string soapBody =
                    "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                    " <s:Body>" +
                    "  <u:GetGenericPortMappingEntry xmlns:u=\"" + serviceType + "\">" +
                    "   <NewPortMappingIndex>" + index + "</NewPortMappingIndex>" +
                    "  </u:GetGenericPortMappingEntry>" +
                    " </s:Body>" +
                    "</s:Envelope>";
                byte[] body = System.Text.UTF8Encoding.ASCII.GetBytes(soapBody);

                Uri uri = new Uri(controlUrl, UriKind.RelativeOrAbsolute);

                if (!uri.IsAbsoluteUri)
                {
                    Uri baseUri = new Uri("http://" + gatewayIp + ":" + gatewayPort.ToString());
                    uri = new Uri(baseUri, uri);
                }

                System.Net.WebRequest wr = System.Net.WebRequest.Create(uri);

                wr.Method = "POST";
                wr.Headers.Add("SOAPAction", "\"" + serviceType + "#GetGenericPortMappingEntry\"");
                wr.ContentType = "text/xml;charset=\"utf-8\"";
                wr.ContentLength = body.Length;
                wr.Timeout = (int)timeout.TotalMilliseconds;

                using (System.IO.Stream stream = wr.GetRequestStream())
                {
                    stream.Write(body, 0, body.Length);
                }

                string text = null;

                using (WebResponse wres = wr.GetResponse())
                {
                    if (((HttpWebResponse)wres).StatusCode == HttpStatusCode.OK)
                    {
                        using (StreamReader sr = new StreamReader(wres.GetResponseStream()))
                        {
                            text = sr.ReadToEnd();
                        }
                    }
                }

                return text;
            }
            catch (Exception)
            {

            }

            return null;
        }

        public string GetExternalIpAddress(TimeSpan timeout)
        {
            if (_services == null) throw new UpnpClientException();

            try
            {
                var value = GetExternalIpAddressFromService(_services, "urn:schemas-upnp-org:service:WANIPConnection:1", _location.Host, _location.Port, timeout);
                if (value != null) return value;
            }
            catch (Exception)
            {

            }
            try
            {
                var value = GetExternalIpAddressFromService(_services, "urn:schemas-upnp-org:service:WANPPPConnection:1", _location.Host, _location.Port, timeout);
                if (value != null) return value;
            }
            catch (Exception)
            {

            }

            return null;
        }

        public bool OpenPort(UpnpProtocolType protocol, int externalPort, int internalPort, string description, TimeSpan timeout)
        {
            if (_services == null) throw new UpnpClientException();

#if !MONO
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up))
            {
                var machineIp = nic.GetIPProperties().UnicastAddresses
                    .Select(n => n.Address)
                    .Where(n => n.AddressFamily == AddressFamily.InterNetwork)
                    .FirstOrDefault();
                if (machineIp == null) continue;

                var gatewayIp = nic.GetIPProperties().GatewayAddresses
                    .Select(n => n.Address)
                    .Where(n => n.AddressFamily == AddressFamily.InterNetwork)
                    .FirstOrDefault();
                if (gatewayIp == null) continue;

                if (gatewayIp.ToString() == _location.Host && OpenPort(protocol, machineIp.ToString(), externalPort, internalPort, description, timeout))
                {
                    return true;
                }
            }
#else
            string hostname = Dns.GetHostName();

            foreach (var ipAddress in Dns.GetHostAddresses(hostname))
            {
                if (ipAddress.AddressFamily != AddressFamily.InterNetwork) continue;

                if (OpenPort(protocol, ipAddress.ToString(), externalPort, internalPort, description, timeout))
                {
                    return true;
                }
            }
#endif

            return false;
        }

        public bool OpenPort(UpnpProtocolType protocol, string machineIp, int externalPort, int internalPort, string description, TimeSpan timeout)
        {
            if (_services == null) throw new UpnpClientException();

            try
            {
                var value = OpenPortFromService(_services, "urn:schemas-upnp-org:service:WANIPConnection:1", _location.Host, _location.Port, protocol, machineIp, externalPort, internalPort, description, timeout);
                if (value) return value;
            }
            catch (Exception)
            {

            }

            try
            {
                var value = OpenPortFromService(_services, "urn:schemas-upnp-org:service:WANPPPConnection:1", _location.Host, _location.Port, protocol, machineIp, externalPort, internalPort, description, timeout);
                if (value) return value;
            }
            catch (Exception)
            {

            }

            return false;
        }

        public bool ClosePort(UpnpProtocolType protocol, int externalPort, TimeSpan timeout)
        {
            if (_services == null) throw new UpnpClientException();

            try
            {
                var value = ClosePortFromService(_services, "urn:schemas-upnp-org:service:WANIPConnection:1", _location.Host, _location.Port, protocol, externalPort, timeout);
                if (value) return value;
            }
            catch (Exception)
            {

            }

            try
            {
                var value = ClosePortFromService(_services, "urn:schemas-upnp-org:service:WANPPPConnection:1", _location.Host, _location.Port, protocol, externalPort, timeout);
                if (value) return value;
            }
            catch (Exception)
            {

            }

            return false;
        }

        public string GetPortEntry(int index, TimeSpan timeout)
        {
            if (_services == null) throw new UpnpClientException();

            try
            {
                var value = GetPortEntryFromService(_services, "urn:schemas-upnp-org:service:WANIPConnection:1", _location.Host, _location.Port, index, timeout);
                if (value != null) return value;
            }
            catch (Exception)
            {

            }

            try
            {
                var value = GetPortEntryFromService(_services, "urn:schemas-upnp-org:service:WANPPPConnection:1", _location.Host, _location.Port, index, timeout);
                if (value != null) return value;
            }
            catch (Exception)
            {

            }

            return null;
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
    public class UpnpClientException : ManagerException
    {
        public UpnpClientException() : base() { }
        public UpnpClientException(string message) : base(message) { }
        public UpnpClientException(string message, Exception innerException) : base(message, innerException) { }
    }
}
