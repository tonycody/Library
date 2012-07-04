using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    public class FilterList<T> : IList<T>, ICollection<T>, IEnumerable<T>, IList, ICollection, IEnumerable, IThisLock
    {
        private List<T> _list;
        private int? _capacity = null;
        private object _thisLock = new object();

        public FilterList()
        {
            _list = new List<T>();
        }

        public FilterList(int capacity)
        {
            _list = new List<T>(capacity);
        }

        public FilterList(IEnumerable<T> collection)
            : this()
        {
            foreach (var item in collection)
            {
                this.Add(item);
            }
        }

        protected virtual bool Filter(T item)
        {
            return false;
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
                if (this.Filter(item)) return;

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

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var item in _list)
                {
                    yield return item;
                }
            }
        }

        public FilterList<T> GetRange(int index, int count)
        {
            lock (this.ThisLock)
            {
                return new FilterList<T>(_list.GetRange(index, count));
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

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this.CopyTo(array.OfType<T>().ToArray(), arrayIndex);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
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

        object ICollection.SyncRoot
        {
            get
            {
                return this.ThisLock;
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
