using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class CirculationDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, IDictionary, ICollection, IEnumerable, IThisLock
    {
        private Dictionary<TKey, ValueLifeSpan> _dic;
        private readonly TimeSpan _circularTime;
        private int? _capacity = null;

        private DateTime _lastCircularTime = DateTime.MinValue;

        private object _thisLock = new object();

        public CirculationDictionary(TimeSpan circularTime)
        {
            _dic = new Dictionary<TKey, ValueLifeSpan>();
            _circularTime = circularTime;
        }

        public CirculationDictionary(TimeSpan circularTime, int capacity)
        {
            _dic = new Dictionary<TKey, ValueLifeSpan>(capacity);
            _circularTime = circularTime;
            _capacity = capacity;
        }

        public CirculationDictionary(TimeSpan circularTime, IEqualityComparer<TKey> comparer)
        {
            _dic = new Dictionary<TKey, ValueLifeSpan>(comparer);
            _circularTime = circularTime;
        }

        public CirculationDictionary(TimeSpan circularTime, int capacity, IEqualityComparer<TKey> comparer)
        {
            _dic = new Dictionary<TKey, ValueLifeSpan>(capacity, comparer);
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
                        if ((now - item.Value.LifeSpan) > circularTime)
                        {
                            _dic.Remove(item.Key);
                        }
                    }

                    _lastCircularTime = now;
                }
            }
        }

        public KeyValuePair<TKey, TValue>[] ToArray()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                var array = new KeyValuePair<TKey, TValue>[_dic.Count];
                ((IDictionary<TKey, TValue>)_dic).CopyTo(array, 0);

                return array;
            }
        }

        public void Trim()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);
            }
        }

        public CirculationKeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.Circular(_circularTime);
                    
                    return new CirculationKeyCollection(_dic.Keys, this.ThisLock);
                }
            }
        }

        public CirculationValueCollection Values
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.Circular(_circularTime);
                    
                    return new CirculationValueCollection(_dic.Values, this.ThisLock);
                }
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

        public IEqualityComparer<TKey> Comparer
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
                    this.Circular(_circularTime);

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
                    this.Circular(_circularTime);

                    return _dic[key].Value;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    this.Circular(_circularTime);

                    _dic[key] = new ValueLifeSpan() { Value = value, LifeSpan = DateTime.UtcNow };
                }
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.Circular(_circularTime);

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
                    this.Circular(_circularTime);

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

        public bool Add(TKey key, TValue value)
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _dic.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                this.Circular(_circularTime);

                int count = _dic.Count;
                _dic.Add(key, new ValueLifeSpan() { Value = value, LifeSpan = DateTime.UtcNow });
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
                this.Circular(_circularTime);

                return _dic.ContainsKey(key);
            }
        }

        public bool ContainsValue(TValue value)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                HashSet<TValue> hashset = new HashSet<TValue>(_dic.Values.Select(n => n.Value));

                return hashset.Contains(value);
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                foreach (var item in _dic)
                {
                    yield return new KeyValuePair<TKey, TValue>(item.Key, item.Value.Value);
                }
            }
        }

        public bool Remove(TKey key)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                return _dic.Remove(key);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (this.ThisLock)
            {
                this.Circular(_circularTime);

                ValueLifeSpan valueLifeSpan;

                if (_dic.TryGetValue(key, out valueLifeSpan))
                {
                    value = valueLifeSpan.Value;
                    return true;
                }
                else
                {
                    value = default(TValue);
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
                this.Circular(_circularTime);

                HashSet<KeyValuePair<TKey, TValue>> hashset = new HashSet<KeyValuePair<TKey, TValue>>();

                foreach (var item2 in _dic)
                {
                    hashset.Add(new KeyValuePair<TKey, TValue>(item2.Key, item2.Value.Value));
                }

                return hashset.Contains(item);
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                ((IDictionary<TKey, TValue>)_dic).CopyTo(array, arrayIndex);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            lock (this.ThisLock)
            {
                return ((IDictionary<TKey, TValue>)_dic).Remove(keyValuePair);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            lock (this.ThisLock)
            {
                ((ICollection)_dic).CopyTo(array, index);
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
            lock (this.ThisLock)
            {
                return (IDictionaryEnumerator)this.GetEnumerator();
            }
        }

        void IDictionary.Remove(object key)
        {
            lock (this.ThisLock)
            {
                this.Remove((TKey)key);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
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

        object ICollection.SyncRoot
        {
            get
            {
                return this.ThisLock;
            }
        }

        internal sealed class ValueLifeSpan
        {
            public TValue Value { get; set; }
            public DateTime LifeSpan { get; set; }
        }

        public sealed class CirculationKeyCollection : ICollection<TKey>, IEnumerable<TKey>, ICollection, IEnumerable, IThisLock
        {
            private ICollection<TKey> _collection;
            private object _thisLock;

            internal CirculationKeyCollection(ICollection<TKey> collection, object thisLock)
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

            bool ICollection<TKey>.Remove(TKey item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
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

            void ICollection.CopyTo(Array array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    this.CopyTo(array.OfType<TKey>().ToArray(), arrayIndex);
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

        public sealed class CirculationValueCollection : ICollection<TValue>, IEnumerable<TValue>, ICollection, IEnumerable, IThisLock
        {
            private ICollection<ValueLifeSpan> _collection;
            private object _thisLock;

            internal CirculationValueCollection(ICollection<ValueLifeSpan> collection, object thisLock)
            {
                _collection = collection;
                _thisLock = thisLock;
            }

            public TValue[] ToArray()
            {
                lock (this.ThisLock)
                {
                    var array = new TValue[_collection.Count];
                    int i = 0;

                    foreach (var item in _collection)
                    {
                        array[i++] = item.Value;
                    }

                    return array;
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
                    HashSet<TValue> hashset = new HashSet<TValue>(_collection.Select(n => n.Value));

                    return hashset.Contains(item);
                }
            }

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    _collection.Select(n => n.Value).ToList().CopyTo(array, arrayIndex);
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

            bool ICollection<TValue>.Remove(TValue item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            public IEnumerator<TValue> GetEnumerator()
            {
                lock (this.ThisLock)
                {
                    foreach (var item in _collection)
                    {
                        yield return item.Value;
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

            void ICollection.CopyTo(Array array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    this.CopyTo(array.OfType<TValue>().ToArray(), arrayIndex);
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
