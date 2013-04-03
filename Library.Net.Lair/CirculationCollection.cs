using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Library.Collections;

namespace Library.Net.Lair
{
    sealed class CirculationCollection<T> : IEnumerable<T>, IEnumerable, IThisLock
    {
        private LockedHashSet<T> _hashSet;
        private Dictionary<T, DateTime> _circularDictionary;
        private DateTime _lastCircularTime = DateTime.MinValue;
        private object _thisLock = new object();
        private readonly TimeSpan _circularTime;

        public CirculationCollection(TimeSpan circularTime)
        {
            _hashSet = new LockedHashSet<T>();
            _circularDictionary = new Dictionary<T, DateTime>();
            _circularTime = circularTime;
        }

        public CirculationCollection(TimeSpan circularTime, int capacity)
        {
            _hashSet = new LockedHashSet<T>(capacity);
            _circularDictionary = new Dictionary<T, DateTime>();
            _circularTime = circularTime;
        }

        public CirculationCollection(TimeSpan circularTime, IEqualityComparer<T> comparer)
        {
            _hashSet = new LockedHashSet<T>(comparer);
            _circularDictionary = new Dictionary<T, DateTime>(comparer);
            _circularTime = circularTime;
        }

        public CirculationCollection(TimeSpan circularTime, int capacity, IEqualityComparer<T> comparer)
        {
            _hashSet = new LockedHashSet<T>(capacity, comparer);
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

                _hashSet.TrimExcess();
            }
        }

        #region IEnumerable<T>

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
