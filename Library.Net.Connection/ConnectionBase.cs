using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

namespace Library.Net.Connection
{
    public abstract class ConnectionBase : ManagerBase
    {
        protected static TimeSpan CheckTimeout(TimeSpan elapsedTime, TimeSpan timeout)
        {
            var value = timeout - elapsedTime;

            if (value > TimeSpan.Zero)
            {
                return value;
            }
            else
            {
                throw new TimeoutException();
            }
        }

        public abstract IEnumerable<ConnectionBase> GetLayers();

        public abstract long ReceivedByteCount { get; }

        public abstract long SentByteCount { get; }

        public abstract void Connect(TimeSpan timeout);

        public virtual IAsyncResult BeginConnect(TimeSpan timeout, AsyncCallback callback, object state)
        {
            var ar = new AsyncResult(callback, state);

            ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
            {
                try
                {
                    this.Connect(timeout);
                }
                catch (Exception)
                {

                }

                ar.Complete(false);
            }));

            return ar;
        }

        public virtual void EndConnect(IAsyncResult result)
        {
            AsyncResult.End(result);
        }

        public abstract void Close(TimeSpan timeout);

        public virtual IAsyncResult BeginClose(TimeSpan timeout, AsyncCallback callback, object state)
        {
            var ar = new AsyncResult(callback, state);

            ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
            {
                try
                {
                    this.Close(timeout);
                }
                catch (Exception)
                {

                }

                ar.Complete(false);
            }));

            return ar;
        }

        public virtual void EndClose(IAsyncResult result)
        {
            AsyncResult.End(result);
        }

        public abstract Stream Receive(TimeSpan timeout);

        public virtual IAsyncResult BeginReceive(TimeSpan timeout, AsyncCallback callback, object state)
        {
            var ar = new ReturnAsyncResult<Stream>(callback, state);

            ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
            {
                try
                {
                    ar.ReturnObject = this.Receive(timeout);
                }
                catch (Exception)
                {

                }

                ar.Complete(false);
            }));

            return ar;
        }

        public virtual Stream EndReceive(IAsyncResult result)
        {
            ReturnAsyncResult<Stream>.End(result);
            return ((ReturnAsyncResult<Stream>)result).ReturnObject;
        }

        public abstract void Send(Stream stream, TimeSpan timeout);

        public virtual IAsyncResult BeginSend(Stream stream, TimeSpan timeout, AsyncCallback callback, object state)
        {
            var ar = new AsyncResult(callback, state);

            ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
            {
                try
                {
                    this.Send(stream, timeout);
                }
                catch (Exception)
                {

                }

                ar.Complete(false);
            }));

            return ar;
        }

        public virtual void EndSend(IAsyncResult result)
        {
            AsyncResult.End(result);
        }
    }

    [Serializable]
    public class ConnectionException : Exception
    {
        public ConnectionException() : base() { }
        public ConnectionException(string message) : base(message) { }
        public ConnectionException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    public class TimeoutException : ConnectionException
    {
        public TimeoutException() : base() { }
        public TimeoutException(string message) : base(message) { }
        public TimeoutException(string message, Exception innerException) : base(message, innerException) { }
    }
}
