using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class LockedQueue<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock
    {
        private Queue<T> _queue;
        private int? _capacity = null;
        private object _thisLock = new object();

        public LockedQueue()
        {
            _queue = new Queue<T>();
        }

        public LockedQueue(int capacity)
        {
            _queue = new Queue<T>(capacity);
            _capacity = capacity;
        }

        public LockedQueue(IEnumerable<T> collection)
        {
            _queue = new Queue<T>();

            foreach (var item in collection)
            {
                this.Enqueue(item);
            }
        }

        public int Capacity
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _capacity ?? 0;
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
                    return _queue.Count;
                }
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _queue.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                return _queue.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                _queue.CopyTo(array, arrayIndex);
            }
        }

        public T Dequeue()
        {
            lock (this.ThisLock)
            {
                return _queue.Dequeue();
            }
        }

        public void Enqueue(T item)
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _queue.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                _queue.Enqueue(item);
            }
        }

        public T Peek()
        {
            lock (this.ThisLock)
            {
                return _queue.Peek();
            }
        }

        public T[] ToArray()
        {
            lock (this.ThisLock)
            {
                return _queue.ToArray();
            }
        }

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                _queue.TrimExcess();
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
                this.Enqueue(item);
            }
        }

        bool ICollection<T>.Remove(T item)
        {
            lock (this.ThisLock)
            {
                int count = _queue.Count;
                _queue = new Queue<T>(_queue.Where(n => !n.Equals(item)));

                return (count != _queue.Count);
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
                this.CopyTo(array.OfType<T>().ToArray(), index);
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var item in _queue)
                {
                    yield return item;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return ((IEnumerable<T>)this).GetEnumerator();
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
