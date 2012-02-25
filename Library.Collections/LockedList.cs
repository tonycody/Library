using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Library.Collections
{
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// 同期されている(スレッドセーフな)Library.Collections.Generic.LockedList&lt;T&gt;ラッパーを返します
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <returns></returns>
        public static LockedList<T> ToLockedList<T>(this IEnumerable<T> list)
        {
            return new LockedList<T>(list);
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
            : this()
        {
            foreach (var item in collection)
            {
                this.Add(item);
            }
        }

        public int Capacity
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _capacity.Value;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _capacity = value;
                }
            }
        }

        public int Count
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this._list.Count;
                }
            }
        }

        public T this[int index]
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this._list[index];
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    this._list[index] = value;
                }
            }
        }

        public void Add(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_capacity != null && _list.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                this._list.Add(item);
            }
        }

        public void AddRange(IEnumerable<T> collection)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var item in collection)
                {
                    this.Add(item);
                }
            }
        }

        public void Clear()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._list.Clear();
            }
        }

        public bool Contains(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._list.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._list.CopyTo(array, arrayIndex);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var item in _list)
                {
                    yield return item;
                }
            }
        }

        public LockedList<T> GetRange(int index, int count)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return new LockedList<T>(this._list.GetRange(index, count));
            }
        }

        public void Sort(IComparer<T> comparer)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _list.Sort(comparer);
            }
        }

        public void Sort(int index, int count, IComparer<T> comparer)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _list.Sort(index, count, comparer);
            }
        }

        public void Sort(Comparison<T> comparerison)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _list.Sort(comparerison);
            }
        }

        public void Sort()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _list.Sort();
            }
        }

        public void Reverse()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _list.Reverse();
            }
        }

        public void Reverse(int index, int count)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _list.Reverse(index, count);
            }
        }

        public int IndexOf(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._list.IndexOf(item);
            }
        }

        public void Insert(int index, T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._list.Insert(index, item);
            }
        }

        public bool Remove(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._list.Remove(item);
            }
        }

        public void RemoveAt(int index)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._list.RemoveAt(index);
            }
        }

        public void TrimExcess()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._list.TrimExcess();
            }
        }

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.CopyTo(array.OfType<T>().ToArray(), arrayIndex);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.GetEnumerator();
            }
        }

        int IList.Add(object item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Add((T)item);
                return this._list.Count - 1;
            }
        }

        bool IList.Contains(object item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.Contains((T)item);
            }
        }

        int IList.IndexOf(object item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.IndexOf((T)item);
            }
        }

        void IList.Insert(int index, object item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Insert(index, (T)item);
            }
        }

        void IList.Remove(object item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Remove((T)item);
            }
        }

        bool ICollection<T>.IsReadOnly
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return false;
                }
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return true;
                }
            }
        }

        bool IList.IsFixedSize
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return false;
                }
            }
        }

        bool IList.IsReadOnly
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return false;
                }
            }
        }

        object IList.this[int index]
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this[index];
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    this[index] = (T)value;
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

        #region IThisLock メンバ

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
