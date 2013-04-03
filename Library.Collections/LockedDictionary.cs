﻿using System;
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

        public LockedDictionary(IDictionary<TKey, TValue> dictionary)
        {
            _dic = new Dictionary<TKey, TValue>();

            foreach (var item in dictionary)
            {
                this.Add(item.Key, item.Value);
            }
        }

        public LockedDictionary(IEqualityComparer<TKey> comparer)
        {
            _dic = new Dictionary<TKey, TValue>(comparer);
        }

        public LockedDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            _dic = new Dictionary<TKey, TValue>(capacity, comparer);
            _capacity = capacity;
        }

        public LockedDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
        {
            _dic = new Dictionary<TKey, TValue>(comparer);

            foreach (var item in dictionary)
            {
                this.Add(item.Key, item.Value);
            }
        }

        public KeyValuePair<TKey, TValue>[] ToArray()
        {
            lock (this.ThisLock)
            {
                var array = new KeyValuePair<TKey, TValue>[_dic.Count];
                ((IDictionary<TKey, TValue>)_dic).CopyTo(array, 0);

                return array;
            }
        }

        public LockedKeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return new LockedKeyCollection(_dic.Keys, this.ThisLock);
                }
            }
        }

        public LockedValueCollection Values
        {
            get
            {
                lock (this.ThisLock)
                {
                    return new LockedValueCollection(_dic.Values, this.ThisLock);
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
                    return _dic[key];
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _dic[key] = value;
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

        public bool Add(TKey key, TValue value)
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _dic.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                int count = _dic.Count;
                _dic.Add(key, value);
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
                return _dic.ContainsValue(value);
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var item in _dic)
                {
                    yield return item;
                }
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
                return _dic.TryGetValue(key, out value);
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
                return _dic.Contains(item);
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

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion

        public sealed class LockedKeyCollection : ICollection<TKey>, IEnumerable<TKey>, ICollection, IEnumerable, IThisLock
        {
            private ICollection<TKey> _collection;
            private object _thisLock;

            internal LockedKeyCollection(ICollection<TKey> collection, object thisLock)
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

        public sealed class LockedValueCollection : ICollection<TValue>, IEnumerable<TValue>, ICollection, IEnumerable, IThisLock
        {
            private ICollection<TValue> _collection;
            private object _thisLock;

            internal LockedValueCollection(ICollection<TValue> collection, object thisLock)
            {
                _collection = collection;
                _thisLock = thisLock;
            }

            public TValue[] ToArray()
            {
                lock (this.ThisLock)
                {
                    var array = new TValue[_collection.Count];
                    _collection.CopyTo(array, 0);

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
                    return _collection.Contains(item);
                }
            }

            public void CopyTo(TValue[] array, int arrayIndex)
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
    }
}