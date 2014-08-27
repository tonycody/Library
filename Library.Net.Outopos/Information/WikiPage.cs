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
            FormatType = 1,
            Hypertext = 2,
        }

        private string _path;
        private HypertextFormatType _formatType;
        private string _hypertext;

        public static readonly int MaxPathLength = 256;
        public static readonly int MaxHypertextLength = 1024 * 32;

        public WikiPage(string path, HypertextFormatType formatType, string hypertext)
        {
            this.Path = path;
            this.FormatType = formatType;
            this.Hypertext = hypertext;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
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
    }
}
