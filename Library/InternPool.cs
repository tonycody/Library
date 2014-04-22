using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Library
{
    public class InternPool<T> : ManagerBase, IThisLock
    {
        private System.Threading.Timer _watchTimer;
        private Dictionary<T, Info<T>> _dic;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public InternPool()
        {
            _watchTimer = new Timer(this.WatchTimer, null, new TimeSpan(0, 1, 0), new TimeSpan(0, 1, 0));
            _dic = new Dictionary<T, Info<T>>();
        }

        public InternPool(IEqualityComparer<T> comparer)
        {
            _watchTimer = new Timer(this.WatchTimer, null, new TimeSpan(0, 1, 0), new TimeSpan(0, 1, 0));
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
            private List<WeakReference> _list = new List<WeakReference>();
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
                for (int i = 0; i < _list.Count; )
                {
                    if (!_list[i].IsAlive) _list.RemoveAt(i);
                    else i++;
                }
            }

            public void Add(object holder)
            {
                //if (_list.Any(n => object.ReferenceEquals(n.Target, holder))) return;
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
