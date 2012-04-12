using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class LockedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, IDictionary, ICollection, IEnumerable, IThisLock
    {
        private Dictionary<TKey, TValue> _dic;
        private int? _capacity = null;
        private object _thisLock = new object();

        public LockedDictionary()
        {
            _dic = new Dictionary<TKey, TValue>();
        }

        public LockedDictionary(int capacity)
        {
            _dic = new Dictionary<TKey, TValue>(capacity);
            _capacity = capacity;
        }

        public LockedDictionary(IEqualityComparer<TKey> comparer)
        {
            _dic = new Dictionary<TKey, TValue>(comparer);
        }

        public LockedDictionary(IDictionary<TKey, TValue> dictionary)
            : this()
        {
            foreach (var item in dictionary)
            {
                this.Add(item.Key, item.Value);
            }
        }

        public LockedDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            _dic = new Dictionary<TKey, TValue>(capacity, comparer);
            _capacity = capacity;
        }

        public LockedDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
            : this(comparer)
        {
            foreach (var item in dictionary)
            {
                this.Add(item.Key, item.Value);
            }
        }

        public int Capacity
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _capacity.Value;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _capacity = value;
                }
            }
        }

        public IEqualityComparer<TKey> Comparer
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this._dic.Comparer;
                }
            }
        }

        public int Count
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this._dic.Count;
                }
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this._dic[key];
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    this._dic[key] = value;
                }
            }
        }

        public ICollection<TKey> Keys
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this._dic.Keys.ToArray();
                }
            }
        }

        public ICollection<TValue> Values
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this._dic.Values.ToList();
                }
            }
        }

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Add(key, value);
            }
        }

        public bool Add(TKey key, TValue value)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_capacity != null && _dic.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                if (_dic.ContainsKey(key))
                {
                    return false;
                }
                else
                {
                    this._dic.Add(key, value);
                    return true;
                }
            }
        }

        public void Clear()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._dic.Clear();
            }
        }

        public bool ContainsKey(TKey key)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._dic.ContainsKey(key);
            }
        }

        public bool ContainsValue(TValue value)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._dic.ContainsValue(value);
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var item in _dic)
                {
                    yield return item;
                }
            }
        }

        public bool Remove(TKey key)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._dic.Remove(key);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._dic.TryGetValue(key, out value);
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Add(item.Key, item.Value);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._dic.Contains(item);
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                ((IDictionary<TKey, TValue>)this._dic).CopyTo(array, arrayIndex);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return ((IDictionary<TKey, TValue>)this._dic).Remove(keyValuePair);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                ((ICollection)this._dic).CopyTo(array, index);
            }
        }

        void IDictionary.Add(object key, object value)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Add((TKey)key, (TValue)value);
            }
        }

        bool IDictionary.Contains(object key)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.ContainsKey((TKey)key);
            }
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return (IDictionaryEnumerator)this.GetEnumerator();
            }
        }

        void IDictionary.Remove(object key)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Remove((TKey)key);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.GetEnumerator();
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return false;
                }
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this.Keys.ToArray();
                }
            }
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this.Values.ToArray();
                }
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return true;
                }
            }
        }

        bool IDictionary.IsFixedSize
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return false;
                }
            }
        }

        bool IDictionary.IsReadOnly
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return false;
                }
            }
        }

        object IDictionary.this[object key]
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this[(TKey)key];
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    this[(TKey)key] = (TValue)value;
                }
            }
        }

        ICollection IDictionary.Keys
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return (ICollection)this.Keys;
                }
            }
        }

        ICollection IDictionary.Values
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
