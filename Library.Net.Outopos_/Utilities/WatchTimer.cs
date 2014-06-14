using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Library.Net.Outopos
{
    class WatchTimer : ManagerBase
    {
        private Action _callback;

        private System.Threading.Timer _watchTimer;
        private object _syncObject = new object();

        private volatile bool _disposed;

        public WatchTimer(Action callback, TimeSpan period)
        {
            _callback = callback;
            _watchTimer = new Timer(this.Timer, null, period, period);
        }

        public WatchTimer(Action callback, TimeSpan start, TimeSpan period)
        {
            _callback = callback;
            _watchTimer = new Timer(this.Timer, null, start, period);
        }

        public WatchTimer(Action callback, int period)
        {
            _callback = callback;
            _watchTimer = new Timer(this.Timer, null, period, period);
        }

        public WatchTimer(Action callback, int start, int period)
        {
            _callback = callback;
            _watchTimer = new Timer(this.Timer, null, start, period);
        }

        private void Timer(object state)
        {
            bool taken = false;

            try
            {
                Monitor.TryEnter(_syncObject, ref taken);

                if (taken)
                {
                    _callback();
                }
            }
            finally
            {
                if (taken) Monitor.Exit(_syncObject);
            }
        }

        public void Change(TimeSpan period)
        {
            _watchTimer.Change(period, period);
        }

        public void Change(TimeSpan start, TimeSpan period)
        {
            _watchTimer.Change(start, period);
        }

        public void Change(int period)
        {
            _watchTimer.Change(period, period);
        }

        public void Change(int start, int period)
        {
            _watchTimer.Change(start, period);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _watchTimer.Dispose();
            }
        }
    }
}
