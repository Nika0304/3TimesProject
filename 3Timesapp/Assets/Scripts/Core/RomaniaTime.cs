using System;
using System.Globalization;

namespace ThreeTimes.Core
{
    /// <summary>
    /// Ora României (EET, UTC+2; vara EEST, UTC+3, din ultima duminică din martie până în ultima duminică din octombrie).
    /// Calculată aici, fără baza de fusuri orare a sistemului (numele lor diferă între Windows și Android).
    /// Capsulele se deschid la miezul nopții, ora României.
    /// </summary>
    public static class RomaniaTime
    {
        public static readonly string[] Months =
            { "ianuarie", "februarie", "martie", "aprilie", "mai", "iunie", "iulie", "august", "septembrie", "octombrie", "noiembrie", "decembrie" };

        public static DateTime Now => FromUtc(TestClock.UtcNow);

        public static DateTime Today => Now.Date;

        /// <summary>Ora României (fără fus) pentru un moment UTC.</summary>
        public static DateTime FromUtc(DateTime utc)
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            // Vara: de la ultima duminică din martie, 01:00 UTC, până la ultima duminică din octombrie, 01:00 UTC.
            var start = LastSunday(utc.Year, 3).AddHours(1);
            var end = LastSunday(utc.Year, 10).AddHours(1);
            bool summer = utc >= start && utc < end;
            return DateTime.SpecifyKind(utc.AddHours(summer ? 3 : 2), DateTimeKind.Unspecified);
        }

        /// <summary>Miezul nopții (ora României) al zilei date, ca moment UTC.</summary>
        public static DateTime MidnightUtc(DateTime localDate)
        {
            var d = localDate.Date;
            // La miezul nopții nu se schimbă niciodată ora, deci încercăm +2 și verificăm.
            var guess = DateTime.SpecifyKind(d.AddHours(-2), DateTimeKind.Utc);
            if (FromUtc(guess) == d) return guess;
            return DateTime.SpecifyKind(d.AddHours(-3), DateTimeKind.Utc);
        }

        public static string Iso(DateTime utc) =>
            DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        public static bool TryParseUtc(string iso, out DateTime utc)
        {
            bool ok = DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
            if (ok) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return ok;
        }

        /// <summary>„1 ianuarie 2027”.</summary>
        public static string DateText(DateTime local) => $"{local.Day} {Months[local.Month - 1]} {local.Year}";

        /// <summary>„peste 37 de zile”, „mâine”, „azi la 14:05”, sau null dacă momentul a trecut.</summary>
        public static string Until(DateTime unlockUtc)
        {
            var now = TestClock.UtcNow;
            if (unlockUtc <= now) return null;
            var at = FromUtc(unlockUtc);
            int days = (int)(at.Date - Today).TotalDays;
            if (days <= 0) return $"azi la {at:HH:mm}";
            if (days == 1) return "mâine";
            return $"peste {days} {Days(days)}";
        }

        /// <summary>„zile” sau „de zile” (20 de zile, 101 zile).</summary>
        public static string Days(int n) => n % 100 >= 20 || (n % 100 == 0 && n > 0) ? "de zile" : "zile";

        static DateTime LastSunday(int year, int month)
        {
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);
            return last.AddDays(-(int)last.DayOfWeek);
        }
    }
}
