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

        private RNGCryptoServiceProvider _random = new RNGCryptoServiceProvider();

        private SecureConnectionVersion _version;
        private SecureConnectionVersion _myVersion;
        private SecureConnectionVersion _otherVersion;

        private InformationVersion1 _informationVersion1;
        private InformationVersion2 _informationVersion2;

        private Certificate _certificate;

        private long _totalReceiveSize;
        private long _totalSendSize;

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;
        private volatile bool _disposed;

        public SecureConnection(SecureConnectionType type, SecureConnectionVersion version, ConnectionBase connection, DigitalSignature digitalSignature, BufferManager bufferManager)
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

        private static byte[] Xor(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                int length = Math.Min(x.Length, y.Length);
                byte[] buffer = new byte[Math.Max(x.Length, y.Length)];

                for (int i = 0; i < length; i++)
                {
                    buffer[i] = (byte)(x[i] ^ y[i]);
                }

                if (x.Length > y.Length)
                {
                    for (int i = x.Length - y.Length; i < buffer.Length; i++)
                    {
                        buffer[i] = x[i];
                    }
                }
                else
                {
                    for (int i = y.Length - x.Length; i < buffer.Length; i++)
                    {
                        buffer[i] = y[i];
                    }
                }

                return buffer;
            }
            else
            {
                byte[] buffer = new byte[x.Length];

                for (int i = 0; i < x.Length; i++)
                {
                    buffer[i] = (byte)(x[i] ^ y[i]);
                }

                return buffer;
            }
        }

        public override void Connect(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    SecureVersion1.ProtocolInformation myProtocol1;
                    SecureVersion1.ProtocolInformation otherProtocol1;

                    SecureVersion2.ProtocolInformation myProtocol2;
                    SecureVersion2.ProtocolInformation otherProtocol2;

                    {
                        OperatingSystem osInfo = Environment.OSVersion;

                        if (osInfo.Platform == PlatformID.Win32NT && osInfo.Version.Major >= 6)
                        {
                            myProtocol1 = new SecureVersion1.ProtocolInformation()
                            {
                                KeyExchangeAlgorithm = SecureVersion1.KeyExchangeAlgorithm.ECDiffieHellmanP521_Sha512 | SecureVersion1.KeyExchangeAlgorithm.Rsa2048,
                                CryptoAlgorithm = SecureVersion1.CryptoAlgorithm.Rijndael256,
                                HashAlgorithm = SecureVersion1.HashAlgorithm.Sha512
                            };

                            byte[] sessionId = new byte[64];
                            _random.GetBytes(sessionId);

                            myProtocol2 = new SecureVersion2.ProtocolInformation()
                            {
                                KeyExchangeAlgorithm = SecureVersion2.KeyExchangeAlgorithm.ECDiffieHellmanP521_Sha512 | SecureVersion2.KeyExchangeAlgorithm.Rsa2048,
                                KeyDerivationFunctionAlgorithm = SecureVersion2.KeyDerivationFunctionAlgorithm.ANSI_X963,
                                CryptoAlgorithm = SecureVersion2.CryptoAlgorithm.Rijndael256,
                                HashAlgorithm = SecureVersion2.HashAlgorithm.Sha512,
                                SessionId = sessionId,
                            };
                        }
                        else
                        {
                            myProtocol1 = new SecureVersion1.ProtocolInformation()
                            {
                                KeyExchangeAlgorithm = SecureVersion1.KeyExchangeAlgorithm.Rsa2048,
                                CryptoAlgorithm = SecureVersion1.CryptoAlgorithm.Rijndael256,
                                HashAlgorithm = SecureVersion1.HashAlgorithm.Sha512
                            };

                            byte[] sessionId = new byte[64];
                            _random.GetBytes(sessionId);

                            myProtocol2 = new SecureVersion2.ProtocolInformation()
                            {
                                KeyExchangeAlgorithm = SecureVersion2.KeyExchangeAlgorithm.Rsa2048,
                                KeyDerivationFunctionAlgorithm = SecureVersion2.KeyDerivationFunctionAlgorithm.ANSI_X963,
                                CryptoAlgorithm = SecureVersion2.CryptoAlgorithm.Rijndael256,
                                HashAlgorithm = SecureVersion2.HashAlgorithm.Sha512,
                                SessionId = sessionId,
                            };
                        }
                    }

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    using (BufferStream stream = new BufferStream(_bufferManager))
                    using (XmlTextWriter xml = new XmlTextWriter(stream, new UTF8Encoding(false)))
                    {
                        xml.WriteStartDocument();

                        xml.WriteStartElement("Protocol");

                        if (_myVersion.HasFlag(SecureConnectionVersion.Version1))
                        {
                            xml.WriteStartElement("SecureConnection");
                            xml.WriteAttributeString("Version", "1");

                            xml.WriteElementString("KeyExchangeAlgorithm", myProtocol1.KeyExchangeAlgorithm.ToString());
                            xml.WriteElementString("CryptoAlgorithm", myProtocol1.CryptoAlgorithm.ToString());
                            xml.WriteElementString("HashAlgorithm", myProtocol1.HashAlgorithm.ToString());

                            xml.WriteEndElement(); //Protocol
                        }

                        if (_myVersion.HasFlag(SecureConnectionVersion.Version2))
                        {
                            xml.WriteStartElement("SecureConnection");
                            xml.WriteAttributeString("Version", "2");
                            xml.WriteEndElement(); //Protocol
                        }

                        xml.WriteEndElement(); //Configuration

                        xml.WriteEndDocument();
                        xml.Flush();
                        stream.Flush();

                        stream.Seek(0, SeekOrigin.Begin);
                        _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                    }

                    otherProtocol1 = new SecureVersion1.ProtocolInformation();
                    otherProtocol2 = new SecureVersion2.ProtocolInformation();

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
                                        _otherVersion |= SecureConnectionVersion.Version1;

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
                                                                otherProtocol1.KeyExchangeAlgorithm |= (SecureVersion1.KeyExchangeAlgorithm)Enum.Parse(typeof(SecureVersion1.KeyExchangeAlgorithm), item);
                                                            }
                                                        }
                                                        catch (Exception)
                                                        {

                                                        }
                                                    }
                                                    else if (xmlReader.LocalName == "CryptoAlgorithm")
                                                    {
                                                        try
                                                        {
                                                            string text = xmlReader.ReadString();

                                                            foreach (var item in text.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries))
                                                            {
                                                                otherProtocol1.CryptoAlgorithm |= (SecureVersion1.CryptoAlgorithm)Enum.Parse(typeof(SecureVersion1.CryptoAlgorithm), item);
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
                                                                otherProtocol1.HashAlgorithm |= (SecureVersion1.HashAlgorithm)Enum.Parse(typeof(SecureVersion1.HashAlgorithm), item);
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
                                    else if (xml.GetAttribute("Version") == "2")
                                    {
                                        _otherVersion |= SecureConnectionVersion.Version2;
                                    }
                                }
                            }
                        }
                    }

                    _version = _myVersion & _otherVersion;

                    // Version2
                    if (_version.HasFlag(SecureConnectionVersion.Version2))
                    {
                        using (Stream stream = myProtocol2.Export(_bufferManager))
                        {
                            _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                        }

                        using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                        {
                            otherProtocol2 = SecureVersion2.ProtocolInformation.Import(stream, _bufferManager);
                        }

                        var keyExchangeAlgorithm = myProtocol2.KeyExchangeAlgorithm & otherProtocol2.KeyExchangeAlgorithm;
                        var keyDerivationFunctionAlgorithm = myProtocol2.KeyDerivationFunctionAlgorithm & otherProtocol2.KeyDerivationFunctionAlgorithm;
                        var cryptoAlgorithm = myProtocol2.CryptoAlgorithm & otherProtocol2.CryptoAlgorithm;
                        var hashAlgorithm = myProtocol2.HashAlgorithm & otherProtocol2.HashAlgorithm;

                        byte[] myCryptoKey;
                        byte[] otherCryptoKey;
                        byte[] myHmacKey;
                        byte[] otherHmacKey;

                        byte[] myHash = null;
                        byte[] otherHash = null;

                        if (hashAlgorithm.HasFlag(SecureVersion2.HashAlgorithm.Sha512))
                        {
                            using (var myHashStream = myProtocol2.Export(_bufferManager))
                            using (var otherHashStream = otherProtocol2.Export(_bufferManager))
                            {
                                myHash = Sha512.ComputeHash(myHashStream);
                                otherHash = Sha512.ComputeHash(otherHashStream);
                            }
                        }

                        byte[] seed;

                        if (keyExchangeAlgorithm.HasFlag(SecureVersion2.KeyExchangeAlgorithm.ECDiffieHellmanP521_Sha512))
                        {
                            byte[] publicKey, privateKey;
                            ECDiffieHellmanP521_Sha512.CreateKeys(out publicKey, out privateKey);

                            {
                                SecureVersion2.ConnectionSignature connectionSignature = new SecureVersion2.ConnectionSignature();
                                connectionSignature.Key = publicKey;

                                if (_digitalSignature != null)
                                {
                                    connectionSignature.CreationTime = DateTime.UtcNow;
                                    connectionSignature.MyHash = myHash;
                                    connectionSignature.OtherHash = otherHash;
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
                                SecureVersion2.ConnectionSignature connectionSignature = SecureVersion2.ConnectionSignature.Import(stream, _bufferManager);

                                if (connectionSignature.VerifyCertificate())
                                {
                                    if (connectionSignature.Certificate != null)
                                    {
                                        DateTime now = DateTime.UtcNow;
                                        TimeSpan span = (now > connectionSignature.CreationTime) ? now - connectionSignature.CreationTime : connectionSignature.CreationTime - now;
                                        if (span > new TimeSpan(0, 30, 0)) throw new ConnectionException();

                                        if (!Collection.Equals(connectionSignature.OtherHash, myHash)) throw new ConnectionException();
                                        if (!Collection.Equals(connectionSignature.MyHash, otherHash)) throw new ConnectionException();
                                    }

                                    _certificate = connectionSignature.Certificate;
                                    otherPublicKey = connectionSignature.Key;
                                }
                                else
                                {
                                    throw new ConnectionException();
                                }
                            }

                            seed = ECDiffieHellmanP521_Sha512.DeriveKeyMaterial(privateKey, otherPublicKey);
                            if (seed == null) throw new ConnectionException();
                        }
                        else if (keyExchangeAlgorithm.HasFlag(SecureVersion2.KeyExchangeAlgorithm.Rsa2048))
                        {
                            byte[] publicKey, privateKey;
                            Rsa2048.CreateKeys(out publicKey, out privateKey);

                            {
                                SecureVersion2.ConnectionSignature connectionSignature = new SecureVersion2.ConnectionSignature();
                                connectionSignature.Key = publicKey;

                                if (_digitalSignature != null)
                                {
                                    connectionSignature.CreationTime = DateTime.UtcNow;
                                    connectionSignature.MyHash = myHash;
                                    connectionSignature.OtherHash = otherHash;
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
                                SecureVersion2.ConnectionSignature connectionSignature = SecureVersion2.ConnectionSignature.Import(stream, _bufferManager);

                                if (connectionSignature.VerifyCertificate())
                                {
                                    if (connectionSignature.Certificate != null)
                                    {
                                        DateTime now = DateTime.UtcNow;
                                        TimeSpan span = (now > connectionSignature.CreationTime) ? now - connectionSignature.CreationTime : connectionSignature.CreationTime - now;
                                        if (span > new TimeSpan(0, 30, 0)) throw new ConnectionException();

                                        if (!Collection.Equals(connectionSignature.OtherHash, myHash)) throw new ConnectionException();
                                        if (!Collection.Equals(connectionSignature.MyHash, otherHash)) throw new ConnectionException();
                                    }

                                    _certificate = connectionSignature.Certificate;
                                    otherPublicKey = connectionSignature.Key;
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

                            seed = SecureConnection.Xor(mySeed, otherSeed);
                            if (seed == null) throw new ConnectionException();
                        }
                        else
                        {
                            throw new ConnectionException();
                        }

                        using (MemoryStream seedStream = new MemoryStream())
                        {
                            seedStream.Write(seed, 0, seed.Length);

                            byte[] xorSessionId = SecureConnection.Xor(myProtocol2.SessionId, otherProtocol2.SessionId);
                            seedStream.Write(xorSessionId, 0, xorSessionId.Length);

                            KeyDerivation kdf = null;

                            if (keyDerivationFunctionAlgorithm.HasFlag(SecureVersion2.KeyDerivationFunctionAlgorithm.ANSI_X963))
                            {
                                System.Security.Cryptography.HashAlgorithm hashFunction = null;

                                if (hashAlgorithm.HasFlag(SecureVersion2.HashAlgorithm.Sha512))
                                {
                                    hashFunction = new SHA512Managed();
                                }

                                kdf = new ANSI_X963_KDF(hashFunction);
                            }

                            if (cryptoAlgorithm.HasFlag(SecureVersion2.CryptoAlgorithm.Rijndael256))
                            {
                                myCryptoKey = new byte[32];
                                otherCryptoKey = new byte[32];
                                myHmacKey = new byte[64];
                                otherHmacKey = new byte[64];

                                using (MemoryStream stream = new MemoryStream(kdf.Calculate(seedStream.ToArray(), (32 + 64) * 2)))
                                {
                                    if (_type == SecureConnectionType.Client)
                                    {
                                        stream.Read(myCryptoKey, 0, myCryptoKey.Length);
                                        stream.Read(otherCryptoKey, 0, otherCryptoKey.Length);
                                        stream.Read(myHmacKey, 0, myHmacKey.Length);
                                        stream.Read(otherHmacKey, 0, otherHmacKey.Length);
                                    }
                                    else if (_type == SecureConnectionType.Server)
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
                        }

                        _informationVersion2 = new InformationVersion2();
                        _informationVersion2.CryptoAlgorithm = cryptoAlgorithm;
                        _informationVersion2.HashAlgorithm = hashAlgorithm;
                        _informationVersion2.MyCryptoKey = myCryptoKey;
                        _informationVersion2.OtherCryptoKey = otherCryptoKey;
                        _informationVersion2.MyHmacKey = myHmacKey;
                        _informationVersion2.OtherHmacKey = otherHmacKey;
                    }

                    // Version1
                    else if (_version.HasFlag(SecureConnectionVersion.Version1))
                    {
                        var keyExchangeAlgorithm = myProtocol1.KeyExchangeAlgorithm & otherProtocol1.KeyExchangeAlgorithm;
                        var cryptoAlgorithm = myProtocol1.CryptoAlgorithm & otherProtocol1.CryptoAlgorithm;
                        var hashAlgorithm = myProtocol1.HashAlgorithm & otherProtocol1.HashAlgorithm;

                        byte[] myCryptoKey;
                        byte[] otherCryptoKey;
                        byte[] myHmacKey;
                        byte[] otherHmacKey;

                        if (keyExchangeAlgorithm.HasFlag(SecureVersion1.KeyExchangeAlgorithm.ECDiffieHellmanP521_Sha512))
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
                                        TimeSpan span = (now > connectionSignature.CreationTime) ? now - connectionSignature.CreationTime : connectionSignature.CreationTime - now;

                                        if (span > new TimeSpan(0, 30, 0)) throw new ConnectionException();
                                    }

                                    _certificate = connectionSignature.Certificate;
                                    otherPublicKey = connectionSignature.Key;
                                }
                                else
                                {
                                    throw new ConnectionException();
                                }
                            }

                            byte[] seed = ECDiffieHellmanP521_Sha512.DeriveKeyMaterial(privateKey, otherPublicKey);

                            if (seed == null) throw new ConnectionException();

                            if (cryptoAlgorithm.HasFlag(SecureVersion1.CryptoAlgorithm.Rijndael256))
                            {
                                ANSI_X963_KDF kdf = new ANSI_X963_KDF(new SHA512Managed());

                                myCryptoKey = new byte[32 + 32];
                                otherCryptoKey = new byte[32 + 32];
                                myHmacKey = new byte[64];
                                otherHmacKey = new byte[64];

                                using (MemoryStream stream = new MemoryStream(kdf.Calculate(seed, (32 + 32 + 64) * 2)))
                                {
                                    if (_type == SecureConnectionType.Client)
                                    {
                                        stream.Read(myCryptoKey, 0, myCryptoKey.Length);
                                        stream.Read(otherCryptoKey, 0, otherCryptoKey.Length);
                                        stream.Read(myHmacKey, 0, myHmacKey.Length);
                                        stream.Read(otherHmacKey, 0, otherHmacKey.Length);
                                    }
                                    else if (_type == SecureConnectionType.Server)
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
                        }
                        else if (keyExchangeAlgorithm.HasFlag(SecureVersion1.KeyExchangeAlgorithm.Rsa2048))
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
                                        TimeSpan span = (now > connectionSignature.CreationTime) ? now - connectionSignature.CreationTime : connectionSignature.CreationTime - now;

                                        if (span > new TimeSpan(0, 30, 0)) throw new ConnectionException();
                                    }

                                    _certificate = connectionSignature.Certificate;
                                    otherPublicKey = connectionSignature.Key;
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

                            if (cryptoAlgorithm.HasFlag(SecureVersion1.CryptoAlgorithm.Rijndael256))
                            {
                                myCryptoKey = new byte[32 + 32];
                                otherCryptoKey = new byte[32 + 32];
                                myHmacKey = new byte[64];
                                otherHmacKey = new byte[64];

                                using (MemoryStream stream = new MemoryStream(mySeed))
                                {
                                    stream.Read(myCryptoKey, 0, myCryptoKey.Length);
                                    stream.Read(myHmacKey, 0, myHmacKey.Length);
                                }

                                using (MemoryStream stream = new MemoryStream(otherSeed))
                                {
                                    stream.Read(otherCryptoKey, 0, otherCryptoKey.Length);
                                    stream.Read(otherHmacKey, 0, otherHmacKey.Length);
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

                        _informationVersion1 = new InformationVersion1();
                        _informationVersion1.CryptoAlgorithm = cryptoAlgorithm;
                        _informationVersion1.HashAlgorithm = hashAlgorithm;
                        _informationVersion1.MyCryptoKey = myCryptoKey;
                        _informationVersion1.OtherCryptoKey = otherCryptoKey;
                        _informationVersion1.MyHmacKey = myHmacKey;
                        _informationVersion1.OtherHmacKey = otherHmacKey;
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

        public override Stream Receive(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (_receiveLock)
            {
                try
                {
                    if (_version.HasFlag(SecureConnectionVersion.Version2))
                    {
                        using (Stream stream = _connection.Receive(timeout))
                        {
                            byte[] totalReceiveSizeBuff = new byte[8];
                            if (stream.Read(totalReceiveSizeBuff, 0, totalReceiveSizeBuff.Length) != totalReceiveSizeBuff.Length) throw new ConnectionException();
                            long totalReceiveSize = NetworkConverter.ToInt64(totalReceiveSizeBuff);

                            if (_informationVersion2.HashAlgorithm.HasFlag(SecureVersion2.HashAlgorithm.Sha512))
                            {
                                _totalReceiveSize += (stream.Length - (8 + 64));

                                if (totalReceiveSize != _totalReceiveSize) throw new ConnectionException();

                                byte[] otherHmacBuff = new byte[64];
                                byte[] myHmacBuff = new byte[64];

                                stream.Seek(-64, SeekOrigin.End);
                                if (stream.Read(otherHmacBuff, 0, otherHmacBuff.Length) != otherHmacBuff.Length) throw new ConnectionException();
                                stream.SetLength(stream.Length - 64);
                                stream.Seek(0, SeekOrigin.Begin);

                                using (var hmacSha512 = new HMACSHA512(_informationVersion2.OtherHmacKey))
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

                            if (_informationVersion2.CryptoAlgorithm.HasFlag(SecureVersion2.CryptoAlgorithm.Rijndael256))
                            {
                                byte[] iv = new byte[32];
                                stream.Read(iv, 0, iv.Length);

                                byte[] receiveBuffer = null;

                                try
                                {
                                    receiveBuffer = _bufferManager.TakeBuffer(1024 * 32);

                                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                                    using (CryptoStream cs = new CryptoStream(stream, rijndael.CreateDecryptor(_informationVersion2.OtherCryptoKey, iv), CryptoStreamMode.Read))
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
                    else if (_version.HasFlag(SecureConnectionVersion.Version1))
                    {
                        using (Stream stream = _connection.Receive(timeout))
                        {
                            byte[] totalReceiveSizeBuff = new byte[8];
                            if (stream.Read(totalReceiveSizeBuff, 0, totalReceiveSizeBuff.Length) != totalReceiveSizeBuff.Length) throw new ConnectionException();
                            long totalReceiveSize = NetworkConverter.ToInt64(totalReceiveSizeBuff);

                            if (_informationVersion1.HashAlgorithm.HasFlag(SecureVersion1.HashAlgorithm.Sha512))
                            {
                                _totalReceiveSize += (stream.Length - (8 + 64));

                                if (totalReceiveSize != _totalReceiveSize) throw new ConnectionException();

                                byte[] otherHmacBuff = new byte[64];
                                byte[] myHmacBuff = new byte[64];

                                stream.Seek(-64, SeekOrigin.End);
                                if (stream.Read(otherHmacBuff, 0, otherHmacBuff.Length) != otherHmacBuff.Length) throw new ConnectionException();
                                stream.SetLength(stream.Length - 64);
                                stream.Seek(0, SeekOrigin.Begin);

                                using (var hmacSha512 = new HMACSHA512(_informationVersion1.OtherHmacKey))
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

                            if (_informationVersion1.CryptoAlgorithm.HasFlag(SecureVersion1.CryptoAlgorithm.Rijndael256))
                            {
                                byte[] receiveBuffer = null;

                                try
                                {
                                    receiveBuffer = _bufferManager.TakeBuffer(1024 * 32);

                                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                                    using (CryptoStream cs = new CryptoStream(stream,
                                        rijndael.CreateDecryptor(_informationVersion1.OtherCryptoKey.Take(32).ToArray(), _informationVersion1.OtherCryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
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
                    if (_version.HasFlag(SecureConnectionVersion.Version2))
                    {
                        using (BufferStream bufferStream = new BufferStream(_bufferManager))
                        {
                            bufferStream.SetLength(8);
                            bufferStream.Seek(8, SeekOrigin.Begin);

                            if (_informationVersion2.CryptoAlgorithm.HasFlag(SecureVersion2.CryptoAlgorithm.Rijndael256))
                            {
                                byte[] iv = new byte[32];
                                _random.GetBytes(iv);
                                bufferStream.Write(iv, 0, iv.Length);

                                byte[] sendBuffer = null;

                                try
                                {
                                    sendBuffer = _bufferManager.TakeBuffer(1024 * 32);

                                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                                    using (CryptoStream cs = new CryptoStream(new WrapperStream(stream, true), rijndael.CreateEncryptor(_informationVersion2.MyCryptoKey, iv), CryptoStreamMode.Read))
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

                            if (_informationVersion2.HashAlgorithm.HasFlag(SecureVersion2.HashAlgorithm.Sha512))
                            {
                                bufferStream.Seek(0, SeekOrigin.Begin);
                                byte[] hmacBuff;

                                using (var hmacSha512 = new HMACSHA512(_informationVersion2.MyHmacKey))
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
                    else if (_version.HasFlag(SecureConnectionVersion.Version1))
                    {
                        using (BufferStream bufferStream = new BufferStream(_bufferManager))
                        {
                            bufferStream.SetLength(8);
                            bufferStream.Seek(8, SeekOrigin.Begin);

                            if (_informationVersion1.CryptoAlgorithm.HasFlag(SecureVersion1.CryptoAlgorithm.Rijndael256))
                            {
                                byte[] sendBuffer = null;

                                try
                                {
                                    sendBuffer = _bufferManager.TakeBuffer(1024 * 32);

                                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                                    using (CryptoStream cs = new CryptoStream(new WrapperStream(stream, true),
                                        rijndael.CreateEncryptor(_informationVersion1.MyCryptoKey.Take(32).ToArray(), _informationVersion1.MyCryptoKey.Skip(32).Take(32).ToArray()), CryptoStreamMode.Read))
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

                            if (_informationVersion1.HashAlgorithm.HasFlag(SecureVersion1.HashAlgorithm.Sha512))
                            {
                                bufferStream.Seek(0, SeekOrigin.Begin);
                                byte[] hmacBuff;

                                using (var hmacSha512 = new HMACSHA512(_informationVersion1.MyHmacKey))
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

        class InformationVersion1
        {
            public SecureVersion1.CryptoAlgorithm CryptoAlgorithm { get; set; }
            public SecureVersion1.HashAlgorithm HashAlgorithm { get; set; }

            public byte[] MyCryptoKey { get; set; }
            public byte[] OtherCryptoKey { get; set; }

            public byte[] MyHmacKey { get; set; }
            public byte[] OtherHmacKey { get; set; }
        }

        class InformationVersion2
        {
            public SecureVersion2.CryptoAlgorithm CryptoAlgorithm { get; set; }
            public SecureVersion2.HashAlgorithm HashAlgorithm { get; set; }

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
