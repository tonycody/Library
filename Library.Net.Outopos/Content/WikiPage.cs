using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiPage", Namespace = "http://Library/Net/Outopos")]
    public sealed class WikiPage : ItemBase<WikiPage>, IWikiPage
    {
        private enum SerializeId : byte
        {
            Path = 0,
            CreationTime = 1,
            FormatType = 2,
            Hypertext = 3,
        }

        private string _path;
        private DateTime _creationTime;
        private HypertextFormatType _formatType;
        private string _hypertext;

        private volatile object _thisLock;

        public static readonly int MaxPathLength = 256;
        public static readonly int MaxHypertextLength = 1024 * 32;

        public WikiPage(string path, DateTime creationTime, HypertextFormatType formatType, string hypertext)
        {
            this.Path = path;
            this.CreationTime = creationTime;
            this.FormatType = formatType;
            this.Hypertext = hypertext;
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (_thisLock)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Path)
                        {
                            this.Path = ItemUtilities.GetString(rangeStream);
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                        else if (id == (byte)SerializeId.FormatType)
                        {
                            this.FormatType = (HypertextFormatType)Enum.Parse(typeof(HypertextFormatType), ItemUtilities.GetString(rangeStream));
                        }
                        else if (id == (byte)SerializeId.Hypertext)
                        {
                            this.Hypertext = ItemUtilities.GetString(rangeStream);
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (_thisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Path
                if (this.Path != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Path, this.Path);
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                }
                // FormatType
                if (this.FormatType != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.FormatType, this.FormatType.ToString());
                }
                // Hypertext
                if (this.Hypertext != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Hypertext, this.Hypertext);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }
        }

        public override int GetHashCode()
        {
            lock (_thisLock)
            {
                if (this.Hypertext == null) return 0;
                else return this.Hypertext.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is WikiPage)) return false;

            return this.Equals((WikiPage)obj);
        }

        public override bool Equals(WikiPage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Path != other.Path
                || this.CreationTime != other.CreationTime
                || this.FormatType != other.FormatType
                || this.Hypertext != other.Hypertext)
            {
                return false;
            }

            return true;
        }

        [DataMember(Name = "Path")]
        public string Path
        {
            get
            {
                lock (_thisLock)
                {
                    return _path;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    if (value != null && value.Length > WikiPage.MaxPathLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _path = value;
                    }
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (_thisLock)
                {
                    return _creationTime;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    var utc = value.ToUniversalTime();
                    _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
            }
        }

        [DataMember(Name = "FormatType")]
        public HypertextFormatType FormatType
        {
            get
            {
                lock (_thisLock)
                {
                    return _formatType;
                }
            }
            set
            {
                lock (_thisLock)
                {
                    if (!Enum.IsDefined(typeof(HypertextFormatType), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _formatType = value;
                    }
                }
            }
        }

        [DataMember(Name = "Hypertext")]
        public string Hypertext
        {
            get
            {
                lock (_thisLock)
                {
                    return _hypertext;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    if (value != null && value.Length > WikiPage.MaxHypertextLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _hypertext = value;
                    }
                }
            }
        }
    }
}
