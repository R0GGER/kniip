using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Kniip.Update;

/// <summary>Kijkt op de achtergrond of er een nieuwere versie is en meldt dat aan de vensters.</summary>
public sealed class UpdateNotifier
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly DispatcherTimer _timer = new();
    private bool _enabled;
    private bool _checking;

    public UpdateNotifier()
    {
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync();
        };
    }

    /// <summary>Nieuwste versie die nieuwer is dan deze exe; null als er niets nieuws is.</summary>
    public ReleaseInfo? Available { get; private set; }

    public bool IsUpdateAvailable => Available != null;

    /// <summary>Wordt op de UI-thread afgevuurd als <see cref="Available"/> verandert.</summary>
    public event EventHandler? AvailabilityChanged;

    public void Apply(bool enabled)
    {
        if (enabled == _enabled)
        {
            return;
        }

        _enabled = enabled;
        _timer.Stop();
        if (enabled)
        {
            _timer.Interval = FirstCheckDelay;
            _timer.Start();
        }
        else
        {
            Report(null);
        }
    }

    /// <summary>Resultaat van een controle elders (de tab Update), zodat de melding gelijkloopt.</summary>
    public void Report(ReleaseInfo? newest)
    {
        if (newest != null && newest.Version <= UpdateChecker.CurrentVersion)
        {
            newest = null;
        }

        bool changed = Available?.Version != newest?.Version;
        Available = newest;
        if (changed)
        {
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task CheckAsync()
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            IReadOnlyList<ReleaseInfo> releases = await UpdateChecker.GetReleasesAsync();
            if (_enabled)
            {
                Report(releases.FirstOrDefault(r => r.Version > UpdateChecker.CurrentVersion));
            }
        }
        catch (UpdateException)
        {
            // Een mislukte achtergrondcontrole is geen melding waard; de volgende poging komt vanzelf.
        }
        finally
        {
            _checking = false;
        }
    }
}
