using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Index", Namespace = "http://Library/Net/Amoeba")]
    sealed class Index : ItemBase<Index>, IIndex<Key>
    {
        private enum SerializeId : byte
        {
            Key = 0,
        }

        private volatile KeyCollection _keys;

        public Index(IEnumerable<Key> keys)
        {
            if (keys != null) this.ProtectedKeys.AddRange(keys);
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
                    if (id == (byte)SerializeId.Key)
                    {
                        this.ProtectedKeys.Add(Key.Import(rangeStream, bufferManager));
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // Keys
            foreach (var value in this.Keys)
            {
                using (var stream = value.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                }
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            if (this.ProtectedKeys.Count == 0) return 0;
            else return this.ProtectedKeys[0].GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Index)) return false;

            return this.Equals((Index)obj);
        }

        public override bool Equals(Index other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!CollectionUtilities.Equals(this.Keys, other.Keys))
            {
                return false;
            }

            return true;
        }

        #region IIndex<Key>

        private volatile ReadOnlyCollection<Key> _readOnlyKeys;

        public IEnumerable<Key> Keys
        {
            get
            {
                if (_readOnlyKeys == null)
                    _readOnlyKeys = new ReadOnlyCollection<Key>(this.ProtectedKeys);

                return _readOnlyKeys;
            }
        }

        [DataMember(Name = "Keys")]
        private KeyCollection ProtectedKeys
        {
            get
            {
                if (_keys == null)
                    _keys = new KeyCollection();

                return _keys;
            }
        }

        #endregion
    }
}
