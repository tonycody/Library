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

        private Dictionary<T, Info<T>> _dic;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public InternPool()
        {
            _watchTimer = new System.Threading.Timer(this.WatchTimer, null, new TimeSpan(0, 3, 0), new TimeSpan(0, 3, 0));
            _dic = new Dictionary<T, Info<T>>();
        }

        public InternPool(IEqualityComparer<T> comparer)
        {
            _watchTimer = new System.Threading.Timer(this.WatchTimer, null, new TimeSpan(0, 3, 0), new TimeSpan(0, 3, 0));
            _dic = new Dictionary<T, Info<T>>(comparer);
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
                    LinkedList<T> list = null;

                    foreach (var pair in _dic)
                    {
                        var key = pair.Key;
                        var info = pair.Value;

                        if (info.Count == 0)
                        {
                            if (list == null)
                                list = new LinkedList<T>();

                            list.AddLast(key);
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
                Info<T> info;

                if (!_dic.TryGetValue(value, out info))
                {
                    info = new Info<T>(value);
                    _dic[value] = info;
                }

                info.Add(holder);
                return info.Value;
            }
        }

        private class Info<TValue>
        {
            private LinkedList<WeakReference> _list = new LinkedList<WeakReference>();
            private TValue _value;

            public Info(TValue value)
            {
                _value = value;
            }

            public TValue Value
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
                var currentItem = _list.First;

                while (currentItem != null)
                {
                    if (!currentItem.Value.IsAlive)
                    {
                        var removeNode = currentItem;
                        currentItem = currentItem.Next;
                        _list.Remove(removeNode);
                    }
                    else
                    {
                        currentItem = currentItem.Next;
                    }
                }
            }

            public void Add(object holder)
            {
                _list.AddLast(new WeakReference(holder));
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
