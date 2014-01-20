using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Library
{

#if !MONITOR

    public class BufferManager : ManagerBase
    {
        private static readonly BufferManager _instance = new BufferManager(1024 * 1024 * 1024, 1024 * 1024 * 256);

        //private System.ServiceModel.Channels.BufferManager _bufferManager;

        private long _maxBufferPoolSize;
        private int _maxBufferSize;

        private int[] _sizes;
        private List<byte[]>[] _buffers;
        private long[] _callCounts;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public BufferManager(long maxBufferPoolSize, int maxBufferSize)
        {
            //_bufferManager = System.ServiceModel.Channels.BufferManager.CreateBufferManager(maxBufferPoolSize, maxBufferSize);

            if (maxBufferPoolSize < maxBufferSize) throw new ArgumentOutOfRangeException();
            if (maxBufferPoolSize == 0) throw new ArgumentOutOfRangeException();

            _maxBufferPoolSize = maxBufferPoolSize;
            _maxBufferSize = maxBufferSize;

            List<int> sizes = new List<int>();
            List<List<byte[]>> buffers = new List<List<byte[]>>();

            for (int i = 32; i < _maxBufferSize; i *= 2)
            {
                sizes.Add(i);
                buffers.Add(new List<byte[]>());
            }

            _sizes = sizes.ToArray();
            _buffers = buffers.ToArray();
            _callCounts = new long[sizes.Count];
        }

        public static BufferManager Instance
        {
            get
            {
                return _instance;
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
                        _callCounts[i]++;

                        if (_buffers[i].Count > 0)
                        {
                            byte[] buffer = _buffers[i][0];
                            _buffers[i].RemoveAt(0);

                            return buffer;
                        }
                        else
                        {
                            return new byte[_sizes[i]];
                        }
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

                for (; ; )
                {
                    long memorySize = 0;

                    memorySize += buffer.Length;

                    for (int i = 0; i < _sizes.Length; i++)
                    {
                        memorySize += (_sizes[i] * _buffers[i].Count);
                    }

                    if (_maxBufferPoolSize > memorySize) break;

                    {
                        var sortItems = new List<KeyValuePair<long, int>>();

                        for (int i = 0; i < _callCounts.Length; i++)
                        {
                            sortItems.Add(new KeyValuePair<long, int>(_callCounts[i], i));
                        }

                        sortItems.Sort((x, y) =>
                        {
                            return x.Key.CompareTo(y.Key);
                        });

                        for (int i = 0; i < sortItems.Count; i++)
                        {
                            int index = sortItems[i].Value;

                            if (_buffers[index].Count > 0)
                            {
                                _buffers[index].RemoveAt(0);

                                break;
                            }
                        }
                    }
                }

                for (int i = 0; i < _sizes.Length; i++)
                {
                    if (buffer.Length == _sizes[i])
                    {
                        _buffers[i].Add(buffer);

                        break;
                    }
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
