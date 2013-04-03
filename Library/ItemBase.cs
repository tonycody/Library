using System;
using System.IO;
using System.Runtime.Serialization;

namespace Library
{
    [DataContract(Name = "ItemBase", Namespace = "http://Library")]
    public abstract class ItemBase<T> : IEquatable<T>, IDeepCloneable<T>
        where T : ItemBase<T>
    {
        public static T Import(Stream stream, BufferManager bufferManager)
        {
            var item = (T)FormatterServices.GetUninitializedObject(typeof(T));
            item.ProtectedImport(stream, bufferManager);
            return item;
        }

        protected abstract void ProtectedImport(Stream stream, BufferManager bufferManager);
        public abstract Stream Export(BufferManager bufferManager);

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

        #region IDeepCloneable<T>

        public abstract T DeepClone();

        #endregion
    }
}
