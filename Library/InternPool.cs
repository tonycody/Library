using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library
{
    public class InternPool<T> : ManagerBase, IThisLock
    {
        private System.Threading.Timer _watchTimer;
        private volatile bool _isRefreshing = false;

        private Dictionary<T, Info> _dic;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public InternPool()
        {
            _watchTimer = new System.Threading.Timer(this.WatchTimer, null, new TimeSpan(0, 1, 0), new TimeSpan(0, 1, 0));
            _dic = new Dictionary<T, Info>();
        }

        public InternPool(IEqualityComparer<T> comparer)
        {
            _watchTimer = new System.Threading.Timer(this.WatchTimer, null, new TimeSpan(0, 1, 0), new TimeSpan(0, 1, 0));
            _dic = new Dictionary<T, Info>(comparer);
        }

        private void WatchTimer(object state)
        {
            this.Refresh();
        }

        public int Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _dic.Count;
                }
            }
        }

        internal void Refresh()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                lock (this.ThisLock)
                {
                    List<T> list = null;

                    foreach (var pair in _dic)
                    {
                        var key = pair.Key;
                        var info = pair.Value;

                        if (info.Count == 0)
                        {
                            if (list == null)
                                list = new List<T>();

                            list.Add(key);
                        }
                    }

                    if (list != null)
                    {
                        foreach (var key in list)
                        {
                            _dic.Remove(key);
                        }
                    }
                }
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        public T GetValue(T value, object holder)
        {
            lock (this.ThisLock)
            {
                Info info;

                if (!_dic.TryGetValue(value, out info))
                {
                    info = new Info(value);
                    _dic[value] = info;
                }

                info.Add(holder);
                return info.Value;
            }
        }

        private class Info
        {
            private SimpleLinkedList<WeakReference> _list = new SimpleLinkedList<WeakReference>();
            private T _value;

            public Info(T value)
            {
                _value = value;
            }

            public T Value
            {
                get
                {
                    return _value;
                }
            }

            public int Count
            {
                get
                {
                    this.Refresh();

                    return _list.Count;
                }
            }

            private void Refresh()
            {
                List<WeakReference> list = null;

                foreach (var weakReference in _list)
                {
                    if (!weakReference.IsAlive)
                    {
                        if (list == null)
                            list = new List<WeakReference>();

                        list.Add(weakReference);
                    }
                }

                if (list != null)
                {
                    foreach (var weakReference in list)
                    {
                        _list.Remove(weakReference);
                    }
                }
            }

            public void Add(object holder)
            {
                _list.Add(new WeakReference(holder));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_watchTimer != null)
                {
                    try
                    {
                        _watchTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _watchTimer = null;
                }
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
