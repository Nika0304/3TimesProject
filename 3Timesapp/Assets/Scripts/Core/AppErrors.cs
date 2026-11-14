namespace ThreeTimes.Core
{
    /// <summary>Ce face butonul principal al ecranului de eroare.</summary>
    public enum ErrorAction
    {
        Retry,
        OpenAppSettings,
        OpenLocationSettings
    }

    public readonly struct ErrorInfo
    {
        public readonly string Title;
        public readonly string Message;
        public readonly string PrimaryLabel;
        public readonly ErrorAction PrimaryAction;
        /// <summary>Null dacă nu există buton secundar.</summary>
        public readonly string SecondaryLabel;

        public ErrorInfo(string title, string message, string primaryLabel, ErrorAction primaryAction, string secondaryLabel = null)
        {
            Title = title;
            Message = message;
            PrimaryLabel = primaryLabel;
            PrimaryAction = primaryAction;
            SecondaryLabel = secondaryLabel;
        }
    }

    /// <summary>Textele ecranelor de eroare, într-un singur loc.</summary>
    public static class AppErrors
    {
        const string Retry = "Reîncearcă";
        const string Back = "Înapoi, fără cameră";

        public static ErrorInfo Describe(AppErrorKind kind)
        {
            switch (kind)
            {
                case AppErrorKind.PermissionsDenied:
                    return new ErrorInfo(
                        "Avem nevoie de cameră și locație",
                        "Fără ele nu putem așeza poveștile peste clădirile reale. Activează-le din setările aplicației, apoi revino aici.",
                        "Deschide setările", ErrorAction.OpenAppSettings, Back);

                case AppErrorKind.LocationOff:
                    return new ErrorInfo(
                        "Locația telefonului e oprită",
                        "Pornește locația (GPS) ca să știm în fața cărei clădiri te afli.",
                        "Deschide setările de locație", ErrorAction.OpenLocationSettings, Back);

                case AppErrorKind.NoInternet:
                    return new ErrorInfo(
                        "Nu ești conectat la internet",
                        "Localizarea precisă folosește serviciile Google. Conectează-te la date mobile sau la Wi-Fi.",
                        Retry, ErrorAction.Retry, Back);

                case AppErrorKind.ArUnsupported:
                    return new ErrorInfo(
                        "Telefonul nu suportă AR",
                        "3Times are nevoie de un telefon compatibil cu Google Play Services for AR (ARCore).",
                        Retry, ErrorAction.Retry, Back);

                case AppErrorKind.ArInstallFailed:
                    return new ErrorInfo(
                        "Lipsește Google Play Services for AR",
                        "Instalează sau actualizează aplicația din Magazin Play, apoi revino aici.",
                        Retry, ErrorAction.Retry, Back);

                case AppErrorKind.GeospatialUnsupported:
                    return new ErrorInfo(
                        "Telefonul nu suportă localizarea după clădiri",
                        "Funcția ARCore Geospatial nu e disponibilă pe acest telefon, așa că nu putem așeza poveștile în locul exact.",
                        Retry, ErrorAction.Retry, Back);

                case AppErrorKind.NotAuthorized:
                    return new ErrorInfo(
                        "Serviciul de localizare nu e autorizat",
                        "Aplicația nu a putut folosi ARCore API. Pentru echipă: verificați cheia API din Project Settings → XR Plug-in Management → ARCore Extensions.",
                        Retry, ErrorAction.Retry, Back);

                default:
                    return new ErrorInfo(
                        "Localizarea a întâmpinat o eroare",
                        "Încearcă din nou peste câteva momente.",
                        Retry, ErrorAction.Retry, Back);
            }
        }
    }
}
