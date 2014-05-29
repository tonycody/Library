using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using System.Collections.ObjectModel;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiPageContent", Namespace = "http://Library/Net/Outopos")]
    sealed class WikiPageContent : ItemBase<WikiPageContent>, IHypertext
    {
        private enum SerializeId : byte
        {
            FormatType = 0,
            Hypertext = 1,
        }

        private HypertextFormatType _formatType;
        private string _hypertext;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxHypertextLength = 1024 * 32;

        public WikiPageContent(HypertextFormatType formatType, string hypertext)
        {
            this.FormatType = formatType;
            this.Hypertext = hypertext;
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
                    if (id == (byte)SerializeId.FormatType)
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
            if ((object)obj == null || !(obj is WikiPageContent)) return false;

            return this.Equals((WikiPageContent)obj);
        }

        public override bool Equals(WikiPageContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.FormatType != other.FormatType
                || this.Hypertext != other.Hypertext)
            {
                return false;
            }

            return true;
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
                if (value != null && value.Length > WikiPageContent.MaxHypertextLength)
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
