using UnityEngine;

namespace ThreeTimes.Core
{
    /// <summary>Setări mici păstrate pe telefon între sesiuni.</summary>
    public static class AppPrefs
    {
        const string OnboardingSeenKey = "3times.onboarding_seen";

        public static bool OnboardingSeen
        {
            get => PlayerPrefs.GetInt(OnboardingSeenKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(OnboardingSeenKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(OnboardingSeenKey);
            PlayerPrefs.Save();
        }
    }
}
