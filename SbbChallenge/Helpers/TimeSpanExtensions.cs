using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace SbbChallenge.Helpers
{
    /// <summary>
    /// As we use Timespans for the lengths of arcs, I added simple additional Timespan extension methods:
    /// - Sum ({t1, ..., tn}) -> t.
    /// - Max (t1, t2) -> t.
    /// - Min (t1, t2) -> t.
    /// - Show (t) -> "xxh xxm xxs". (hours, minutes, seconds; all the dates are only confusing in the scope of the
    /// sbb problems) 
    /// </summary>
    public static class TimeSpanExtensions
    {
        [Pure] public static TimeSpan Sum (this IEnumerable<TimeSpan> enumerable) =>
            enumerable.Aggregate(TimeSpan.Zero, (t0, t1) => t0 + t1);
        
        [Pure] public static TimeSpan Min(this TimeSpan t0, TimeSpan t1)
        {
            if (t0.CompareTo(t1) <= 0) return t0;
            return t1;
        }
        
        [Pure] public static TimeSpan Max(this TimeSpan t0, TimeSpan t1)
        {
            if (t0.CompareTo(t1) > 0) return t0;
            return t1;
        }

        [Pure] public static string Show(this TimeSpan t)
        {
            if (t == TimeSpan.MaxValue) return "∞";
            
            string str = "";
            if (t.Hours != 0) str += $"{t.Hours}h";
            if (t.Minutes != 0) str += $"{t.Minutes}m";
            if (t.Seconds != 0) str += $"{t.Seconds}s";
            if (str == "") str = "0";
            return str;
        }
    }
}