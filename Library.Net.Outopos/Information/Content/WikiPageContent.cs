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
    sealed class WikiPageContent : ItemBase<WikiPageContent>, IWikiPageContent
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
                        if (id == (byte)SerializeId.FormatType)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.FormatType = (HypertextFormatType)Enum.Parse(typeof(HypertextFormatType), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.Hypertext)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Hypertext = reader.ReadToEnd();
                            }
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // FormatType
                if (this.FormatType != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.FormatType.ToString());
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.FormatType);

                    streams.Add(bufferStream);
                }
                // Hypertext
                if (this.Hypertext != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.Hypertext);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Hypertext);

                    streams.Add(bufferStream);
                }

                return new UniteStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Hypertext == null) return 0;
                else return this.Hypertext.GetHashCode();
            }
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

        private object ThisLock
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

        #region IWikiPageContent

        [DataMember(Name = "FormatType")]
        public HypertextFormatType FormatType
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _formatType;
                }
            }
            set
            {
                lock (this.ThisLock)
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
                lock (this.ThisLock)
                {
                    return _hypertext;
                }
            }
            private set
            {
                lock (this.ThisLock)
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

        #endregion
    }
}
