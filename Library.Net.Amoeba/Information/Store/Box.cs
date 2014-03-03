using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Box", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Box : CertificateItemBase<Box>, IBox, ICloneable<Box>, IThisLock
    {
        private enum SerializeId : byte
        {
            Name = 0,
            CreationTime = 1,
            Comment = 2,
            Seed = 3,
            Box = 4,

            Certificate = 5,
        }

        private string _name;
        private DateTime _creationTime;
        private string _comment;
        private SeedCollection _seeds;
        private BoxCollection _boxes;

        private Certificate _certificate;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxCommentLength = 1024;
        public static readonly int MaxBoxCount = 8192;
        public static readonly int MaxSeedCount = 1024 * 64;

        public Box()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            if (count > 256) throw new ArgumentException();

            lock (this.ThisLock)
            {
                Encoding encoding = new UTF8Encoding(false);
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Name)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Name = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CreationTime = DateTime.ParseExact(reader.ReadToEnd(), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                            }
                        }
                        else if (id == (int)SerializeId.Comment)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Comment = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.Seed)
                        {
                            this.Seeds.Add(Seed.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.Box)
                        {
                            this.Boxes.Add(Box.Import(rangeStream, bufferManager, count + 1));
                        }

                        else if (id == (byte)SerializeId.Certificate)
                        {
                            this.Certificate = Certificate.Import(rangeStream, bufferManager);
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            if (count > 256) throw new ArgumentException();

            lock (this.ThisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                Encoding encoding = new UTF8Encoding(false);

                // Name
                if (this.Name != null)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.Name.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Name);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.CreationTime);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                // Comment
                if (this.Comment != null)
                {
                    byte[] buffer = null;

                    try
                    {
                        var value = this.Comment.ToString();

                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Comment);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                // Seeds
                foreach (var value in this.Seeds)
                {
                    using (Stream exportStream = value.Export(bufferManager))
                    {
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Seed);

                        byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                        try
                        {
                            int length = 0;

                            while (0 < (length = exportStream.Read(buffer, 0, buffer.Length)))
                            {
                                bufferStream.Write(buffer, 0, length);
                            }
                        }
                        finally
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
                // Boxes
                foreach (var value in this.Boxes)
                {
                    using (Stream exportStream = value.Export(bufferManager, count + 1))
                    {
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Box);

                        byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                        try
                        {
                            int length = 0;

                            while (0 < (length = exportStream.Read(buffer, 0, buffer.Length)))
                            {
                                bufferStream.Write(buffer, 0, length);
                            }
                        }
                        finally
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }

                // Certificate
                if (this.Certificate != null)
                {
                    using (Stream exportStream = this.Certificate.Export(bufferManager))
                    {
                        bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.Certificate);

                        byte[] buffer = bufferManager.TakeBuffer(1024 * 4);

                        try
                        {
                            int length = 0;

                            while (0 < (length = exportStream.Read(buffer, 0, buffer.Length)))
                            {
                                bufferStream.Write(buffer, 0, length);
                            }
                        }
                        finally
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return _hashCode;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Box)) return false;

            return this.Equals((Box)obj);
        }

        public override bool Equals(Box other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Name != other.Name
                || this.CreationTime != other.CreationTime
                || this.Comment != other.Comment

                || this.Certificate != other.Certificate

                || !Collection.Equals(this.Seeds, other.Seeds)
                || !Collection.Equals(this.Boxes, other.Boxes))
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            lock (this.ThisLock)
            {
                return this.Name;
            }
        }

        public override void CreateCertificate(DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                base.CreateCertificate(digitalSignature);
            }
        }

        public override bool VerifyCertificate()
        {
            lock (this.ThisLock)
            {
                return base.VerifyCertificate();
            }
        }

        protected override Stream GetCertificateStream()
        {
            lock (this.ThisLock)
            {
                var temp = this.Certificate;
                this.Certificate = null;

                try
                {
                    return this.Export(BufferManager.Instance);
                }
                finally
                {
                    this.Certificate = temp;
                }
            }
        }

        public override Certificate Certificate
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _certificate;
                }
            }
            protected set
            {
                lock (this.ThisLock)
                {
                    _certificate = value;
                }
            }
        }

        #region IBox

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _name;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Box.MaxNameLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _name = value;
                        _hashCode = _name.GetHashCode();
                    }
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _creationTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _comment;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Box.MaxCommentLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _comment = value;
                    }
                }
            }
        }

        [DataMember(Name = "Seeds")]
        public SeedCollection Seeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_seeds == null)
                        _seeds = new SeedCollection(Box.MaxSeedCount);

                    return _seeds;
                }
            }
        }

        [DataMember(Name = "Boxes")]
        public BoxCollection Boxes
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_boxes == null)
                        _boxes = new BoxCollection(Box.MaxBoxCount);

                    return _boxes;
                }
            }
        }

        #endregion

        #region ICloneable<Box>

        public Box Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Box.Import(stream, BufferManager.Instance);
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                if (_thisLock == null)
                {
                    lock (_initializeLock)
                    {
                        if (_thisLock == null)
                        {
                            _thisLock = new object();
                        }
                    }
                }

                return _thisLock;
            }
        }

        #endregion
    }
}