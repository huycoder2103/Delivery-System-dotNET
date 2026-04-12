using System;

namespace Delivery_System.Helpers
{
    public static class TimeHelper
    {
        private static readonly TimeZoneInfo VniZone = 
            TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

        public static DateTime NowVni() => 
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VniZone);

        public static DateTime DateVni() => NowVni().Date;
    }
}
