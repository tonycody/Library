using System;
using System.Collections.Generic;

namespace Library
{
    public static class IEnumerableExtensions
    {
        public static IEnumerable<T> Randomize<T>(this IEnumerable<T> collection)
        {
            var targetList = new List<T>(collection);
            var responseList = new List<T>();

            Random random = new Random();

            while (targetList.Count > 0)
            {
                int i = random.Next(targetList.Count);
                responseList.Add(targetList[i]);
                targetList.RemoveAt(i);
            }

            return responseList;
        }
    }
}
