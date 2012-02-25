using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.IO;
using Library.Io;

namespace Library.Net.Nest
{
    [DataContract(Name = "CommandMessage", Namespace = "http://Library/Net/Nest")]
    public class CommandMessage : ItemBase<CommandMessage>, IThisLock
    {
        private enum SerializeId : byte
        {
            Command = 0,
            Content = 1,
        }

        private string _command;
        private ArraySegment<byte> _content;
        private object _thisLock;
        private static object _thisStaticLock = new object();

        public CommandMessage()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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
                        if (id == (byte)SerializeId.Command)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Command = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.Content)
                        {
                            byte[] buff = new byte[(int)rangeStream.Length];
                            rangeStream.Read(buff, 0, buff.Length);

                            this.Content = new ArraySegment<byte>(buff, 0, (int)rangeStream.Length);
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Command
                if (this.Command != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.Command);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Command);

                    streams.Add(bufferStream);
                }
                // Content
                if (this.Content != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Content.Count), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Content);
                    bufferStream.Write(this.Content.Array, this.Content.Offset, this.Content.Count);

                    streams.Add(bufferStream);
                }

                return new AddStream(streams);
            }
        }

        public override int GetHashCode()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.Command == null) return 0;
                else return this.Command.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is CommandMessage)) return false;

            return this.Equals((CommandMessage)obj);
        }

        public override bool Equals(CommandMessage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Command != other.Command)
                || (this.Content.Offset != other.Content.Offset)
                || (this.Content.Count != other.Content.Count)
                || ((this.Content.Array == null) != (other.Content.Array == null)))
            {
                return false;
            }

            if (this.Content.Array != null && other.Content.Array != null)
            {
                if (!Collection.Equals(this.Content.Array, this.Content.Offset,
                    other.Content.Array, other.Content.Offset, this.Content.Count)) return false;
            }

            return true;
        }

        public override CommandMessage DeepClone()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return CommandMessage.Import(stream, bufferManager);
                }
            }
        }

        [DataMember(Name = "Command")]
        public string Command
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _command;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _command = value;
                }
            }
        }

        [DataMember(Name = "Content")]
        public ArraySegment<byte> Content
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _content;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _content = value;
                }
            }
        }

        #region IThisLock メンバ

        public object ThisLock
        {
            get
            {
                using (DeadlockMonitor.Lock(_thisStaticLock))
                {
                    if (_thisLock == null)
                        _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }
}
