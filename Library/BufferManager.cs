using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library
{
    public class BufferManager : ManagerBase, IThisLock
    {
        private HashSet<byte[]> _bufferList = new HashSet<byte[]>();

        private static System.ServiceModel.Channels.BufferManager _bufferManager = System.ServiceModel.Channels.BufferManager.CreateBufferManager(1024 * 1024 * 256, 1024 * 1024 * 128);
        private object _thisLock = new object();
        private bool _disposed = false;

        public BufferManager()
        {

        }

        public byte[] TakeBuffer(int bufferSize)
        {
            lock (this.ThisLock)
            {
                var buffer = _bufferManager.TakeBuffer(bufferSize);

                _bufferList.Add(buffer);
                return buffer;
            }
        }

        public void ReturnBuffer(byte[] buffer)
        {
            lock (this.ThisLock)
            {
                _bufferManager.ReturnBuffer(buffer);

                if (!_bufferList.Remove(buffer))
                {
                    Log.Error("BufferManager\r\n" + Environment.StackTrace);
                }
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {

            }
        }

        protected override void Dispose(bool disposing)
        {
            lock (this.ThisLock)
            {
                if (_disposed) return;

                if (disposing)
                {

                }

                _disposed = true;
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
