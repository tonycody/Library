using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace Library
{
    public static class Collection
    {
        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IEnumerable<T> sourceCollection, IEnumerable<T> destinationCollection)
        //where T : IEquatable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return true;

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i] == null)
                {
                    if (list2[i] == null) continue;

                    if (!list2[i].Equals(list1[i]))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!list1[i].Equals(list2[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IEnumerable<T> sourceCollection, int sourceIndex, IEnumerable<T> destinationCollection, int destinationIndex, int length)
        //where T : IEquatable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (length > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (list1[i] == null)
                {
                    if (list2[j] == null) continue;

                    if (!list2[j].Equals(list1[i]))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!list1[i].Equals(list2[j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IEnumerable<T> sourceCollection, IEnumerable<T> destinationCollection, IEqualityComparer<T> equalityComparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return true;

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                if (!equalityComparer.Equals(list1[i], list2[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較することで、2つのシーケンスが等しいかどうかを判断します
        /// </summary>
        public static bool Equals<T>(IEnumerable<T> sourceCollection, int sourceIndex, IEnumerable<T> destinationCollection, int destinationIndex, int length, IEqualityComparer<T> equalityComparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (equalityComparer == null) throw new ArgumentNullException("equalityComparer");

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (length > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (!equalityComparer.Equals(list1[i], list2[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IEnumerable<T> sourceCollection, IEnumerable<T> destinationCollection)
            where T : IComparable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return 0;

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (list1.Count != list2.Count)
            {
                return list1.Count - list2.Count;
            }

            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i] == null)
                {
                    if (list2[i] == null) continue;

                    int c = 0;
                    if ((c = list2[i].CompareTo(list1[i])) != 0) return c * -1;
                }
                else
                {
                    int c = 0;
                    if ((c = list1[i].CompareTo(list2[i])) != 0) return c;
                }
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IEnumerable<T> sourceCollection, int sourceIndex, IEnumerable<T> destinationCollection, int destinationIndex, int length)
            where T : IComparable<T>
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (length > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (list1[i] == null)
                {
                    if (list2[j] == null) continue;

                    int c = 0;
                    if ((c = list2[j].CompareTo(list1[i])) != 0) return c * -1;
                }
                else
                {
                    int c = 0;
                    if ((c = list1[i].CompareTo(list2[j])) != 0) return c;
                }
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IEnumerable<T> sourceCollection, IEnumerable<T> destinationCollection, IComparer<T> comparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (comparer == null) throw new ArgumentNullException("comparer");

            if (object.ReferenceEquals(sourceCollection, destinationCollection)) return 0;

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (list1.Count != list2.Count)
            {
                return list1.Count - list2.Count;
            }

            for (int i = 0; i < list1.Count; i++)
            {
                int c = 0;
                if ((c = comparer.Compare(list1[i], list2[i])) != 0) return c;
            }

            return 0;
        }

        /// <summary>
        /// 配列を比較し、これらの相対値を示す値を返します
        /// </summary>
        /// <returns>xが小さい場合は0以下、xとyが等価の場合0、xが大きい場合0以上</returns>
        public static int Compare<T>(IEnumerable<T> sourceCollection, int sourceIndex, IEnumerable<T> destinationCollection, int destinationIndex, int length, IComparer<T> comparer)
        {
            if (sourceCollection == null) throw new ArgumentNullException("sourceCollection");
            if (destinationCollection == null) throw new ArgumentNullException("destinationCollection");
            if (comparer == null) throw new ArgumentNullException("comparer");

            var list1 = sourceCollection.ToList();
            var list2 = destinationCollection.ToList();

            if (length > (list1.Count - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (list2.Count - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                int c = 0;
                if ((c = comparer.Compare(list1[i], list2[j])) != 0) return c;
            }

            return 0;
        }
    }
}
