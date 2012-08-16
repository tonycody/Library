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
        {
            _queue = new Queue<T>();
            _capacity = capacity;
        }

        public WaitQueue(IEnumerable<T> collection)
        {
            _queue = new Queue<T>();

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
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                
                    return _capacity.Value;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return _queue.Count;
                }
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _queue.Clear();
            }
        }

        public bool Contains(T item)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _queue.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _queue.CopyTo(array, arrayIndex);
            }
        }

        public T Dequeue()
        {
            for (; ; )
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    if (_queue.Count > 0)
                    {
                        _upperResetEvent.Set();
                        return _queue.Dequeue();
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
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    if (_queue.Count > 0)
                    {
                        _upperResetEvent.Set();
                        return _queue.Dequeue();
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

            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _queue.Enqueue(item);

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

            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _queue.Enqueue(item);

                _lowerResetEvent.Set();
                _upperResetEvent.Reset();
            }
        }

        public T Peek()
        {
            for (; ; )
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    if (_queue.Count > 0)
                    {
                        return _queue.Peek();
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
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    if (_queue.Count > 0)
                    {
                        return _queue.Peek();
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
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _queue.ToArray();
            }
        }

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _queue.TrimExcess();
            }
        }

        public bool DequeueWait(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _lowerResetEvent.WaitOne(timeout, false);
        }

        public bool EnqueueWait(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _upperResetEvent.WaitOne(timeout, false);
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                foreach (var item in _queue)
                {
                    yield return item;
                }
            }
        }

        public void Close()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                this.Dispose();
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                ((ICollection)_queue).CopyTo(array.OfType<T>().ToArray(), index);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return this.GetEnumerator();
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return true;
                }
            }
        }

        #region ICollection<T>

        void ICollection<T>.Add(T item)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                this.Enqueue(item);
            }
        }

        bool ICollection<T>.IsReadOnly
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    return false;
                }
            }
        }

        bool ICollection<T>.Remove(T item)
        {
            lock (this.ThisLock)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return this.ThisLock;
            }
        }

        protected void Dispose(bool disposing)
        {
            lock (this.ThisLock)
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

        #region IDisposable

        public void Dispose()
        {
            lock (this.ThisLock)
            {
                Dispose(true);
                GC.SuppressFinalize(this);
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
