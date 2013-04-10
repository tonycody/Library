using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class CirculationCollection<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock
    {
        private HashSet<T> _hashSet;
        private Dictionary<T, DateTime> _circularDictionary;

        private DateTime _lastCircularTime = DateTime.MinValue;
        private readonly TimeSpan _circularTime;

        private object _thisLock = new object();

        public CirculationCollection(TimeSpan circularTime)
        {
            _hashSet = new HashSet<T>();
            _circularDictionary = new Dictionary<T, DateTime>();
            _circularTime = circularTime;
        }

        public CirculationCollection(TimeSpan circularTime, IEqualityComparer<T> comparer)
        {
            _hashSet = new HashSet<T>(comparer);
            _circularDictionary = new Dictionary<T, DateTime>(comparer);
            _circularTime = circularTime;
        }

        public T[] ToArray()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                return _hashSet.ToArray();
            }
        }

        private void Circular(TimeSpan circularTime)
        {
            lock (this.ThisLock)
            {
                var now = DateTime.UtcNow;

                if ((now - _lastCircularTime) > new TimeSpan(0, 0, 30))
                {
                    foreach (var item in _hashSet.ToArray())
                    {
                        if ((now - _circularDictionary[item]) > circularTime)
                        {
                            _hashSet.Remove(item);
                        }
                    }

                    foreach (var item in _circularDictionary.Keys.ToArray())
                    {
                        if (!_hashSet.Contains(item))
                        {
                            _circularDictionary.Remove(item);
                        }
                    }

                    _hashSet.TrimExcess();
                    _lastCircularTime = now;
                }
            }
        }

        public IEqualityComparer<T> Comparer
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hashSet.Comparer;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.Circular(_circularTime);

                    return _hashSet.Count;
                }
            }
        }

        public void AddRange(IEnumerable<T> collection)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                foreach (var item in collection)
                {
                    _circularDictionary[item] = DateTime.UtcNow;
                    _hashSet.Add(item);
                }
            }
        }

        public bool Add(T item)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                _circularDictionary[item] = DateTime.UtcNow;
                return _hashSet.Add(item);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _circularDictionary.Clear();
                _hashSet.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                return _hashSet.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                _hashSet.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(T item)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                return _hashSet.Remove(item);
            }
        }

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                foreach (var item in _hashSet)
                {
                    yield return item;
                }
            }
        }

        #region ICollection<T>

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

        void ICollection<T>.Add(T item)
        {
            lock (this.ThisLock)
            {
                this.Add(item);
            }
        }

        #endregion

        #region ICollection

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this.CopyTo(array.OfType<T>().ToArray(), arrayIndex);
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

        #endregion

        #region IEnumerable

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
            }
        }

        #endregion

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
