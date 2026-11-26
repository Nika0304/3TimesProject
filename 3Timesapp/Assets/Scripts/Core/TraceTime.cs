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
        public static int AnniversaryYears(SavedTrace t) =>
            RomaniaTime.TryParseUtc(t.createdAt, out var created) ? AnniversaryOf(created, AnniversaryDays) : 0;

        /// <summary>
        /// Câți ani au trecut, dacă acum e în cele `days` zile ale unei aniversări a momentului (cel mult maxYears);
        /// altfel 0.
        /// </summary>
        static int AnniversaryOf(DateTime utc, int days, int maxYears = 30)
        {
            var day = RomaniaTime.FromUtc(utc).Date;
            var today = RomaniaTime.Today;
            for (int k = 1; k <= maxYears; k++)
            {
                var start = day.AddYears(k);
                if (start > today) break;
                if (today < start.AddDays(days)) return k;
            }
            return 0;
        }

        // ---------------------------------------------------------------- capsulele (decis 26 nov)

        /// <summary>După dată, capsula se mai poate deschide atâtea zile; apoi dispare pentru cine n-a deschis-o.</summary>
        public const int CapsuleOpenDays = 14;
        /// <summary>După deschidere, urmele din ea se văd atâtea zile (și la fiecare aniversare a deschiderii).</summary>
        public const int CapsuleItemsDays = 7;

        /// <summary>N-am deschis-o și au trecut cele două săptămâni de după dată: nu se mai poate deschide.</summary>
        public static bool CapsuleExpired(SavedTrace c)
        {
            if (c.opened || !RomaniaTime.TryParseUtc(c.unlockAt, out var unlock)) return false;
            return TestClock.UtcNow >= unlock.AddDays(CapsuleOpenDays);
        }

        /// <summary>Câte zile mai sunt ca s-o deschizi (după dată), sau -1 dacă nu e cazul.</summary>
        public static int CapsuleDaysLeft(SavedTrace c)
        {
            if (c.opened || !RomaniaTime.TryParseUtc(c.unlockAt, out var unlock)) return -1;
            var now = TestClock.UtcNow;
            if (now < unlock) return -1;
            var left = unlock.AddDays(CapsuleOpenDays) - now;
            return left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalDays);
        }

        /// <summary>
        /// Urmele dintr-o capsulă deschisă se văd o săptămână de când am deschis-o și încă o săptămână la un an de la
        /// deschidere; apoi nu mai apar (decis 26 nov: după aceea se fac oricum alte capsule).
        /// </summary>
        public static bool CapsuleItemsVisible(SavedTrace c)
        {
            if (!c.opened) return false;
            if (!RomaniaTime.TryParseUtc(c.openedAt, out var opened)) return true; // abia deschisă
            return TestClock.UtcNow < opened.AddDays(CapsuleItemsDays) || AnniversaryOf(opened, CapsuleItemsDays, 1) > 0;
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
