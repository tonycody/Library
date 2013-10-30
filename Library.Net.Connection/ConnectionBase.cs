using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Library.Net.Connection
{
    public abstract class ConnectionBase : ManagerBase
    {
        public abstract IEnumerable<ConnectionBase> GetLayers();

        public abstract long ReceivedByteCount { get; }

        public abstract long SentByteCount { get; }

        public abstract void Connect(TimeSpan timeout);

        public virtual Task ConnectAsync(TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Connect(timeout);
            });
        }

        public abstract void Close(TimeSpan timeout);

        public virtual Task CloseAsync(TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Close(timeout);
            });
        }

        public abstract Stream Receive(TimeSpan timeout);

        public virtual Task<Stream> ReceiveAsync(TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                return this.Receive(timeout);
            });
        }

        public abstract void Send(Stream stream, TimeSpan timeout);

        public virtual Task SendAsync(Stream stream, TimeSpan timeout)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Send(stream, timeout);
            });
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
