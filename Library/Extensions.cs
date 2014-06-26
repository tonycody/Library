using System;
using System.Collections.Generic;
using System.Threading;

namespace Library
{
    public static class IEnumerableExtensions
    {
        private static readonly ThreadLocal<Random> _threadLocalRandom = new ThreadLocal<Random>(() => new Random());

        public static IEnumerable<T> Randomize<T>(this IEnumerable<T> collection)
        {
            var random = _threadLocalRandom.Value;
            var list = new List<T>(collection);
            int n = list.Count;

            while (n > 1)
            {
                int k = random.Next(n--);
                T temp = list[n];
                list[n] = list[k];
                list[k] = temp;
            }

            return list;
        }

        public static IEnumerable<T> Randomize<T>(this IEnumerable<T> collection, Random random)
        {
            var list = new List<T>(collection);
            int n = list.Count;

            while (n > 1)
            {
                int k = random.Next(n--);
                T temp = list[n];
                list[n] = list[k];
                list[k] = temp;
            }

            return list;
        }

        public static IEnumerable<T> Extract<T>(this IEnumerable<IEnumerable<T>> source)
        {
            foreach (var collection in source)
            {
                foreach (var item in collection)
                {
                    yield return item;
                }
            }
        }
    }

    public static class StringExtensions
    {
        public static bool Contains(this string target, string value, StringComparison comparisonType)
        {
            return target.IndexOf(value, comparisonType) != -1;
        }
    }

    public static class RandomExtensions
    {
        public static void Shuffle<T>(this Random random, IList<T> collection)
        {
            int n = collection.Count;

            while (n > 1)
            {
                int k = random.Next(n--);
                T temp = collection[n];
                collection[n] = collection[k];
                collection[k] = temp;
            }
        }
    }

    public static class TimerExtensions
    {
        public static void Change(this Timer timer, TimeSpan period)
        {
            timer.Change(period, period);
        }

        public static void Change(this Timer timer, int period)
        {
            timer.Change(period, period);
        }

        public static void Change(this Timer timer, long period)
        {
            timer.Change(period, period);
        }
    }
}
