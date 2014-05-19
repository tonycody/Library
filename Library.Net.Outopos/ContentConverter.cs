using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Library.Compression;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    static class ContentConverter
    {
        private static BufferManager _bufferManager = BufferManager.Instance;

        public static Stream CollectionToStream<T>(IEnumerable<T> items)
            where T : ItemBase<T>
        {
            var itemCollection = new ItemCollection<T>(items);
            return itemCollection.Export(_bufferManager);
        }

        public static IEnumerable<T> StreamToCollection<T>(Stream stream)
            where T : ItemBase<T>
        {
            var itemCollection = ItemCollection<T>.Import(stream, _bufferManager);
            return itemCollection.Items;
        }

        sealed class ItemCollection<T> : ItemBase<ItemCollection<T>>
            where T : ItemBase<T>
        {
            private enum SerializeId : byte
            {
                Item = 0,
            }

            private volatile List<T> _items;

            public ItemCollection(IEnumerable<T> items)
            {
                if (items != null) this.Items.AddRange(items);
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

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        this.Items.Add(ItemBase<T>.Import(rangeStream, bufferManager));
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Items
                foreach (var value in this.Items)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        bufferStream.Write(NetworkConverter.GetBytes((int)stream.Length), 0, 4);

                        byte[] buffer = null;

                        try
                        {
                            buffer = bufferManager.TakeBuffer(1024 * 4);
                            int length = 0;

                            while ((length = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                bufferStream.Write(buffer, 0, length);
                            }
                        }
                        finally
                        {
                            if (buffer != null)
                            {
                                _bufferManager.ReturnBuffer(buffer);
                            }
                        }
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            public override int GetHashCode()
            {
                if (this.Items.Count == 0) return 0;
                else return this.Items[0].GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if ((object)obj == null || !(obj is ItemCollection<T>)) return false;

                return this.Equals((ItemCollection<T>)obj);
            }

            public override bool Equals(ItemCollection<T> other)
            {
                if ((object)other == null) return false;
                if (object.ReferenceEquals(this, other)) return true;

                if (!CollectionUtilities.Equals(this.Items, other.Items))
                {
                    return false;
                }

                return true;
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
}
