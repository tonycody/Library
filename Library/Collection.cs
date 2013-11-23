﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Library
{
    public static class Collection
    {
        public static IEnumerable<T> Merge<T>(params IEnumerable<T>[] items)
        {
            if (items == null) throw new ArgumentNullException("items");

            return items.SelectMany(list => list);
        }

        public static bool Equals<T>(IList<T> source, IList<T> destination, IEqualityComparer<T> equalityComparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            if (object.ReferenceEquals(source, destination)) return true;
            if (source.Count != destination.Count) return false;

            for (int i = source.Count - 1; i >= 0; i--)
            {
                if (!equalityComparer.Equals(source[i], destination[i])) return false;
            }

            return true;
        }

        public static bool Equals<T>(IEnumerable<T> source, IEnumerable<T> destination, IEqualityComparer<T> equalityComparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            if (object.ReferenceEquals(source, destination)) return true;

            using (var s = source.GetEnumerator())
            using (var d = destination.GetEnumerator())
            {
                while (s.MoveNext())
                {
                    if (!d.MoveNext()) return false;
                    if (!equalityComparer.Equals(s.Current, d.Current)) return false;
                }

                if (d.MoveNext()) return false;
            }

            return true;
        }

        public static bool Equals<T>(IList<T> source, int sourceIndex, IList<T> destination, int destinationIndex, int length, IEqualityComparer<T> equalityComparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            if (0 > (source.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destination.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (source.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destination.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (!equalityComparer.Equals(source[i], destination[j])) return false;
            }

            return true;
        }

        public static bool Equals<T>(IEnumerable<T> source, int sourceIndex, IEnumerable<T> destination, int destinationIndex, int length, IEqualityComparer<T> equalityComparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            using (var s = source.GetEnumerator())
            using (var d = destination.GetEnumerator())
            {
                for (int i = 0; i < sourceIndex; i++)
                {
                    if (!s.MoveNext()) throw new ArgumentOutOfRangeException("sourceIndex");
                }

                for (int i = 0; i < destinationIndex; i++)
                {
                    if (!d.MoveNext()) throw new ArgumentOutOfRangeException("destinationIndex");
                }

                for (int i = 0; i < length; i++)
                {
                    if (!s.MoveNext()) throw new ArgumentOutOfRangeException("length");
                    if (!d.MoveNext()) throw new ArgumentOutOfRangeException("length");

                    if (!equalityComparer.Equals(s.Current, d.Current)) return false;
                }
            }

            return true;
        }

        public static bool Equals<T>(IList<T> source, IList<T> destination)
        {
            var equalityComparer = EqualityComparer<T>.Default;
            return Collection.Equals(source, destination, equalityComparer);
        }

        public static bool Equals<T>(IEnumerable<T> source, IEnumerable<T> destination)
        {
            var equalityComparer = EqualityComparer<T>.Default;
            return Collection.Equals(source, destination, equalityComparer);
        }

        public static bool Equals<T>(IList<T> source, int sourceIndex, IList<T> destination, int destinationIndex, int length)
        {
            var equalityComparer = EqualityComparer<T>.Default;
            return Collection.Equals(source, sourceIndex, destination, destinationIndex, length, equalityComparer);
        }

        public static bool Equals<T>(IEnumerable<T> source, int sourceIndex, IEnumerable<T> destination, int destinationIndex, int length)
        {
            var equalityComparer = EqualityComparer<T>.Default;
            return Collection.Equals(source, sourceIndex, destination, destinationIndex, length, equalityComparer);
        }

        public static int Compare<T>(IList<T> source, IList<T> destination, IComparer<T> comparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (comparer == null) throw new ArgumentNullException("comparer");

            if (object.ReferenceEquals(source, destination)) return 0;
            if (source.Count != destination.Count) return (source.Count > destination.Count) ? 1 : -1;

            int c = 0;

            for (int i = source.Count - 1; i >= 0; i--)
            {
                if ((c = comparer.Compare(source[i], destination[i])) != 0) return c;
            }

            return 0;
        }

        public static int Compare<T>(IEnumerable<T> source, IEnumerable<T> destination, IComparer<T> comparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (comparer == null) throw new ArgumentNullException("comparer");

            if (object.ReferenceEquals(source, destination)) return 0;

            int c = 0;

            using (var s = source.GetEnumerator())
            using (var d = destination.GetEnumerator())
            {
                while (s.MoveNext())
                {
                    if (!d.MoveNext()) return 1;
                    if ((c = comparer.Compare(s.Current, d.Current)) != 0) break;
                }

                while (s.MoveNext())
                {
                    if (!d.MoveNext()) return 1;
                }

                if (d.MoveNext()) return -1;
            }

            return c;
        }

        public static int Compare<T>(IList<T> source, int sourceIndex, IList<T> destination, int destinationIndex, int length, IComparer<T> comparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (comparer == null) throw new ArgumentNullException("comparer");

            if (0 > (source.Count - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destination.Count - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (source.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destination.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            int c;

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if ((c = comparer.Compare(source[i], destination[j])) != 0) return c;
            }

            return 0;
        }

        public static int Compare<T>(IEnumerable<T> source, int sourceIndex, IEnumerable<T> destination, int destinationIndex, int length, IComparer<T> comparer)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");
            if (comparer == null) throw new ArgumentNullException("comparer");

            using (var s = source.GetEnumerator())
            using (var d = destination.GetEnumerator())
            {
                for (int i = 0; i < sourceIndex; i++)
                {
                    if (!s.MoveNext()) throw new ArgumentOutOfRangeException("sourceIndex");
                }

                for (int i = 0; i < destinationIndex; i++)
                {
                    if (!d.MoveNext()) throw new ArgumentOutOfRangeException("destinationIndex");
                }

                int c = 0;

                for (int i = 0; i < length; i++)
                {
                    if (!s.MoveNext()) throw new ArgumentOutOfRangeException("length");
                    if (!d.MoveNext()) throw new ArgumentOutOfRangeException("length");

                    if ((c = comparer.Compare(s.Current, d.Current)) != 0) return c;
                }
            }

            return 0;
        }

        public static int Compare<T>(IList<T> source, IList<T> destination)
        {
            var compare = Comparer<T>.Default;
            return Collection.Compare(source, destination, compare);
        }

        public static int Compare<T>(IEnumerable<T> source, IEnumerable<T> destination)
        {
            var compare = Comparer<T>.Default;
            return Collection.Compare(source, destination, compare);
        }

        public static int Compare<T>(IList<T> source, int sourceIndex, IList<T> destination, int destinationIndex, int length)
        {
            var compare = Comparer<T>.Default;
            return Collection.Compare(source, sourceIndex, destination, destinationIndex, length, compare);
        }

        public static int Compare<T>(IEnumerable<T> source, int sourceIndex, IEnumerable<T> destination, int destinationIndex, int length)
        {
            var compare = Comparer<T>.Default;
            return Collection.Compare(source, sourceIndex, destination, destinationIndex, length, compare);
        }
    }
}
