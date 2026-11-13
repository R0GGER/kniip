namespace Kniip.Infrastructure;

/// <summary>
/// Namen waaronder kniip zich in Windows inschrijft. De testbuild (<c>-p:KniipTest=true</c>) gebruikt eigen
/// namen voor register, instellingen, mutex en tijdelijke bestanden, zodat hij naast de geïnstalleerde
/// kniip kan draaien en die nooit overschrijft.
/// </summary>
internal static class AppIdentity
{
#if KNIIP_TEST
    public static bool IsTest => true;
    public const string Key = "KniipTest";
    public const string DisplayName = "kniip test";
#else
    public static bool IsTest => false;
    public const string Key = "Kniip";
    public const string DisplayName = "kniip";
#endif
}
