using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Library.Io;
using Library.Security;

namespace Library.Net.Connections
{
    public class SecureConnection : ConnectionBase, IThisLock
    {
        private SecureConnectionType _type;
        private ConnectionBase _connection;
        private DigitalSignature _digitalSignature;
        private BufferManager _bufferManager;

        private RandomNumberGenerator _random = RandomNumberGenerator.Create();

        private SecureConnectionVersion _version;
        private SecureConnectionVersion _myVersion;
        private SecureConnectionVersion _otherVersion;

        private InformationVersion3 _informationVersion3;

        private Certificate _certificate;

        private long _totalReceiveSize;
        private long _totalSendSize;

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;

        private volatile bool _disposed;

        public SecureConnection(SecureConnectionVersion version, SecureConnectionType type, ConnectionBase connection, DigitalSignature digitalSignature, BufferManager bufferManager)
        {
            _type = type;
            _connection = connection;
            _digitalSignature = digitalSignature;
            _bufferManager = bufferManager;

            _myVersion = version;
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

        public SecureConnectionType Type
        {
            get
            {
                return _type;
            }
        }

        public Certificate Certificate
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _certificate;
            }
        }

        public override void Connect(TimeSpan timeout, Information options)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    SecureVersion3.ProtocolInformation myProtocol3;
                    SecureVersion3.ProtocolInformation otherProtocol3;

                    {
                        OperatingSystem osInfo = Environment.OSVersion;

                        // Windows VistaˆÈãB
                        if (osInfo.Platform == PlatformID.Win32NT && osInfo.Version >= new Version(6, 0))
                        {
                            {
                                byte[] sessionId = new byte[64];
                                _random.GetBytes(sessionId);

                                myProtocol3 = new SecureVersion3.ProtocolInformation()
                                {
                                    KeyExchangeAlgorithm = SecureVersion3.KeyExchangeAlgorithm.EcDiffieHellmanP521 | SecureVersion3.KeyExchangeAlgorithm.Rsa2048,
                                    KeyDerivationAlgorithm = SecureVersion3.KeyDerivationAlgorithm.Pbkdf2,
                                    CryptoAlgorithm = SecureVersion3.CryptoAlgorithm.Aes256,
                                    HashAlgorithm = SecureVersion3.HashAlgorithm.Sha512,
                                    SessionId = sessionId,
                                };
                            }
                        }
                        else
                        {
                            {
                                byte[] sessionId = new byte[64];
                                _random.GetBytes(sessionId);

                                myProtocol3 = new SecureVersion3.ProtocolInformation()
                                {
                                    KeyExchangeAlgorithm = SecureVersion3.KeyExchangeAlgorithm.Rsa2048,
                                    KeyDerivationAlgorithm = SecureVersion3.KeyDerivationAlgorithm.Pbkdf2,
                                    CryptoAlgorithm = SecureVersion3.CryptoAlgorithm.Aes256,
                                    HashAlgorithm = SecureVersion3.HashAlgorithm.Sha512,
                                    SessionId = sessionId,
                                };
                            }
                        }
                    }

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    using (BufferStream stream = new BufferStream(_bufferManager))
                    using (XmlTextWriter xml = new XmlTextWriter(stream, new UTF8Encoding(false)))
                    {
                        xml.WriteStartDocument();

                        xml.WriteStartElement("Protocol");

                        if (_myVersion.HasFlag(SecureConnectionVersion.Version3))
                        {
                            xml.WriteStartElement("SecureConnection");
                            xml.WriteAttributeString("Version", "3");
                            xml.WriteEndElement(); //Protocol
                        }

                        xml.WriteEndElement(); //Configuration

                        xml.WriteEndDocument();
                        xml.Flush();
                        stream.Flush();

                        stream.Seek(0, SeekOrigin.Begin);
                        _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                    }

                    otherProtocol3 = new SecureVersion3.ProtocolInformation();

                    using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                    using (XmlTextReader xml = new XmlTextReader(stream))
                    {
                        while (xml.Read())
                        {
                            if (xml.NodeType == XmlNodeType.Element)
                            {
                                if (xml.LocalName == "SecureConnection")
                                {
                                    if (xml.GetAttribute("Version") == "3")
                                    {
                                        _otherVersion |= SecureConnectionVersion.Version3;
                                    }
                                }
                            }
                        }
                    }

                    _version = _myVersion & _otherVersion;

                    // Version3
                    if (_version.HasFlag(SecureConnectionVersion.Version3))
                    {
                        using (Stream stream = myProtocol3.Export(_bufferManager))
                        {
                            _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                        }

                        using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                        {
                            otherProtocol3 = SecureVersion3.ProtocolInformation.Import(stream, _bufferManager);
                        }

                        var keyExchangeAlgorithm = myProtocol3.KeyExchangeAlgorithm & otherProtocol3.KeyExchangeAlgorithm;
                        var keyDerivationFunctionAlgorithm = myProtocol3.KeyDerivationAlgorithm & otherProtocol3.KeyDerivationAlgorithm;
                        var cryptoAlgorithm = myProtocol3.CryptoAlgorithm & otherProtocol3.CryptoAlgorithm;
                        var hashAlgorithm = myProtocol3.HashAlgorithm & otherProtocol3.HashAlgorithm;

                        byte[] myCryptoKey;
                        byte[] otherCryptoKey;
                        byte[] myHmacKey;
                        byte[] otherHmacKey;

                        byte[] myProtocolHash = null;
                        byte[] otherProtocolHash = null;

                        if (hashAlgorithm.HasFlag(SecureVersion3.HashAlgorithm.Sha512))
                        {
                            using (var myProtocolHashStream = myProtocol3.Export(_bufferManager))
                            using (var otherProtocolHashStream = otherProtocol3.Export(_bufferManager))
                            {
                                myProtocolHash = Sha512.ComputeHash(myProtocolHashStream);
                                otherProtocolHash = Sha512.ComputeHash(otherProtocolHashStream);
                            }
                        }

                        byte[] seed = null;

                        if (keyExchangeAlgorithm.HasFlag(SecureVersion3.KeyExchangeAlgorithm.EcDiffieHellmanP521))
                        {
                            byte[] publicKey, privateKey;
                            EcDiffieHellmanP521.CreateKeys(out publicKey, out privateKey);

                            {
                                SecureVersion3.ConnectionSignature connectionSignature = new SecureVersion3.ConnectionSignature();
                                connectionSignature.ExchangeKey = publicKey;

                                if (_digitalSignature != null)
                                {
                                    connectionSignature.CreationTime = DateTime.UtcNow;
                                    connectionSignature.ProtocolHash = myProtocolHash;
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
                                SecureVersion3.ConnectionSignature connectionSignature = SecureVersion3.ConnectionSignature.Import(stream, _bufferManager);

                                if (connectionSignature.VerifyCertificate())
                                {
                                    if (connectionSignature.Certificate != null)
                                    {
                                        DateTime now = DateTime.UtcNow;
                                        TimeSpan span = (now > connectionSignature.CreationTime) ? now - connectionSignature.CreationTime : connectionSignature.CreationTime - now;
                                        if (span > new TimeSpan(0, 30, 0)) throw new ConnectionException();

                                        if (!Unsafe.Equals(connectionSignature.ProtocolHash, otherProtocolHash)) throw new ConnectionException();
                                    }

                                    _certificate = connectionSignature.Certificate;
                                    otherPublicKey = connectionSignature.ExchangeKey;
                                }
                                else
                                {
                                    throw new ConnectionException();
                                }
                            }

                            if (hashAlgorithm.HasFlag(SecureVersion3.HashAlgorithm.Sha512))
                            {
                                seed = EcDiffieHellmanP521.DeriveKeyMaterial(privateKey, otherPublicKey, CngAlgorithm.Sha512);
                            }

                            if (seed == null) throw new ConnectionException();
                        }
                        else if (keyExchangeAlgorithm.HasFlag(SecureVersion3.KeyExchangeAlgorithm.Rsa2048))
                        {
                            byte[] publicKey, privateKey;
                            Rsa2048.CreateKeys(out publicKey, out privateKey);

                            {
                                SecureVersion3.ConnectionSignature connectionSignature = new SecureVersion3.ConnectionSignature();
                                connectionSignature.ExchangeKey = publicKey;

                                if (_digitalSignature != null)
                                {
                                    connectionSignature.CreationTime = DateTime.UtcNow;
                                    connectionSignature.ProtocolHash = myProtocolHash;
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
                                SecureVersion3.ConnectionSignature connectionSignature = SecureVersion3.ConnectionSignature.Import(stream, _bufferManager);

                                if (connectionSignature.VerifyCertificate())
                                {
                                    if (connectionSignature.Certificate != null)
                                    {
                                        DateTime now = DateTime.UtcNow;
                                        TimeSpan span = (now > connectionSignature.CreationTime) ? now - connectionSignature.CreationTime : connectionSignature.CreationTime - now;
                                        if (span > new TimeSpan(0, 30, 0)) throw new ConnectionException();

                                        if (!Unsafe.Equals(connectionSignature.ProtocolHash, otherProtocolHash)) throw new ConnectionException();
                                    }

                                    _certificate = connectionSignature.Certificate;
                                    otherPublicKey = connectionSignature.ExchangeKey;
                                }
                                else
                                {
                                    throw new ConnectionException();
                                }
                            }

                            byte[] mySeed = new byte[128];
                            _random.GetBytes(mySeed);

                            using (MemoryStream stream = new MemoryStream(Rsa2048.Encrypt(otherPublicKey, mySeed)))
                            {
                                _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                            }

                            byte[] otherSeed;

                            using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                            {
                                var buffer = new byte[stream.Length];
                                stream.Read(buffer, 0, buffer.Length);

                                otherSeed = Rsa2048.Decrypt(privateKey, buffer);
                            }

                            if (otherSeed == null) throw new ConnectionException();

                            seed = new byte[Math.Max(mySeed.Length, otherSeed.Length)];
                            Unsafe.Xor(mySeed, otherSeed, seed);
                        }
                        else
                        {
                            throw new ConnectionException();
                        }

                        if (keyDerivationFunctionAlgorithm.HasFlag(SecureVersion3.KeyDerivationAlgorithm.Pbkdf2))
                        {
                            byte[] xorSessionId = new byte[Math.Max(myProtocol3.SessionId.Length, otherProtocol3.SessionId.Length)];
                            Unsafe.Xor(myProtocol3.SessionId, otherProtocol3.SessionId, xorSessionId);

                            HMAC hmac = null;

                            if (hashAlgorithm.HasFlag(SecureVersion3.HashAlgorithm.Sha512))
                            {
                                hmac = new HMACSHA512();
                                hmac.HashName = "System.Security.Cryptography.SHA512";
                            }
                            else
                            {
                                throw new ConnectionException();
                            }

                            Pbkdf2 pbkdf2 = new Pbkdf2(hmac, seed, xorSessionId, 1024);

                            int cryptoKeyLength;
                            int hmacKeyLength;

                            if (cryptoAlgorithm.HasFlag(SecureVersion3.CryptoAlgorithm.Aes256))
                            {
                                cryptoKeyLength = 32;
                            }
                            else
                            {
                                throw new ConnectionException();
                            }

                            if (hashAlgorithm.HasFlag(SecureVersion3.HashAlgorithm.Sha512))
                            {
                                hmacKeyLength = 64;
                            }
                            else
                            {
                                throw new ConnectionException();
                            }

                            myCryptoKey = new byte[cryptoKeyLength];
                            otherCryptoKey = new byte[cryptoKeyLength];
                            myHmacKey = new byte[hmacKeyLength];
                            otherHmacKey = new byte[hmacKeyLength];

                            using (MemoryStream stream = new MemoryStream(pbkdf2.GetBytes((cryptoKeyLength + hmacKeyLength) * 2)))
                            {
                                if (_type == SecureConnectionType.Connect)
                                {
                                    stream.Read(myCryptoKey, 0, myCryptoKey.Length);
                                    stream.Read(otherCryptoKey, 0, otherCryptoKey.Length);
                                    stream.Read(myHmacKey, 0, myHmacKey.Length);
                                    stream.Read(otherHmacKey, 0, otherHmacKey.Length);
                                }
                                else if (_type == SecureConnectionType.Accept)
                                {
                                    stream.Read(otherCryptoKey, 0, otherCryptoKey.Length);
                                    stream.Read(myCryptoKey, 0, myCryptoKey.Length);
                                    stream.Read(otherHmacKey, 0, otherHmacKey.Length);
                                    stream.Read(myHmacKey, 0, myHmacKey.Length);
                                }
                                else
                                {
                                    throw new ConnectionException();
                                }
                            }
                        }
                        else
                        {
                            throw new ConnectionException();
                        }

                        _informationVersion3 = new InformationVersion3();
                        _informationVersion3.CryptoAlgorithm = cryptoAlgorithm;
                        _informationVersion3.HashAlgorithm = hashAlgorithm;
                        _informationVersion3.MyCryptoKey = myCryptoKey;
                        _informationVersion3.OtherCryptoKey = otherCryptoKey;
                        _informationVersion3.MyHmacKey = myHmacKey;
                        _informationVersion3.OtherHmacKey = otherHmacKey;
                    }
                    else
                    {
                        throw new ConnectionException();
                    }
                }
                catch (ConnectionException e)
                {
                    throw e;
                }
                catch (Exception e)
                {
                    throw new ConnectionException(e.Message, e);
                }

                _connect = true;
            }
        }

        public override void Close(TimeSpan timeout, Information options)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (this.ThisLock)
            {
                if (_connection != null)
                {
                    try
                    {
                        _connection.Close(timeout);
                    }
                    catch (Exception)
                    {

                    }

                    _connection = null;
                }

                _connect = false;
            }
        }

        public override Stream Receive(TimeSpan timeout, Information options)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (_receiveLock)
            {
                try
                {
                    if (_version.HasFlag(SecureConnectionVersion.Version3))
                    {
                        using (Stream stream = _connection.Receive(timeout, options))
                        {
                            byte[] totalReceiveSizeBuff = new byte[8];
                            if (stream.Read(totalReceiveSizeBuff, 0, totalReceiveSizeBuff.Length) != totalReceiveSizeBuff.Length) throw new ConnectionException();
                            long totalReceiveSize = NetworkConverter.ToInt64(totalReceiveSizeBuff);

                            if (_informationVersion3.HashAlgorithm.HasFlag(SecureVersion3.HashAlgorithm.Sha512))
                            {
                                const int hashLength = 64;

                                _totalReceiveSize += (stream.Length - (8 + hashLength));

                                if (totalReceiveSize != _totalReceiveSize) throw new ConnectionException();

                                byte[] otherHmacBuff = new byte[hashLength];
                                stream.Seek(-hashLength, SeekOrigin.End);
                                if (stream.Read(otherHmacBuff, 0, otherHmacBuff.Length) != otherHmacBuff.Length) throw new ConnectionException();
                                stream.SetLength(stream.Length - hashLength);
                                stream.Seek(0, SeekOrigin.Begin);

                                byte[] myHmacBuff = HmacSha512.ComputeHash(stream, _informationVersion3.OtherHmacKey);

                                if (!Unsafe.Equals(otherHmacBuff, myHmacBuff)) throw new ConnectionException();

                                stream.Seek(8, SeekOrigin.Begin);
                            }
                            else
                            {
                                throw new ConnectionException();
                            }

                            TempStream tempStream = new TempStream();

                            if (_informationVersion3.CryptoAlgorithm.HasFlag(SecureVersion3.CryptoAlgorithm.Aes256))
                            {
                                byte[] iv = new byte[16];
                                stream.Read(iv, 0, iv.Length);

                                byte[] receiveBuffer = null;

                                try
                                {
                                    receiveBuffer = _bufferManager.TakeBuffer(1024 * 4);

                                    //using (var aes = new AesCryptoServiceProvider())
                                    using (var aes = Aes.Create())
                                    {
                                        aes.KeySize = 256;
                                        aes.Mode = CipherMode.CBC;
                                        aes.Padding = PaddingMode.PKCS7;

                                        using (CryptoStream cs = new CryptoStream(new WrapperStream(tempStream, true), aes.CreateDecryptor(_informationVersion3.OtherCryptoKey, iv), CryptoStreamMode.Write))
                                        {
                                            int i = -1;

                                            while ((i = stream.Read(receiveBuffer, 0, receiveBuffer.Length)) > 0)
                                            {
                                                cs.Write(receiveBuffer, 0, i);
                                            }
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

                            tempStream.Seek(0, SeekOrigin.Begin);
                            return tempStream;
                        }
                    }
                    else
                    {
                        throw new ConnectionException();
                    }
                }
                catch (ConnectionException e)
                {
                    throw e;
                }
                catch (Exception e)
                {
                    throw new ConnectionException(e.Message, e);
                }
            }
        }

        public override void Send(Stream stream, TimeSpan timeout, Information options)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");
            if (!_connect) throw new ConnectionException();

            lock (_sendLock)
            {
                try
                {
                    using (RangeStream targetStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position, true))
                    {
                        if (_version.HasFlag(SecureConnectionVersion.Version3))
                        {
                            using (TempStream tempStream = new TempStream())
                            {
                                tempStream.SetLength(8);
                                tempStream.Seek(8, SeekOrigin.Begin);

                                if (_informationVersion3.CryptoAlgorithm.HasFlag(SecureVersion3.CryptoAlgorithm.Aes256))
                                {
                                    byte[] iv = new byte[16];
                                    _random.GetBytes(iv);
                                    tempStream.Write(iv, 0, iv.Length);

                                    byte[] sendBuffer = null;

                                    try
                                    {
                                        sendBuffer = _bufferManager.TakeBuffer(1024 * 4);

                                        //using (var aes = new AesCryptoServiceProvider())
                                        using (var aes = Aes.Create())
                                        {
                                            aes.KeySize = 256;
                                            aes.Mode = CipherMode.CBC;
                                            aes.Padding = PaddingMode.PKCS7;

                                            using (CryptoStream cs = new CryptoStream(new WrapperStream(tempStream, true), aes.CreateEncryptor(_informationVersion3.MyCryptoKey, iv), CryptoStreamMode.Write))
                                            {
                                                int i = -1;

                                                while ((i = targetStream.Read(sendBuffer, 0, sendBuffer.Length)) > 0)
                                                {
                                                    cs.Write(sendBuffer, 0, i);
                                                }
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

                                _totalSendSize += (tempStream.Length - 8);

                                tempStream.Seek(0, SeekOrigin.Begin);

                                byte[] totalSendSizeBuff = NetworkConverter.GetBytes(_totalSendSize);
                                tempStream.Write(totalSendSizeBuff, 0, totalSendSizeBuff.Length);

                                if (_informationVersion3.HashAlgorithm.HasFlag(SecureVersion3.HashAlgorithm.Sha512))
                                {
                                    tempStream.Seek(0, SeekOrigin.Begin);
                                    byte[] hmacBuff = HmacSha512.ComputeHash(tempStream, _informationVersion3.MyHmacKey);

                                    tempStream.Seek(0, SeekOrigin.End);
                                    tempStream.Write(hmacBuff, 0, hmacBuff.Length);
                                }
                                else
                                {
                                    throw new ConnectionException();
                                }

                                tempStream.Seek(0, SeekOrigin.Begin);

                                _connection.Send(tempStream, timeout, options);
                            }
                        }
                        else
                        {
                            throw new ConnectionException();
                        }
                    }
                }
                catch (ConnectionException e)
                {
                    throw e;
                }
                catch (Exception e)
                {
                    throw new ConnectionException(e.Message, e);
                }
            }
        }

        private class InformationVersion3
        {
            public SecureVersion3.CryptoAlgorithm CryptoAlgorithm { get; set; }
            public SecureVersion3.HashAlgorithm HashAlgorithm { get; set; }

            public byte[] MyCryptoKey { get; set; }
            public byte[] OtherCryptoKey { get; set; }

            public byte[] MyHmacKey { get; set; }
            public byte[] OtherHmacKey { get; set; }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

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

                if (_random != null)
                {
                    try
                    {
                        _random.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _random = null;
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
