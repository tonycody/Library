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

        private volatile string _path;
        private DateTime _creationTime;

        private volatile HypertextFormatType _formatType;
        private volatile string _hypertext;

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
            for (; ; )
            {
                byte id;
                {
                    byte[] idBuffer = new byte[1];
                    if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                    id = idBuffer[0];
                }

                int length;
                {
                    byte[] lengthBuffer = new byte[4];
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    length = NetworkConverter.ToInt32(lengthBuffer);
                }

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

        protected override Stream Export(BufferManager bufferManager, int count)
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

        public override int GetHashCode()
        {
            if (this.Hypertext == null) return 0;
            else return this.Hypertext.GetHashCode();
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

        #region IWikiPage

        [DataMember(Name = "Path")]
        public string Path
        {
            get
            {
                return _path;
            }
            private set
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

        #endregion

        #region IHypertext

        [DataMember(Name = "FormatType")]
        public HypertextFormatType FormatType
        {
            get
            {
                return _formatType;
            }
            set
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

        [DataMember(Name = "Hypertext")]
        public string Hypertext
        {
            get
            {
                return _hypertext;
            }
            private set
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

        #endregion
    }
}
