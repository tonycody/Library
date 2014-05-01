using System;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;

namespace Library
{
    [Serializable]
    [DataContract(Name = "ItemBase", Namespace = "http://Library")]
    public abstract class ItemBase<T> : IEquatable<T>
        where T : ItemBase<T>
    {
        public ItemBase()
        {
            this.Initialize();
        }

        protected abstract void Initialize();

#if DEBUG
        private int _callCount = 0;

        [OnDeserializing]
        private void OnDeserializingMethod(StreamingContext context)
        {
            if (Interlocked.Increment(ref _callCount) > 1) Log.Error("ItemBase<T>.OnDeserializingMethod");
            this.Initialize();
        }
#else
        [OnDeserializing]
        private void OnDeserializingMethod(StreamingContext context)
        {
            this.Initialize();
        }
#endif

        public static T Import(Stream stream, BufferManager bufferManager)
        {
            var item = (T)FormatterServices.GetUninitializedObject(typeof(T));
            item.Initialize();
            item.ProtectedImport(stream, bufferManager);

            return item;
        }

        protected virtual void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            this.ProtectedImport(stream, bufferManager, 0);
        }

        protected static T Import(Stream stream, BufferManager bufferManager, int count)
        {
            var item = (T)FormatterServices.GetUninitializedObject(typeof(T));
            item.Initialize();
            item.ProtectedImport(stream, bufferManager, count);

            return item;
        }

        protected abstract void ProtectedImport(Stream stream, BufferManager bufferManager, int count);

        public virtual Stream Export(BufferManager bufferManager)
        {
            return this.Export(bufferManager, 0);
        }

        protected abstract Stream Export(BufferManager bufferManager, int count);

        public static bool operator ==(ItemBase<T> x, ItemBase<T> y)
        {
            if ((object)x == null)
            {
                if ((object)y == null) return true;

                return ((T)y).Equals((T)x);
            }
            else
            {
                return ((T)x).Equals((T)y);
            }
        }

        public static bool operator !=(ItemBase<T> x, ItemBase<T> y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        #region IEquatable<T>

        public virtual bool Equals(T other)
        {
            return this.Equals((object)other);
        }

        #endregion
    }
}
