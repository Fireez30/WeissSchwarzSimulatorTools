// The window's state: the screen on display, the loaded cards, the linked account, and what the
// screens have to tell the user (snackbars, error dialogs).
//
// The screens:
// - setup (SetupViewModel): picking the simulator folder, then loading the cards;
// - Cards: the CardBrowserViewModel;
// - Decks (DecksViewModel): the simulator's decks, the deck editor, imports, uploads, PDFs;
// - Encore Deck (EncoreViewModel): the linked Encore Decks account's decks;
// - Damage (DamageViewModel): simulating the damage of a turn of attacks;
// - Settings (this class and AccountViewModel).

using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuDeckParser.App.Controls;
using SimuDeckParser.Simulator;

namespace SimuDeckParser.App.ViewModels;

public enum Screen
{
    Setup,
    Cards,
    Decks,
    Encore,
    Damage,
    Settings,
}

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

public sealed partial class MainViewModel : ObservableObject
{
    public Settings Settings { get; }
    public SetupViewModel Setup { get; } = new();
    public AccountViewModel Account { get; }
    public CardBrowserViewModel Cards { get; } = new("Cards");
    public DecksViewModel Decks { get; }
    public EncoreViewModel Encore { get; }
    public DamageViewModel Damage { get; }

    /// <summary>Every card of the simulator, shared by every screen.</summary>
    public IReadOnlyList<Serie> Series { get; private set; } = [];

    /// <summary>The simulator whose cards are loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SimulatorPath), nameof(DecksPath), nameof(DecksFolderText))]
    private SimulatorFolder? _folder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup), nameof(IsCards), nameof(IsDecks), nameof(IsEncore), nameof(IsDamage), nameof(IsSettings), nameof(IsMain))]
    private Screen _screen = Screen.Setup;

    public bool IsSetup => Screen == Screen.Setup;
    public bool IsMain => Screen != Screen.Setup;
    public bool IsCards => Screen == Screen.Cards;
    public bool IsDecks => Screen == Screen.Decks;
    public bool IsEncore => Screen == Screen.Encore;
    public bool IsDamage => Screen == Screen.Damage;
    public bool IsSettings => Screen == Screen.Settings;

    /// <summary>Screen to open once the simulator is loaded.</summary>
    private readonly Screen _startScreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme))]
    private ThemeChoice _theme;

    public bool IsSystemTheme => Theme == ThemeChoice.System;
    public bool IsLightTheme => Theme == ThemeChoice.Light;
    public bool IsDarkTheme => Theme == ThemeChoice.Dark;

    /// <summary>Last serie written to the settings.</summary>
    private string? _savedSerie;

    public MainViewModel(Settings settings)
    {
        Settings = settings;
        Account = new AccountViewModel(this);
        Decks = new DecksViewModel(this);
        Encore = new EncoreViewModel(this);
        Damage = new DamageViewModel(this);
        Theme = settings.Get(SimuDeckParser.Settings.Theme) switch
        {
            "light" => ThemeChoice.Light,
            "dark" => ThemeChoice.Dark,
            _ => ThemeChoice.System,
        };
        ApplyTheme();
        _startScreen = settings.Get(SimuDeckParser.Settings.StartScreen) switch
        {
            "decks" => Screen.Decks,
            "damage" => Screen.Damage,
            _ => Screen.Cards,
        };
        Decks.Sort = settings.Get(SimuDeckParser.Settings.DeckSort) == "date" ? DeckSort.Date : DeckSort.Name;
        Cards.PropertyChanged += (_, e) =>
        {
            // Remember the serie for the next start.
            if (e.PropertyName == nameof(CardBrowserViewModel.Serie) && Cards.SerieName is { } name && name != _savedSerie)
            {
                SaveSetting(SimuDeckParser.Settings.Serie, name);
                _savedSerie = name;
            }
        };
        RelatedCards.PinRequested += PinRequested;
        Account.Restore(settings.Get(SimuDeckParser.Settings.EncoreUser));

        Setup.PathInput = settings.Get(SimuDeckParser.Settings.SimulatorPath) ?? "";
        if (Setup.Resolved is not null) LoadSimulator(save: false);
        else Setup.StartDetecting();
    }

    // Notices: what the screens have to say.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnackbar))]
    private string? _snackbar;

    public bool HasSnackbar => Snackbar is not null;
    private DispatcherTimer? _snackbarTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorDialog))]
    private Notice.Error? _errorDialog;

    public bool HasErrorDialog => ErrorDialog is not null;

    public void Notify(Notice notice)
    {
        switch (notice)
        {
            case Notice.Info info:
                Snackbar = info.Text;
                _snackbarTimer?.Stop();
                _snackbarTimer = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Normal, (_, _) =>
                {
                    _snackbarTimer?.Stop();
                    Snackbar = null;
                });
                _snackbarTimer.Start();
                break;
            case Notice.Error error:
                if (error.SessionExpired) _ = Account.Unlink();
                ErrorDialog = error;
                break;
        }
    }

    [RelayCommand]
    private void CloseSnackbar() => Snackbar = null;

    [RelayCommand]
    private void CloseErrorDialog() => ErrorDialog = null;

    /// <summary>Writes a setting, unless it already has this value.</summary>
    public void SaveSetting(string key, string value)
    {
        try
        {
            if (Settings.Get(key) == value) return;
            Settings.Set(key, value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notify(new Notice.Error("Could not save settings", $"{Settings.Path}: {e.Message}"));
        }
    }

    public void DeleteSetting(string key)
    {
        try
        {
            Settings.Delete(key);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notify(new Notice.Error("Could not save settings", e.Message));
        }
    }

    // Navigation.

    [RelayCommand]
    private void Navigate(Screen screen) => SwitchTo(screen);

    public void SwitchTo(Screen screen)
    {
        if (screen == Screen) return;
        if (screen == Screen.Decks && Folder is not null) Decks.Reload();
        if (screen == Screen.Damage && Folder is not null) Damage.Reload();
        if (screen is Screen.Cards or Screen.Decks or Screen.Damage)
            SaveSetting(SimuDeckParser.Settings.StartScreen, screen.ToString().ToLowerInvariant());
        Screen = screen;
    }

    /// <summary>A card clicked in the related cards window: the detail pane on screen shows it.</summary>
    private void PinRequested(Card card)
    {
        switch (Screen)
        {
            case Screen.Cards:
                Cards.Pane.Pin(card);
                break;
            case Screen.Decks when Decks.Editor is { } editor:
                editor.Browser.Pane.Pin(card);
                break;
            default:
                // The Cards screen shows it.
                Cards.OpenSerieOf(card.Key);
                Cards.Pane.Pin(card);
                SwitchTo(Screen.Cards);
                break;
        }
    }

    // Theme.

    [RelayCommand]
    private void SetTheme(ThemeChoice choice)
    {
        Theme = choice;
        ApplyTheme();
        SaveSetting(SimuDeckParser.Settings.Theme, choice.ToString().ToLowerInvariant());
    }

    /// <summary>The rail's sun / moon button: the other theme than the one on screen.</summary>
    [RelayCommand]
    private void ToggleTheme() =>
        SetTheme(Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? ThemeChoice.Light : ThemeChoice.Dark);

    private void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Theme switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }

    // The simulator.

    public string SimulatorPath => Folder?.Root ?? "";
    public string DecksPath => Folder?.Decks ?? "";
    public string SleevesPath => Folder?.Sleeves ?? "";

    public string DecksFolderText => Directory.Exists(DecksPath)
        ? "One text file per deck. Imports are saved here as new files; existing decks are never overwritten."
        : "Not found yet: it is created by the simulator, or by the first import.";

    public string SeriesText => $"{Series.Count} series";
    public string SetsText => $"{Series.Sum(s => s.Sets.Count)} sets";
    public string CardsText => $"{Series.Sum(s => s.Sets.Sum(set => set.Cards.Count))} cards";

    public string AboutText =>
        $"SimuDeckParser {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)} — settings in {Settings.Path}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReload))]
    private bool _loading;

    public bool CanReload => !Loading;

    [RelayCommand]
    private void LoadFromSetup() => LoadSimulator(save: true);

    /// <summary>Starts reading the cards of the folder picked in the form; <paramref name="save"/> remembers it for the next start.</summary>
    public async void LoadSimulator(bool save)
    {
        if (Setup.Resolved is not { } folder)
        {
            Setup.Error = "This is not the simulator folder. Pick the folder that contains CardContent and Decks.";
            return;
        }
        if (Loading) return;
        if (save) SaveSetting(SimuDeckParser.Settings.SimulatorPath, folder.Root);
        Loading = true;
        if (Screen == Screen.Setup) Setup.LoadingPath = folder.Root;
        try
        {
            var series = await Task.Run(folder.LoadSeries);
            var names = await Task.Run(() => new NameIndex(series));
            var reloaded = Folder == folder;
            Series = series;
            CardNames.Index = names;
            Folder = folder;
            _savedSerie = Settings.Get(SimuDeckParser.Settings.Serie);
            Cards.SetSeries(series, _savedSerie);
            Decks.SetSeries(series);
            Damage.SetSeries(series);
            OnPropertyChanged(nameof(SeriesText));
            OnPropertyChanged(nameof(SetsText));
            OnPropertyChanged(nameof(CardsText));
            OnPropertyChanged(nameof(SleevesPath));
            if (reloaded)
            {
                Notify(new Notice.Info($"Reloaded {series.Count} series ({series.Sum(s => s.Sets.Count)} sets)."));
            }
            else
            {
                Screen = Screen.Setup;
                SwitchTo(_startScreen);
            }
        }
        catch (Exception e)
        {
            Setup.Error = $"Could not load {folder.Root}: {e.Message}";
            Screen = Screen.Setup;
        }
        finally
        {
            Loading = false;
            Setup.LoadingPath = null;
        }
    }

    /// <summary>Reads the card data again, e.g. after a simulator update.</summary>
    [RelayCommand]
    private void ReloadCards()
    {
        Setup.PathInput = SimulatorPath;
        LoadSimulator(save: false);
    }

    /// <summary>Forgets the simulator ("Change folder…" in Settings) and goes back to the form.</summary>
    [RelayCommand]
    private void UnloadSimulator()
    {
        DeleteSetting(SimuDeckParser.Settings.SimulatorPath);
        Series = [];
        CardNames.Index = null;
        RelatedCards.Close();
        Folder = null;
        Cards.SetSeries([], null);
        Decks.SetSeries([]);
        Damage.SetSeries([]);
        Screen = Screen.Setup;
        Setup.Revalidate();
        Setup.StartDetecting();
        Pictures.ClearCache();
    }

    [RelayCommand]
    private void PickDetected(DetectedInstall install) => Setup.Pick(install);

    // Dialogs over the window, one at a time.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private DialogViewModel? _dialog;

    public bool HasDialog => Dialog is not null;

    public void ShowDialog(DialogViewModel dialog)
    {
        dialog.Closer = () =>
        {
            if (Dialog == dialog) Dialog = null;
        };
        Dialog = dialog;
    }

    // Quitting over a deck with unsaved changes.

    /// <summary>The user confirmed quitting anyway.</summary>
    public bool Quitting { get; private set; }

    public event Action? QuitConfirmed;

    /// <summary>The window asks to close: false (and a confirmation) when a deck has unsaved changes.</summary>
    public bool CanClose()
    {
        if (Quitting || !Decks.HasUnsavedDeck) return true;
        ShowDialog(new ConfirmDialogViewModel("Quit without saving?", "The deck you are building has changes that are not saved.", "Quit",
            () =>
            {
                Quitting = true;
                QuitConfirmed?.Invoke();
            },
            () => SwitchTo(Screen.Decks)));
        return false;
    }
}
