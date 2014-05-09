//#define MONITOR

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Library
{

#if !MONITOR

    public class BufferManager : ManagerBase
    {
        private static readonly BufferManager _instance = new BufferManager(1024 * 1024 * 1024, 1024 * 1024 * 256);

        //private System.ServiceModel.Channels.BufferManager _bufferManager;

        private System.Threading.Timer _watchTimer;
        private volatile bool _isRefreshing = false;

        private long _maxBufferPoolSize;
        private int _maxBufferSize;

        private int[] _sizes;
        private LinkedList<WeakReference>[] _buffers;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public BufferManager(long maxBufferPoolSize, int maxBufferSize)
        {
            //_bufferManager = System.ServiceModel.Channels.BufferManager.CreateBufferManager(maxBufferPoolSize, maxBufferSize);

            if (maxBufferPoolSize < maxBufferSize) throw new ArgumentOutOfRangeException();
            if (maxBufferPoolSize == 0) throw new ArgumentOutOfRangeException();

            _maxBufferPoolSize = maxBufferPoolSize;
            _maxBufferSize = maxBufferSize;

            var sizes = new List<int>();
            var buffers = new List<LinkedList<WeakReference>>();

            for (int i = 256; i < _maxBufferSize; i *= 2)
            {
                sizes.Add(i);
                buffers.Add(new LinkedList<WeakReference>());
            }

            _sizes = sizes.ToArray();
            _buffers = buffers.ToArray();

            _watchTimer = new System.Threading.Timer(this.WatchTimer, null, new TimeSpan(0, 0, 30), new TimeSpan(0, 0, 30));
        }

        private void WatchTimer(object state)
        {
            this.Refresh();
        }

        internal void Refresh()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                lock (_thisLock)
                {
                    long memorySize = 0;

                    for (int i = 0; i < _sizes.Length; i++)
                    {
                        memorySize += (_sizes[i] * _buffers[i].Count);
                    }

                    if (_maxBufferPoolSize > memorySize) return;

                    for (int i = 0; i < _sizes.Length; i++)
                    {
                        while (_buffers[i].Count > 0)
                        {
                            _buffers[i].RemoveFirst();
                            memorySize -= _sizes[i];

                            if (_maxBufferPoolSize > memorySize) return;
                        }
                    }
                }
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        public static BufferManager Instance
        {
            get
            {
                return _instance;
            }
        }

        public long Size
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (_thisLock)
                {
                    long size = 0;

                    foreach (var weakReference in _buffers.Extract())
                    {
                        byte[] buffer = weakReference.Target as byte[];
                        if (buffer == null) continue;

                        size += buffer.Length;
                    }

                    return size;
                }
            }
        }

        public byte[] TakeBuffer(int bufferSize)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_thisLock)
            {
                for (int i = 0; i < _sizes.Length; i++)
                {
                    if (bufferSize <= _sizes[i])
                    {
                        while (_buffers[i].Count > 0)
                        {
                            var weakReference = _buffers[i].First.Value;
                            byte[] buffer = weakReference.Target as byte[];
                            _buffers[i].RemoveFirst();

                            if (buffer != null) return buffer;
                        }

                        return new byte[_sizes[i]];
                    }
                }

                return new byte[bufferSize];
            }
        }

        public void ReturnBuffer(byte[] buffer)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (buffer == null) throw new ArgumentNullException("buffer");

            lock (_thisLock)
            {
                if (buffer.Length > _maxBufferSize) return;

                for (int i = 0; i < _sizes.Length; i++)
                {
                    if (buffer.Length == _sizes[i])
                    {
                        _buffers[i].AddFirst(new WeakReference(buffer));

                        break;
                    }
                }
            }
        }

        public void Clear()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_thisLock)
            {
                for (int i = 0; i < _buffers.Length; i++)
                {
                    _buffers[i].Clear();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

            }
        }
    }

#else

    public class BufferManager : ManagerBase, IThisLock
    {
        private static readonly BufferManager _instance = new BufferManager(1024 * 1024 * 256, 1024 * 1024 * 256);

        private System.ServiceModel.Channels.BufferManager _bufferManager;
        private ConditionalWeakTable<byte[], BufferTracker> _trackLeakedBuffers = new ConditionalWeakTable<byte[], BufferTracker>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed = false;

        public BufferManager(long maxBufferPoolSize, int maxBufferSize)
        {
            _bufferManager = System.ServiceModel.Channels.BufferManager.CreateBufferManager(maxBufferPoolSize, maxBufferSize);
        }

        public static BufferManager Instance
        {
            get
            {
                return _instance;
            }
        }

        public long Size
        {
            get
            {
                return 0;
            }
        }

        public byte[] TakeBuffer(int size)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                var buffer = _bufferManager.TakeBuffer(size);
                _trackLeakedBuffers.GetOrCreateValue(buffer).TrackAllocation();

                return buffer;
            }
        }

        public void ReturnBuffer(byte[] buffer)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                BufferTracker value;

                if (_trackLeakedBuffers.TryGetValue(buffer, out value))
                {
                    value.Discard();
                }

                _bufferManager.ReturnBuffer(buffer);
            }
        }

        public void Clear()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _bufferManager.Clear();
            }
        }

        private class BufferTracker
        {
            private volatile StackTrace _stackTrace;

            public void TrackAllocation()
            {
                _stackTrace = new StackTrace(true);
                GC.ReRegisterForFinalize(this);
            }

            public void Discard()
            {
                if (_stackTrace == null) throw new ArgumentException("Buffer returned twice.");

                _stackTrace = null;
                GC.SuppressFinalize(this);
            }

            ~BufferTracker()
            {
                if (_stackTrace == null) return;

                throw new MemoryLeakException(_stackTrace.ToString());
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _bufferManager.Clear();
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

    [Serializable]
    class BufferManagerException : ManagerException
    {
        public BufferManagerException() : base() { }
        public BufferManagerException(string message) : base(message) { }
        public BufferManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class MemoryLeakException : BufferManagerException
    {
        public MemoryLeakException() : base() { }
        public MemoryLeakException(string message) : base(message) { }
        public MemoryLeakException(string message, Exception innerException) : base(message, innerException) { }
    }

#endif

}
