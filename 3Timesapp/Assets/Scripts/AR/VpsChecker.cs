using System;
using System.Collections;
using UnityEngine;
using Google.XR.ARCoreExtensions;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Verifică dacă Google are acoperire VPS (imagini Street View) în locul dat.
    /// Fără VPS, Geospatial se bazează pe GPS și e mai puțin precis.
    /// </summary>
    public static class VpsChecker
    {
        /// <summary>Întoarce null dacă verificarea nu s-a terminat în timp util.</summary>
        public static IEnumerator Check(double latitude, double longitude, float timeoutSeconds,
                                        Action<VpsAvailability?> onDone)
        {
            var promise = AREarthManager.CheckVpsAvailabilityAsync(latitude, longitude);

            float elapsed = 0f;
            while (promise.State == PromiseState.Pending && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (promise.State != PromiseState.Done)
            {
                promise.Cancel();
                onDone?.Invoke(null);
                yield break;
            }

            Debug.Log($"[3Times] VPS la ({latitude:F5}, {longitude:F5}): {promise.Result}");
            onDone?.Invoke(promise.Result);
        }
    }
}
