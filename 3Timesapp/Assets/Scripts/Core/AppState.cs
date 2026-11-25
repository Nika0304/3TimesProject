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

    /// <summary>Emoțiile unei urme (prima versiune: trei).</summary>
    public enum Emotion
    {
        Joy,
        Sadness,
        Longing
    }

    /// <summary>Ce completează utilizatorul pe ecranul „Lasă o urmă”.</summary>
    public struct TraceDraft
    {
        /// <summary>TEXT, PHOTO sau VOICE.</summary>
        public string Type;
        /// <summary>Mesajul (la poză și voce, descrierea opțională).</summary>
        public string Text;
        /// <summary>Fișierul pozei sau al înregistrării, deja salvat în media/.</summary>
        public string MediaFile;
        /// <summary>SELFIE, PLACE sau PLACE_WITH_TRACES (doar la poză).</summary>
        public string PhotoSource;
        public float DurationSeconds;
        /// <summary>Emoția (nume liber) și forma ei, propuse de AI sau alese de utilizator.</summary>
        public ThreeTimes.Data.EmotionLook Look;
        public Emotion Emotion;
        /// <summary>0 = încet, 1 = mediu, 2 = puternic.</summary>
        public int Intensity;

        /// <summary>E o capsulă a timpului (modul Viitor), nu o urmă.</summary>
        public bool Capsule;
        /// <summary>PUBLIC sau PERSONAL (doar cu cont).</summary>
        public string Visibility;
        /// <summary>Când se deschide: ISO 8601, UTC.</summary>
        public string UnlockAt;
        /// <summary>Urma se pune într-o capsulă care există deja (id-ul ei); gol = capsulă nouă.</summary>
        public string CapsuleId;
        /// <summary>„Pune-o și în capsula anului” (doar la urme).</summary>
        public bool YearCapsule;
    }
}
