using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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

        private object _thisLock = new object();
        private bool _disposed = false;

        public void Connect(TimeSpan timeout)
        {
            lock (this.ThisLock)
            {
                _services = GetServicesFromDevice(out _location, IPAddress.Parse("239.255.255.250"), new TimeSpan(0, 0, 10));
                if (_services == null) _services = GetServicesFromDevice(out _location, IPAddress.Parse("255.255.255.255"), new TimeSpan(0, 0, 10));

                if (_services == null)
                {
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

                        if (_services == null)
                        {
                            _services = GetServicesFromDevice(out _location, gatewayIp, new TimeSpan(0, 0, 10));
                            if (_services == null) continue;
                        }

                        break;
                    }
                }
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

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Uri tempLocation = null;

            for (; ; )
            {
                TimeoutCheck(stopwatch.Elapsed, timeout);

                string queryResponse = null;

                try
                {
                    string query = "M-SEARCH * HTTP/1.1\r\n" +
                        "Host:" + "239.255.255.250" + ":1900\r\n" +
                        "ST:upnp:rootdevice\r\n" +
                        "Man:\"ssdp:discover\"\r\n" +
                        "MX:3\r\n" +
                        "\r\n" +
                        "\r\n";

                    using (Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                    {
                        client.ReceiveTimeout = 3000;
                        if (ip.ToString() == "255.255.255.255") client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

                        byte[] q = Encoding.ASCII.GetBytes(query);

                        IPEndPoint endPoint = new IPEndPoint(ip, 1900);
                        client.SendTo(q, q.Length, SocketFlags.None, endPoint);

                        IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                        EndPoint senderEP = (EndPoint)sender;
                        byte[] data = new byte[1024];
                        int dataLength = client.ReceiveFrom(data, ref senderEP);

                        queryResponse = Encoding.ASCII.GetString(data, 0, dataLength);
                    }
                }
                catch (Exception)
                {

                }

                if (string.IsNullOrWhiteSpace(queryResponse)) continue;

                var regexLocation = Regex.Match(queryResponse.ToLower(), "^location.*?:(.*)", RegexOptions.Multiline).Groups[1].Value;
                if (string.IsNullOrWhiteSpace(regexLocation)) continue;

                tempLocation = new Uri(regexLocation);

                break;
            }

            Debug.WriteLine("UPnP Router: " + ip.ToString());

            string downloadString = null;

            try
            {
                using (var webClient = new WebClient())
                {
                    Thread thread = new Thread(new ThreadStart(delegate()
                    {
                        try
                        {
                            downloadString = webClient.DownloadString(tempLocation);
                        }
                        catch (Exception)
                        {
                        }
                    }));

                    thread.Start();
                    thread.Join(TimeoutCheck(stopwatch.Elapsed, timeout));
                }
            }
            catch (Exception)
            {
                return null;
            }

            location = tempLocation;
            return downloadString;
        }

        private static string GetExternalIpAddressFromService(string services, string serviceType, string gatewayIp, int gatewayPort)
        {
            if (services == null || services == "" || !services.Contains(serviceType)) return null;

            services = services.Substring(services.IndexOf(serviceType));

            string controlUrl = Regex.Match(services, "<controlURL>(.*)</controlURL>").Groups[1].Value;
            string soapBody =
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                " <s:Body>" +
                "  <u:GetExternalIPAddress xmlns:u=\"" + serviceType + "\">" + "</u:GetExternalIPAddress>" +
                " </s:Body>" +
                "</s:Envelope>";
            byte[] body = System.Text.UTF8Encoding.ASCII.GetBytes(soapBody);
            string url = "http://" + gatewayIp + ":" + gatewayPort.ToString() + controlUrl;

            try
            {
                System.Net.WebRequest wr = System.Net.WebRequest.Create(url);

                wr.Method = "POST";
                wr.Headers.Add("SOAPAction", "\"" + serviceType + "#GetExternalIPAddress\"");
                wr.ContentType = "text/xml;charset=\"utf-8\"";
                wr.ContentLength = body.Length;

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

        private static bool OpenPortFromService(string services, string serviceType, string gatewayIp, int gatewayPort, UpnpProtocolType protocol, string machineIp, int externalPort, int internalPort, string description)
        {
            if (services == null || services == "" || !services.Contains(serviceType)) return false;

            services = services.Substring(services.IndexOf(serviceType));

            string controlUrl = Regex.Match(services, "<controlURL>(.*)</controlURL>").Groups[1].Value;
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
            string url = "http://" + gatewayIp + ":" + gatewayPort.ToString() + controlUrl;

            try
            {
                System.Net.WebRequest wr = System.Net.WebRequest.Create(url);

                wr.Method = "POST";
                wr.Headers.Add("SOAPAction", "\"" + serviceType + "#AddPortMapping\"");
                wr.ContentType = "text/xml;charset=\"utf-8\"";
                wr.ContentLength = body.Length;

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

        private static bool ClosePortFromService(string services, string serviceType, string gatewayIp, int gatewayPort, UpnpProtocolType protocol, int externalPort)
        {
            if (services == null || services == "" || !services.Contains(serviceType)) return false;

            services = services.Substring(services.IndexOf(serviceType));

            string controlUrl = Regex.Match(services, "<controlURL>(.*)</controlURL>").Groups[1].Value;
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
            string url = "http://" + gatewayIp + ":" + gatewayPort.ToString() + controlUrl;

            try
            {
                System.Net.WebRequest wr = System.Net.WebRequest.Create(url);

                wr.Method = "POST";
                wr.Headers.Add("SOAPAction", "\"" + serviceType + "#DeletePortMapping\"");
                wr.ContentType = "text/xml;charset=\"utf-8\"";
                wr.ContentLength = body.Length;

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

        private static string GetPortEntryFromService(string services, string serviceType, string gatewayIp, int gatewayPort, int index)
        {
            if (services == null || services == "" || !services.Contains(serviceType)) return null;

            services = services.Substring(services.IndexOf(serviceType));

            string controlUrl = Regex.Match(services, "<controlURL>(.*)</controlURL>").Groups[1].Value;
            string soapBody =
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                " <s:Body>" +
                "  <u:GetGenericPortMappingEntry xmlns:u=\"" + serviceType + "\">" +
                "   <NewPortMappingIndex>" + index + "</NewPortMappingIndex>" +
                "  </u:GetGenericPortMappingEntry>" +
                " </s:Body>" +
                "</s:Envelope>";
            byte[] body = System.Text.UTF8Encoding.ASCII.GetBytes(soapBody);
            string url = "http://" + gatewayIp + ":" + gatewayPort.ToString() + controlUrl;

            try
            {
                System.Net.WebRequest wr = System.Net.WebRequest.Create(url);

                wr.Method = "POST";
                wr.Headers.Add("SOAPAction", "\"" + serviceType + "#GetGenericPortMappingEntry\"");
                wr.ContentType = "text/xml;charset=\"utf-8\"";
                wr.ContentLength = body.Length;

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

            string value = null;

            Thread startThread = new Thread(new ThreadStart(delegate()
            {
                try
                {
                    if (null != (value = GetExternalIpAddressFromService(_services, "urn:schemas-upnp-org:service:WANIPConnection:1", _location.Host, _location.Port)))
                        return;
                    if (null != (value = GetExternalIpAddressFromService(_services, "urn:schemas-upnp-org:service:WANPPPConnection:1", _location.Host, _location.Port)))
                        return;
                }
                catch (Exception)
                {
                }
            }));
            startThread.Start();
            startThread.Join(timeout);

            return value;
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

            bool flag = false;

            Thread startThread = new Thread(new ThreadStart(delegate()
            {
                try
                {
                    if (flag = OpenPortFromService(_services, "urn:schemas-upnp-org:service:WANIPConnection:1", _location.Host, _location.Port, protocol, machineIp, externalPort, internalPort, description))
                        return;
                    if (flag = OpenPortFromService(_services, "urn:schemas-upnp-org:service:WANPPPConnection:1", _location.Host, _location.Port, protocol, machineIp, externalPort, internalPort, description))
                        return;
                }
                catch (Exception)
                {
                }
            }));
            startThread.Start();
            startThread.Join(timeout);

            return flag;
        }

        public bool ClosePort(UpnpProtocolType protocol, int externalPort, TimeSpan timeout)
        {
            if (_services == null) throw new UpnpClientException();

            bool flag = false;

            Thread startThread = new Thread(new ThreadStart(delegate()
            {
                try
                {
                    if (flag = ClosePortFromService(_services, "urn:schemas-upnp-org:service:WANIPConnection:1", _location.Host, _location.Port, protocol, externalPort))
                        return;
                    if (flag = ClosePortFromService(_services, "urn:schemas-upnp-org:service:WANPPPConnection:1", _location.Host, _location.Port, protocol, externalPort))
                        return;
                }
                catch (Exception)
                {
                }
            }));
            startThread.Start();
            startThread.Join(timeout);

            return flag;
        }

        public string GetPortEntry(int index, TimeSpan timeout)
        {
            if (_services == null) throw new UpnpClientException();

            string value = null;

            Thread startThread = new Thread(new ThreadStart(delegate()
            {
                try
                {
                    if (null != (value = GetPortEntryFromService(_services, "urn:schemas-upnp-org:service:WANIPConnection:1", _location.Host, _location.Port, index)))
                        return;
                    if (null != (value = GetPortEntryFromService(_services, "urn:schemas-upnp-org:service:WANPPPConnection:1", _location.Host, _location.Port, index)))
                        return;
                }
                catch (Exception)
                {
                }
            }));
            startThread.Start();
            startThread.Join(timeout);

            return value;
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
