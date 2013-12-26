using System;
using System.Threading.Tasks;

namespace Library.Net.Caps
{
    public abstract class CapBase : ManagerBase
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

        public abstract int Receive(byte[] buffer, int offset, int size, TimeSpan timeout);

        public virtual Task<int> ReceiveAsync(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                return this.Receive(buffer, offset, size, timeout);
            });
        }

        public virtual int Receive(byte[] buffer, TimeSpan timeout)
        {
            return this.Receive(buffer, 0, buffer.Length, timeout);
        }

        public virtual Task<int> ReceiveAsync(byte[] buffer, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                return this.Receive(buffer, timeout);
            });
        }

        public abstract int Send(byte[] buffer, int offset, int size, TimeSpan timeout);

        public virtual Task<int> SendAsync(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                return this.Send(buffer, offset, size, timeout);
            });
        }

        public virtual int Send(byte[] buffer, TimeSpan timeout)
        {
            return this.Send(buffer, 0, buffer.Length, timeout);
        }

        public virtual Task<int> SendAsync(byte[] buffer, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                return this.Send(buffer, timeout);
            });
        }
    }

    [Serializable]
    public class CapException : Exception
    {
        public CapException() : base() { }
        public CapException(string message) : base(message) { }
        public CapException(string message, Exception innerException) : base(message, innerException) { }
    }
}
