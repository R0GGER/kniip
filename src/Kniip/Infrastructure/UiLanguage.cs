using System.Globalization;
using System.Threading;
using Kniip.Settings;

namespace Kniip.Infrastructure;

/// <summary>
/// Taal van kniip: de keuze uit de instellingen, of bij Automatisch Nederlands als de Windows-weergavetaal
/// Nederlands is en anders Engels. Alleen de UI-cultuur verandert; datums en getallen blijven in de notatie van de gebruiker.
/// </summary>
internal static class UiLanguage
{
    /// <summary>Vastgelegd vóór de eerste <see cref="Apply"/>, die de UI-cultuur van het proces overschrijft.</summary>
    private static readonly bool SystemIsDutch = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl";

    public static bool IsDutch { get; private set; }

    /// <summary>Moet vóór het eerste venster of trayicoon draaien; teksten worden bij het aanmaken gelezen.</summary>
    public static void Apply(AppLanguage language = AppLanguage.System)
    {
        IsDutch = IsDutchFor(language);
        var culture = CultureInfo.GetCultureInfo(IsDutch ? "nl-NL" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    public static bool IsDutchFor(AppLanguage language) => language switch
    {
        AppLanguage.Dutch => true,
        AppLanguage.English => false,
        _ => SystemIsDutch
    };
}
