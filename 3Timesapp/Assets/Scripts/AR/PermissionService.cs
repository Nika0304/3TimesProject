using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ThreeTimes.AR
{
    /// <summary>Permisiunile de cameră și locație precisă cerute de ARCore Geospatial.</summary>
    public static class PermissionService
    {
        public static bool HasAll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Camera)
                && Permission.HasUserAuthorizedPermission(Permission.FineLocation);
#else
            return true; // în Editor nu există permisiuni
#endif
        }

        /// <summary>Cere permisiunile lipsă și raportează dacă le avem pe toate.</summary>
        public static IEnumerator RequestAll(Action<bool> onDone)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var missing = new List<string>();
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera)) missing.Add(Permission.Camera);
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation)) missing.Add(Permission.FineLocation);

            if (missing.Count > 0)
            {
                int pending = missing.Count;
                bool lostFocus = false;
                bool regainedFocus = false;

                void OnFocusChanged(bool focused)
                {
                    if (!focused) lostFocus = true;
                    else if (lostFocus) regainedFocus = true;
                }

                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => pending--;
                callbacks.PermissionDenied += _ => pending--;

                // Dialogul de sistem pune aplicația în fundal. Când revine în prim-plan,
                // utilizatorul a răspuns, chiar dacă un callback nu a fost apelat
                // (de exemplu la „Nu mai întreba”).
                Application.focusChanged += OnFocusChanged;
                Permission.RequestUserPermissions(missing.ToArray(), callbacks);

                float elapsed = 0f;
                while (pending > 0 && !regainedFocus && elapsed < 120f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                Application.focusChanged -= OnFocusChanged;

                yield return new WaitForSecondsRealtime(0.3f);
            }
#endif
            yield return null;
            onDone?.Invoke(HasAll());
        }
    }
}
