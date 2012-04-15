using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Library.Net.Proxy
{
    internal class AsyncResult : IAsyncResult, IThisLock
    {
        private AsyncCallback _callback;
        private object _state;

        private bool _completedSynchronously;
        private bool _isCompleted;
        private ManualResetEvent _manualResetEvent;
        private bool _endCalled;
        private object _thisLock = new object();

        public AsyncResult(AsyncCallback callback, object state)
        {
            _manualResetEvent = new ManualResetEvent(_isCompleted);

            _callback = callback;
            _state = state;
        }

        public object AsyncState
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
            }
        }

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _manualResetEvent;
                }
            }
        }

        public bool CompletedSynchronously
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _completedSynchronously;
                }
            }
        }

        public bool IsCompleted
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _isCompleted;
                }
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

        public static void End(IAsyncResult result)
        {
            if (result == null) throw new ArgumentNullException("result");

            var asyncResult = result as AsyncResult;
            if (asyncResult == null) throw new ArgumentException(string.Format("{0} にキャストできませんでした", typeof(AsyncResult).AssemblyQualifiedName), "result");

            lock (asyncResult.ThisLock)
            {
                if (asyncResult._endCalled)
                {
                    throw new InvalidOperationException("resultは既に終了しました");
                }

                asyncResult._endCalled = true;
            }

            if (!asyncResult._isCompleted)
            {
                asyncResult.AsyncWaitHandle.WaitOne();
            }

            asyncResult._manualResetEvent.Close();
        }

        public void Complete(bool completedSynchronously)
        {
            lock (this.ThisLock)
            {
                if (_isCompleted) throw new InvalidOperationException("Complete済みです");

                _completedSynchronously = completedSynchronously;
                _isCompleted = true;

                _manualResetEvent.Set();

                if (_callback != null)
                {
                    _callback(this);
                }
            }
        }
    }

    internal class ReturnAsyncResult<T> : AsyncResult
    {
        public ReturnAsyncResult(AsyncCallback callback, object state)
            : base(callback, state)
        {
        }

        public T ReturnObject { get; set; }
    }
}
