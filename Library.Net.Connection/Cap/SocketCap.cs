using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Library.Io;

namespace Library.Net.Connection
{
    public class SocketCap : CapBase, IThisLock
    {
        private Socket _socket;

        private object _sendLock = new object();
        private object _receiveLock = new object();
        private object _thisLock = new object();

        private volatile bool _connect = false;
        private volatile bool _disposed = false;

        public SocketCap(Socket socket)
        {
            _socket = socket;
            _connect = true;
        }

        public Socket Socket
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _socket;
                }
            }
        }

        public override int Receive(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new CapException();

            try
            {
                lock (_receiveLock)
                {
                    _socket.ReceiveTimeout = (int)timeout.TotalMilliseconds;

                    var i = _socket.Receive(buffer, offset, size, SocketFlags.None);
                    if (i == 0) _connect = false;

                    return i;
                }
            }
            catch (Exception e)
            {
                _connect = false;

                throw new CapException("Receive", e);
            }
        }

        public override int Send(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new CapException();

            try
            {
                lock (_sendLock)
                {
                    _socket.SendTimeout = (int)timeout.TotalMilliseconds;

                    return _socket.Send(buffer, offset, size, SocketFlags.None);
                }
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
                        _socket.Shutdown(SocketShutdown.Both);
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

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }
}
