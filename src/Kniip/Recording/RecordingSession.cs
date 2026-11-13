using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using ScreenRecorderLib;

namespace Kniip.Recording;

internal sealed record RecordingOptions(bool SystemAudio, bool Microphone, bool ShowCursor, bool HighlightClicks, int Fps)
{
    public bool HasAudio => SystemAudio || Microphone;
}

/// <summary>Eén opname naar een tijdelijk MP4-bestand. Gebeurtenissen komen binnen op de UI-thread.</summary>
internal sealed class RecordingSession : IDisposable
{
    public static string TempFolder { get; } = Path.Combine(Path.GetTempPath(), Infrastructure.AppIdentity.Key);

    private readonly Dispatcher _dispatcher;
    private readonly RecordingRegion _region;
    private Recorder? _recorder;
    private bool _everRecorded;

    public event Action<string>? Completed;

    /// <summary>Foutmelding en of de opname al gestart was (anders is opnieuw proberen zinvol).</summary>
    public event Action<string, bool>? Failed;

    public RecordingSession(RecordingRegion region, RecordingOptions options, Dispatcher dispatcher)
    {
        _region = region;
        Options = options;
        _dispatcher = dispatcher;
        OutputPath = Path.Combine(TempFolder, $"opname-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mp4");
    }

    public RecordingOptions Options { get; }

    public string OutputPath { get; }

    /// <summary>Gekozen geluidsbron(nen) die niet bestaan op deze pc.</summary>
    public string? MissingAudio { get; private set; }

    public bool IsPaused => _recorder?.Status == RecorderStatus.Paused;

    public void Start()
    {
        Directory.CreateDirectory(TempFolder);
        _recorder = Recorder.CreateRecorder(BuildOptions());
        _recorder.OnStatusChanged += (_, e) =>
        {
            if (e.Status == RecorderStatus.Recording)
            {
                _everRecorded = true;
            }
        };
        _recorder.OnRecordingComplete += (_, e) => _dispatcher.BeginInvoke(() => Completed?.Invoke(e.FilePath));
        _recorder.OnRecordingFailed += (_, e) =>
        {
            bool started = _everRecorded;
            _dispatcher.BeginInvoke(() => Failed?.Invoke(e.Error, started));
        };
        _recorder.Record(OutputPath);
    }

    /// <summary>Verwijdert achtergebleven opnames en klembordkopieën van meer dan een dag oud.</summary>
    public static void CleanupTempFolder()
    {
        try
        {
            if (!Directory.Exists(TempFolder))
            {
                return;
            }

            DateTime limit = DateTime.Now.AddDays(-1);
            foreach (string file in Directory.EnumerateFiles(TempFolder, "*", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                }
            }

            foreach (string folder in Directory.EnumerateDirectories(TempFolder, "*", SearchOption.AllDirectories).Reverse())
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
        }
        catch (Exception)
        {
            // Opruimen is best effort.
        }
    }

    public void Pause() => _recorder?.Pause();

    public void Resume() => _recorder?.Resume();

    public void Stop() => _recorder?.Stop();

    private RecorderOptions BuildOptions()
    {
        System.Drawing.Rectangle rect = _region.MonitorRelative;
        var source = new DisplayRecordingSource(_region.DeviceName)
        {
            SourceRect = new ScreenRect(rect.X, rect.Y, rect.Width, rect.Height),
            IsCursorCaptureEnabled = Options.ShowCursor
        };

        var audioSources = new List<AudioSourceBase>();
        var missing = new List<string>();
        if (Options.SystemAudio)
        {
            AddAudio(audioSources, missing, AudioDevices.DefaultPlaybackId, id => new LoopbackAudioSource(id), "systeemgeluid");
        }

        if (Options.Microphone)
        {
            AddAudio(audioSources, missing, AudioDevices.DefaultMicrophoneId, id => new CaptureAudioSource(id), "microfoon");
        }

        MissingAudio = missing.Count == 0 ? null : string.Join(" en ", missing);

        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = new List<RecordingSourceBase> { source } },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                OutputFrameSize = new ScreenSize(rect.Width, rect.Height),
                Stretch = StretchMode.Uniform
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = audioSources.Count > 0,
                AudioSources = audioSources
            },
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = Options.ShowCursor,
                IsMouseClicksDetected = Options.HighlightClicks,
                MouseClickDetectionMode = MouseDetectionMode.Hook,
                MouseLeftClickDetectionColor = "#FFD400",
                MouseRightClickDetectionColor = "#00B4FF",
                MouseClickDetectionRadius = 22,
                MouseClickDetectionDuration = 200
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.Quality,
                    EncoderProfile = H264Profile.High
                },
                Quality = 80,
                Framerate = Options.Fps,
                IsHardwareEncodingEnabled = true,
                IsMp4FastStartEnabled = true
            }
        };
    }

    private static void AddAudio(List<AudioSourceBase> sources, List<string> missing, string? deviceId,
        Func<string, AudioSourceBase> create, string label)
    {
        if (deviceId == null)
        {
            missing.Add(label);
            return;
        }

        sources.Add(create(deviceId));
    }

    public void Dispose()
    {
        _recorder?.Dispose();
        _recorder = null;
    }
}
