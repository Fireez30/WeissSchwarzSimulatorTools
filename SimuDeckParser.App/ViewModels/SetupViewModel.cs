using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SimuDeckParser.Simulator;

namespace SimuDeckParser.App.ViewModels;

/// <summary>A simulator install found on this computer.</summary>
public sealed record DetectedInstall(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Parent => System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>The first screen: asks where the simulator is installed, then loads its cards.</summary>
public sealed partial class SetupViewModel : ObservableObject
{
    /// <summary>The path as typed.</summary>
    [ObservableProperty]
    private string _pathInput = "";

    /// <summary>The simulator install <see cref="PathInput"/> points to, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoad))]
    private SimulatorFolder? _resolved;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _detecting;

    /// <summary>The folder whose cards are being read, while loading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsForm), nameof(CanLoad))]
    private string? _loadingPath;

    public bool IsLoading => LoadingPath is not null;
    public bool IsForm => LoadingPath is null;
    public bool CanLoad => Resolved is not null && !IsLoading;

    private List<string> _detected = [];

    /// <summary>The detected installs, except the one already in the field.</summary>
    public ObservableCollection<DetectedInstall> Detected { get; } = [];
    public bool ShowDetected => Detecting || Detected.Count > 0;

    public string Hint => OperatingSystem.IsWindows() ? @"C:\Games\SimuWeiss" : "/home/me/Games/SimuWeiss";

    partial void OnPathInputChanged(string value) => Revalidate();
    partial void OnErrorChanged(string? value) => RefreshStatus();
    partial void OnDetectingChanged(bool value) => OnPropertyChanged(nameof(ShowDetected));

    public void Revalidate()
    {
        Error = null;
        var trimmed = PathInput.Trim();
        Resolved = trimmed.Length == 0 ? null : SimulatorFolder.Find(trimmed);
        RefreshStatus();
        RefreshDetected();
    }

    /// <summary>Looks for simulator installs in the background, unless it was already done.</summary>
    public async void StartDetecting()
    {
        if (_detected.Count > 0 || Detecting) return;
        Detecting = true;
        try
        {
            _detected = await Task.Run(SimulatorFolder.DetectInstalls);
        }
        catch (Exception)
        {
            _detected = [];
        }
        Detecting = false;
        RefreshDetected();
    }

    private void RefreshDetected()
    {
        Detected.Clear();
        foreach (var path in _detected.Where(p => Resolved?.Root != p))
            Detected.Add(new DetectedInstall(path));
        OnPropertyChanged(nameof(ShowDetected));
    }

    public void Pick(DetectedInstall install) => PathInput = install.Path;

    // The status line under the field: its kind (none, ok, error) and its text.

    public bool StatusOk => Error is null && Resolved is not null;
    public bool StatusError => Error is not null || (Resolved is null && PathInput.Trim().Length > 0);
    public bool StatusNeutral => !StatusOk && !StatusError;

    public string StatusText => (Resolved, Error) switch
    {
        (_, { } e) => e,
        ({ } folder, null) => PathInput.Trim() == folder.Root ? "Simulator found." : $"Simulator found in {folder.Root}",
        (null, null) when PathInput.Trim().Length == 0 => "Paste the folder path, or pick a detected one below.",
        _ => "No CardContent/Cards folder here.",
    };

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusOk));
        OnPropertyChanged(nameof(StatusError));
        OnPropertyChanged(nameof(StatusNeutral));
        OnPropertyChanged(nameof(StatusText));
    }
}
