using System;
using System.Runtime;
using System.Windows;
using System.Windows.Threading;

namespace Kniip.Infrastructure;

/// <summary>
/// Geeft het geheugen van grote, net losgelaten bitmaps (vastgelegde schermen, gesloten tabbladen) meteen terug.
/// De pixels van een WPF-bitmap staan buiten de .NET-heap en komen pas vrij als de finalizer draait; omdat de
/// .NET-heap zelf klein blijft, gebeurt dat anders pas veel later, en blijft kniip honderden MB vasthouden.
/// </summary>
internal static class MemoryRelief
{
    private static bool _scheduled;

    /// <summary>Ruimt op zodra de app niets meer te doen heeft, dus nadat gesloten vensters echt weg zijn.</summary>
    public static void Schedule()
    {
        if (_scheduled)
        {
            return;
        }

        _scheduled = true;
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Collect);
    }

    private static void Collect()
    {
        _scheduled = false;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
