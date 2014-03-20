using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Library
{
    public class InternPool<TKey, TValue>
    {
        private System.Threading.Timer _watchTimer;
        private Dictionary<TKey, Info<TValue>> _dic;
        private readonly object _thisLock = new object();

        public InternPool()
        {
            _watchTimer = new Timer(this.WatchTimer, null, new TimeSpan(0, 1, 0), new TimeSpan(0, 1, 0));
            _dic = new Dictionary<TKey, Info<TValue>>();
        }

        public InternPool(IEqualityComparer<TKey> comparer)
        {
            _watchTimer = new Timer(this.WatchTimer, null, new TimeSpan(0, 1, 0), new TimeSpan(0, 1, 0));
            _dic = new Dictionary<TKey, Info<TValue>>(comparer);
        }

        private void WatchTimer(object state)
        {
            this.Refresh();
        }

        public int Count
        {
            get
            {
                return _dic.Count;
            }
        }

        internal void Refresh()
        {
            lock (_thisLock)
            {
                List<TKey> list = null;

                foreach (var pair in _dic)
                {
                    var key = pair.Key;
                    var info = pair.Value;

                    if (info.Count == 0)
                    {
                        if (list == null)
                            list = new List<TKey>();

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

        public TValue GetValue(TKey key, TValue value, object holder)
        {
            lock (_thisLock)
            {
                Info<TValue> info;

                if (!_dic.TryGetValue(key, out info))
                {
                    info = new Info<TValue>(value);
                    _dic[key] = info;
                }

                info.Add(holder);
                return info.Value;
            }
        }

        class Info<T>
        {
            private List<WeakReference> _list = new List<WeakReference>();
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
    }
}
