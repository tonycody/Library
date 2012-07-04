using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library
{
    public class BufferManager : ManagerBase, IThisLock
    {
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
                return _bufferManager.TakeBuffer(bufferSize);
            }
        }

        public void ReturnBuffer(byte[] buffer)
        {
            lock (this.ThisLock)
            {
                _bufferManager.ReturnBuffer(buffer);
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
