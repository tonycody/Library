using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Library.Io;
using Library.Security;

namespace Library.Net.Connection
{
    public class SecureClientConnection : ConnectionBase, IThisLock
    {
        private ConnectionBase _connection;
        private DigitalSignature _digitalSignature;
        private BufferManager _bufferManager;

        private SecureProtocolVersion _protocolVersion;
        private SecureProtocolVersion _myProtocolVersion;
        private SecureProtocolVersion _otherProtocolVersion;
        private SecureVersion1.Protocol _protocol1;
        private SecureVersion1.Protocol _myProtocol1;
        private SecureVersion1.Protocol _otherProtocol1;
        private byte[] _myCryptoKey;
        private byte[] _otherCryptoKey;
        private byte[] _myHmacKey;
        private byte[] _otherHmacKey;
        private SecureVersion1.ConnectionSignature _connectionSignature;

        private long _totalReceiveSize = 0;
        private long _totalSendSize = 0;

        private object _sendLock = new object();
        private object _receiveLock = new object();
        private object _thisLock = new object();

        private volatile bool _connect = false;
        private volatile bool _disposed = false;

        public SecureClientConnection(ConnectionBase connection, DigitalSignature digitalSignature, BufferManager bufferManager)
        {
            _connection = connection;
            _digitalSignature = digitalSignature;
            _bufferManager = bufferManager;

            _myProtocolVersion = SecureProtocolVersion.Version1;
            OperatingSystem osInfo = Environment.OSVersion;

            if (osInfo.Platform == PlatformID.Win32NT && osInfo.Version.Major >= 6)
            {
                _myProtocol1 = new SecureVersion1.Protocol()
                {
                    KeyExchangeAlgorithm = SecureVersion1.KeyExchangeAlgorithm.ECDiffieHellmanP521_Sha512 | SecureVersion1.KeyExchangeAlgorithm.Rsa2048,
                    CryptoAlgorithm = SecureVersion1.CryptoAlgorithm.Rijndael256,
                    HashAlgorithm = SecureVersion1.HashAlgorithm.Sha512
                };
            }
            else
            {
                _myProtocol1 = new SecureVersion1.Protocol()
                {
                    KeyExchangeAlgorithm = SecureVersion1.KeyExchangeAlgorithm.Rsa2048,
                    CryptoAlgorithm = SecureVersion1.CryptoAlgorithm.Rijndael256,
                    HashAlgorithm = SecureVersion1.HashAlgorithm.Sha512
                };
            }
        }

        public override IEnumerable<ConnectionBase> GetLayers()
        {
            var list = new List<ConnectionBase>(_connection.GetLayers());
            list.Add(this);

            return list;
        }

        public override long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.ReceivedByteCount;
            }
        }

        public override long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.SentByteCount;
            }
        }

        public Certificate Certificate
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connectionSignature.Certificate;
            }
        }

        public override void Connect(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    using (BufferStream stream = new BufferStream(_bufferManager))
                    using (XmlTextWriter xml = new XmlTextWriter(stream, new UTF8Encoding(false)))
                    {
                        xml.WriteStartDocument();

                        xml.WriteStartElement("Protocol");

                        if (_myProtocolVersion == SecureProtocolVersion.Version1)
                        {
                            xml.WriteStartElement("SecureConnection");
                            xml.WriteAttributeString("Version", "1");

                            xml.WriteElementString("KeyExchangeAlgorithm", _myProtocol1.KeyExchangeAlgorithm.ToString());
                            xml.WriteElementString("CryptoAlgorithm", _myProtocol1.CryptoAlgorithm.ToString());
                            xml.WriteElementString("HashAlgorithm", _myProtocol1.HashAlgorithm.ToString());

                            xml.WriteEndElement(); //Protocol
                        }

                        xml.WriteEndElement(); //Configuration

                        xml.WriteEndDocument();
                        xml.Flush();
                        stream.Flush();

                        stream.Seek(0, SeekOrigin.Begin);
                        _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                    }

                    _otherProtocol1 = new SecureVersion1.Protocol();

                    using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                    using (XmlTextReader xml = new XmlTextReader(stream))
                    {
                        while (xml.Read())
                        {
                            if (xml.NodeType == XmlNodeType.Element)
                            {
                                if (xml.LocalName == "SecureConnection")
                                {
                                    if (xml.GetAttribute("Version") == "1")
                                    {
                                        _otherProtocolVersion |= SecureProtocolVersion.Version1;

                                        using (var xmlReader = xml.ReadSubtree())
                                        {
                                            while (xmlReader.Read())
                                            {
                                                if (xmlReader.NodeType == XmlNodeType.Element)
                                                {
                                                    if (xmlReader.LocalName == "KeyExchangeAlgorithm")
                                                    {
                                                        try
                                                        {
                                                            string text = xmlReader.ReadString();

                                                            foreach (var item in text.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries))
                                                            {
                                                                _otherProtocol1.KeyExchangeAlgorithm |= (SecureVersion1.KeyExchangeAlgorithm)Enum.Parse(typeof(SecureVersion1.KeyExchangeAlgorithm), item);
                                                            }
                                                        }
                                                        catch (Exception)
                                                        {

                                                        }
                                                    }
                                                    else if (xml.LocalName == "CryptoAlgorithm")
                                                    {
                                                        try
                                                        {
                                                            string text = xmlReader.ReadString();

                                                            foreach (var item in text.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries))
                                                            {
                                                                _otherProtocol1.CryptoAlgorithm |= (SecureVersion1.CryptoAlgorithm)Enum.Parse(typeof(SecureVersion1.CryptoAlgorithm), item);
                                                            }
                                                        }
                                                        catch (Exception)
                                                        {

                                                        }
                                                    }
                                                    else if (xmlReader.LocalName == "HashAlgorithm")
                                                    {
                                                        try
                                                        {
                                                            string text = xmlReader.ReadString();

                                                            foreach (var item in text.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries))
                                                            {
                                                                _otherProtocol1.HashAlgorithm |= (SecureVersion1.HashAlgorithm)Enum.Parse(typeof(SecureVersion1.HashAlgorithm), item);
                                                            }
                                                        }
                                                        catch (Exception)
                                                        {

                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    _protocolVersion = _myProtocolVersion & _otherProtocolVersion;

                    if (_protocolVersion.HasFlag(SecureProtocolVersion.Version1))
                    {
                        _protocol1 = _myProtocol1 & _otherProtocol1;

                        if (_protocol1.KeyExchangeAlgorithm.HasFlag(SecureVersion1.KeyExchangeAlgorithm.ECDiffieHellmanP521_Sha512))
                        {
                            byte[] publicKey, privateKey;
                            ECDiffieHellmanP521_Sha512.CreateKeys(out publicKey, out privateKey);

                            {
                                SecureVersion1.ConnectionSignature connectionSignature = new SecureVersion1.ConnectionSignature();
                                connectionSignature.Key = publicKey;

                                if (_digitalSignature != null)
                                {
                                    connectionSignature.CreationTime = DateTime.UtcNow;
                                    connectionSignature.CreateCertificate(_digitalSignature);
                                }

                                using (Stream stream = connectionSignature.Export(_bufferManager))
                                {
                                    _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                                }
                            }

                            byte[] otherPublicKey = null;

                            using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                            {
                                SecureVersion1.ConnectionSignature connectionSignature = SecureVersion1.ConnectionSignature.Import(stream, _bufferManager);

                                if (connectionSignature.VerifyCertificate())
                                {
                                    if (connectionSignature.Certificate != null)
                                    {
                                        DateTime now = DateTime.UtcNow;
                                        TimeSpan span = (now < connectionSignature.CreationTime) ? connectionSignature.CreationTime - now : now - connectionSignature.CreationTime;

                                        if (span > new TimeSpan(0, 30, 0)) throw new ConnectionException();
                                    }

                                    _connectionSignature = connectionSignature;
                                    otherPublicKey = _connectionSignature.Key;
                                }
                                else
                                {
                                    throw new ConnectionException();
                                }
                            }

                            byte[] cryptoKey = ECDiffieHellmanP521_Sha512.DeriveKeyMaterial(privateKey, otherPublicKey);

                            if (cryptoKey == null) throw new ConnectionException();

                            if (_protocol1.CryptoAlgorithm.HasFlag(SecureVersion1.CryptoAlgorithm.Rijndael256))
                            {
                                ANSI_X963_KDF kdf = new ANSI_X963_KDF(new SHA512Managed());

                                _myCryptoKey = new byte[32 + 32];
                                _otherCryptoKey = new byte[32 + 32];
                                _myHmacKey = new byte[64];
                                _otherHmacKey = new byte[64];

                                using (MemoryStream stream = new MemoryStream(kdf.Calculate(cryptoKey, (32 + 32 + 64) * 2)))
                                {
                                    stream.Read(_myCryptoKey, 0, _myCryptoKey.Length);
                                    stream.Read(_otherCryptoKey, 0, _otherCryptoKey.Length);
                                    stream.Read(_myHmacKey, 0, _myHmacKey.Length);
                                    stream.Read(_otherHmacKey, 0, _otherHmacKey.Length);
                                }
                            }
                            else
                            {
                                throw new ConnectionException();
                            }
                        }
                        else if (_protocol1.KeyExchangeAlgorithm.HasFlag(SecureVersion1.KeyExchangeAlgorithm.Rsa2048))
                        {
                            byte[] publicKey, privateKey;
                            Rsa2048.CreateKeys(out publicKey, out privateKey);

                            {
                                SecureVersion1.ConnectionSignature connectionSignature = new SecureVersion1.ConnectionSignature();
                                connectionSignature.Key = publicKey;

                                if (_digitalSignature != null)
                                {
                                    connectionSignature.CreationTime = DateTime.UtcNow;
                                    connectionSignature.CreateCertificate(_digitalSignature);
                                }

                                using (Stream stream = connectionSignature.Export(_bufferManager))
                                {
                                    _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                                }
                            }

                            byte[] otherPublicKey;

                            using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                            {
                                SecureVersion1.ConnectionSignature connectionSignature = SecureVersion1.ConnectionSignature.Import(stream, _bufferManager);

                                if (connectionSignature.VerifyCertificate())
                                {
                                    if (connectionSignature.Certificate != null)
                                    {
                                        DateTime now = DateTime.UtcNow;
                                        TimeSpan span = (now < connectionSignature.CreationTime) ? connectionSignature.CreationTime - now : now - connectionSignature.CreationTime;

                                        if (span > new TimeSpan(0, 30, 0)) throw new ConnectionException();
                                    }

                                    _connectionSignature = connectionSignature;
                                    otherPublicKey = _connectionSignature.Key;
                                }
                                else
                                {
                                    throw new ConnectionException();
                                }
                            }

                            byte[] myCryptoKey = new byte[128];
                            (new RNGCryptoServiceProvider()).GetBytes(myCryptoKey);

                            using (MemoryStream stream = new MemoryStream(Rsa2048.Encrypt(otherPublicKey, myCryptoKey)))
                            {
                                _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                            }

                            byte[] otherCryptoKey;

                            using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                            {
                                var buffer = new byte[stream.Length];
                                stream.Read(buffer, 0, buffer.Length);

                                otherCryptoKey = Rsa2048.Decrypt(privateKey, buffer);
                            }

                            if (otherCryptoKey == null) throw new ConnectionException();

                            if (_protocol1.CryptoAlgorithm.HasFlag(SecureVersion1.CryptoAlgorithm.Rijndael256))
                            {
                                _myCryptoKey = new byte[32 + 32];
                                _otherCryptoKey = new byte[32 + 32];
                                _myHmacKey = new byte[64];
                                _otherHmacKey = new byte[64];

                                using (MemoryStream stream = new MemoryStream(myCryptoKey))
                                {
                                    stream.Read(_myCryptoKey, 0, _myCryptoKey.Length);
                                    stream.Read(_myHmacKey, 0, _myHmacKey.Length);
                                }

                                using (MemoryStream stream = new MemoryStream(otherCryptoKey))
                                {
                                    stream.Read(_otherCryptoKey, 0, _otherCryptoKey.Length);
                                    stream.Read(_otherHmacKey, 0, _otherHmacKey.Length);
                                }
                            }
                            else
                            {
                                throw new ConnectionException();
                            }
                        }
                        else
                        {
                            throw new ConnectionException();
                        }
                    }
                    else
                    {
                        throw new ConnectionException();
                    }
                }
                catch (ConnectionException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    throw new ConnectionException(ex.Message, ex);
                }

                _connect = true;
            }
        }

        public override void Close(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    _connection.Close(timeout);
                }
                catch (ConnectionException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    throw new ConnectionException(ex.Message, ex);
                }
            }
        }

        public override Stream Receive(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (_receiveLock)
            {
                try
                {
                    if (_protocolVersion.HasFlag(SecureProtocolVersion.Version1))
                    {
                        using (Stream stream = _connection.Receive(timeout))
                        {
                            byte[] totalReceiveSizeBuff = new byte[8];
                            if (stream.Read(totalReceiveSizeBuff, 0, totalReceiveSizeBuff.Length) != totalReceiveSizeBuff.Length) throw new ConnectionException();
                            long totalReceiveSize = NetworkConverter.ToInt64(totalReceiveSizeBuff);

                            if (_protocol1.HashAlgorithm.HasFlag(SecureVersion1.HashAlgorithm.Sha512))
                            {
                                _totalReceiveSize += (stream.Length - (8 + 64));

                                if (totalReceiveSize != _totalReceiveSize) throw new ConnectionException();

                                byte[] otherHmacBuff = new byte[64];
                                byte[] myHmacBuff = new byte[64];

                                stream.Seek(-64, SeekOrigin.End);
                                if (stream.Read(otherHmacBuff, 0, otherHmacBuff.Length) != otherHmacBuff.Length) throw new ConnectionException();
                                stream.SetLength(stream.Length - 64);
                                stream.Seek(0, SeekOrigin.Begin);

                                using (var hmacSha512 = new HMACSHA512(_otherHmacKey))
                                {
                                    myHmacBuff = hmacSha512.ComputeHash(stream);
                                }

                                if (!Collection.Equals(otherHmacBuff, myHmacBuff)) throw new ConnectionException();

                                stream.Seek(8, SeekOrigin.Begin);
                            }
                            else
                            {
                                throw new ConnectionException();
                            }

                            BufferStream bufferStream = new BufferStream(_bufferManager);

                            if (_protocol1.CryptoAlgorithm.HasFlag(SecureVersion1.CryptoAlgorithm.Rijndael256))
                            {
                                byte[] receiveBuffer = null;

                                try
                                {
                                    receiveBuffer = _bufferManager.TakeBuffer(1024 * 1024);

                                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                                    using (CryptoStream cs = new CryptoStream(stream,
                                        rijndael.CreateDecryptor(_otherCryptoKey.Take(32).ToArray(), _otherCryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
                                    {
                                        int i = -1;

                                        while ((i = cs.Read(receiveBuffer, 0, receiveBuffer.Length)) > 0)
                                        {
                                            bufferStream.Write(receiveBuffer, 0, i);
                                        }
                                    }
                                }
                                finally
                                {
                                    _bufferManager.ReturnBuffer(receiveBuffer);
                                }
                            }
                            else
                            {
                                throw new ConnectionException();
                            }

                            bufferStream.Seek(0, SeekOrigin.Begin);
                            return bufferStream;
                        }
                    }
                    else
                    {
                        throw new ConnectionException();
                    }
                }
                catch (ConnectionException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    throw new ConnectionException(ex.Message, ex);
                }
            }
        }

        public override void Send(Stream stream, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");
            if (!_connect) throw new ConnectionException();

            lock (_sendLock)
            {
                try
                {
                    if (_protocolVersion.HasFlag(SecureProtocolVersion.Version1))
                    {
                        using (BufferStream bufferStream = new BufferStream(_bufferManager))
                        {
                            bufferStream.SetLength(8);
                            bufferStream.Seek(8, SeekOrigin.Begin);

                            if (_protocol1.CryptoAlgorithm.HasFlag(SecureVersion1.CryptoAlgorithm.Rijndael256))
                            {
                                byte[] sendBuffer = null;

                                try
                                {
                                    sendBuffer = _bufferManager.TakeBuffer(1024 * 1024);

                                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                                    using (CryptoStream cs = new CryptoStream(new RangeStream(stream, true),
                                        rijndael.CreateEncryptor(_myCryptoKey.Take(32).ToArray(), _myCryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
                                    {
                                        int i = -1;

                                        while ((i = cs.Read(sendBuffer, 0, sendBuffer.Length)) > 0)
                                        {
                                            bufferStream.Write(sendBuffer, 0, i);
                                        }
                                    }
                                }
                                finally
                                {
                                    _bufferManager.ReturnBuffer(sendBuffer);
                                }
                            }
                            else
                            {
                                throw new ConnectionException();
                            }

                            _totalSendSize += (bufferStream.Length - 8);

                            bufferStream.Seek(0, SeekOrigin.Begin);

                            byte[] totalSendSizeBuff = NetworkConverter.GetBytes(_totalSendSize);
                            bufferStream.Write(totalSendSizeBuff, 0, totalSendSizeBuff.Length);

                            if (_protocol1.HashAlgorithm.HasFlag(SecureVersion1.HashAlgorithm.Sha512))
                            {
                                bufferStream.Seek(0, SeekOrigin.Begin);
                                byte[] hmacBuff;

                                using (var hmacSha512 = new HMACSHA512(_myHmacKey))
                                {
                                    hmacBuff = hmacSha512.ComputeHash(bufferStream);
                                }

                                bufferStream.Seek(0, SeekOrigin.End);
                                bufferStream.Write(hmacBuff, 0, hmacBuff.Length);
                            }
                            else
                            {
                                throw new ConnectionException();
                            }

                            bufferStream.Seek(0, SeekOrigin.Begin);

                            _connection.Send(bufferStream, timeout);
                        }
                    }
                    else
                    {
                        throw new ConnectionException();
                    }
                }
                catch (ConnectionException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    throw new ConnectionException(ex.Message, ex);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_connection != null)
                {
                    try
                    {
                        _connection.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _connection = null;
                }
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
}
