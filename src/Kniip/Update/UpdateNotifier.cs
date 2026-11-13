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

    private readonly DispatcherTimer _timer = new();
    private TimeSpan _checkInterval = TimeSpan.FromHours(24);
    private bool _enabled;
    private bool _firstCheckDone;
    private bool _checking;

    public UpdateNotifier()
    {
        _timer.Tick += async (_, _) =>
        {
            _firstCheckDone = true;
            _timer.Interval = _checkInterval;
            await CheckAsync();
        };
    }

    /// <summary>Nieuwste versie die nieuwer is dan deze exe; null als er niets nieuws is.</summary>
    public ReleaseInfo? Available { get; private set; }

    public bool IsUpdateAvailable => Available != null;

    /// <summary>Wordt op de UI-thread afgevuurd als <see cref="Available"/> verandert.</summary>
    public event EventHandler? AvailabilityChanged;

    public void Apply(bool enabled, TimeSpan checkInterval)
    {
        if (enabled == _enabled && checkInterval == _checkInterval)
        {
            return;
        }

        bool wasEnabled = _enabled;
        _enabled = enabled;
        _checkInterval = checkInterval;
        if (!enabled)
        {
            _timer.Stop();
            Report(null);
        }
        else if (!wasEnabled)
        {
            _firstCheckDone = false;
            _timer.Stop();
            _timer.Interval = FirstCheckDelay;
            _timer.Start();
        }
        else if (_firstCheckDone)
        {
            // Een nieuw Interval herstart een lopende DispatcherTimer.
            _timer.Interval = checkInterval;
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
