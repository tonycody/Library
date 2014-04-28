using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class LockedSortedSet<T> : ISet<T>, ISetOperators<T>, ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock
    {
        private SortedSet<T> _sortedSet;
        private int? _capacity;
        private readonly object _thisLock = new object();

        public LockedSortedSet()
        {
            _sortedSet = new SortedSet<T>();
        }

        public LockedSortedSet(int capacity)
        {
            _sortedSet = new SortedSet<T>();
            _capacity = capacity;
        }

        public LockedSortedSet(IEnumerable<T> collection)
        {
            _sortedSet = new SortedSet<T>(collection);
        }

        public LockedSortedSet(IComparer<T> comparer)
        {
            _sortedSet = new SortedSet<T>(comparer);
        }

        public LockedSortedSet(int capacity, IComparer<T> comparer)
        {
            _sortedSet = new SortedSet<T>(comparer);
            _capacity = capacity;
        }

        public LockedSortedSet(IEnumerable<T> collection, IComparer<T> comparer)
        {
            _sortedSet = new SortedSet<T>(collection, comparer);
        }

        public T[] ToArray()
        {
            lock (this.ThisLock)
            {
                var array = new T[_sortedSet.Count];
                _sortedSet.CopyTo(array, 0);

                return array;
            }
        }

        public IComparer<T> Comparer
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _sortedSet.Comparer;
                }
            }
        }

        public int Capacity
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _capacity ?? 0;
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

        public IEnumerable<T> IntersectFrom(IEnumerable<T> collection)
        {
            lock (this.ThisLock)
            {
                foreach (var item in collection)
                {
                    if (_sortedSet.Contains(item))
                    {
                        yield return item;
                    }
                }
            }
        }

        public IEnumerable<T> ExceptFrom(IEnumerable<T> collection)
        {
            lock (this.ThisLock)
            {
                foreach (var item in collection)
                {
                    if (!_sortedSet.Contains(item))
                    {
                        yield return item;
                    }
                }
            }
        }

        public bool Add(T item)
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _sortedSet.Count > _capacity.Value) throw new OverflowException();

                return _sortedSet.Add(item);
            }
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                _sortedSet.ExceptWith(other);
            }
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                _sortedSet.IntersectWith(other);
            }
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _sortedSet.IsProperSubsetOf(other);
            }
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _sortedSet.IsProperSupersetOf(other);
            }
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _sortedSet.IsSubsetOf(other);
            }
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _sortedSet.IsSupersetOf(other);
            }
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _sortedSet.Overlaps(other);
            }
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _sortedSet.SetEquals(other);
            }
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                _sortedSet.SymmetricExceptWith(other);
            }
        }

        public void UnionWith(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                foreach (var item in other)
                {
                    this.Add(item);
                }
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _sortedSet.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                return _sortedSet.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                _sortedSet.CopyTo(array, arrayIndex);
            }
        }

        public int Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _sortedSet.Count;
                }
            }
        }

        public bool IsReadOnly
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        public bool Remove(T item)
        {
            lock (this.ThisLock)
            {
                return _sortedSet.Remove(item);
            }
        }

        void ICollection<T>.Add(T item)
        {
            lock (this.ThisLock)
            {
                this.Add(item);
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

        void ICollection.CopyTo(Array array, int index)
        {
            lock (this.ThisLock)
            {
                ((ICollection)_sortedSet).CopyTo(array, index);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var item in _sortedSet)
                {
                    yield return item;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
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
