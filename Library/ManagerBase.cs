using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library
{
    public abstract class ManagerBase : IDisposable
    {
        ~ManagerBase()
        {
            this.Dispose(false);
        }

        protected abstract void Dispose(bool disposing);

        #region IDisposable メンバ

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    [Serializable]
    public class ManagerException : Exception
    {
        public ManagerException() : base() { }
        public ManagerException(string message) : base(message) { }
        public ManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
