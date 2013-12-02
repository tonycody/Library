using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "Link", Namespace = "http://Library/Net/Lair")]
    internal sealed class Link : ReadOnlyCertificateItemBase<Link>, ILink<Tag>
    {
        private enum SerializeId : byte
        {
            Tag = 0,
            Option = 1,
        }

        private Tag _tag;
        private OptionCollection _options;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxTypeLength = 256;
        public static readonly int MaxOptionCount = 32;

        public Link(Tag tag, IEnumerable<string> options)
        {
            this.Tag = tag;
            if (options != null) this.ProtectedOptions.AddRange(options);
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
                        if (id == (byte)SerializeId.Tag)
                        {
                            this.Tag = Tag.Import(rangeStream, bufferManager);
                        }
                        else if (id == (byte)SerializeId.Option)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.ProtectedOptions.Add(reader.ReadToEnd());
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

                // Tag
                if (this.Tag != null)
                {
                    Stream exportStream = this.Tag.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Tag);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // Options
                foreach (var o in this.Options)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(o);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Option);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
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
            if ((object)obj == null || !(obj is Link)) return false;

            return this.Equals((Link)obj);
        }

        public override bool Equals(Link other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Tag != other.Tag
                || (this.Options == null) != (other.Options == null))
            {
                return false;
            }

            if (this.Options != null && other.Options != null)
            {
                if (!Collection.Equals(this.Options, other.Options)) return false;
            }

            return true;
        }

        public override Link DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Link.Import(stream, BufferManager.Instance);
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

        #region ILink<Tag>

        [DataMember(Name = "Tag")]
        public Tag Tag
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _tag;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _tag = value;
                }
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyOptions;

        public IEnumerable<string> Options
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyOptions == null)
                        _readOnlyOptions = new ReadOnlyCollection<string>(this.ProtectedOptions);

                    return _readOnlyOptions;
                }
            }
        }

        [DataMember(Name = "Options")]
        private OptionCollection ProtectedOptions
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_options == null)
                        _options = new OptionCollection(Link.MaxOptionCount);

                    return _options;
                }
            }
        }

        #endregion
    }
}
