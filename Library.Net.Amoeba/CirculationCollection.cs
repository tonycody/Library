using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Collections;
using System.Collections;

namespace Library.Net.Amoeba
{
    class CirculationCollection<T> : IEnumerable<T>, IEnumerable, IThisLock
    {
        private HashSet<T> _hashSet;
        private Dictionary<T, DateTime> _circularDictionary;
        private DateTime _lastCircularTime = DateTime.MinValue;
        private object _thisLock = new object();
        private readonly TimeSpan _circularTime;

        public CirculationCollection(TimeSpan circularTime)
        {
            _hashSet = new HashSet<T>();
            _circularDictionary = new Dictionary<T, DateTime>();
            _circularTime = circularTime;
        }

        public CirculationCollection(IEnumerable<T> collection, TimeSpan circularTime)
        {
            _hashSet = new HashSet<T>();
            _circularDictionary = new Dictionary<T, DateTime>();
            _circularTime = circularTime;

            this.AddRange(collection);
        }

        public CirculationCollection(IEqualityComparer<T> comparer, TimeSpan circularTime)
        {
            _hashSet = new HashSet<T>(comparer);
            _circularDictionary = new Dictionary<T, DateTime>();
            _circularTime = circularTime;
        }

        public CirculationCollection(IEnumerable<T> collection, IEqualityComparer<T> comparer, TimeSpan circularTime)
            : this(comparer, circularTime)
        {
            this.AddRange(collection);
        }

        private void Circular(TimeSpan circularTime)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                var now = DateTime.UtcNow;

                if ((now - _lastCircularTime) > new TimeSpan(0, 0, 10))
                {
                    foreach (var item in _hashSet.ToArray())
                    {
                        if (_circularDictionary.ContainsKey(item) && (now - _circularDictionary[item]) > circularTime)
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _hashSet.Count;
                }
            }
        }

        public void AddRange(IEnumerable<T> collection)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Circular(_circularTime);

                _circularDictionary[item] = DateTime.UtcNow;
                return _hashSet.Add(item);
            }
        }

        public void Clear()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                _circularDictionary.Clear();
                _hashSet.Clear();
            }
        }

        public bool Contains(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Circular(_circularTime);

                return _hashSet.Contains(item);
            }
        }

        public bool Remove(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Circular(_circularTime);

                return _hashSet.Remove(item);
            }
        }

        public void TrimExcess()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Circular(_circularTime);

                _hashSet.TrimExcess();
            }
        }

        #region IEnumerable<T> メンバ

        public IEnumerator<T> GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Circular(_circularTime);

                foreach (var item in _hashSet)
                {
                    yield return item;
                }
            }
        }

        #endregion

        #region IEnumerable メンバ

        IEnumerator IEnumerable.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.GetEnumerator();
            }
        }

        #endregion

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
