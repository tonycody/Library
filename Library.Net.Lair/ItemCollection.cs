using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Library.Io;

namespace Library.Net.Lair
{
    class ItemCollection<T> : ItemBase<ItemCollection<T>>
         where T : ItemBase<T>
    {
        private enum SerializeId : byte
        {
            Item = 0,
        }

        private List<T> _items;
        private static object _thisStaticLock = new object();

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            Encoding encoding = new UTF8Encoding(false);
            byte[] lengthBuffer = new byte[4];

            for (; ; )
            {
                if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length)
                    return;
                int length = NetworkConverter.ToInt32(lengthBuffer);
                byte id = (byte)stream.ReadByte();

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.Item)
                    {
                        this.Items.Add(ItemBase<T>.Import(rangeStream, bufferManager));
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();

            // Items
            foreach (var m in this.Items)
            {
                Stream exportStream = m.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Item);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            return new JoinStream(streams);
        }

        public override ItemCollection<T> DeepClone()
        {
            throw new NotImplementedException();
        }

        public List<T> Items
        {
            get
            {
                if (_items == null)
                    _items = new List<T>();

                return _items;
            }
        }
    }
}
