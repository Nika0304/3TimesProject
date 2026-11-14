namespace ThreeTimes.Core
{
    /// <summary>Stările aplicației, de la deschidere până la ecranul principal.</summary>
    public enum AppState
    {
        Splash,      // numele aplicației, câteva secunde
        Onboarding,  // explicație + cererea permisiunilor (prima rulare)
        Checking,    // internet, ARCore, locație, acoperire VPS
        Localizing,  // așteptăm ca Geospatial să dea o poziție precisă
        Main,        // ecranul principal (vedere AR + cursorul timpului)
        Error        // ecran de eroare cu acțiune de rezolvare
    }

    /// <summary>Cele trei poziții ale cursorului timpului.</summary>
    public enum TimeMode
    {
        Past,
        Present,
        Future
    }

    /// <summary>Motivele pentru care aplicația nu poate continua.</summary>
    public enum AppErrorKind
    {
        PermissionsDenied,
        LocationOff,
        NoInternet,
        ArUnsupported,
        ArInstallFailed,
        GeospatialUnsupported,
        NotAuthorized,
        LocalizationServiceError
    }
}
