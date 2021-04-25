using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudWatchLogPump.Extensions
{
    public static class EnumerableExtensions
    {
        public static bool SafeAny<T>(this IEnumerable<T> enumerable)
        {
            return enumerable?.Any() ?? false;
        }

        public static bool SafeAny<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate)
        {
            return enumerable?.Any(predicate) ?? false;
        }

        public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T> enumerable)
        {
            return enumerable ?? Enumerable.Empty<T>();
        }

        public static IEnumerable<T> SafeUnion<T>(this IEnumerable<T> first, IEnumerable<T> second)
        {
            return first.OrEmpty().Union(second.OrEmpty());
        }
    }
}