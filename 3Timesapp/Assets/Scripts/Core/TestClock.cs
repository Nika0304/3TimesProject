using System;

namespace ThreeTimes.Core
{
    /// <summary>
    /// Ceasul de test (Meniu → Jurnal de test → „Peste un an”): aplicația se poartă ca și cum ar fi peste un an, ca să
    /// vedem urmele la aniversare („acum un an, aici”) și capsula anului deschisă. Ora mutată pleacă și la server
    /// (antetul X-Test-Now), care o folosește doar dacă e pornit cu TEST_CLOCK=true (serverul local).
    /// Ține doar până se închide aplicația (nu se salvează), ca să nu rămână pornit din greșeală.
    /// </summary>
    public static class TestClock
    {
        static int years;

        /// <summary>Se declanșează când se mută ceasul (TraceManager cere din nou urmele și capsulele).</summary>
        public static event Action Changed;

        public static int Years
        {
            get => years;
            set
            {
                if (years == value) return;
                years = value;
                Changed?.Invoke();
            }
        }

        public static bool Active => years != 0;

        /// <summary>Ora „de acum” pentru tot ce ține de timp (ce se vede, ce se poate deschide).</summary>
        public static DateTime UtcNow => years == 0 ? DateTime.UtcNow : DateTime.UtcNow.AddYears(years);
    }
}
