using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Kniip.Capture;
using Kniip.Editor;
using Kniip.Resources;
using Kniip.Settings;
using Kniip.Upload;

namespace Kniip.Workflows;

/// <summary>Het onderwerp van een lopende workflow; elke stap vult aan wat hij oplevert.</summary>
internal sealed class WorkflowItem
{
    /// <summary>Het knipsel; null bij een screencast.</summary>
    public BitmapSource? Image { get; set; }

    /// <summary>Alleen bij een vensterknipsel dat nog niet in de editor is geweest: straal van de vensterhoeken, voor de schaduw.</summary>
    public int? CornerRadius { get; set; }

    /// <summary>Screencast: de opname, of na <see cref="MediaExported"/> het bestand in het gekozen formaat.</summary>
    public string? MediaPath { get; set; }

    /// <summary>Formaat van de screencast; null bij een knipsel.</summary>
    public ScreencastFormat? Format { get; set; }

    /// <summary><see cref="MediaPath"/> is al in het gekozen formaat (en ingekort); anders is het nog de ruwe opname.</summary>
    public bool MediaExported { get; set; }

    /// <summary>De ruwe opname, om na afloop op te ruimen; null als het screencastvenster dat al doet.</summary>
    public string? Recording { get; set; }

    public WindowInfo? Window { get; set; }

    /// <summary>Waar een stap Opslaan het bestand neerzette.</summary>
    public string? FilePath { get; set; }

    /// <summary>Tijdelijk bestand voor Openen met en Map openen als er (nog) niet is opgeslagen.</summary>
    public string? TempFile { get; set; }

    public string? Url { get; set; }

    public bool UrlCopied { get; set; }

    public bool IsVideo => Format != null;
}

/// <summary>Voert workflows uit: eerst de opname, daarna de acties op volgorde; Bewerken wacht op de gebruiker.</summary>
internal sealed class WorkflowRunner
{
    private readonly App _app;
    private bool _capturing;

    public WorkflowRunner(App app)
    {
        _app = app;
    }

    private AppSettings Settings => _app.Settings;

    public void Run(WorkflowDefinition definition)
    {
        WorkflowDefinition workflow = definition.Clone();
        List<string> problems = WorkflowValidator.Problems(workflow, Settings.Uploaders);
        if (problems.Count > 0)
        {
            _app.ShowBalloon(workflow.Title, problems[0], error: true);
            return;
        }

        if (_capturing)
        {
            return;
        }

        switch (workflow.Steps[0].Kind)
        {
            case WorkflowStepKind.CaptureRegion:
            case WorkflowStepKind.CaptureWindow:
                bool pickWindow = workflow.Steps[0].Kind == WorkflowStepKind.CaptureWindow;
                _capturing = _app.BeginWorkflowSnip(pickWindow,
                    (image, window, cornerRadius) => Start(workflow, new WorkflowItem { Image = image, Window = window, CornerRadius = cornerRadius }),
                    () => _capturing = false);
                break;
            case WorkflowStepKind.RecordMp4:
            case WorkflowStepKind.RecordGif:
                ScreencastFormat format = workflow.Steps[0].Kind == WorkflowStepKind.RecordGif ? ScreencastFormat.Gif : ScreencastFormat.Mp4;
                if (!_app.BeginWorkflowRecording(format, (path, window) =>
                        Start(workflow, new WorkflowItem { MediaPath = path, Recording = path, Format = format, Window = window })))
                {
                    _app.ShowBalloon(workflow.Title, Strings.Workflow_RecordingBusy, error: true);
                }

                break;
            default:
                BitmapSource? clipboard = ClipboardImageReader.TryRead(includeFiles: true);
                if (clipboard == null)
                {
                    _app.ShowBalloon(workflow.Title, Strings.Workflow_NoClipboardImage, error: true);
                    return;
                }

                Start(workflow, new WorkflowItem { Image = clipboard });
                break;
        }
    }

    private void Start(WorkflowDefinition workflow, WorkflowItem item)
    {
        // Zonder editor direct na de opname krijgt een vensterknipsel hier zijn schaduw, net als in de editor.
        if (item is { Image: { } image, CornerRadius: int radius }
            && (workflow.Steps.Count < 2 || workflow.Steps[1].Kind != WorkflowStepKind.Edit))
        {
            if (Settings.WindowShadow)
            {
                var document = new EditorDocument(image) { CornerRadius = radius };
                document.AddShadow();
                item.Image = document.Image;
            }

            item.CornerRadius = null;
        }

        _ = ContinueAsync(workflow, item, 1);
    }

    private async Task ContinueAsync(WorkflowDefinition workflow, WorkflowItem item, int index)
    {
        for (int i = index; i < workflow.Steps.Count; i++)
        {
            WorkflowStep step = workflow.Steps[i];
            if (step.Kind == WorkflowStepKind.Edit)
            {
                BeginEdit(workflow, item, i);
                return;
            }

            try
            {
                if (!await RunStepAsync(workflow, step, item))
                {
                    Finish(item);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                Finish(item);
                return;
            }
            catch (Exception ex)
            {
                _app.ShowBalloon(string.Format(Strings.Workflow_FailedTitle, workflow.Title),
                    string.Format(Strings.Workflow_StepFailed, step.Describe(Settings.Uploaders), ex.Message), error: true);
                Finish(item);
                return;
            }
        }

        Finish(item);
        if (item.Url is { } url)
        {
            _app.ShowLinkBalloon(workflow.Title, url);
        }
        else
        {
            _app.ShowBalloon(workflow.Title, DoneText(item));
        }
    }

    private static string DoneText(WorkflowItem item)
    {
        return item.FilePath != null
            ? string.Format(Strings.Workflow_DoneSaved, Path.GetFileName(item.FilePath))
            : Strings.Workflow_Done;
    }

    /// <summary>De ruwe opname opruimen als het screencastvenster dat niet doet.</summary>
    private static void Finish(WorkflowItem item)
    {
        if (item.Recording is { } recording)
        {
            item.Recording = null;
            TryDelete(recording);
        }
    }

    private void BeginEdit(WorkflowDefinition workflow, WorkflowItem item, int index)
    {
        List<WorkflowStep> rest = workflow.Steps.Skip(index + 1).ToList();
        string remaining = rest.Count == 0 ? Strings.Workflow_NothingAfterEdit : string.Join(" › ", rest.Select(s => s.Describe(Settings.Uploaders)));
        bool nextIsSave = rest.FirstOrDefault()?.Kind == WorkflowStepKind.Save;
        var continuation = new WorkflowContinuation(workflow.Title, remaining, nextIsSave,
            result =>
            {
                int next = index + 1;
                if (result.SavedPath != null)
                {
                    item.FilePath = result.SavedPath;
                    if (nextIsSave)
                    {
                        next++;
                    }
                }

                if (result.Image != null)
                {
                    item.Image = result.Image;
                    item.CornerRadius = null;
                }

                if (result.MediaPath != null)
                {
                    item.MediaPath = result.MediaPath;
                    item.Format = result.Format ?? item.Format;
                    item.MediaExported = true;
                }

                _ = ContinueAsync(workflow, item, next);
            },
            () => Finish(item));

        if (item.IsVideo)
        {
            // Het screencastvenster ruimt de opname zelf op.
            item.Recording = null;
            _app.OpenWorkflowScreencast(item.MediaPath!, item.Format, item.Window, continuation);
        }
        else
        {
            _app.OpenWorkflowEditor(item.Image!, item.Window, item.CornerRadius, continuation);
        }
    }

    /// <returns>False als de gebruiker de stap annuleert; de workflow stopt dan zonder melding.</returns>
    private async Task<bool> RunStepAsync(WorkflowDefinition workflow, WorkflowStep step, WorkflowItem item)
    {
        switch (step.Kind)
        {
            case WorkflowStepKind.Save:
                return await SaveAsync(step, item);
            case WorkflowStepKind.Copy:
                await CopyAsync(item);
                return true;
            case WorkflowStepKind.Upload:
                await UploadAsync(workflow, step, item);
                return true;
            case WorkflowStepKind.CopyUrl:
                if (item.Url == null)
                {
                    throw new InvalidOperationException(Strings.Workflow_ProblemCopyUrl);
                }

                item.UrlCopied = ClipboardWriter.SetText(item.Url);
                if (!item.UrlCopied)
                {
                    throw new InvalidOperationException(Strings.Common_CopyFailedBusy);
                }

                return true;
            case WorkflowStepKind.OpenWith:
                OpenWith(step, await FileForAsync(item), item.Url);
                return true;
            case WorkflowStepKind.OpenFolder:
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{await FileForAsync(item)}\"") { UseShellExecute = true });
                return true;
            default:
                return true;
        }
    }

    private async Task<bool> SaveAsync(WorkflowStep step, WorkflowItem item)
    {
        if (item.IsVideo)
        {
            ScreencastFormat format = item.Format!.Value;
            string? target = step.AskLocation ? null : VideoTargetOrNull(item);
            target ??= AskVideoTarget(item);
            if (target == null)
            {
                return false;
            }

            string exported = await ExportedAsync(item);
            await Task.Run(() => File.Copy(exported, target, overwrite: true));
            WorkflowActions.AddVideoHistory(format, target, null, target);
            item.FilePath = target;
            return true;
        }

        string? path = step.AskLocation ? null : SnipTargetOrNull(item);
        path ??= AskSnipTarget(item);
        if (path == null)
        {
            return false;
        }

        WorkflowActions.SaveSnip(item.Image!, path);
        item.FilePath = path;
        return true;
    }

    private string? SnipTargetOrNull(WorkflowItem item)
    {
        try
        {
            return WorkflowActions.SnipTarget(Settings, item.Window);
        }
        catch (Exception)
        {
            // Onbruikbare opslagmap: vragen, net als de knop Opslaan in de editor.
            return null;
        }
    }

    private string? VideoTargetOrNull(WorkflowItem item)
    {
        try
        {
            return WorkflowActions.VideoTarget(Settings, item.Window, item.Format!.Value);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? AskSnipTarget(WorkflowItem item)
    {
        string folder = Settings.LastSaveFolder is { } last && Directory.Exists(last)
            ? last
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Editor_SaveDialogTitle,
            Filter = Strings.Editor_SaveDialogFilter,
            FileName = FileNameTemplate.SuggestedName(Settings.SnipFileNameTemplate, WorkflowActions.SnipTemplateWindow(item.Window),
                WorkflowActions.SnipDefaultName),
            DefaultExt = ".png",
            InitialDirectory = folder,
            AddExtension = true
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        Settings.LastSaveFolder = Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }

    private string? AskVideoTarget(WorkflowItem item)
    {
        ScreencastFormat format = item.Format!.Value;
        string folder = Settings.LastVideoFolder is { } last && Directory.Exists(last) ? last : _app.VideoFolder;
        Directory.CreateDirectory(folder);
        bool gif = format == ScreencastFormat.Gif;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Screencast_SaveDialogTitle,
            Filter = gif ? Strings.Screencast_GifFilter : Strings.Screencast_Mp4Filter,
            FileName = FileNameTemplate.SuggestedName(Settings.VideoFileNameTemplate, WorkflowActions.VideoTemplateWindow(item.Window),
                WorkflowActions.VideoDefaultName) + WorkflowActions.VideoExtension(format),
            InitialDirectory = folder,
            AddExtension = true
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        Settings.LastVideoFolder = Path.GetDirectoryName(dialog.FileName);
        return dialog.FileName;
    }

    /// <summary>De screencast in het gekozen formaat; zonder Bewerken eerst de hele opname exporteren.</summary>
    private async Task<string> ExportedAsync(WorkflowItem item)
    {
        if (item.MediaExported)
        {
            return item.MediaPath!;
        }

        ScreencastFormat format = item.Format!.Value;
        string target = Path.Combine(WorkflowActions.NewTempFolder("Workflows"),
            WorkflowActions.VideoDefaultName + WorkflowActions.VideoExtension(format));
        Task export = WorkflowActions.ExportRecordingAsync(item.MediaPath!, target, format, Settings);
        if (format == ScreencastFormat.Gif)
        {
            await NoticeIfSlowAsync(export, AppNameTitle, Strings.Workflow_ExportingGif);
        }

        await export;
        item.MediaPath = target;
        item.MediaExported = true;
        return target;
    }

    private static string AppNameTitle => Infrastructure.AppIdentity.DisplayName;

    private async Task CopyAsync(WorkflowItem item)
    {
        bool copied = item.IsVideo
            ? ClipboardWriter.SetFile(item.FilePath ?? await ExportedAsync(item))
            : ClipboardWriter.SetImage(item.Image!, item.FilePath);
        if (!copied)
        {
            throw new InvalidOperationException(Strings.Common_CopyFailedBusy);
        }
    }

    private async Task UploadAsync(WorkflowDefinition workflow, WorkflowStep step, WorkflowItem item)
    {
        UploadDestination destination = Settings.Uploaders.FirstOrDefault(u => u.Id == step.UploaderId && u.IsConfigured)
                                        ?? Settings.UploaderFor(UploadKind.Media)
                                        ?? throw new UploadException(Strings.Upload_NotConfigured);
        Task<string> upload;
        if (item.IsVideo)
        {
            string file = item.FilePath ?? await ExportedAsync(item);
            upload = WorkflowActions.UploadVideoAsync(destination, file, item.Format!.Value);
        }
        else
        {
            string name = item.FilePath != null ? Path.GetFileName(item.FilePath) : WorkflowActions.SnipDefaultName + ".png";
            upload = WorkflowActions.UploadSnipAsync(destination, item.Image!, name);
        }

        await NoticeIfSlowAsync(upload, workflow.Title, string.Format(Strings.Editor_Uploading, destination.Title));
        item.Url = await upload;
    }

    /// <summary>
    /// Windows toont meldingen van één app na elkaar en elke melding blijft een paar seconden staan; een "bezig"-melding
    /// bij een snelle stap houdt zo de eindmelding op. Daarom alleen melden als de stap na even nog loopt.
    /// </summary>
    private async Task NoticeIfSlowAsync(Task work, string title, string text)
    {
        if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(2))) != work)
        {
            _app.ShowBalloon(title, text);
        }
    }

    /// <summary>Het opgeslagen bestand, anders een tijdelijk bestand, voor een ander programma of Verkenner.</summary>
    private async Task<string> FileForAsync(WorkflowItem item)
    {
        if (item.FilePath != null)
        {
            return item.FilePath;
        }

        if (item.IsVideo)
        {
            return await ExportedAsync(item);
        }

        if (item.TempFile == null)
        {
            string path = Path.Combine(WorkflowActions.NewTempFolder("Workflows"), WorkflowActions.SnipDefaultName + ".png");
            ImageExporter.Save(item.Image!, path);
            item.TempFile = path;
        }

        return item.TempFile;
    }

    private static void OpenWith(WorkflowStep step, string file, string? url)
    {
        string program = Environment.ExpandEnvironmentVariables(step.ProgramPath!.Trim().Trim('"'));
        string arguments = (string.IsNullOrWhiteSpace(step.Arguments) ? "\"%f\"" : step.Arguments)
            .Replace("%f", file, StringComparison.Ordinal)
            .Replace("%u", url ?? string.Empty, StringComparison.Ordinal);
        Process.Start(new ProcessStartInfo(program, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(file) ?? string.Empty
        });
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Blijft in de tijdelijke map staan; die wordt bij de volgende start opgeruimd.
        }
    }
}
