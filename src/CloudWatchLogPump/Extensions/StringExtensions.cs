namespace CloudWatchLogPump.Extensions
{
    public static class StringExtensions
    {
        public static bool IsNullOrWhitespace(this string s)
        {
            return string.IsNullOrWhiteSpace(s);
        }
        
        public static bool HasValue(this string s)
        {
            return !string.IsNullOrWhiteSpace(s);
        }
        
        public static string Or(this string s1, string s2)
        {
            return string.IsNullOrWhiteSpace(s1) ? s2 : s1;
        }
    }
}