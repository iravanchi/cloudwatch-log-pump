using System.Collections.Generic;
using CloudWatchLogPump.Utils;

namespace CloudWatchLogPump.Extensions
{
    public static class ObjectExtensions
    {
        public static string Dump(this object obj)
        {
            return ObjectDumper.Dump(obj);
        }

        public static IEnumerable<T> Yield<T>(this T t)
        {
            yield return t;
        }
    }
}