// The Encore Deck screen: the linked account's decks on encoredecks.com (24 per page, private and
// unfinished ones included), previewed with the loaded cards and imported into the simulator.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeissSchwarzSimulatorTools.EncoreDecks;
using WeissSchwarzSimulatorTools.Simulator;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>A deck of the account, as the list on the left shows it.</summary>
public sealed record AccountDeckItem(AccountDeck Deck, string Supporting)
{
    public string Name => Deck.Name;
}

public sealed partial class EncoreViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    /// <summary>Names of the simulator's decks, to mark the decks already imported.</summary>
    private HashSet<string> _localNames = [];
    /// <summary>The last request: older answers are dropped (the user moved on).</summary>
    private int _request;

    public EncoreViewModel(MainViewModel main)
    {
        _main = main;
        main.Account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AccountViewModel.Linked) or nameof(AccountViewModel.Restoring)) AccountChanged();
        };
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Screen) && main.IsEncore) Opened();
        };
    }

    public bool AccountPending => _main.Account.Restoring;
    public bool NoAccount => _main.Account.Linked is null && !AccountPending;
    public bool HasAccount => _main.Account.Linked is not null;
    public string AccountName => _main.Account.Linked?.Name ?? "";

    /// <summary>1-based page shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText), nameof(CanGoBack), nameof(CanGoForward))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText), nameof(CanGoForward), nameof(TotalText), nameof(IsEmpty))]
    private DeckPage? _loaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack), nameof(CanGoForward), nameof(CanRefresh), nameof(IsEmpty))]
    private bool _loading;

    /// <summary>The page could not be read: no automatic retry, only the buttons start one.</summary>
    [ObservableProperty]
    private bool _failed;

    public ObservableCollection<AccountDeckItem> Decks { get; } = [];
    public string PageText => $"Page {Loaded?.Page ?? Page} of {Loaded?.TotalPages ?? Page}";
    public string TotalText => Loaded?.TotalDecks.ToString() ?? "";
    public bool CanGoBack => !Loading && Page > 1;
    public bool CanGoForward => !Loading && Page < (Loaded?.TotalPages ?? Page);
    public bool CanRefresh => !Loading;
    public bool IsEmpty => !Loading && Loaded is { Decks.Count: 0 };

    [ObservableProperty]
    private AccountDeckItem? _selected;

    partial void OnSelectedChanged(AccountDeckItem? value)
    {
        if (value is not null && !_selecting) Pick(value.Deck);
    }

    private bool _selecting;

    /// <summary>Deck picked on the left, previewed in the middle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NothingPicked))]
    private AccountDeck? _picked;

    /// <summary>The picked deck's cards, matched to the loaded simulator.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(SavedText))]
    private Deck? _resolved;

    [ObservableProperty]
    private DeckPreviewViewModel? _preview;

    [ObservableProperty]
    private bool _previewing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _importing;

    public bool NothingPicked => Picked is null;
    public bool HasPreview => Resolved is not null;
    public bool CanImport => !Importing;

    public string SavedText => Resolved is null ? "" : Resolved.Date.Trim().Length == 0
        ? "On encoredecks.com"
        : $"On encoredecks.com, saved {Resolved.Date.Trim()}";

    private void AccountChanged()
    {
        OnPropertyChanged(nameof(AccountPending));
        OnPropertyChanged(nameof(NoAccount));
        OnPropertyChanged(nameof(HasAccount));
        OnPropertyChanged(nameof(AccountName));
        if (_main.Account.Linked is null && !AccountPending)
        {
            // Forgets everything: the account is gone.
            _request++;
            Page = 1;
            Loaded = null;
            Failed = false;
            Decks.Clear();
            Picked = null;
            Resolved = null;
            Preview = null;
        }
        else if (_main.IsEncore)
        {
            Opened();
        }
    }

    /// <summary>The screen is shown: reads the list unless it is there (or failed).</summary>
    private void Opened()
    {
        if (_main.Account.Linked is null) return;
        if (Loaded is null && !Loading && !Failed) _ = FetchPage(Page);
        _ = ReadLocalNames();
    }

    private async Task ReadLocalNames()
    {
        var dir = _main.DecksPath;
        try
        {
            _localNames = await Task.Run(() => DeckFiles.List(dir).Where(d => !d.Ai).Select(d => d.Name).ToHashSet());
            RefreshItems();
        }
        catch (Exception)
        {
            // Only used to mark the imported decks.
        }
    }

    /// <summary>Loads the account's <paramref name="page"/>th deck page off the UI thread.</summary>
    private async Task FetchPage(int page)
    {
        if (_main.Account.Linked is not { } account) return;
        var request = ++_request;
        Failed = false;
        Loaded = null;
        Page = page;
        Loading = true;
        try
        {
            var loaded = await Task.Run(() => WeissSchwarzSimulatorTools.EncoreDecks.Decks.FetchAccountDecksAsync(account.Session, page));
            if (request != _request) return;
            Page = Math.Max(1, loaded.Page);
            Loaded = loaded;
            RefreshItems();
        }
        catch (Exception e)
        {
            if (request != _request) return;
            Failed = true;
            Decks.Clear();
            _main.Notify(Notice.FromException("Could not read your decks", e));
        }
        finally
        {
            if (request == _request) Loading = false;
        }
    }

    private void RefreshItems()
    {
        _selecting = true;
        Decks.Clear();
        foreach (var deck in Loaded?.Decks ?? [])
        {
            var details = new List<string> { deck.Date.Trim(), deck.Description };
            if (_localNames.Contains(deck.Name)) details.Add("in the simulator");
            Decks.Add(new AccountDeckItem(deck, string.Join(" · ", details.Where(d => d.Length > 0))));
        }
        Selected = Decks.FirstOrDefault(d => d.Deck.Id == Picked?.Id);
        _selecting = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private Task Refresh() => FetchPage(Page);

    [RelayCommand]
    private Task PreviousPage() => FetchPage(Page - 1);

    [RelayCommand]
    private Task NextPage() => FetchPage(Page + 1);

    /// <summary>Previews <paramref name="deck"/>: its cards are read from encoredecks.com and matched to loaded cards.</summary>
    private async void Pick(AccountDeck deck)
    {
        Picked = deck;
        Resolved = null;
        Preview = null;
        Previewing = true;
        var index = _main.CardIndex;
        try
        {
            var resolved = await Task.Run(async () =>
            {
                var fetched = await WeissSchwarzSimulatorTools.EncoreDecks.Decks.FetchDeckAsync(deck.Id);
                return index.ResolveDeck(fetched.Name, fetched.Date, fetched.Codes);
            });
            if (Picked != deck) return;
            Resolved = resolved;
            Preview = new DeckPreviewViewModel(resolved);
        }
        catch (Exception e)
        {
            if (Picked != deck) return;
            _main.Notify(Notice.FromException("Could not read the deck", e));
            Picked = null;
        }
        finally
        {
            if (Picked == deck || Picked is null) Previewing = false;
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (Picked is not { } picked || Importing) return;
        Importing = true;
        var (index, dir) = (_main.CardIndex, _main.DecksPath);
        try
        {
            var deck = await Task.Run(() => WeissSchwarzSimulatorTools.EncoreDecks.Decks.ImportAsync(WeissSchwarzSimulatorTools.EncoreDecks.Decks.DeckLink(picked.Id), index, dir));
            _main.Notify(new Notice.Info(ImportDialogViewModel.Message(deck)));
            await ReadLocalNames();
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("Import failed", e));
        }
        finally
        {
            Importing = false;
        }
    }

    [RelayCommand]
    private async Task OpenOnSite()
    {
        if (Picked is { } picked) await Shell.OpenUrl(WeissSchwarzSimulatorTools.EncoreDecks.Decks.DeckLink(picked.Id));
    }
}
