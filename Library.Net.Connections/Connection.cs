using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Library.Net.Connections
{
    public abstract class Connection : ManagerBase
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

        public abstract IEnumerable<Connection> GetLayers();

        public abstract long ReceivedByteCount { get; }

        public abstract long SentByteCount { get; }

        // Connect
        public abstract void Connect(TimeSpan timeout, Information options);

        public virtual void Connect(TimeSpan timeout)
        {
            this.Connect(timeout, null);
        }

        public virtual Task ConnectAsync(TimeSpan timeout, Information options)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Connect(timeout, options);
            });
        }

        public virtual Task ConnectAsync(TimeSpan timeout)
        {
            return this.ConnectAsync(timeout, null);
        }

        // Close
        public abstract void Close(TimeSpan timeout, Information options);

        public virtual void Close(TimeSpan timeout)
        {
            this.Close(timeout, null);
        }

        public virtual Task CloseAsync(TimeSpan timeout, Information options)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Close(timeout, options);
            });
        }

        public virtual Task CloseAsync(TimeSpan timeout)
        {
            return this.CloseAsync(timeout, null);
        }

        // Receive
        public abstract Stream Receive(TimeSpan timeout, Information options);

        public virtual Stream Receive(TimeSpan timeout)
        {
            return this.Receive(timeout, null);
        }

        public virtual Task<Stream> ReceiveAsync(TimeSpan timeout, Information options)
        {
            return Task.Factory.StartNew(() =>
            {
                return this.Receive(timeout, options);
            });
        }

        public virtual Task<Stream> ReceiveAsync(TimeSpan timeout)
        {
            return this.ReceiveAsync(timeout, null);
        }

        // Send
        public abstract void Send(Stream stream, TimeSpan timeout, Information options);

        public virtual void Send(Stream stream, TimeSpan timeout)
        {
            this.Send(stream, timeout, null);
        }

        public virtual Task SendAsync(Stream stream, TimeSpan timeout, Information options)
        {
            return Task.Factory.StartNew(() =>
            {
                this.Send(stream, timeout, options);
            });
        }

        public virtual Task SendAsync(Stream stream, TimeSpan timeout)
        {
            return this.SendAsync(stream, timeout, null);
        }
    }

    [Serializable]
    public class ConnectionException : Exception
    {
        public ConnectionException() : base() { }
        public ConnectionException(string message) : base(message) { }
        public ConnectionException(string message, Exception innerException) : base(message, innerException) { }
    }
}
