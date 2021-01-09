using NodaTime;

namespace CloudWatchLogPump.Extensions
{
    public static class InstantUtils
    {
        public static Instant Now => SystemClock.Instance.GetCurrentInstant();
        public static Instant Tomorrow => Now.Plus(Duration.FromDays(1));
        public static Instant Yesterday => Now.Plus(Duration.FromDays(1));

        public static Instant HoursAgo(int hours) => Now.Minus(Duration.FromHours(hours));
        public static Instant DaysAgo(int days) => Now.Minus(Duration.FromDays(days));
        public static Instant HoursAhead(int hours) => Now.Plus(Duration.FromHours(hours));
        public static Instant DaysAhead(int days) => Now.Plus(Duration.FromDays(days));
    }
}