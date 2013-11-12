using System;
using System.Net.Sockets;
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

        public abstract Socket Create(TimeSpan timeout);

        public virtual Task<Socket> CreateAsync(TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                return this.Create(timeout);
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
