using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class CirculationCollection<T> : ICollection<T>, IEnumerable<T>, IEnumerable, IThisLock
    {
        private Dictionary<T, DateTime> _dic;
        private readonly TimeSpan _circularTime;
        private int? _capacity = null;

        private DateTime _lastCircularTime = DateTime.MinValue;

        private object _thisLock = new object();

        public CirculationCollection(TimeSpan circularTime)
        {
            _dic = new Dictionary<T, DateTime>();
            _circularTime = circularTime;
        }

        public CirculationCollection(TimeSpan circularTime, int capacity)
        {
            _dic = new Dictionary<T, DateTime>(capacity);
            _circularTime = circularTime;
            _capacity = capacity;
        }

        public CirculationCollection(TimeSpan circularTime, IEqualityComparer<T> comparer)
        {
            _dic = new Dictionary<T, DateTime>(comparer);
            _circularTime = circularTime;
        }

        public CirculationCollection(TimeSpan circularTime, int capacity, IEqualityComparer<T> comparer)
        {
            _dic = new Dictionary<T, DateTime>(capacity, comparer);
            _circularTime = circularTime;
            _capacity = capacity;
        }

        private void Circular(TimeSpan circularTime)
        {
            lock (this.ThisLock)
            {
                var now = DateTime.UtcNow;

                if ((now - _lastCircularTime) > new TimeSpan(0, 0, 30))
                {
                    foreach (var item in _dic.ToArray())
                    {
                        if ((now - item.Value) > circularTime)
                        {
                            _dic.Remove(item.Key);
                        }
                    }

                    _lastCircularTime = now;
                }
            }
        }

        public T[] ToArray()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                return _dic.Keys.ToArray();
            }
        }

        public void Trim()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);
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
                    this.Circular(_circularTime);

                    return _dic.Count;
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
                    _dic[item] = DateTime.UtcNow;
                }
            }
        }

        public bool Add(T item)
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _dic.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                this.Circular(_circularTime);

                int count = _dic.Count;
                _dic[item] = DateTime.UtcNow;
                return (count != _dic.Count);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _dic.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                _dic.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                return _dic.ContainsKey(item);
            }
        }

        public bool Remove(T item)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                return _dic.Remove(item);
            }
        }

        #region ICollection<T>

        void ICollection<T>.Add(T item)
        {
            lock (this.ThisLock)
            {
                this.Add(item);
            }
        }

        void ICollection<T>.CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                _dic.Keys.CopyTo(array, arrayIndex);
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

        #endregion

        #region IEnumerable<T>

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                foreach (var item in _dic.Keys)
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
