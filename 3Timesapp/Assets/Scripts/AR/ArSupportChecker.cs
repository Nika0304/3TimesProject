using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ThreeTimes.Core;

namespace ThreeTimes.AR
{
    /// <summary>Verifică internetul și dacă telefonul are ARCore (îl instalează dacă lipsește).</summary>
    public static class ArSupportChecker
    {
        public static bool HasInternet =>
            Application.internetReachability != NetworkReachability.NotReachable;

        /// <summary>Întoarce null dacă totul e în regulă, altfel tipul erorii.</summary>
        public static IEnumerator CheckArCore(Action<AppErrorKind?> onDone)
        {
            if (ARSession.state == ARSessionState.None ||
                ARSession.state == ARSessionState.CheckingAvailability)
            {
                yield return ARSession.CheckAvailability();
            }

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                // Deschide Magazin Play pentru Google Play Services for AR.
                yield return ARSession.Install();
            }

            switch (ARSession.state)
            {
                case ARSessionState.Unsupported:
                    onDone?.Invoke(AppErrorKind.ArUnsupported);
                    break;
                case ARSessionState.NeedsInstall:
                case ARSessionState.Installing:
                    onDone?.Invoke(AppErrorKind.ArInstallFailed);
                    break;
                default:
                    onDone?.Invoke(null);
                    break;
            }
        }
    }
}
