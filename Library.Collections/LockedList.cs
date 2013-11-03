using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// 同期されている(スレッドセーフな)Library.Collections.LockedList&lt;T&gt;ラッパーを返します
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <returns></returns>
        public static LockedList<T> ToLockedList<T>(this IEnumerable<T> list)
        {
            object lockObject = null;

            if (list is ICollection)
            {
                var collection = (ICollection)list;

                if (collection.IsSynchronized)
                {
                    lockObject = collection.SyncRoot;
                }
            }

            if (lockObject == null && list is IThisLock)
            {
                lockObject = ((IThisLock)list).ThisLock;
            }

            if (lockObject != null)
            {
                lock (lockObject)
                {
                    return new LockedList<T>(list);
                }
            }
            else
            {
                return new LockedList<T>(list);
            }
        }
    }

    public class LockedList<T> : IList<T>, ICollection<T>, IEnumerable<T>, IList, ICollection, IEnumerable, IThisLock
    {
        private List<T> _list;
        private int? _capacity = null;
        private object _thisLock = new object();

        public LockedList()
        {
            _list = new List<T>();
        }

        public LockedList(int capacity)
        {
            _list = new List<T>(capacity);
        }

        public LockedList(IEnumerable<T> collection)
        {
            _list = new List<T>();

            foreach (var item in collection)
            {
                this.Add(item);
            }
        }

        public T[] ToArray()
        {
            lock (this.ThisLock)
            {
                var array = new T[_list.Count];
                _list.CopyTo(array, 0);

                return array;
            }
        }

        public int Capacity
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _capacity.Value;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _capacity = value;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _list.Count;
                }
            }
        }

        public T this[int index]
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _list[index];
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _list[index] = value;
                }
            }
        }

        public void Add(T item)
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _list.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                _list.Add(item);
            }
        }

        public void AddRange(IEnumerable<T> collection)
        {
            lock (this.ThisLock)
            {
                foreach (var item in collection)
                {
                    this.Add(item);
                }
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _list.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                return _list.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                _list.CopyTo(array, arrayIndex);
            }
        }

        public LockedList<T> GetRange(int index, int count)
        {
            lock (this.ThisLock)
            {
                return new LockedList<T>(_list.GetRange(index, count));
            }
        }

        public void Sort(IComparer<T> comparer)
        {
            lock (this.ThisLock)
            {
                _list.Sort(comparer);
            }
        }

        public void Sort(int index, int count, IComparer<T> comparer)
        {
            lock (this.ThisLock)
            {
                _list.Sort(index, count, comparer);
            }
        }

        public void Sort(Comparison<T> comparerison)
        {
            lock (this.ThisLock)
            {
                _list.Sort(comparerison);
            }
        }

        public void Sort()
        {
            lock (this.ThisLock)
            {
                _list.Sort();
            }
        }

        public void Reverse()
        {
            lock (this.ThisLock)
            {
                _list.Reverse();
            }
        }

        public void Reverse(int index, int count)
        {
            lock (this.ThisLock)
            {
                _list.Reverse(index, count);
            }
        }

        public int IndexOf(T item)
        {
            lock (this.ThisLock)
            {
                return _list.IndexOf(item);
            }
        }

        public void Insert(int index, T item)
        {
            lock (this.ThisLock)
            {
                _list.Insert(index, item);
            }
        }

        public bool Remove(T item)
        {
            lock (this.ThisLock)
            {
                return _list.Remove(item);
            }
        }

        public void RemoveAt(int index)
        {
            lock (this.ThisLock)
            {
                _list.RemoveAt(index);
            }
        }

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                _list.TrimExcess();
            }
        }

        bool ICollection<T>.IsReadOnly
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        bool IList.IsFixedSize
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        bool IList.IsReadOnly
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        object IList.this[int index]
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this[index];
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    this[index] = (T)value;
                }
            }
        }

        int IList.Add(object item)
        {
            lock (this.ThisLock)
            {
                this.Add((T)item);
                return _list.Count - 1;
            }
        }

        bool IList.Contains(object item)
        {
            lock (this.ThisLock)
            {
                return this.Contains((T)item);
            }
        }

        int IList.IndexOf(object item)
        {
            lock (this.ThisLock)
            {
                return this.IndexOf((T)item);
            }
        }

        void IList.Insert(int index, object item)
        {
            lock (this.ThisLock)
            {
                this.Insert(index, (T)item);
            }
        }

        void IList.Remove(object item)
        {
            lock (this.ThisLock)
            {
                this.Remove((T)item);
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                lock (this.ThisLock)
                {
                    return true;
                }
            }
        }

        object ICollection.SyncRoot
        {
            get
            {
                return this.ThisLock;
            }
        }

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this.CopyTo(array.OfType<T>().ToArray(), arrayIndex);
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var item in _list)
                {
                    yield return item;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return ((IEnumerable<T>)this).GetEnumerator();
            }
        }

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }
}
