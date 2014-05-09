using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class VolatileSortedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, IDictionary, ICollection, IEnumerable, IThisLock
    {
        private SortedDictionary<TKey, Info<TValue>> _dic;
        private readonly TimeSpan _survivalTime;

        private readonly object _thisLock = new object();

        public VolatileSortedDictionary(TimeSpan survivalTime)
        {
            _dic = new SortedDictionary<TKey, Info<TValue>>();
            _survivalTime = survivalTime;
        }

        public VolatileSortedDictionary(TimeSpan survivalTime, IComparer<TKey> comparer)
        {
            _dic = new SortedDictionary<TKey, Info<TValue>>(comparer);
            _survivalTime = survivalTime;
        }

        public TimeSpan SurvivalTime
        {
            get
            {
                return _survivalTime;
            }
        }

        public KeyValuePair<TKey, TValue>[] ToArray()
        {
            lock (this.ThisLock)
            {
                var array = new KeyValuePair<TKey, TValue>[_dic.Count];
                ((IDictionary<TKey, TValue>)this).CopyTo(array, 0);

                return array;
            }
        }

        private void CheckLifeTime()
        {
            var now = DateTime.UtcNow;

            lock (this.ThisLock)
            {
                List<TKey> list = null;

                foreach (var pair in _dic)
                {
                    var key = pair.Key;
                    var info = pair.Value;

                    if ((now - info.UpdateTime) > _survivalTime)
                    {
                        if (list == null)
                            list = new List<TKey>();

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

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();
            }
        }

        public VolatileKeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return new VolatileKeyCollection(_dic.Keys, this.ThisLock);
                }
            }
        }

        public VolatileValueCollection Values
        {
            get
            {
                lock (this.ThisLock)
                {
                    return new VolatileValueCollection(_dic.Values, this.ThisLock);
                }
            }
        }

        public IComparer<TKey> Comparer
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

        public TValue this[TKey key]
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _dic[key].Value;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    this.Add(key, value);
                }
            }
        }

        public bool Add(TKey key, TValue value)
        {
            lock (this.ThisLock)
            {
                int count = _dic.Count;
                _dic[key] = new Info<TValue>() { Value = value, UpdateTime = DateTime.UtcNow };

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

        public bool ContainsKey(TKey key)
        {
            lock (this.ThisLock)
            {
                return _dic.ContainsKey(key);
            }
        }

        public bool ContainsValue(TValue value)
        {
            lock (this.ThisLock)
            {
                return _dic.Values.Select(n => n.Value).Contains(value);
            }
        }

        public bool Remove(TKey key)
        {
            lock (this.ThisLock)
            {
                return _dic.Remove(key);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (this.ThisLock)
            {
                Info<TValue> info;

                if (_dic.TryGetValue(key, out info))
                {
                    value = info.Value;

                    return true;
                }
                else
                {
                    value = default(TValue);

                    return false;
                }
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Keys;
                }
            }
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Values;
                }
            }
        }

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
        {
            lock (this.ThisLock)
            {
                this.Add(key, value);
            }
        }

        bool IDictionary.IsFixedSize
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        bool IDictionary.IsReadOnly
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        ICollection IDictionary.Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return (ICollection)this.Keys;
                }
            }
        }

        ICollection IDictionary.Values
        {
            get
            {
                lock (this.ThisLock)
                {
                    return (ICollection)this.Values;
                }
            }
        }

        object IDictionary.this[object key]
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this[(TKey)key];
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    this[(TKey)key] = (TValue)value;
                }
            }
        }

        void IDictionary.Add(object key, object value)
        {
            lock (this.ThisLock)
            {
                this.Add((TKey)key, (TValue)value);
            }
        }

        bool IDictionary.Contains(object key)
        {
            lock (this.ThisLock)
            {
                return this.ContainsKey((TKey)key);
            }
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            throw new NotSupportedException();
        }

        void IDictionary.Remove(object key)
        {
            lock (this.ThisLock)
            {
                this.Remove((TKey)key);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            lock (this.ThisLock)
            {
                this.Add(item.Key, item.Value);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            lock (this.ThisLock)
            {
                var keyComparer = EqualityComparer<TKey>.Default;
                var valueComparer = EqualityComparer<TValue>.Default;

                foreach (var pair in _dic)
                {
                    var key = pair.Key;
                    var info = pair.Value;

                    if (keyComparer.Equals(item.Key, key)
                        && valueComparer.Equals(item.Value, info.Value))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                foreach (var pair in _dic)
                {
                    var key = pair.Key;
                    var info = pair.Value;

                    array.SetValue(new KeyValuePair<TKey, TValue>(key, info.Value), arrayIndex++);
                }
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            lock (this.ThisLock)
            {
                var keyComparer = EqualityComparer<TKey>.Default;
                var valueComparer = EqualityComparer<TValue>.Default;

                bool flag = false;

                foreach (var pair in _dic)
                {
                    var key = pair.Key;
                    var info = pair.Value;

                    if (keyComparer.Equals(keyValuePair.Key, key)
                        && valueComparer.Equals(keyValuePair.Value, info.Value))
                    {
                        ((ICollection<KeyValuePair<TKey, Info<TValue>>>)_dic).Remove(pair);

                        flag = true;
                    }
                }

                return flag;
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
                foreach (var pair in _dic)
                {
                    var key = pair.Key;
                    var info = pair.Value;

                    array.SetValue(new KeyValuePair<TKey, TValue>(key, info.Value), index++);
                }
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var pair in _dic)
                {
                    var key = pair.Key;
                    var info = pair.Value;

                    yield return new KeyValuePair<TKey, TValue>(key, info.Value);
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

        internal struct Info<T>
        {
            public T Value { get; set; }
            public DateTime UpdateTime { get; set; }
        }

        public sealed class VolatileKeyCollection : ICollection<TKey>, IEnumerable<TKey>, ICollection, IEnumerable, IThisLock
        {
            private ICollection<TKey> _collection;
            private readonly object _thisLock;

            internal VolatileKeyCollection(ICollection<TKey> collection, object thisLock)
            {
                _collection = collection;
                _thisLock = thisLock;
            }

            public TKey[] ToArray()
            {
                lock (this.ThisLock)
                {
                    var array = new TKey[_collection.Count];
                    _collection.CopyTo(array, 0);

                    return array;
                }
            }

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    _collection.CopyTo(array, arrayIndex);
                }
            }

            public int Count
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _collection.Count;
                    }
                }
            }

            bool ICollection<TKey>.IsReadOnly
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return true;
                    }
                }
            }

            void ICollection<TKey>.Add(TKey item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            void ICollection<TKey>.Clear()
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            bool ICollection<TKey>.Contains(TKey item)
            {
                lock (this.ThisLock)
                {
                    return _collection.Contains(item);
                }
            }

            bool ICollection<TKey>.Remove(TKey item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
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
                    ((ICollection)_collection).CopyTo(array, index);
                }
            }

            public IEnumerator<TKey> GetEnumerator()
            {
                lock (this.ThisLock)
                {
                    foreach (var item in _collection)
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

        public sealed class VolatileValueCollection : ICollection<TValue>, IEnumerable<TValue>, ICollection, IEnumerable, IThisLock
        {
            private ICollection<Info<TValue>> _collection;
            private readonly object _thisLock;

            internal VolatileValueCollection(ICollection<Info<TValue>> collection, object thisLock)
            {
                _collection = collection;
                _thisLock = thisLock;
            }

            public TValue[] ToArray()
            {
                lock (this.ThisLock)
                {
                    return _collection.Select(n => n.Value).ToArray();
                }
            }

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    foreach (var info in _collection)
                    {
                        array[arrayIndex++] = info.Value;
                    }
                }
            }

            public int Count
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _collection.Count;
                    }
                }
            }

            bool ICollection<TValue>.IsReadOnly
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return true;
                    }
                }
            }

            void ICollection<TValue>.Add(TValue item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            void ICollection<TValue>.Clear()
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            bool ICollection<TValue>.Contains(TValue item)
            {
                lock (this.ThisLock)
                {
                    return _collection.Select(n => n.Value).Contains(item);
                }
            }

            bool ICollection<TValue>.Remove(TValue item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
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
                    ((ICollection)_collection).CopyTo(array, index);
                }
            }

            public IEnumerator<TValue> GetEnumerator()
            {
                lock (this.ThisLock)
                {
                    foreach (var info in _collection)
                    {
                        yield return info.Value;
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
