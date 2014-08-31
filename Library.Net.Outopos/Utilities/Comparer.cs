using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Outopos
{
    class KeyComparer : IComparer<Key>
    {
        public int Compare(Key x, Key y)
        {
            int c = x.GetHashCode().CompareTo(y.GetHashCode());
            if (c != 0) return c;

            c = x.HashAlgorithm.CompareTo(y.HashAlgorithm);
            if (c != 0) return c;

            c = ((x.Hash == null) ? 0 : 1) - ((y.Hash == null) ? 0 : 1);
            if (c != 0) return c;

            if (x.Hash != null && y.Hash != null)
            {
                c = Unsafe.Compare(x.Hash, y.Hash);
                if (c != 0) return c;
            }

            return 0;
        }
    }

    class ByteArrayComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y)
        {
            return Unsafe.Compare(x, y);
        }
    }

    class WikiComparer : IComparer<Wiki>
    {
        public int Compare(Wiki x, Wiki y)
        {
            int c = x.GetHashCode().CompareTo(y.GetHashCode());
            if (c != 0) return c;

            c = x.Name.CompareTo(y.Name);
            if (c != 0) return c;

            c = ((x.Id == null) ? 0 : 1) - ((y.Id == null) ? 0 : 1);
            if (c != 0) return c;

            if (x.Id != null && y.Id != null)
            {
                c = Unsafe.Compare(x.Id, y.Id);
                if (c != 0) return c;
            }

            return 0;
        }
    }

    class ChatComparer : IComparer<Chat>
    {
        public int Compare(Chat x, Chat y)
        {
            int c = x.GetHashCode().CompareTo(y.GetHashCode());
            if (c != 0) return c;

            c = x.Name.CompareTo(y.Name);
            if (c != 0) return c;

            c = ((x.Id == null) ? 0 : 1) - ((y.Id == null) ? 0 : 1);
            if (c != 0) return c;

            if (x.Id != null && y.Id != null)
            {
                c = Unsafe.Compare(x.Id, y.Id);
                if (c != 0) return c;
            }

            return 0;
        }
    }
}
