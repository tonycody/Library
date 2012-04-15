using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;

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

        public LockedQueue(IEnumerable<T> collection)
            : this()
        {
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
                    return this._queue.Count;
                }
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                this._queue.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                return this._queue.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this._queue.CopyTo(array, arrayIndex);
            }
        }

        public T Dequeue()
        {
            lock (this.ThisLock)
            {
                return this._queue.Dequeue();
            }
        }

        public void Enqueue(T item)
        {
            lock (this.ThisLock)
            {
                if (_capacity != null && _queue.Count > _capacity.Value) throw new ArgumentOutOfRangeException();

                this._queue.Enqueue(item);
            }
        }

        public T Peek()
        {
            lock (this.ThisLock)
            {
                return this._queue.Peek();
            }
        }

        public T[] ToArray()
        {
            lock (this.ThisLock)
            {
                return this._queue.ToArray();
            }
        }

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                this._queue.TrimExcess();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var item in _queue)
                {
                    yield return item;
                }
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            lock (this.ThisLock)
            {
                this.CopyTo(array.OfType<T>().ToArray(), index);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
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

        #region ICollection<T> メンバ

        void ICollection<T>.Add(T item)
        {
            lock (this.ThisLock)
            {
                this.Enqueue(item);
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

        bool ICollection<T>.Remove(T item)
        {
            lock (this.ThisLock)
            {
                int count = _queue.Count;
                _queue = new Queue<T>(_queue.Where(n => !n.Equals(item)));

                return (count != _queue.Count);
            }
        }

        #endregion

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
