﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "DocumentPageContent", Namespace = "http://Library/Net/Lair")]
    public sealed class DocumentPageContent : ItemBase<DocumentPageContent>, IDocumentPageContent
    {
        private enum SerializeId : byte
        {
            FormatType = 0,
            Hypertext = 1,
            Comment = 2,
        }

        private HypertextFormatType _formatType;
        private string _hypertext;
        private string _comment;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxHypertextLength = 1024 * 32;
        public static readonly int MaxCommentLength = 1024 * 4;

        public DocumentPageContent(HypertextFormatType formatType, string hypertext, string comment)
        {
            this.FormatType = formatType;
            this.Hypertext = hypertext;
            this.Comment = comment;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
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
                        else if (id == (byte)SerializeId.Comment)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Comment = reader.ReadToEnd();
                            }
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
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
                // Comment
                if (this.Comment != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.Comment);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Comment);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (_hypertext == null) return 0;
                else return _hypertext.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is DocumentPageContent)) return false;

            return this.Equals((DocumentPageContent)obj);
        }

        public override bool Equals(DocumentPageContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.FormatType != other.FormatType
                || this.Hypertext != other.Hypertext
                || this.Comment != other.Comment)
            {
                return false;
            }

            return true;
        }

        public override DocumentPageContent DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return DocumentPageContent.Import(stream, BufferManager.Instance);
                }
            }
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

        #region IDocumentPage

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
                    if (value != null && value.Length > DocumentPageContent.MaxHypertextLength)
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
            private set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > DocumentPageContent.MaxCommentLength)
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

        #endregion
    }
}
