using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;

namespace Library.Collections
{
    public class WaitQueue<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock, IDisposable
    {
        private Queue<T> _queue;
        private int? _capacity = null;
        private volatile ManualResetEvent _lowerResetEvent = new ManualResetEvent(false);
        private volatile ManualResetEvent _upperResetEvent = new ManualResetEvent(false);
        private object _thisLock = new object();
        private bool _disposed = false;

        public WaitQueue()
        {
            _queue = new Queue<T>();
        }

        public WaitQueue(int capacity)
            : this()
        {
            this.Capacity = capacity;
        }

        public WaitQueue(IEnumerable<T> collection)
            : this()
        {
            foreach (var item in collection)
            {
                this.Enqueue(item);
            }
        }

        ~WaitQueue()
        {
            Dispose(false);
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

        public int Count
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this._queue.Count;
                }
            }
        }

        public void Clear()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._queue.Clear();
            }
        }

        public bool Contains(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._queue.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._queue.CopyTo(array, arrayIndex);
            }
        }

        public T Dequeue()
        {
            for (; ; )
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (this._queue.Count > 0)
                    {
                        _upperResetEvent.Set();
                        return this._queue.Dequeue();
                    }
                    else
                    {
                        _lowerResetEvent.Reset();
                    }
                }

                if (!_lowerResetEvent.WaitOne()) throw new TimeoutException();
            }
        }

        public T Dequeue(TimeSpan timeout)
        {
            for (; ; )
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (this._queue.Count > 0)
                    {
                        _upperResetEvent.Set();
                        return this._queue.Dequeue();
                    }
                    else
                    {
                        _lowerResetEvent.Reset();
                    }
                }

                if (!_lowerResetEvent.WaitOne(timeout, false)) throw new TimeoutException();
            }
        }

        public void Enqueue(T item)
        {
            if (_capacity != null && _queue.Count > _capacity.Value) _upperResetEvent.WaitOne();

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._queue.Enqueue(item);

                _lowerResetEvent.Set();
                _upperResetEvent.Reset();
            }
        }

        public void Enqueue(T item, TimeSpan timeout)
        {
            if (_capacity != null && _queue.Count > _capacity.Value)
            {
                if (!_upperResetEvent.WaitOne(timeout, false)) throw new TimeoutException();
            }

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_disposed) throw new ObjectDisposedException("WaitQueue<T>");

                this._queue.Enqueue(item);

                _lowerResetEvent.Set();
                _upperResetEvent.Reset();
            }
        }

        public T Peek()
        {
            for (; ; )
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (this._queue.Count > 0)
                    {
                        return this._queue.Peek();
                    }
                    else
                    {
                        _lowerResetEvent.Reset();
                    }
                }

                if (!_lowerResetEvent.WaitOne()) throw new TimeoutException();
            }
        }

        public T Peek(TimeSpan timeout)
        {
            for (; ; )
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (this._queue.Count > 0)
                    {
                        return this._queue.Peek();
                    }
                    else
                    {
                        _lowerResetEvent.Reset();
                    }
                }

                if (!_lowerResetEvent.WaitOne(timeout, false)) throw new TimeoutException();
            }
        }

        public T[] ToArray()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this._queue.ToArray();
            }
        }

        public void TrimExcess()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this._queue.TrimExcess();
            }
        }

        public bool DequeueWait(TimeSpan timeout)
        {
            return _lowerResetEvent.WaitOne(timeout, false);
        }

        public bool EnqueueWait(TimeSpan timeout)
        {
            return _upperResetEvent.WaitOne(timeout, false);
        }

        public IEnumerator<T> GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                foreach (var item in _queue)
                {
                    yield return item;
                }
            }
        }

        public void Close()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Dispose();
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                ((ICollection)this._queue).CopyTo(array.OfType<T>().ToArray(), index);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.GetEnumerator();
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

        #region ICollection<T> メンバ

        void ICollection<T>.Add(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                this.Enqueue(item);
            }
        }

        bool ICollection<T>.IsReadOnly
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return false;
                }
            }
        }

        bool ICollection<T>.Remove(T item)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
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

        protected void Dispose(bool disposing)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        if (_lowerResetEvent != null)
                        {
                            _lowerResetEvent.Set();
                            _lowerResetEvent.Close();
                        }
                        if (_upperResetEvent != null)
                        {
                            _upperResetEvent.Set();
                            _upperResetEvent.Close();
                        }
                    }
                }

                _disposed = true;
            }
        }

        #region IDisposable メンバ

        public void Dispose()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }

        #endregion

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
