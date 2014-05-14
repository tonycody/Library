using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace Library.Net
{
    public class SocketCap : CapBase
    {
        private Socket _socket;

        private Stopwatch _receiveStopwatch = new Stopwatch();

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;

        private volatile bool _disposed;

        public SocketCap(Socket socket)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (!socket.Connected) throw new ArgumentException("Socket is not connected.");

            _socket = socket;
            _socket.Blocking = true;
            _socket.ReceiveBufferSize = 1024 * 32;
            _socket.SendBufferSize = 1024 * 32;

            _connect = true;
        }

        public Socket Socket
        {
            get
            {
                return _socket;
            }
        }

        public override void Receive(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new CapException("Closed");

            try
            {
                lock (_receiveLock)
                {
                    _receiveStopwatch.Restart();

                    do
                    {
                        var time = SocketCap.CheckTimeout(_receiveStopwatch.Elapsed, timeout);
                        _socket.ReceiveTimeout = (int)Math.Min(int.MaxValue, time.TotalMilliseconds);

                        int receiveLength;

                        if ((receiveLength = _socket.Receive(buffer, offset, size, SocketFlags.None)) == 0)
                        {
                            _connect = false;

                            throw new CapException("Closed");
                        }

                        offset += receiveLength;
                        size -= receiveLength;
                    } while (size > 0);
                }
            }
            catch (CapException)
            {
                throw;
            }
            catch (Exception e)
            {
                _connect = false;

                throw new CapException("Receive", e);
            }
        }

        public override void Send(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new CapException();

            try
            {
                lock (_sendLock)
                {
                    _socket.SendTimeout = (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds);

                    _socket.Send(buffer, offset, size, SocketFlags.None);
                }
            }
            catch (CapException)
            {
                throw;
            }
            catch (Exception e)
            {
                _connect = false;

                throw new CapException("Send", e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_socket != null)
                {
                    try
                    {
                        _socket.Shutdown(SocketShutdown.Send);
                        _socket.Close();
                        _socket.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _socket = null;
                }

                _connect = false;
            }
        }
    }
}
