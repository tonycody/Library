using System;
using System.Threading.Tasks;

namespace Library.Net
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

        public abstract void Receive(byte[] buffer, int offset, int size, TimeSpan timeout);

        public virtual Task ReceiveAsync(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Receive(buffer, offset, size, timeout);
            });
        }

        public virtual void Receive(byte[] buffer, TimeSpan timeout)
        {
            this.Receive(buffer, 0, buffer.Length, timeout);
        }

        public virtual Task ReceiveAsync(byte[] buffer, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Receive(buffer, timeout);
            });
        }

        public abstract void Send(byte[] buffer, int offset, int size, TimeSpan timeout);

        public virtual Task SendAsync(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Send(buffer, offset, size, timeout);
            });
        }

        public virtual void Send(byte[] buffer, TimeSpan timeout)
        {
            this.Send(buffer, 0, buffer.Length, timeout);
        }

        public virtual Task SendAsync(byte[] buffer, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Send(buffer, timeout);
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
