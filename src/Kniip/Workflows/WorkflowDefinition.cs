using System;
using System.Collections.Generic;
using System.Linq;
using Kniip.Resources;
using Kniip.Upload;

namespace Kniip.Workflows;

public enum WorkflowStepKind
{
    // Opnames: alleen als eerste stap.
    CaptureRegion,
    CaptureWindow,
    RecordMp4,
    RecordGif,
    FromClipboard,

    // Acties op het resultaat van de vorige stappen.
    Edit,
    Save,
    Copy,
    Upload,
    CopyUrl,
    OpenWith,
    OpenFolder,

    /// <summary>Het watermerk uit de instellingen op het knipsel of de screencast zetten.</summary>
    Watermark
}

/// <summary>Eén stap in een workflow; welke velden gelden hangt af van <see cref="Kind"/>.</summary>
public sealed class WorkflowStep
{
    public WorkflowStepKind Kind { get; set; }

    /// <summary>Upload: bestemming; leeg betekent de standaardbestemming voor afbeeldingen en video's.</summary>
    public Guid? UploaderId { get; set; }

    /// <summary>Opslaan: elke keer een map en naam vragen in plaats van de ingestelde opslagmap.</summary>
    public bool AskLocation { get; set; }

    /// <summary>Openen met: het programma (exe).</summary>
    public string? ProgramPath { get; set; }

    /// <summary>Openen met: argumenten; %f is het bestand, %u de link van een eerdere upload.</summary>
    public string? Arguments { get; set; } = "\"%f\"";

    public WorkflowStep Clone() => (WorkflowStep)MemberwiseClone();

    public static bool IsCapture(WorkflowStepKind kind) => kind <= WorkflowStepKind.FromClipboard;

    public static bool IsRecording(WorkflowStepKind kind) => kind is WorkflowStepKind.RecordMp4 or WorkflowStepKind.RecordGif;

    public static string Glyph(WorkflowStepKind kind) => kind switch
    {
        WorkflowStepKind.CaptureRegion => "\uF407",
        WorkflowStepKind.CaptureWindow => "\uE737",
        WorkflowStepKind.RecordMp4 => "\uE714",
        WorkflowStepKind.RecordGif => "\uF4A9",
        WorkflowStepKind.FromClipboard => "\uE77F",
        WorkflowStepKind.Edit => "\uE70F",
        WorkflowStepKind.Save => "\uE74E",
        WorkflowStepKind.Copy => "\uE8C8",
        WorkflowStepKind.Upload => "\uE898",
        WorkflowStepKind.CopyUrl => "\uE71B",
        WorkflowStepKind.OpenWith => "\uE7AC",
        WorkflowStepKind.Watermark => "\uE8EC",
        _ => "\uE838"
    };

    public static string Name(WorkflowStepKind kind) => kind switch
    {
        WorkflowStepKind.CaptureRegion => Strings.Workflow_StepCaptureRegion,
        WorkflowStepKind.CaptureWindow => Strings.Workflow_StepCaptureWindow,
        WorkflowStepKind.RecordMp4 => Strings.Workflow_StepRecordMp4,
        WorkflowStepKind.RecordGif => Strings.Workflow_StepRecordGif,
        WorkflowStepKind.FromClipboard => Strings.Workflow_StepFromClipboard,
        WorkflowStepKind.Edit => Strings.Workflow_StepEdit,
        WorkflowStepKind.Save => Strings.Workflow_StepSave,
        WorkflowStepKind.Copy => Strings.Workflow_StepCopy,
        WorkflowStepKind.Upload => Strings.Workflow_StepUpload,
        WorkflowStepKind.CopyUrl => Strings.Workflow_StepCopyUrl,
        WorkflowStepKind.OpenWith => Strings.Workflow_StepOpenWith,
        WorkflowStepKind.Watermark => Strings.Workflow_StepWatermark,
        _ => Strings.Workflow_StepOpenFolder
    };

    /// <summary>Naam met de belangrijkste instelling, voor de stappenlijst en de knop Doorgaan.</summary>
    public string Describe(IReadOnlyList<UploadDestination> uploaders) => Kind switch
    {
        WorkflowStepKind.Upload when UploaderId is Guid id && uploaders.FirstOrDefault(u => u.Id == id) is { } target =>
            string.Format(Strings.Workflow_StepUploadTo, target.Title),
        WorkflowStepKind.Save when AskLocation => Strings.Workflow_StepSaveAs,
        WorkflowStepKind.OpenWith when !string.IsNullOrWhiteSpace(ProgramPath) =>
            string.Format(Strings.Workflow_StepOpenWithNamed, System.IO.Path.GetFileNameWithoutExtension(ProgramPath)),
        _ => Name(Kind)
    };
}

/// <summary>Een eigen workflow: een opname gevolgd door acties, met een eigen naam, icoon en sneltoets.</summary>
public sealed class WorkflowDefinition
{
    public const string DefaultGlyph = "\uE945";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Teken uit Segoe Fluent Icons; geldt als <see cref="IconFile"/> leeg is of niet te laden.</summary>
    public string Glyph { get; set; } = DefaultGlyph;

    /// <summary>Eigen icoon (.png of .ico); leeg betekent <see cref="Glyph"/>.</summary>
    public string? IconFile { get; set; }

    /// <summary>Sneltoets (zie HotkeyGesture); leeg betekent geen sneltoets.</summary>
    public string Hotkey { get; set; } = string.Empty;

    public bool ShowInTray { get; set; } = true;

    public bool ShowInBar { get; set; } = true;

    public List<WorkflowStep> Steps { get; set; } = new();

    public string Title => string.IsNullOrWhiteSpace(Name) ? Strings.Workflow_Untitled : Name.Trim();

    public WorkflowDefinition Clone()
    {
        var copy = (WorkflowDefinition)MemberwiseClone();
        copy.Steps = Steps.Select(s => s.Clone()).ToList();
        return copy;
    }

    /// <summary>De stappen achter elkaar, bijvoorbeeld "Knippen › Opslaan › Uploaden".</summary>
    public string Summary(IReadOnlyList<UploadDestination> uploaders) => string.Join(" › ", Steps.Select(s => s.Describe(uploaders)));

    /// <summary>De voorbeelden bij de eerste start met workflows.</summary>
    public static IEnumerable<WorkflowDefinition> Examples()
    {
        yield return new WorkflowDefinition
        {
            Name = Strings.Workflow_ExampleSaveUpload,
            Glyph = "\uE898",
            Steps =
            {
                new WorkflowStep { Kind = WorkflowStepKind.CaptureRegion },
                new WorkflowStep { Kind = WorkflowStepKind.Save },
                new WorkflowStep { Kind = WorkflowStepKind.Upload },
                new WorkflowStep { Kind = WorkflowStepKind.CopyUrl }
            }
        };
        yield return new WorkflowDefinition
        {
            Name = Strings.Workflow_ExampleEditShare,
            Glyph = "\uE72D",
            Steps =
            {
                new WorkflowStep { Kind = WorkflowStepKind.CaptureRegion },
                new WorkflowStep { Kind = WorkflowStepKind.Edit },
                new WorkflowStep { Kind = WorkflowStepKind.Save },
                new WorkflowStep { Kind = WorkflowStepKind.Upload },
                new WorkflowStep { Kind = WorkflowStepKind.CopyUrl }
            }
        };
    }

    private static readonly string[] ExampleNameKeys = { nameof(Strings.Workflow_ExampleSaveUpload), nameof(Strings.Workflow_ExampleEditShare) };

    private static readonly System.Globalization.CultureInfo[] ExampleCultures =
        { System.Globalization.CultureInfo.GetCultureInfo("nl-NL"), System.Globalization.CultureInfo.GetCultureInfo("en-US") };

    /// <summary>
    /// Zet de naam van een niet-hernoemd voorbeeld in de huidige taal; de naam staat als tekst in de instellingen.
    /// Geeft true als er iets veranderd is.
    /// </summary>
    public static bool LocalizeExampleNames(IEnumerable<WorkflowDefinition> workflows)
    {
        bool changed = false;
        foreach (var workflow in workflows)
        {
            foreach (string key in ExampleNameKeys)
            {
                if (!ExampleCultures.Any(c => Strings.ResourceManager.GetString(key, c) == workflow.Name))
                {
                    continue;
                }

                string? current = Strings.ResourceManager.GetString(key, Strings.Culture);
                if (current != null && current != workflow.Name)
                {
                    workflow.Name = current;
                    changed = true;
                }

                break;
            }
        }

        return changed;
    }
}
