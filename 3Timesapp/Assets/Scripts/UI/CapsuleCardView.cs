using System;
using UnityEngine.UIElements;
using ThreeTimes.Data;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Ecranul unei capsule care nu se poate deschide încă: câte urme are, până când primește urme, când se deschide.
    /// Butoane: „Pune o urmă în capsulă” (cât se mai umple), „Sigilează acum” (creatorul), „Șterge capsula” (creatorul).
    /// </summary>
    public class CapsuleCardView
    {
        readonly VisualElement overlay, card, icon;
        readonly Button dim;
        readonly Label title, count, text;
        readonly Button add, seal, delete, removeMine;
        Action onAdd, onSeal, onDelete, onRemoveMine;

        public bool IsOpen { get; private set; }

        public CapsuleCardView(VisualElement root)
        {
            overlay = root.Q("capsule-overlay");
            card = root.Q("capsule-card");
            dim = root.Q<Button>("capsule-dim");
            icon = root.Q("capsule-card-icon");
            title = root.Q<Label>("capsule-card-title");
            count = root.Q<Label>("capsule-card-count");
            text = root.Q<Label>("capsule-card-text");
            add = root.Q<Button>("capsule-card-add");
            seal = root.Q<Button>("capsule-card-seal");
            delete = root.Q<Button>("capsule-card-delete");
            removeMine = root.Q<Button>("capsule-card-remove-mine");

            dim.clicked += Close;
            root.Q<Button>("capsule-card-close").clicked += Close;
            add.clicked += () => Run(onAdd);
            seal.clicked += () => Run(onSeal);
            delete.clicked += () => Run(onDelete);
            removeMine.clicked += () => Run(onRemoveMine);
        }

        void Run(Action action)
        {
            Close();
            action?.Invoke();
        }

        public void Open(SavedTrace c, Action addItem, Action sealNow, Action deleteCapsule, Action removeMyItems = null)
        {
            onAdd = addItem;
            onSeal = sealNow;
            onDelete = deleteCapsule;
            onRemoveMine = removeMyItems;
            IsOpen = true;

            bool personal = c.visibility == "PERSONAL";
            bool ready = CapsuleText.IsReady(c);
            icon.style.backgroundImage = new StyleBackground(CapsuleIcon.Get(ready));
            title.text = personal ? "Capsula ta personală" : c.mine ? "Capsula ta de grup" : "Capsulă de grup";

            int max = c.maxItems > 0 ? c.maxItems : 10;
            string mineText = c.myItems == 0 ? "" : c.myItems == 1 ? " (una e a ta)" : $" ({c.myItems} sunt ale tale)";
            count.text = c.collecting
                ? $"{c.itemCount} din {max} urme{mineText}"
                : $"{c.itemCount} {(c.itemCount == 1 ? "urmă" : "urme")}{mineText}, sigilată";

            string when = CapsuleText.When(c);
            if (c.collecting)
            {
                text.text = personal
                    ? $"O umpli doar tu. Când ai terminat, sigileaz-o. Se deschide {when}."
                    : c.mine
                        ? $"Oricine trece pe aici își poate pune urma, până o sigilezi tu (cel mult 7 zile). Se deschide {when}, pentru toți."
                        : $"Pune-ți și tu urma. Ce e înăuntru rămâne surpriză până {when}, când se deschide pentru toți.";
            }
            else
            {
                text.text = $"Nu mai primește urme. Se deschide {when}.";
            }

            UIRouter.SetVisible(add, c.collecting && (!personal || c.mine) && addItem != null);
            UIRouter.SetVisible(seal, c.collecting && c.mine && sealNow != null);
            // După sigilare nu se mai schimbă nimic înăuntru: urmele nu se mai scot, iar capsula se poate șterge doar
            // dacă toate urmele din ea sunt ale creatorului.
            UIRouter.SetVisible(removeMine, c.collecting && c.myItems > 0 && !c.mine && removeMyItems != null);
            UIRouter.SetVisible(delete, c.mine && (c.collecting || c.itemCount <= c.myItems) && deleteCapsule != null);

            UIRouter.SetVisible(overlay, true);
            card.schedule.Execute(() =>
            {
                card.RemoveFromClassList("detail-card--closed");
                dim.RemoveFromClassList("sheet-dim--hidden");
            }).StartingIn(30);
        }

        /// <summary>Capsula anului: înainte de 1 ianuarie (câte urme a strâns) sau după deschidere (emoțiile anului).</summary>
        public void OpenYear(SavedTrace y)
        {
            onAdd = onSeal = onDelete = onRemoveMine = null;
            IsOpen = true;
            bool opened = y.opened && !y.@sealed;
            icon.style.backgroundImage = new StyleBackground(CapsuleIcon.Get(CapsuleText.IsReady(y)));
            string city = string.IsNullOrEmpty(y.city) ? "orașul tău" : y.city;
            string mineText = y.myItems == 0 ? "" : y.myItems == 1 ? " (una e a ta)" : $" ({y.myItems} sunt ale tale)";
            string urme = y.itemCount == 1 ? "urmă" : y.itemCount % 100 >= 20 || (y.itemCount % 100 == 0 && y.itemCount > 0) ? "de urme" : "urme";

            if (opened)
            {
                title.text = $"{city} în {y.year}";
                count.text = $"{y.itemCount} {urme}{mineText}";
                var names = new System.Collections.Generic.List<string>();
                foreach (var e in y.yearEmotions ?? new System.Collections.Generic.List<YearEmotion>())
                {
                    if (names.Count == 4) break;
                    names.Add(TraceStyle.LabelOf(e.emotion));
                }
                string felt = names.Count == 0 ? "" : $"Cele mai simțite: {string.Join(", ", names)}. ";
                int shown = y.items?.Count ?? 0;
                text.text = felt + (shown == 0 ? "" : shown == 1 ? "O urmă a ieșit din sferă: atinge-o."
                                                  : $"{shown} urme alese la întâmplare au ieșit din sferă: atinge-le.");
            }
            else
            {
                title.text = $"Capsula anului {y.year}";
                count.text = $"{y.itemCount} {urme} până acum{mineText}";
                text.text = $"Urmele lăsate în {city} în {y.year}, cu bifa „capsula anului”, se strâng aici. " +
                            $"Se deschide {CapsuleText.When(y)}, pentru toți.";
            }
            UIRouter.SetVisible(add, false);
            UIRouter.SetVisible(seal, false);
            UIRouter.SetVisible(delete, false);
            UIRouter.SetVisible(removeMine, false);

            UIRouter.SetVisible(overlay, true);
            card.schedule.Execute(() =>
            {
                card.RemoveFromClassList("detail-card--closed");
                dim.RemoveFromClassList("sheet-dim--hidden");
            }).StartingIn(30);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            card.AddToClassList("detail-card--closed");
            dim.AddToClassList("sheet-dim--hidden");
            overlay.schedule.Execute(() => { if (!IsOpen) UIRouter.SetVisible(overlay, false); }).StartingIn(450);
        }
    }
}
