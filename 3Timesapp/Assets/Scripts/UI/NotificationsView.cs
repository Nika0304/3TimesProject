using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ThreeTimes.Core;
using ThreeTimes.Data;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Meniu → Notificări: de exemplu „urma ta a fost ștearsă de moderatori”, cu motivul ales de admin.
    /// Câte sunt necitite se vede pe rândul din Meniu și pe butonul de meniu (bulină roșie). O notificare devine
    /// citită când o atingi; „Marchează tot ca citit” le face pe toate citite.
    /// Lista se cere la pornire, la schimbarea contului, la deschiderea Meniului și o dată pe minut.
    /// </summary>
    public class NotificationsView
    {
        const float RefreshSeconds = 60f;

        readonly UIRouter router;
        readonly MonoBehaviour host;
        readonly VisualElement screen, list, menuBadgeDot;
        readonly Label menuBadge, empty;
        readonly Button readAll;

        List<AppNotification> items = new List<AppNotification>();
        bool loading;
        int lastUnread = -1;

        public bool IsOpen => screen != null && !screen.ClassListContains("hidden");

        public NotificationsView(VisualElement root, UIRouter router, MonoBehaviour host)
        {
            this.router = router;
            this.host = host;
            screen = root.Q("screen-notifications");
            list = root.Q("notifications-list");
            empty = root.Q<Label>("notifications-empty");
            readAll = root.Q<Button>("notifications-read-all");
            menuBadge = root.Q<Label>("menu-notifications-badge");
            menuBadgeDot = root.Q("menu-button-badge");

            root.Q<Button>("notifications-back").clicked += Close;
            root.Q<Button>("menu-notifications").clicked += Open;
            readAll.clicked += MarkAllRead;

            AccountSession.Changed += () =>
            {
                items = new List<AppNotification>();
                lastUnread = -1;
                Render();
                Refresh();
            };
            host.StartCoroutine(RefreshLoop());
        }

        public void Open()
        {
            UIRouter.SetVisible(screen, true);
            Render();
            Refresh();
        }

        public void Close() => UIRouter.SetVisible(screen, false);

        /// <summary>Cere din nou lista (de exemplu la deschiderea Meniului).</summary>
        public void Refresh()
        {
            if (loading || !ServerSettings.IsConfigured) return;
            loading = true;
            host.StartCoroutine(NotificationApi.List((list, error) =>
            {
                loading = false;
                if (list == null) return; // fără server: rămâne ce știam
                items = list;
                Render();
            }));
        }

        IEnumerator RefreshLoop()
        {
            var wait = new WaitForSecondsRealtime(RefreshSeconds);
            while (true)
            {
                Refresh();
                yield return wait;
            }
        }

        int Unread()
        {
            int n = 0;
            foreach (var item in items) if (!item.read) n++;
            return n;
        }

        void Render()
        {
            int unread = Unread();
            // Bulinele: pe rândul din Meniu (cu numărul) și pe butonul de meniu.
            menuBadge.text = unread > 9 ? "9+" : unread.ToString();
            UIRouter.SetVisible(menuBadge, unread > 0);
            UIRouter.SetVisible(menuBadgeDot, unread > 0);
            if (lastUnread >= 0 && unread > lastUnread && !IsOpen)
                router.Toast(unread - lastUnread == 1 ? "Ai o notificare nouă: Meniu → Notificări."
                                                      : $"Ai {unread - lastUnread} notificări noi: Meniu → Notificări.", 4f);
            lastUnread = unread;

            if (!IsOpen) return;
            list.Clear();
            UIRouter.SetVisible(empty, items.Count == 0);
            empty.text = ServerSettings.IsConfigured ? "Nu ai notificări." : "Notificările vin de la server: pune adresa lui în Meniu, la „Server”.";
            UIRouter.SetVisible(readAll, unread > 0);
            foreach (var item in items) list.Add(Card(item));
        }

        VisualElement Card(AppNotification item)
        {
            var card = new Button();
            card.AddToClassList("notification");
            card.EnableInClassList("notification--unread", !item.read);
            var head = new VisualElement();
            head.AddToClassList("row");
            head.AddToClassList("notification__head");
            head.pickingMode = PickingMode.Ignore;
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("notification__dot");
            UIRouter.SetVisible(dot, !item.read);
            var title = new Label(item.title) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("notification__title");
            head.Add(dot);
            head.Add(title);
            var body = new Label(item.body) { pickingMode = PickingMode.Ignore };
            body.AddToClassList("notification__body");
            var when = new Label(When(item.createdAt)) { pickingMode = PickingMode.Ignore };
            when.AddToClassList("caption");
            when.AddToClassList("notification__when");
            card.Add(head);
            card.Add(body);
            card.Add(when);
            card.clicked += () =>
            {
                if (item.read) return;
                item.read = true;
                Render();
                host.StartCoroutine(NotificationApi.MarkRead(item.id));
            };
            return card;
        }

        void MarkAllRead()
        {
            foreach (var item in items) item.read = true;
            Render();
            host.StartCoroutine(NotificationApi.MarkRead(null));
        }

        static string When(string iso)
        {
            if (!RomaniaTime.TryParseUtc(iso, out var utc)) return "";
            var local = RomaniaTime.FromUtc(utc);
            return $"{RomaniaTime.DateText(local)}, {local:HH:mm}";
        }
    }
}
