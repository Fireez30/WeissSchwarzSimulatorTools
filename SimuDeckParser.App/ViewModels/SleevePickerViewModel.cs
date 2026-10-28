// The sleeve picker: a button showing the deck's sleeve, which opens a popup with every sleeve of
// the simulator as thumbnails, a name filter, and a form to add a new sleeve from a file or a
// link. Used by the deck editor and the deck page.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuDeckParser.Simulator;

namespace SimuDeckParser.App.ViewModels;

public sealed partial class SleeveItemViewModel(Sleeve sleeve, bool selected) : ObservableObject
{
    public Sleeve Sleeve { get; } = sleeve;
    public string Name => SimuDeckParser.Simulator.Sleeve.DisplayName(Sleeve.Name);
    public string Path => Sleeve.Path;
    public bool Selected { get; } = selected;
}

public sealed partial class SleevePickerViewModel : ObservableObject
{
    /// <summary>The simulator's Sleeves folder, where added images are copied.</summary>
    private readonly Func<string> _dir;
    /// <summary>Called with the picked sleeve's file name, empty for none.</summary>
    private readonly Action<string> _picked;
    private List<Sleeve> _sleeves = [];

    public SleevePickerViewModel(Func<string> dir, Action<string> picked)
    {
        _dir = dir;
        _picked = picked;
        Refresh();
    }

    /// <summary>The deck's sleeve: a file name of the Sleeves folder; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(CurrentPath), nameof(HasCurrent), nameof(NoSleeveSelected))]
    private string _current = "";

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = "";

    public ObservableCollection<SleeveItemViewModel> Shown { get; } = [];
    public bool NoMatch => Shown.Count == 0;
    public string NoMatchText => _sleeves.Count == 0 ? "The simulator has no sleeve yet." : $"No sleeve matches \"{Query.Trim()}\"";
    public bool NoSleeveSelected => Current.Trim().Length == 0;

    public string Label => _sleeves.Count == 0 && Current.Trim().Length == 0 ? "No sleeves found"
        : Current.Trim().Length == 0 ? "None"
        : Sleeve.DisplayName(Current.Trim());

    public string? CurrentPath => _sleeves.FirstOrDefault(s => s.Name.Equals(Current.Trim(), StringComparison.OrdinalIgnoreCase))?.Path;
    public bool HasCurrent => CurrentPath is not null;

    /// <summary>Re-reads the sleeve images: some may have been added since the last time.</summary>
    public void Refresh()
    {
        _sleeves = _dir() is { Length: > 0 } dir ? Sleeves.List(dir) : [];
        Refilter();
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(HasCurrent));
    }

    partial void OnQueryChanged(string value) => Refilter();

    partial void OnCurrentChanged(string value) => Refilter();

    partial void OnIsOpenChanged(bool value)
    {
        if (value) Refresh();
        else Query = "";
    }

    private void Refilter()
    {
        Shown.Clear();
        var needle = Query.Trim();
        foreach (var sleeve in _sleeves.Where(s => needle.Length == 0 || s.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            Shown.Add(new SleeveItemViewModel(sleeve, sleeve.Name.Equals(Current.Trim(), StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(NoMatch));
        OnPropertyChanged(nameof(NoMatchText));
    }

    [RelayCommand]
    private void Pick(SleeveItemViewModel item) => Choose(item.Sleeve.Name);

    [RelayCommand]
    private void PickNone() => Choose("");

    private void Choose(string name)
    {
        IsOpen = false;
        _picked(name);
    }

    // Adding a sleeve from a path or a link.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private string _source = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    private bool _adding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddError))]
    private string? _addError;

    public bool HasAddError => AddError is not null;
    public bool CanAdd => !Adding && SleeveSource.Parse(Source) is not null;

    [RelayCommand]
    private async Task Add()
    {
        if (SleeveSource.Parse(Source) is not { } source || Adding) return;
        var dir = _dir();
        if (dir.Length == 0)
        {
            AddError = "The simulator folder is not set.";
            return;
        }
        AddError = null;
        Adding = true;
        try
        {
            var name = Name.Trim();
            var sleeve = await Task.Run(() => Sleeves.AddAsync(dir, source, name));
            Source = "";
            Name = "";
            Refresh();
            Choose(sleeve.Name);
        }
        catch (Exception e)
        {
            AddError = e.Message;
        }
        finally
        {
            Adding = false;
        }
    }
}
