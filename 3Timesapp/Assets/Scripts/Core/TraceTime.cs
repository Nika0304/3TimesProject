using System;
using ThreeTimes.Data;

namespace ThreeTimes.Core
{
    /// <summary>
    /// Cât se vede o urmă în Prezent (decis 25 nov, Alexandra + Nicoleta): două săptămâni de când e lăsată; apoi se
    /// retrage și revine în locul ei la fiecare aniversare, o săptămână („acum un an, aici”), începând din ziua
    /// aniversării (ora României). Aceeași regulă e și pe server (TraceRepository.IN_PRESENT).
    /// </summary>
    public static class TraceTime
    {
        public const int FreshDays = 14;
        public const int AnniversaryDays = 7;

        /// <summary>Se vede acum în Prezent (proaspătă sau la aniversare)?</summary>
        public static bool InPresent(SavedTrace t) => IsFresh(t) || AnniversaryYears(t) > 0;

        public static bool IsFresh(SavedTrace t)
        {
            if (!RomaniaTime.TryParseUtc(t.createdAt, out var created)) return true; // fără dată: o arătăm
            return TestClock.UtcNow - created < TimeSpan.FromDays(FreshDays);
        }

        /// <summary>Câți ani are urma, dacă e acum în săptămâna aniversării ei; altfel 0.</summary>
        public static int AnniversaryYears(SavedTrace t)
        {
            if (!RomaniaTime.TryParseUtc(t.createdAt, out var created)) return 0;
            var day = RomaniaTime.FromUtc(created).Date;
            var today = RomaniaTime.Today;
            for (int k = 1; k <= 30; k++)
            {
                var start = day.AddYears(k);
                if (start > today) break;
                if (today < start.AddDays(AnniversaryDays)) return k;
            }
            return 0;
        }

        /// <summary>
        /// Urma e în capsula unui an deja încheiat (sigilată de la 1 ianuarie): nu se mai scoate din aplicație,
        /// ca la capsulele sigilate. Rămân ștergerea contului și adminul.
        /// </summary>
        public static bool LockedInYearCapsule(SavedTrace t)
        {
            if (!t.yearCapsule || !RomaniaTime.TryParseUtc(t.createdAt, out var created)) return false;
            return RomaniaTime.FromUtc(created).Year < RomaniaTime.Today.Year;
        }

        /// <summary>„Acum un an, aici” / „Acum 3 ani, aici”.</summary>
        public static string AnniversaryText(int years) => years == 1 ? "Acum un an, aici" : $"Acum {years} ani, aici";
    }
}
