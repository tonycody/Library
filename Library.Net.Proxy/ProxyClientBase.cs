using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Library.Net.Proxy
{
    public abstract class ProxyClientBase
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

        public abstract Socket CreateConnection(TimeSpan timeout);

        public virtual Task<Socket> CreateConnectionAsync(TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                return this.CreateConnection(timeout);
            });
        }
    }

    [Serializable]
    public class ProxyClientException : Exception
    {
        public ProxyClientException() { }
        public ProxyClientException(string message) : base(message) { }
        public ProxyClientException(string message, Exception innerException) : base(message, innerException) { }
    }
}
