using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using ThreeTimes.Media;

namespace ThreeTimes.Data
{
    /// <summary>
    /// Cererile pentru capsulele timpului (backend/README.md). O capsulă vine ca SavedTrace (kind = "CAPSULE"): locul,
    /// data, câte urme are; după deschidere, și urmele din ea (items, kind = "CAPSULE_ITEM"), ca urme obișnuite.
    /// </summary>
    public static class CapsuleApi
    {
        /// <summary>O capsulă, exact ca în răspunsul serverului (cu urmele ei).</summary>
        [Serializable]
        class CapsuleDto
        {
            public string id;
            public double latitude, longitude, altitude;
            public float heightAboveTerrain = 1.2f, accuracyMeters, yawAccuracyDegrees;
            public string createdAt, visibility, unlockAt, openedAt;
            public bool mine, ready, opened, @sealed, collecting;
            public int itemCount, myItems, maxItems;
            public List<SavedTrace> items = new List<SavedTrace>();

            public SavedTrace ToTrace()
            {
                var c = new SavedTrace
                {
                    id = TraceStore.NormalizeId(id),
                    kind = "CAPSULE",
                    latitude = latitude,
                    longitude = longitude,
                    altitude = altitude,
                    heightAboveTerrain = heightAboveTerrain,
                    accuracyMeters = accuracyMeters,
                    yawAccuracyDegrees = yawAccuracyDegrees,
                    createdAt = createdAt,
                    visibility = visibility,
                    unlockAt = unlockAt,
                    mine = mine,
                    ready = ready,
                    opened = opened,
                    openedAt = openedAt,
                    @sealed = @sealed,
                    collecting = collecting,
                    itemCount = itemCount,
                    myItems = myItems,
                    maxItems = maxItems,
                    synced = true,
                    items = new List<SavedTrace>()
                };
                foreach (var item in items ?? new List<SavedTrace>())
                {
                    item.id = TraceStore.NormalizeId(item.id);
                    item.kind = "CAPSULE_ITEM";
                    item.capsuleId = c.id;
                    item.visibility = visibility;
                    item.unlockAt = unlockAt;
                    item.synced = true;
                    c.items.Add(item);
                }
                return c;
            }
        }

        [Serializable]
        class NearbyResponse
        {
            public List<CapsuleDto> capsules = new List<CapsuleDto>();
        }

        /// <summary>Locul capsulei și când se deschide (partea „capsule” la creare).</summary>
        [Serializable]
        class PlaceBody
        {
            public string id;
            public double latitude, longitude, altitude;
            public float heightAboveTerrain, accuracyMeters, yawAccuracyDegrees;
            public string visibility, unlockAt;
        }

        [Serializable]
        class OpenBody
        {
            public double latitude;
            public double longitude;
            public float accuracyMeters;
        }

        [Serializable]
        class ReportBody
        {
            public string reason;
        }

        static string Base => ServerSettings.Url + "/api/v1/capsules";
        static string YearBase => ServerSettings.Url + "/api/v1/year-capsules";

        /// <summary>Capsula anului, ca în răspunsul serverului.</summary>
        [Serializable]
        class YearDto
        {
            public string id, city, createdAt, unlockAt;
            public int year, itemCount, myItems;
            public double latitude, longitude, altitude;
            public float heightAboveTerrain = 2f;
            public bool ready, opened, @sealed, collecting;
            public List<SavedTrace> items = new List<SavedTrace>();
            public List<YearEmotion> emotions = new List<YearEmotion>();

            public SavedTrace ToTrace()
            {
                var c = new SavedTrace
                {
                    id = TraceStore.NormalizeId(id),
                    kind = "YEAR_CAPSULE",
                    city = city,
                    year = year,
                    latitude = latitude,
                    longitude = longitude,
                    altitude = altitude,
                    heightAboveTerrain = heightAboveTerrain,
                    createdAt = createdAt,
                    visibility = "PUBLIC",
                    unlockAt = unlockAt,
                    ready = ready,
                    opened = opened,
                    @sealed = @sealed,
                    collecting = collecting,
                    itemCount = itemCount,
                    myItems = myItems,
                    synced = true,
                    items = new List<SavedTrace>(),
                    yearEmotions = emotions ?? new List<YearEmotion>()
                };
                // Urmele alese din an sunt urme obișnuite (se deschid, se raportează ca orice urmă).
                foreach (var item in items ?? new List<SavedTrace>())
                {
                    item.id = TraceStore.NormalizeId(item.id);
                    item.synced = true;
                    item.yearCapsule = true;   // toate sunt din capsula anului
                    c.items.Add(item);
                }
                return c;
            }
        }

        [Serializable]
        class YearNearbyResponse
        {
            public List<YearDto> capsules = new List<YearDto>();
        }

        /// <summary>Capsulele anului din jur (câte urme au strâns).</summary>
        public static IEnumerator YearNearby(double lat, double lng, float radius, Action<List<SavedTrace>, string> done)
        {
            using var request = UnityWebRequest.Get($"{YearBase}/nearby?lat={F(lat)}&lng={F(lng)}&radius={Mathf.RoundToInt(radius)}");
            TraceApi.Prepare(request);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, TraceApi.ErrorOf(request));
                yield break;
            }
            var list = new List<SavedTrace>();
            try
            {
                var response = JsonUtility.FromJson<YearNearbyResponse>(request.downloadHandler.text);
                foreach (var dto in response?.capsules ?? new List<YearDto>()) list.Add(dto.ToTrace());
            }
            catch (Exception) { /* răspuns ciudat */ }
            done?.Invoke(list, null);
        }

        /// <summary>Deschide capsula anului (după 1 ianuarie, de aproape): emoțiile anului + câteva urme alese.</summary>
        public static IEnumerator YearOpen(string id, double lat, double lng, float accuracy, Action<SavedTrace, string> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                new OpenBody { latitude = lat, longitude = lng, accuracyMeters = accuracy }));
            using var request = new UnityWebRequest($"{YearBase}/{id}/open", "POST", new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            TraceApi.Prepare(request);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, TraceApi.ErrorOf(request));
                yield break;
            }
            SavedTrace opened = null;
            try { opened = JsonUtility.FromJson<YearDto>(request.downloadHandler.text)?.ToTrace(); }
            catch (Exception) { /* răspuns ciudat */ }
            done?.Invoke(opened, opened == null ? "Răspuns neașteptat de la server." : null);
        }

        static string F(double v) => v.ToString("F7", System.Globalization.CultureInfo.InvariantCulture);

        static SavedTrace Parse(string json)
        {
            try { return JsonUtility.FromJson<CapsuleDto>(json)?.ToTrace(); }
            catch (Exception) { return null; }
        }

        public static IEnumerator Nearby(double lat, double lng, float radius, int limit, Action<List<SavedTrace>, string> done) =>
            GetList($"{Base}/nearby?lat={F(lat)}&lng={F(lng)}&radius={Mathf.RoundToInt(radius)}&limit={limit}", done);

        /// <summary>Capsulele mele (create de mine sau cu urmele mele), pentru „Harta mea”.</summary>
        public static IEnumerator Mine(Action<List<SavedTrace>, string> done) => GetList($"{Base}/mine", done);

        static IEnumerator GetList(string url, Action<List<SavedTrace>, string> done)
        {
            using var request = UnityWebRequest.Get(url);
            TraceApi.Prepare(request);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, TraceApi.ErrorOf(request));
                yield break;
            }
            var list = new List<SavedTrace>();
            try
            {
                var response = JsonUtility.FromJson<NearbyResponse>(request.downloadHandler.text);
                foreach (var dto in response?.capsules ?? new List<CapsuleDto>()) list.Add(dto.ToTrace());
            }
            catch (Exception) { /* răspuns ciudat: nicio capsulă */ }
            done?.Invoke(list, null);
        }

        /// <summary>
        /// Creează capsula (în locul `place`) cu prima ei urmă. done(capsula, eroare, refuzată de moderare).
        /// </summary>
        public static IEnumerator Create(SavedTrace place, SavedTrace item, Action<SavedTrace, string, bool> done)
        {
            var body = new PlaceBody
            {
                id = place.id,
                latitude = place.latitude,
                longitude = place.longitude,
                altitude = place.altitude,
                heightAboveTerrain = place.heightAboveTerrain,
                accuracyMeters = place.accuracyMeters,
                yawAccuracyDegrees = place.yawAccuracyDegrees,
                visibility = place.visibility,
                unlockAt = place.unlockAt
            };
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("capsule", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)), "application/json")
            };
            if (!AddItemParts(form, item, out string error))
            {
                done?.Invoke(null, error, false);
                yield break;
            }
            yield return Send(UnityWebRequest.Post(Base, form), done);
        }

        /// <summary>Pune încă o urmă în capsulă (de la cel mult 15 m). done(capsula actualizată, eroare, refuzată).</summary>
        public static IEnumerator AddItem(string capsuleId, SavedTrace item, Action<SavedTrace, string, bool> done)
        {
            var form = new List<IMultipartFormSection>();
            if (!AddItemParts(form, item, out string error))
            {
                done?.Invoke(null, error, false);
                yield break;
            }
            yield return Send(UnityWebRequest.Post($"{Base}/{capsuleId}/items", form), done);
        }

        static bool AddItemParts(List<IMultipartFormSection> form, SavedTrace item, out string error)
        {
            error = null;
            form.Add(new MultipartFormDataSection("item", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(item)), "application/json"));
            if (item.type == "TEXT") return true;
            if (!MediaStore.Exists(item.mediaFile))
            {
                error = "Fișierul urmei lipsește de pe telefon.";
                return false;
            }
            byte[] bytes = File.ReadAllBytes(MediaStore.FullPath(item.mediaFile));
            form.Add(new MultipartFormFileSection("media", bytes, item.mediaFile, item.type == "PHOTO" ? "image/jpeg" : "audio/wav"));
            return true;
        }

        static IEnumerator Send(UnityWebRequest request, Action<SavedTrace, string, bool> done)
        {
            using (request)
            {
                TraceApi.Prepare(request, 30);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    done?.Invoke(null, TraceApi.ErrorOf(request), request.responseCode == 422);
                    yield break;
                }
                var capsule = Parse(request.downloadHandler.text);
                done?.Invoke(capsule, capsule == null ? "Răspuns neașteptat de la server." : null, false);
            }
        }

        /// <summary>Creatorul sigilează capsula: nu mai primește urme.</summary>
        public static IEnumerator Seal(string id, Action<SavedTrace, string> done)
        {
            using var request = new UnityWebRequest($"{Base}/{id}/seal", "POST", new DownloadHandlerBuffer(), null);
            TraceApi.Prepare(request);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, TraceApi.ErrorOf(request));
                yield break;
            }
            var capsule = Parse(request.downloadHandler.text);
            done?.Invoke(capsule, capsule == null ? "Răspuns neașteptat de la server." : null);
        }

        /// <summary>
        /// Încearcă deschiderea: serverul verifică data și locul. done(capsula deschisă, cu urmele ei) sau
        /// done(null, motivul: „Capsula se deschide pe…”, „Mai mergi vreo 20 m…”).
        /// </summary>
        public static IEnumerator Open(string id, double lat, double lng, float accuracy, Action<SavedTrace, string> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                new OpenBody { latitude = lat, longitude = lng, accuracyMeters = accuracy }));
            using var request = new UnityWebRequest($"{Base}/{id}/open", "POST", new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            TraceApi.Prepare(request);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, TraceApi.ErrorOf(request));
                yield break;
            }
            var capsule = Parse(request.downloadHandler.text);
            done?.Invoke(capsule, capsule == null ? "Răspuns neașteptat de la server." : null);
        }

        public static IEnumerator ReportItem(string itemId, string reason, Action<bool, string> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ReportBody { reason = reason }));
            using var request = new UnityWebRequest($"{Base}/items/{itemId}/report", "POST", new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            TraceApi.Prepare(request);
            yield return request.SendWebRequest();
            bool ok = request.result == UnityWebRequest.Result.Success;
            done?.Invoke(ok, ok ? null : TraceApi.ErrorOf(request));
        }

        public static IEnumerator DeleteItem(string itemId, Action<bool, string> done)
        {
            yield return DeleteAt($"{Base}/items/{itemId}", done);
        }

        /// <summary>Scoate toate urmele mele dintr-o capsulă care încă se umple.</summary>
        public static IEnumerator DeleteMyItems(string capsuleId, Action<bool, string> done)
        {
            yield return DeleteAt($"{Base}/{capsuleId}/items/mine", done);
        }

        public static IEnumerator Delete(string capsuleId, Action<bool, string> done)
        {
            yield return DeleteAt($"{Base}/{capsuleId}", done);
        }

        static IEnumerator DeleteAt(string url, Action<bool, string> done)
        {
            using var request = UnityWebRequest.Delete(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            TraceApi.Prepare(request);
            yield return request.SendWebRequest();
            bool ok = request.result == UnityWebRequest.Result.Success || request.responseCode == 404;
            done?.Invoke(ok, ok ? null : TraceApi.ErrorOf(request));
        }
    }
}
