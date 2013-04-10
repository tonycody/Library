using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Library
{
#if !MONITOR

    public class BufferManager : ManagerBase, IThisLock
    {
        private static readonly BufferManager _instance = new BufferManager(1024 * 1024 * 256, 1024 * 1024 * 128);

        private System.ServiceModel.Channels.BufferManager _bufferManager;

        private object _thisLock = new object();
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

        public byte[] TakeBuffer(int bufferSize)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                return _bufferManager.TakeBuffer(bufferSize);
            }
        }

        public void ReturnBuffer(byte[] buffer)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
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

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _bufferManager.Clear();
            }

            _disposed = true;
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

#else

    public class BufferManager : ManagerBase, IThisLock
    {
        private static readonly BufferManager _instance = new BufferManager(1024 * 1024 * 256, 1024 * 1024 * 128);

        private System.ServiceModel.Channels.BufferManager _bufferManager;
        private ConditionalWeakTable<byte[], BufferTracker> _trackLeakedBuffers = new ConditionalWeakTable<byte[], BufferTracker>();

        private object _thisLock = new object();
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

            if (disposing)
            {
                _bufferManager.Clear();
            }

            _disposed = true;
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
