using System;
using System.Collections;
using UnityEngine;

namespace ThreeTimes.AR
{
    /// <summary>
    /// GPS-ul telefonului. Îl folosim doar ca să aflăm aproximativ unde suntem
    /// (pentru verificarea acoperirii VPS). Poziția precisă vine din Geospatial.
    /// </summary>
    public static class DeviceLocation
    {
        public static bool IsRunning => Input.location.status == LocationServiceStatus.Running;

        public static IEnumerator StartService(float timeoutSeconds, Action<bool> onDone)
        {
            if (!Input.location.isEnabledByUser)
            {
                onDone?.Invoke(false);
                yield break;
            }

            if (!IsRunning)
            {
                Input.location.Start(5f, 1f);
                float elapsed = 0f;
                while (!IsRunning &&
                       Input.location.status != LocationServiceStatus.Failed &&
                       elapsed < timeoutSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            onDone?.Invoke(IsRunning);
        }

        public static void Stop()
        {
            if (Input.location.status != LocationServiceStatus.Stopped)
                Input.location.Stop();
        }
    }
}
