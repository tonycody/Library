using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Library.Collections
{
    public class WaitQueue<T> : ICollection<T>, IEnumerable<T>, ICollection, IEnumerable, IThisLock, IDisposable
    {
        private Queue<T> _queue;
        private int? _capacity;
        private volatile ManualResetEvent _lowerResetEvent = new ManualResetEvent(false);
        private volatile ManualResetEvent _upperResetEvent = new ManualResetEvent(false);
        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private readonly TimeSpan _neverTimeout = TimeSpan.MinValue;

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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _capacity ?? 0;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _queue.Count;
                }
            }
        }

        public void Clear()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _queue.Clear();
                if (_capacity != null) _upperResetEvent.Set();
            }
        }

        public bool Contains(T item)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _queue.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _queue.CopyTo(array, arrayIndex);
            }
        }

        public T Dequeue()
        {
            return this.Dequeue(_neverTimeout);
        }

        public T Dequeue(TimeSpan timeout)
        {
            for (; ; )
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    if (_queue.Count > 0)
                    {
                        if (_capacity != null)
                        {
                            var item = _queue.Dequeue();

                            if (_queue.Count < _capacity.Value)
                            {
                                _upperResetEvent.Set();
                            }

                            return item;
                        }
                        else
                        {
                            return _queue.Dequeue();
                        }
                    }
                    else
                    {
                        _lowerResetEvent.Reset();
                    }
                }

                if (timeout < TimeSpan.Zero)
                {
                    _lowerResetEvent.WaitOne();
                }
                else
                {
                    if (!_lowerResetEvent.WaitOne(timeout, false)) throw new TimeoutException();
                }
            }
        }

        public void Enqueue(T item)
        {
            this.Enqueue(item, _neverTimeout);
        }

        public void Enqueue(T item, TimeSpan timeout)
        {
            for (; ; )
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                if (_capacity != null && _queue.Count >= _capacity.Value)
                {
                    if (timeout < TimeSpan.Zero)
                    {
                        _upperResetEvent.WaitOne();
                    }
                    else
                    {
                        if (!_upperResetEvent.WaitOne(timeout, false)) throw new TimeoutException();
                    }
                }

                lock (this.ThisLock)
                {
                    if (_capacity != null)
                    {
                        if (_queue.Count < _capacity.Value)
                        {
                            _queue.Enqueue(item);
                            _lowerResetEvent.Set();

                            return;
                        }
                        else
                        {
                            _upperResetEvent.Reset();
                        }
                    }
                    else
                    {
                        _queue.Enqueue(item);
                        _lowerResetEvent.Set();

                        return;
                    }
                }
            }
        }

        public T Peek()
        {
            return this.Peek(_neverTimeout);
        }

        public T Peek(TimeSpan timeout)
        {
            for (; ; )
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    if (_queue.Count > 0)
                    {
                        return _queue.Peek();
                    }
                    else
                    {
                        _lowerResetEvent.Reset();
                    }
                }

                if (timeout < TimeSpan.Zero)
                {
                    _lowerResetEvent.WaitOne();
                }
                else
                {
                    if (!_lowerResetEvent.WaitOne(timeout, false)) throw new TimeoutException();
                }
            }
        }

        public T[] ToArray()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _queue.ToArray();
            }
        }

        public void TrimExcess()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _queue.TrimExcess();
            }
        }

        public bool WaitDequeue()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _lowerResetEvent.WaitOne();
        }

        public bool WaitDequeue(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _lowerResetEvent.WaitOne(timeout, false);
        }

        public bool WaitEnqueue()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _upperResetEvent.WaitOne();
        }

        public bool WaitEnqueue(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _upperResetEvent.WaitOne(timeout, false);
        }

        public void Close()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                this.Dispose();
            }
        }

        bool ICollection<T>.IsReadOnly
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        void ICollection<T>.Add(T item)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                this.Enqueue(item);
            }
        }

        bool ICollection<T>.Remove(T item)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                int count = _queue.Count;
                _queue = new Queue<T>(_queue.Where(n => !n.Equals(item)));
                if (_capacity != null) _upperResetEvent.Set();

                return (count != _queue.Count);
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return this.ThisLock;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                ((ICollection)_queue).CopyTo(array.OfType<T>().ToArray(), index);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return this.GetEnumerator();
            }
        }

        protected void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_lowerResetEvent != null)
                {
                    try
                    {
                        _lowerResetEvent.Set();
                        _lowerResetEvent.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _lowerResetEvent = null;
                }

                if (_upperResetEvent != null)
                {
                    try
                    {
                        _upperResetEvent.Set();
                        _upperResetEvent.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _upperResetEvent = null;
                }
            }
        }

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
