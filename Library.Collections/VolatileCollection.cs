using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class VolatileCollection<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock
    {
        private HashSet<T> _hashSet;
        private Dictionary<T, DateTime> _volatileDictionary;

        private DateTime _lastCheckTime = DateTime.MinValue;
        private readonly TimeSpan _survivalTime;

        private object _thisLock = new object();

        public VolatileCollection(TimeSpan survivalTime)
        {
            _hashSet = new HashSet<T>();
            _volatileDictionary = new Dictionary<T, DateTime>();
            _survivalTime = survivalTime;
        }

        public VolatileCollection(TimeSpan survivalTime, IEqualityComparer<T> comparer)
        {
            _hashSet = new HashSet<T>(comparer);
            _volatileDictionary = new Dictionary<T, DateTime>(comparer);
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
                this.Refresh();

                return _hashSet.ToArray();
            }
        }

        public void Refresh()
        {
            lock (this.ThisLock)
            {
                var now = DateTime.UtcNow;

                if ((now - _lastCheckTime).TotalSeconds > 10)
                {
                    foreach (var item in _hashSet.ToArray())
                    {
                        if ((now - _volatileDictionary[item]) > _survivalTime)
                        {
                            _hashSet.Remove(item);
                        }
                    }

                    foreach (var item in _volatileDictionary.Keys.ToArray())
                    {
                        if (!_hashSet.Contains(item))
                        {
                            _volatileDictionary.Remove(item);
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
                    this.Refresh();

                    return _hashSet.Count;
                }
            }
        }

        public void AddRange(IEnumerable<T> collection)
        {
            lock (this.ThisLock)
            {
                this.Refresh();

                foreach (var item in collection)
                {
                    _volatileDictionary[item] = DateTime.UtcNow;
                    _hashSet.Add(item);
                }
            }
        }

        public bool Add(T item)
        {
            lock (this.ThisLock)
            {
                this.Refresh();

                _volatileDictionary[item] = DateTime.UtcNow;
                return _hashSet.Add(item);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _volatileDictionary.Clear();
                _hashSet.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                this.Refresh();

                return _hashSet.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this.Refresh();

                _hashSet.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(T item)
        {
            lock (this.ThisLock)
            {
                this.Refresh();

                return _hashSet.Remove(item);
            }
        }

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                this.Refresh();

                _hashSet.TrimExcess();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                this.Refresh();

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
