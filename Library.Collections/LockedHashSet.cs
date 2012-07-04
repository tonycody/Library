using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;

namespace Library.Collections
{
    //public interface ISet<T> : ICollection<T>, IEnumerable<T>, IEnumerable { }

    public class LockedHashSet<T> : ISet<T>, IThisLock
    {
        private HashSet<T> _hashSet;
        private int? _capacity = null;
        private object _thisLock = new object();

        public LockedHashSet()
        {
            _hashSet = new HashSet<T>();
        }

        public LockedHashSet(int capacity)
        {
            _hashSet = new HashSet<T>();
            _capacity = capacity;
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

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                _hashSet.TrimExcess();
            }
        }

        public bool Add(T item)
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _hashSet.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                return _hashSet.Add(item);
            }
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                _hashSet.ExceptWith(other);
            }
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                _hashSet.IntersectWith(other);
            }
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _hashSet.IsProperSubsetOf(other);
            }
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _hashSet.IsProperSupersetOf(other);
            }
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _hashSet.IsSubsetOf(other);
            }
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _hashSet.IsSupersetOf(other);
            }
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _hashSet.Overlaps(other);
            }
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                return _hashSet.SetEquals(other);
            }
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            lock (this.ThisLock)
            {
                _hashSet.SymmetricExceptWith(other);
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

        void ICollection<T>.Add(T item)
        {
            lock (this.ThisLock)
            {
                this.Add(item);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _hashSet.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                return _hashSet.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                _hashSet.CopyTo(array, arrayIndex);
            }
        }

        public int Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hashSet.Count;
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
                return _hashSet.Remove(item);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var item in _hashSet)
                {
                    yield return item;
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
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
