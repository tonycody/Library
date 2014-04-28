using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Library.Net.I2p
{
    public class SamBridgeErrorMessage
    {
        public const string OK = "OK";
        public const string I2P_ERROR = "I2P_ERROR";
        public const string ALREADY_ACCEPTING = "ALREADY_ACCEPTING";
        public const string CANT_REACH_PEER = "CANT_REACH_PEER";
        public const string CONNECTION_REFUSED = "CONNECTION_REFUSED";
        public const string DUPLICATED_DEST = "DUPLICATED_DEST";
        public const string DUPLICATED_ID = "DUPLICATED_ID";
        public const string INVALID_ID = "INVALID_ID";
        public const string INVALID_KEY = "INVALID_KEY";
        public const string KEY_NOT_FOUND = "KEY_NOT_FOUND";
        public const string PEER_NOT_FOUND = "PEER_NOT_FOUND";
        public const string TIMEOUT = "TIMEOUT";

        public static string GetErrorMessage(string result)
        {
            if (result == null)
                return null;

            switch (result)
            {
                case SamBridgeErrorMessage.OK:
                    return "Operation completed successfully";
                case SamBridgeErrorMessage.I2P_ERROR:
                    return "A generic I2P error (e.g. I2CP disconnection, etc.)";
                case SamBridgeErrorMessage.ALREADY_ACCEPTING:
                    //return "Already accepting";
                    break;
                case SamBridgeErrorMessage.CANT_REACH_PEER:
                    return "The peer exists, but cannot be reached";
                case SamBridgeErrorMessage.CONNECTION_REFUSED:
                    //return "Connection refused";
                    break;
                case SamBridgeErrorMessage.DUPLICATED_DEST:
                    return "The specified Destination is already in use";
                case SamBridgeErrorMessage.DUPLICATED_ID:
                    return "The nickname is already associated with a session";
                case SamBridgeErrorMessage.INVALID_ID:
                    //return "Invalid id";
                    break;
                case SamBridgeErrorMessage.INVALID_KEY:
                    return "The specified key is not valid (bad format, etc.)";
                case SamBridgeErrorMessage.KEY_NOT_FOUND:
                    return "The naming system can't resolve the given name";
                case SamBridgeErrorMessage.PEER_NOT_FOUND:
                    return "The peer cannot be found on the network";
                case SamBridgeErrorMessage.TIMEOUT:
                    return "Timeout while waiting for an event (e.g. peer answer)";
            }

            return null;
        }
    }



    public class SamException : Exception
    {
        public SamException()
        {
        }
        public SamException(string message) :
            base(message)
        {
        }
    }
    public class SamBadRequestException : SamException
    {
    }
    public class SamBadReplyException : SamException
    {
    }
    public class SamReplyException : SamException
    {
        SamReply reply;
        public SamReply Reply
        {
            get
            {
                return this.reply;
            }
        }

        private void privateConstructor(SamReply reply)
        {
            if (reply == null)
                throw new ArgumentNullException("reply");
            this.reply = reply;
        }
        public SamReplyException(SamReply reply)
        {
            this.privateConstructor(reply);
        }
        public SamReplyException(SamReply reply, string message) :
            base(message)
        {
            this.privateConstructor(reply);
        }
    }
    public class SamReplyMismatchException : SamReplyException
    {
        public SamReplyMismatchException(SamReply reply) :
            base(reply)
        {
        }
        public SamReplyMismatchException(SamReply reply, string message) :
            base(reply, message)
        {
        }
    }
    public class SamBridgeErrorException : SamReplyException
    {
        private static string replyGetErrorMessage(SamReply reply)
        {
            if (reply == null)
                throw new ArgumentNullException("reply");
            return reply.GetErrorMessage();
        }
        public SamBridgeErrorException(SamReply reply) :
            base(reply, replyGetErrorMessage(reply))
        {
        }
    }



    public class SamTokenValue
    {
        public string Value;
        public int Index;

        public SamTokenValue(string value, int index)
        {
            this.Value = value;
            this.Index = index;
        }
    }
    public class SamReply
    {
        public string Major;
        public string Minor;
        public Dictionary<string, List<SamTokenValue>> Parameters;

        public SamReply(string major, string minor,
                Dictionary<string, List<SamTokenValue>> parameters)
        {
            this.Major = major;
            this.Minor = minor;
            this.Parameters = parameters;
        }

        public int ValueCount(string key)
        {
            if (!this.Parameters.ContainsKey(key))
                return 0;
            return this.Parameters[key].Count;
        }
        public SamTokenValue UniqueValue(string key, string defaultString)
        {
            if (!this.Parameters.ContainsKey(key))
                return new SamTokenValue(defaultString, -1);
            List<SamTokenValue> values = this.Parameters[key];
            if (values.Count != 1)
                return new SamTokenValue(defaultString, -1);
            return values[0];
        }
        public SamTokenValue UniqueValue(string key)
        {
            List<SamTokenValue> values = this.Parameters[key];
            if (values.Count != 1)
                throw new KeyNotFoundException("Key '" + key + "' is not unique");
            return values[0];
        }
        public SamTokenValue FirstValue(string key, string defaultString)
        {
            if (!this.Parameters.ContainsKey(key))
                return new SamTokenValue(defaultString, -1);
            return this.Parameters[key][0];
        }
        public SamTokenValue FirstValue(string key)
        {
            return this.Parameters[key][0];
        }
        public SamTokenValue LastValue(string key, string defaultString)
        {
            if (!this.Parameters.ContainsKey(key))
                return new SamTokenValue(defaultString, -1);
            List<SamTokenValue> values = this.Parameters[key];
            return values[values.Count - 1];
        }
        public SamTokenValue LastValue(string key)
        {
            List<SamTokenValue> values = this.Parameters[key];
            return values[values.Count - 1];
        }

        public bool Check(string major, string minor)
        {
            return this.Major == major && this.Minor == minor;
        }
        public string ResultString
        {
            get
            {
                return this.UniqueValue("RESULT").Value;
            }
        }
        public string Message
        {
            get
            {
                return this.UniqueValue("MESSAGE").Value;
            }
        }

        public string GetErrorMessage()
        {
            string result = this.UniqueValue("RESULT", null).Value;
            string message = null;

            if (result != null)
            {
                if (result == SamBridgeErrorMessage.I2P_ERROR)
                    message = this.UniqueValue("MESSAGE", null).Value;

                if (message == null)
                    message = SamBridgeErrorMessage.GetErrorMessage(result);

                if (message == null)
                    message = "RESULT=" + result;
            }
            else
                message = "RESULT is null";

            return message;
        }
    }
    public class SamReplyParser
    {
        public static string[] UnescapeAndTokenize(string input)
        {
            if (input == null)
                throw new ArgumentNullException("input");

            StringBuilder builder = new StringBuilder(input.Length);
            int begin = 0;
            int end;
            bool quoting = false;
            for (; ; quoting = !quoting)
            {
                end = input.IndexOf('\"', begin);
                if (end == -1)
                    end = input.Length;

                string s = input.Substring(begin, end - begin);
                if (!quoting)
                    s = s.Replace(' ', '\n');
                builder.Append(s);

                if (end == input.Length)
                    break;
                begin = end + 1;
            }
            return builder.ToString().Split('\n');
        }
        public static SamReply Parse(string input)
        {
            if (input == null)
                throw new ArgumentNullException("input");

            string[] tokens = UnescapeAndTokenize(input);
            if (tokens.Length < 2)
                throw new SamBadReplyException();

            string major = tokens[0];
            string minor = tokens[1];
            var parameters = new Dictionary<string, List<SamTokenValue>>();

            for (int i = 0; i != tokens.Length; ++i)
            {
                string pair = tokens[i];
                if (pair.Length == 0)
                    continue;

                int equalsPosition = pair.IndexOf('=');

                string key;
                string valueString;
                if (equalsPosition == -1)
                {
                    key = pair;
                    valueString = null;
                }
                else
                {
                    key = pair.Substring(0, equalsPosition);
                    valueString = pair.Substring(equalsPosition + 1);
                }

                if (!parameters.ContainsKey(key))
                    parameters.Add(key, new List<SamTokenValue>());
                parameters[key].Add(new SamTokenValue(valueString, i));
            }

            SamReply result = new SamReply(major, minor, parameters);
            return result;
        }
        public static SamReply ParseReply(string input, string major, string minor)
        {
            if (input == null)
                throw new ArgumentNullException("input");
            if (major == null)
                throw new ArgumentNullException("major");
            if (minor == null)
                throw new ArgumentNullException("minor");

            SamReply reply = Parse(input);
            if (!reply.Check(major, minor))
                throw new SamReplyMismatchException(reply);
            return reply;
        }
    }
    public class SamRequestBuilder
    {
        List<string> strings = new List<string>();

        public SamRequestBuilder(string major, string minor)
        {
            if (major == null)
                throw new ArgumentNullException("major");
            if (minor == null)
                throw new ArgumentNullException("minor");

            this.Add(major);
            this.Add(minor);
        }
        public void Add(string s)
        {
            if (s == null)
                return;

            Encoding.ASCII.GetBytes(s);
            if (s.IndexOf('\"') != -1)
                throw new SamBadRequestException();
            if (s.IndexOf(' ') != -1)
                s = '\"' + s + '\"';
            this.strings.Add(s);
        }
        public void Add(string key, string valueString)
        {
            if (key == null)
                return;
            if (valueString == null)
                return;

            string s = key + "=" + valueString;
            Encoding.ASCII.GetBytes(s);
            if (s.IndexOf('\"') != -1)
                throw new SamBadRequestException();
            if (s.IndexOf(' ') != -1)
                s = '\"' + s + '\"';
            this.strings.Add(s);
        }
        public void AddFormat(string format, params object[] args)
        {
            if (format == null)
                return;

            string s = String.Format(CultureInfo.InvariantCulture, format, args);
            this.Add(s);
        }
        public void AddDirect(string s)
        {
            if (s == null)
                return;

            Encoding.ASCII.GetBytes(s);
            this.strings.Add(s);
        }
        public string Join()
        {
            return String.Join(" ", strings);
        }
    }
    public struct SamVersion
    {
        public int Major;
        public int Minor;

        public SamVersion(int major, int minor = 0)
        {
            this.Major = major;
            this.Minor = minor;
        }
        public string ToString()
        {
            return Major.ToString() + "." + Minor.ToString();
        }
    }
    public enum SamSessionStyle
    {
        STREAM,
        DATAGRAM,
        RAW
    }
    public class SamLookupResult
    {
        public string Name;
        public string PublicKeyBase64;

        public SamLookupResult(string name, string publicKey)
        {
            this.Name = name;
            this.PublicKeyBase64 = publicKey;
        }
    }
    public class SamDestination
    {
        public string DestinationBase64;
        public string PrivateBase64;

        public SamDestination(string publicKey, string privateKey)
        {
            this.DestinationBase64 = publicKey;
            this.PrivateBase64 = privateKey;
        }
    }



    public class SamV3BridgeBasicCommunicator : IDisposable
    {
        public NetworkStream Stream { get; private set; }

        public SamV3BridgeBasicCommunicator(NetworkStream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");

            this.Stream = stream;
        }
        public void Dispose()
        {
            this.Stream.Dispose();
        }

        public void WriteLine(string command)
        {
            if (command == null)
                throw new ArgumentNullException("command");

            byte[] work = Encoding.ASCII.GetBytes(command + "\n");
            this.Stream.Write(work, 0, work.Length);
        }
        public byte[] ReadLineAsBytes(int capacity = 0xc00)
        {
            int offset;
            byte[] work = new byte[capacity];

            for (offset = 0; offset != work.Length; ++offset)
            {
                int n = this.Stream.ReadByte();
                if (n == -1)
                    throw new SamBadReplyException();

                work[offset] = (byte)n;

                const byte LF = 0x0a;
                if (work[offset] == LF)
                    break;
            }
            if (offset == work.Length)
                throw new SamBadReplyException();

            byte[] result = new byte[offset];
            Array.Copy(work, result, offset);

            return result;
        }
        public string ReadLine(int capacity = 0xc00)
        {
            byte[] b = this.ReadLineAsBytes(capacity);
            return Encoding.ASCII.GetString(b);
        }
        public string Communicate(string command, bool silence = false, int capacity = 0xc00)
        {
            if (command == null)
                throw new ArgumentNullException("command");

            this.WriteLine(command);
            if (silence)
                return null;
            return this.ReadLine(capacity);
        }
    }
    public class SamV3BridgeCommunicator : SamV3BridgeBasicCommunicator
    {
        public SamV3BridgeCommunicator(NetworkStream stream) :
            base(stream)
        {
        }



        public static string GenerateHelloVersion(string minVersion, string maxVersion, string options = null)
        {
            if (minVersion == null)
                throw new ArgumentNullException("minVersion");
            if (maxVersion == null)
                throw new ArgumentNullException("maxVersion");

            SamRequestBuilder request = new SamRequestBuilder("HELLO", "VERSION");
            request.Add("MIN", minVersion);
            request.Add("MAX", maxVersion);
            request.AddDirect(options);

            string requestString = request.Join();
            return requestString;
        }
        public static string ParseHelloReply(SamReply reply)
        {
            if (reply == null)
                throw new ArgumentNullException("reply");
            if (!reply.Check("HELLO", "REPLY"))
                throw new SamReplyMismatchException(reply);
            if (reply.ResultString != SamBridgeErrorMessage.OK)
                throw new SamBridgeErrorException(reply);

            string version = reply.UniqueValue("VERSION").Value;
            return version;
        }
        public string HelloVersion(string minVersion, string maxVersion, string options = null)
        {
            string requestString = GenerateHelloVersion(minVersion, maxVersion, options);
            string replyString = this.Communicate(requestString);

            SamReply reply = SamReplyParser.Parse(replyString);
            return ParseHelloReply(reply);
        }
        public string HelloVersion(SamVersion minVersion, SamVersion maxVersion, string options = null)
        {
            return this.HelloVersion(minVersion.ToString(), maxVersion.ToString(), options);
        }



        public static string GenerateSessionCreate(string style, string id, string destination = null, string options = null)
        {
            if (style == null)
                throw new ArgumentNullException("style");
            if (id == null)
                throw new ArgumentNullException("id");
            if (destination == null)
                destination = "TRANSIENT";

            SamRequestBuilder request = new SamRequestBuilder("SESSION", "CREATE");
            request.Add("STYLE", style);
            request.Add("ID", id);
            request.Add("DESTINATION", destination);
            request.AddDirect(options);

            string requestString = request.Join();
            return requestString;
        }
        public static string ParseSessionStatus(SamReply reply)
        {
            if (reply == null)
                throw new ArgumentNullException("reply");
            if (!reply.Check("SESSION", "STATUS"))
                throw new SamReplyMismatchException(reply);
            if (reply.ResultString != SamBridgeErrorMessage.OK)
                throw new SamBridgeErrorException(reply);

            string destination = reply.UniqueValue("DESTINATION").Value;
            return destination;
        }
        public string SessionCreate(string style, string id, string destination = null, string options = null)
        {
            string requestString = GenerateSessionCreate(style, id, destination, options);
            string replyString = this.Communicate(requestString);

            SamReply reply = SamReplyParser.Parse(replyString);
            return ParseSessionStatus(reply);
        }
        public string SessionCreate(SamSessionStyle style, string id, string destination = null, string options = null)
        {
            return this.SessionCreate(style.ToString(), id, destination, options);
        }



        public static string GenerateStreamConnect(string id, string destination, bool? silence = null, string options = null)
        {
            if (id == null)
                throw new ArgumentNullException("id");
            if (destination == null)
                throw new ArgumentNullException("destination");
            bool silenceBool = silence ?? false;
            string silenceString = (silence == null) ? null : silenceBool ? "true" : "false";

            SamRequestBuilder request = new SamRequestBuilder("STREAM", "CONNECT");
            request.Add("ID", id);
            request.Add("DESTINATION", destination);
            request.Add("SILENCE", silenceString);
            request.AddDirect(options);

            string requestString = request.Join();
            return requestString;
        }
        public static void ParseStreamStatus(SamReply reply)
        {
            if (reply == null)
                throw new ArgumentNullException("reply");
            if (!reply.Check("STREAM", "STATUS"))
                throw new SamReplyMismatchException(reply);
            if (reply.ResultString != SamBridgeErrorMessage.OK)
                throw new SamBridgeErrorException(reply);
        }
        public void StreamConnect(string id, string destination, bool? silence = null, string options = null)
        {
            bool silenceBool = silence ?? false;
            string requestString = GenerateStreamConnect(id, destination, silence, options);
            string replyString = this.Communicate(requestString, silenceBool);

            if (silenceBool)
                return;
            SamReply reply = SamReplyParser.Parse(replyString);
            ParseStreamStatus(reply);
        }



        public static string GenerateStreamAccept(string id, bool? silence = null, string options = null)
        {
            if (id == null)
                throw new ArgumentNullException("id");
            bool silenceBool = silence ?? false;
            string silenceString = (silence == null) ? null : silenceBool ? "true" : "false";

            SamRequestBuilder request = new SamRequestBuilder("STREAM", "ACCEPT");
            request.Add("ID", id);
            request.Add("SILENCE", silenceString);
            request.AddDirect(options);

            string requestString = request.Join();
            return requestString;
        }
        public void StreamAcceptWithoutPeerName(string id, bool? silence = null, string options = null)
        {
            bool silenceBool = silence ?? false;
            string requestString = GenerateStreamAccept(id, silence, options);
            string replyString = this.Communicate(requestString, silenceBool);

            if (silenceBool)
                return;
            SamReply reply = SamReplyParser.Parse(replyString);
            ParseStreamStatus(reply);
        }
        public string StreamAcceptReadPeerName()
        {
            string replyString = this.ReadLine();

            if (replyString.IndexOf(' ') != -1)
            {
                SamReply reply = SamReplyParser.ParseReply(replyString, "STREAM", "STATUS");
                throw new SamBridgeErrorException(reply);
            }

            return replyString;
        }
        public string StreamAccept(string id, bool? silence = null, string options = null)
        {
            this.StreamAcceptWithoutPeerName(id, silence, options);
            return this.StreamAcceptReadPeerName();
        }



        public static string GenerateStreamForward(string id, int port, string host = null, bool? silence = null, string options = null)
        {
            if (id == null)
                throw new ArgumentNullException("id");
            if (port <= 0 || 65535 < port)
                throw new ArgumentOutOfRangeException("port");
            bool silenceBool = silence ?? false;
            string silenceString = (silence == null) ? null : silenceBool ? "true" : "false";

            SamRequestBuilder request = new SamRequestBuilder("STREAM", "FORWARD");
            request.Add("ID", id);
            request.Add("PORT", port.ToString());
            request.Add("HOST", host);
            request.Add("SILENCE", silenceString);
            request.AddDirect(options);

            string requestString = request.Join();
            return requestString;
        }
        public void StreamForward(string id, int port, string host = null, bool? silence = null, string options = null)
        {
            bool silenceBool = silence ?? false;
            string requestString = GenerateStreamForward(id, port, host, silence, options);
            string replyString = this.Communicate(requestString, silenceBool);

            if (silenceBool)
                return;
            SamReply reply = SamReplyParser.Parse(replyString);
            ParseStreamStatus(reply);
        }



        public static string GenerateNamingLookup(string name, string options = null)
        {
            if (name == null)
                name = "ME";

            SamRequestBuilder request = new SamRequestBuilder("NAMING", "LOOKUP");
            request.Add("NAME", name);
            request.AddDirect(options);

            string requestString = request.Join();
            return requestString;
        }
        public static SamLookupResult ParseNamingReply(SamReply reply)
        {
            if (reply == null)
                throw new ArgumentNullException("reply");
            if (!reply.Check("NAMING", "REPLY"))
                throw new SamReplyMismatchException(reply);
            if (reply.ResultString != SamBridgeErrorMessage.OK)
                throw new SamBridgeErrorException(reply);

            string nameInReply = reply.UniqueValue("NAME").Value;
            string valueBase64 = reply.UniqueValue("VALUE").Value;
            return new SamLookupResult(nameInReply, valueBase64);
        }
        public SamLookupResult NamingLookup(string name = null, string options = null)
        {
            string requestString = GenerateNamingLookup(name, options);
            string replyString = this.Communicate(requestString);

            SamReply reply = SamReplyParser.Parse(replyString);
            return ParseNamingReply(reply);
        }



        public static string GenerateDestGenerate(string options = null)
        {
            SamRequestBuilder request = new SamRequestBuilder("DEST", "GENERATE");
            request.AddDirect(options);

            string requestString = request.Join();
            return requestString;
        }
        public static SamDestination ParseDestReply(SamReply reply)
        {
            if (reply == null)
                throw new ArgumentNullException("reply");
            if (!reply.Check("DEST", "REPLY"))
                throw new SamReplyMismatchException(reply);
            if (reply.ResultString != SamBridgeErrorMessage.OK)
                throw new SamBridgeErrorException(reply);

            string pubBase64 = reply.UniqueValue("PUB").Value;
            string privBase64 = reply.UniqueValue("PRIV").Value;
            return new SamDestination(pubBase64, privBase64);
        }
        public SamDestination DestGenerate(string options = null)
        {
            string requestString = GenerateDestGenerate(options);
            string replyString = this.Communicate(requestString);

            SamReply reply = SamReplyParser.Parse(replyString);
            return ParseDestReply(reply);
        }
    }



    public class SamV3BridgeClientBase : IDisposable
    {
        public Socket BridgeSocket { get; private set; }
        public SamV3BridgeCommunicator Bridge { get; private set; }

        public string BridgeHost { get; private set; }
        public int BridgePort { get; private set; }
        public string BridgeProtocolVersion { get { return "3.0"; } }
        public string BridgeSessionId { get; private set; }

        public SamV3BridgeClientBase(string bridgeHost, int bridgePort, string bridgeSessionId, Socket bridgeSocket)
        {
            this.BridgeSocket = bridgeSocket;
            this.BridgeHost = bridgeHost;
            this.BridgePort = bridgePort;
            this.BridgeSessionId = bridgeSessionId;
        }
        public void Handshake()
        {
            if (this.BridgeSocket == null)
            {
                TcpClient tcp = new TcpClient(this.BridgeHost, this.BridgePort);
                this.BridgeSocket = tcp.Client;
                tcp.Client = null;
                tcp.Close();
            }

            NetworkStream stream = null;
            SamV3BridgeCommunicator bridge = null;
            try
            {
                stream = new NetworkStream(this.BridgeSocket);
                bridge = new SamV3BridgeCommunicator(stream);
                stream = null;

                string version = this.BridgeProtocolVersion;
                string protocolVersion = bridge.HelloVersion(version, version);
                if (protocolVersion != version)
                    throw new SamException();

                this.Bridge = bridge;
                bridge = null;
            }
            catch (Exception)
            {
                if (stream != null)
                    stream.Dispose();
                if (bridge != null)
                    bridge.Dispose();
                throw;
            }
        }
        protected string ProtectedLookup(string name)
        {
            return this.Bridge.NamingLookup(name).PublicKeyBase64;
        }
        public string Lookup(string name)
        {
            if (name == null)
                throw new ArgumentNullException();
            return this.ProtectedLookup(name);
        }
        public void Dispose()
        {
            this.Bridge.Dispose();
            this.BridgeSocket.Close();
        }
    }
    public class SamV3Session : SamV3BridgeClientBase
    {
        public string DestinationBase64 { get; private set; }
        public string PrivateBase64 { get; private set; }

        public static string GenerateSessionId()
        {
            return Guid.NewGuid().ToString();
        }
        public SamV3Session(string bridgeHost, int bridgePort, string bridgeSessionId = null, Socket bridgeSocket = null) :
            base(bridgeHost, bridgePort, bridgeSessionId ?? GenerateSessionId(), bridgeSocket)
        {
        }
        public void Create(string destination = null, string options = null)
        {
            string privateBase64 = this.Bridge.SessionCreate(SamSessionStyle.STREAM, this.BridgeSessionId, destination, options);
            string destinationBase64 = this.Lookup();

            this.DestinationBase64 = destinationBase64;
            this.PrivateBase64 = privateBase64;
        }
        public string Lookup(string name = null)
        {
            return this.ProtectedLookup(name);
        }
    }
    public class SamV3Connector : SamV3BridgeClientBase
    {
        public string DestinationBase64 { get; protected set; }

        public SamV3Connector(SamV3Session session, Socket bridgeSocket = null) :
            base(session.BridgeHost, session.BridgePort, session.BridgeSessionId, bridgeSocket)
        {
        }
        public SamV3Connector(string bridgeHost, int bridgePort, string bridgeSessionId, Socket bridgeSocket = null) :
            base(bridgeHost, bridgePort, bridgeSessionId, bridgeSocket)
        {
        }
        public void Connect(string destination)
        {
            string destinationBase64 = this.Lookup(destination);
            this.Bridge.StreamConnect(this.BridgeSessionId, destinationBase64);
            this.DestinationBase64 = destinationBase64;
        }
    }
    public class SamV3StatefulAcceptor : SamV3Connector
    {
        bool isAcceptRequested;

        public SamV3StatefulAcceptor(SamV3Session session, Socket bridgeSocket = null) :
            base(session, bridgeSocket)
        {
        }
        public SamV3StatefulAcceptor(string bridgeHost, int bridgePort, string bridgeSessionId, Socket bridgeSocket = null) :
            base(bridgeHost, bridgePort, bridgeSessionId, bridgeSocket)
        {
        }
        public new void AcceptRequest()
        {
            this.Bridge.StreamAcceptWithoutPeerName(this.BridgeSessionId);
            this.isAcceptRequested = true;
        }
        public new bool AcceptPending()
        {
            return this.isAcceptRequested && this.Bridge.Stream.DataAvailable;
        }
        public new void AcceptComplete()
        {
            this.isAcceptRequested = false;
            string destinationBase64 = this.Bridge.StreamAcceptReadPeerName();
            this.DestinationBase64 = destinationBase64;
        }
    }



    public class SamListener : IDisposable
    {
        public SamV3Session Session;
        public SamV3StatefulAcceptor Listener;

        bool isAccepting;
        public bool IsAccepting
        {
            get
            {
                return this.isAccepting;
            }
            private set
            {
                this.isAccepting = value;
            }
        }

        public SamListener(string bridgeHost, int bridgePort, string options = null)
        {
            SamV3Session session = null;
            try
            {
                session = new SamV3Session(bridgeHost, bridgePort);
                session.Handshake();
                session.Create(null, options);
                this.Session = session;

                this.Create(false);

                session = null;
            }
            catch (Exception)
            {
                if (session != null)
                    session.Dispose();
                throw;
            }
        }
        private void Create(bool rethrow)
        {
            try
            {
                this.Listener = new SamV3StatefulAcceptor(this.Session);
                this.Listener.Handshake();
                this.Listener.AcceptRequest();
                this.IsAccepting = true;
            }
            catch (Exception)
            {
                this.IsAccepting = false;
                if (rethrow)
                    throw;
            }
        }
        public void Stop()
        {
            this.IsAccepting = false;
            this.Listener.Dispose();
        }
        public void Reset()
        {
            this.Stop();
            this.Create(true);
        }
        public void Update()
        {
            if (!this.IsAccepting)
                this.Reset();
        }
        public bool Pending()
        {
            if (!this.IsAccepting)
                return false;
            return this.Listener.AcceptPending();
        }
        public SamV3StatefulAcceptor Dequeue()
        {
            SamV3StatefulAcceptor listener = this.Listener;
            this.Create(false);
            return listener;
        }
        public void Dispose()
        {
            if (this.Listener != null)
                this.Listener.Dispose();
            this.Session.Dispose();
        }
    }



    public static class I2PEncoding
    {
        public static class Base32
        {
            static readonly char[] lowerTable = "abcdefghijklmnopqrstuvwxyz234567".ToCharArray();

            const int inBitsPerByte = 8;
            const int outBitsPerByte = 5;
            const int outBitMask = 0x1f;

            public static int CalculateLength(int length)
            {
                int lengthOut = ((length * inBitsPerByte) + (outBitsPerByte - 1)) / outBitsPerByte;
                return lengthOut;
            }
            public static int ToCharArray(byte[] inArray, int offsetIn, int lengthIn, char[] outArray, int offsetOut)
            {
                if (inArray == null)
                    throw new ArgumentNullException("inArray");
                if (offsetIn < 0 || inArray.Length < offsetIn)
                    throw new ArgumentOutOfRangeException("offsetIn");
                if (lengthIn < 0 || inArray.Length < lengthIn)
                    throw new ArgumentOutOfRangeException("lengthIn");
                if (inArray.Length - offsetIn < lengthIn)
                    throw new ArgumentOutOfRangeException();
                if (outArray == null)
                    throw new ArgumentNullException("outArray");
                if (offsetOut < 0 || outArray.Length < offsetOut)
                    throw new ArgumentOutOfRangeException("offsetOut");

                int lengthOut = CalculateLength(lengthIn);

                if (lengthOut < 0 || outArray.Length < lengthOut)
                    throw new ArgumentOutOfRangeException("offsetOut");
                if (outArray.Length - offsetOut < lengthOut)
                    throw new ArgumentOutOfRangeException();

                int positionIn = offsetIn;
                int positionOut = offsetOut;

                int queue = 0;
                int bitsInQueue = 0;

                for (int i = 0; i != lengthIn; ++i)
                {
                    queue <<= inBitsPerByte;
                    queue |= inArray[positionIn];
                    ++positionIn;
                    bitsInQueue += inBitsPerByte;
                    for (; bitsInQueue >= outBitsPerByte; bitsInQueue -= outBitsPerByte)
                    {
                        int outIndex = (queue >> (bitsInQueue - outBitsPerByte)) & outBitMask;
                        outArray[positionOut] = lowerTable[outIndex];
                        ++positionOut;
                    }
                }

#if DEBUG
                System.Diagnostics.Debug.Assert(bitsInQueue < outBitsPerByte);
#endif

                if (bitsInQueue != 0)
                {
                    int outIndex = (queue << (outBitsPerByte - bitsInQueue)) & outBitMask;
                    outArray[positionOut] = lowerTable[outIndex];
                    ++positionOut;
                    bitsInQueue = 0;
                }

#if DEBUG
                System.Diagnostics.Debug.Assert(positionIn == offsetIn + lengthIn);
                System.Diagnostics.Debug.Assert(positionOut == offsetOut + lengthOut);
                System.Diagnostics.Debug.Assert(bitsInQueue == 0);
#endif

                return lengthOut;
            }
            public static string ToString(byte[] inArray)
            {
                return ToString(inArray, 0, inArray.Length);
            }
            public static string ToString(byte[] inArray, int offset, int length)
            {
                char[] outArray = new char[CalculateLength(length)];
                ToCharArray(inArray, offset, length, outArray, 0);
                return new string(outArray);
            }
        }
        public static class Base64
        {
            public static byte[] FromString(string s)
            {
                return Convert.FromBase64String(s.Replace('-', '+').Replace('~', '/'));
            }
            public static byte[] FromCharArray(char[] inArray, int offset, int length)
            {
                return FromString(new string(inArray, offset, length));
            }
        }
        public static class SHA256
        {
            public static byte[] Compute(byte[] buffer, int offset, int count)
            {
                return System.Security.Cryptography.SHA256.Create().ComputeHash(buffer, offset, count);
            }
            public static byte[] Compute(byte[] buffer)
            {
                return Compute(buffer, 0, buffer.Length);
            }
        }
        public static class Base32Address
        {
            public static string FromDestination(byte[] destination)
            {
                byte[] hashResult = SHA256.Compute(destination);
                return Base32.ToString(hashResult) + ".b32.i2p";
            }
            public static string FromDestinationBase64(string destinationBase64)
            {
                byte[] destination = Base64.FromString(destinationBase64);
                return FromDestination(destination);
            }
        }
    }
}
