using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class VolatileSortedSet<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock
    {
        private SortedDictionary<T, DateTime> _dic;
        private readonly TimeSpan _survivalTime;

        private readonly object _thisLock = new object();

        public VolatileSortedSet(TimeSpan survivalTime)
        {
            _dic = new SortedDictionary<T, DateTime>();
            _survivalTime = survivalTime;
        }

        public VolatileSortedSet(TimeSpan survivalTime, IComparer<T> comparer)
        {
            _dic = new SortedDictionary<T, DateTime>(comparer);
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
                return _dic.Keys.ToArray();
            }
        }

        private void CheckLifeTime()
        {
            var now = DateTime.UtcNow;

            lock (this.ThisLock)
            {
                List<T> list = null;

                foreach (var pair in _dic)
                {
                    var key = pair.Key;
                    var value = pair.Value;

                    if ((now - value) > _survivalTime)
                    {
                        if (list == null)
                            list = new List<T>();

                        list.Add(key);
                    }
                }

                if (list != null)
                {
                    foreach (var key in list)
                    {
                        _dic.Remove(key);
                    }
                }
            }
        }

        public IComparer<T> Comparer
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
                    return _dic.Count;
                }
            }
        }

        public void AddRange(IEnumerable<T> collection)
        {
            lock (this.ThisLock)
            {
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
                return _dic.ContainsKey(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                _dic.Keys.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(T item)
        {
            lock (this.ThisLock)
            {
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

        void ICollection.CopyTo(Array array, int index)
        {
            lock (this.ThisLock)
            {
                ((ICollection)_dic.Keys).CopyTo(array, index);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
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
