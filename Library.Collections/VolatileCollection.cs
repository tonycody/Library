using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class VolatileCollection<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock
    {
        private Dictionary<T, DateTime> _dic;

        private DateTime _lastCheckTime = DateTime.MinValue;
        private readonly TimeSpan _survivalTime;

        private readonly object _thisLock = new object();

        public VolatileCollection(TimeSpan survivalTime)
        {
            _dic = new Dictionary<T, DateTime>();
            _survivalTime = survivalTime;
        }

        public VolatileCollection(TimeSpan survivalTime, IEqualityComparer<T> comparer)
        {
            _dic = new Dictionary<T, DateTime>(comparer);
            _survivalTime = survivalTime;
        }

        public TimeSpan SurvivalTime
        {
            get
            {
                return _survivalTime;
            }
        }

        public T[] ToArray()
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return _dic.Keys.ToArray();
            }
        }

        public void Refresh(T item)
        {
            this.CheckLifeTime();

            _dic[item] = DateTime.UtcNow;
        }

        private void CheckLifeTime()
        {
            lock (this.ThisLock)
            {
                var now = DateTime.UtcNow;

                if ((now - _lastCheckTime).TotalSeconds >= 10)
                {
                    foreach (var pair in _dic.ToArray())
                    {
                        var key = pair.Key;
                        var value = pair.Value;

                        if ((now - value) > _survivalTime)
                        {
                            _dic.Remove(key);
                        }
                    }

                    _lastCheckTime = now;
                }
            }
        }

        public IEqualityComparer<T> Comparer
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _dic.Comparer;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.CheckLifeTime();

                    return _dic.Count;
                }
            }
        }

        public void AddRange(IEnumerable<T> collection)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

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
                this.CheckLifeTime();

                int count = _dic.Count;
                _dic[item] = DateTime.UtcNow;

                return (count != _dic.Count);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _dic.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return _dic.ContainsKey(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                _dic.Keys.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(T item)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return _dic.Remove(item);
            }
        }

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();
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

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this.CopyTo(array.OfType<T>().ToArray(), arrayIndex);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                foreach (var item in _dic.Keys)
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
