using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Store", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Store : ItemBase<Store>, IStore, ICloneable<Store>, IThisLock
    {
        private enum SerializeId : byte
        {
            Box = 0,
        }

        private BoxCollection _boxes;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxBoxCount = 8192;

        public Store()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Box)
                        {
                            this.Boxes.Add(Box.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Boxes
                foreach (var value in this.Boxes)
                {
                    using (Stream exportStream = value.Export(bufferManager))
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

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Boxes.Count == 0) return 0;
                else return this.Boxes[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Store)) return false;

            return this.Equals((Store)obj);
        }

        public override bool Equals(Store other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!Collection.Equals(this.Boxes, other.Boxes))
            {
                return false;
            }

            return true;
        }

        #region IStore

        [DataMember(Name = "Boxes")]
        public BoxCollection Boxes
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_boxes == null)
                        _boxes = new BoxCollection(Store.MaxBoxCount);

                    return _boxes;
                }
            }
        }

        #endregion

        #region ICloneable<Store>

        public Store Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Store.Import(stream, BufferManager.Instance);
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