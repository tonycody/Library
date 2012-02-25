using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Library.Net.Proxy
{
    public abstract class ProxyClientBase
    {
        public abstract Socket CreateConnection(TimeSpan timeout);

        public virtual IAsyncResult BeginCreateConnection(string destinationHost, int destinationPort, TimeSpan timeout, AsyncCallback callback, object state)
        {
            var ar = new ReturnAsyncResult<Socket>(callback, state);

            ThreadPool.QueueUserWorkItem(new WaitCallback((object wstate) =>
            {
                try
                {
                    ar.ReturnObject = this.CreateConnection(timeout);
                }
                catch (Exception)
                {
                }

                ar.Complete(false);
            }));

            return ar;
        }

        public virtual void EndCreateConnection(IAsyncResult result)
        {
            ReturnAsyncResult<Socket>.End(result);
        }

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
    }

    [Serializable]
    public class ProxyClientException : Exception
    {
        public ProxyClientException()
        {
        }

        public ProxyClientException(string message)
            : base(message)
        {
        }

        public ProxyClientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    [Serializable]
    public class TimeoutException : ProxyClientException
    {
        public TimeoutException()
            : base()
        {
        }

        public TimeoutException(string message)
            : base(message)
        {
        }

        public TimeoutException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
