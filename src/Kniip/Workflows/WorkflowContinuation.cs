using System;
using System.Windows.Media.Imaging;
using Kniip.Settings;

namespace Kniip.Workflows;

/// <summary>Resultaat van de stap Bewerken: het bewerkte knipsel of de geëxporteerde screencast.</summary>
/// <param name="SavedPath">Al opgeslagen met de knop Opslaan; de volgende stap Opslaan valt dan weg.</param>
internal sealed record WorkflowEditResult(BitmapSource? Image, string? MediaPath, ScreencastFormat? Format, string? SavedPath);

/// <summary>Een workflow die wacht tot de gebruiker in de editor of het screencastvenster op Doorgaan of Opslaan klikt.</summary>
internal sealed class WorkflowContinuation
{
    private Action<WorkflowEditResult>? _continue;
    private Action? _cancel;

    public WorkflowContinuation(string workflowName, string remainingSteps, bool nextIsSave,
        Action<WorkflowEditResult> onContinue, Action onCancel)
    {
        WorkflowName = workflowName;
        RemainingSteps = remainingSteps;
        NextIsSave = nextIsSave;
        _continue = onContinue;
        _cancel = onCancel;
    }

    public string WorkflowName { get; }

    /// <summary>De stappen na Bewerken, bijvoorbeeld "Opslaan › Uploaden".</summary>
    public string RemainingSteps { get; }

    /// <summary>De stap na Bewerken is Opslaan; de gewone knop Opslaan zet de workflow dan ook voort.</summary>
    public bool NextIsSave { get; }

    /// <summary>De workflow heeft een stap Watermerk; het screencastvenster zet het watermerk dan al aan.</summary>
    public bool Watermark { get; init; }

    /// <summary>Doorgegaan of gestopt; verdere aanroepen doen niets.</summary>
    public bool IsDone => _continue == null;

    public void Continue(WorkflowEditResult result)
    {
        Action<WorkflowEditResult>? next = _continue;
        _continue = null;
        _cancel = null;
        next?.Invoke(result);
    }

    public void Cancel()
    {
        Action? cancel = _cancel;
        _continue = null;
        _cancel = null;
        cancel?.Invoke();
    }
}
