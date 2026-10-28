// The dialogs of the Decks screen: importing from Encore Decks, uploading to it, writing PDFs.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuDeckParser.EncoreDecks;
using SimuDeckParser.Pdf;
using SimuDeckParser.Simulator;


namespace SimuDeckParser.App.ViewModels;

/// <summary>The Import dialog: paste an Encore Decks link, and the deck is saved in the simulator.</summary>
public sealed partial class ImportDialogViewModel(Func<string, Task> import) : DialogViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(Hint), nameof(HintOk), nameof(HintError), nameof(CanImport))]
    private string _link = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport), nameof(CanDismiss), nameof(IsIdle))]
    private bool _running;

    public bool IsIdle => !Running;
    public override bool CanDismiss => !Running;
    public bool IsValid => Decks.DeckIdFromLink(Link) is not null;
    public bool CanImport => IsValid && !Running;
    public bool HintOk => IsValid;
    public bool HintError => !IsValid && Link.Trim().Length > 0;

    public string Hint => Link.Trim().Length == 0 ? "e.g. https://www.encoredecks.com/deck/PPyvcLuvt"
        : IsValid ? "Link looks good"
        : "This is not an Encore Decks deck link";

    [RelayCommand]
    private async Task Import()
    {
        if (!CanImport) return;
        Running = true;
        try
        {
            await import(Link.Trim());
        }
        finally
        {
            Running = false;
            Close();
        }
    }

    /// <summary>What to tell the user once <paramref name="deck"/> is imported.</summary>
    public static string Message(Deck deck)
    {
        var message = $"Imported \"{deck.Name}\" ({deck.Cards.Count} cards) into the simulator decks.";
        if (deck.Unmatched.Count > 0) message += $" {deck.Unmatched.Count} code(s) matched no card.";
        return message;
    }
}

/// <summary>
/// The Upload dialog: publishing a deck on Encore Decks. It asks for confirmation, shows
/// progress, then the new deck's link.
/// </summary>
public sealed partial class UploadDialogViewModel : DialogViewModel
{
    private readonly MainViewModel _main;
    /// <summary>Every code of the deck file, unmatched ones included.</summary>
    private readonly IReadOnlyList<string> _codes;

    public UploadDialogViewModel(MainViewModel main, string name, IReadOnlyList<string> codes)
    {
        _main = main;
        Name = name;
        _codes = codes;
        try
        {
            SimuDeckParser.EncoreDecks.Upload.CheckDeckSize(codes);
        }
        catch (UserException e)
        {
            SizeError = $"Cannot upload: {e.Message}.";
        }
        main.Account.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Destination));
            OnPropertyChanged(nameof(AccountPending));
            OnPropertyChanged(nameof(CanUpload));
        };
    }

    public string Name { get; }
    public string? SizeError { get; }
    public bool HasSizeError => SizeError is not null;
    public bool AccountPending => _main.Account.Restoring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpload), nameof(CanDismiss), nameof(IsIdle))]
    private bool _running;

    /// <summary>Link of the published deck.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDone), nameof(IsAsking), nameof(Title), nameof(DoneText))]
    private string? _done;

    /// <summary>Account the deck was sent to; anonymous when null.</summary>
    private Account? _sentTo;

    public bool IsIdle => !Running;
    public bool IsDone => Done is not null;
    public bool IsAsking => Done is null;
    public override bool CanDismiss => !Running;
    public bool CanUpload => !Running && SizeError is null && !AccountPending;
    public string Title => IsDone ? "Uploaded to Encore Decks" : "Upload to Encore Decks";

    public string Destination =>
        $"\"{Name}\" ({_codes.Count} cards) is published on encoredecks.com " + (_main.Account.Linked is { } account
            ? $"as a new public deck of your account {account.Name}, where you can edit or delete it."
            : "as a new public, anonymous deck: it cannot be edited or deleted there afterwards. Link your Encore Decks account in Settings to upload to it instead.");

    public string DoneText => $"\"{Name}\" is online{(_sentTo is { } a ? $" in the account {a.Name}" : "")}:";

    [RelayCommand]
    private async Task Upload()
    {
        if (!CanUpload) return;
        Running = true;
        _sentTo = _main.Account.Linked;
        var session = _sentTo?.Session;
        try
        {
            Done = await Task.Run(() => SimuDeckParser.EncoreDecks.Upload.UploadAsync(Name, _codes, session));
        }
        catch (Exception e)
        {
            Close();
            _main.Notify(Notice.FromException("Upload failed", e));
        }
        finally
        {
            Running = false;
        }
    }

    [RelayCommand]
    private async Task Open()
    {
        if (Done is not null) await Shell.OpenUrl(Done);
    }

    [RelayCommand]
    private async Task CopyLink()
    {
        if (Done is not null) await Shell.Copy(Done);
    }
}

/// <summary>The two PDFs a deck can be printed as.</summary>
public enum Printout
{
    /// <summary>Every card at real size, to cut out and put in sleeves.</summary>
    Proxies,
    /// <summary>Each different card with its English text.</summary>
    Translation,
}

/// <summary>The PDF dialogs (Proxies, Translation): paper choice, progress, then where the file went.</summary>
public sealed partial class ExportDialogViewModel : DialogViewModel
{
    private readonly MainViewModel _main;
    private readonly Deck _deck;

    public ExportDialogViewModel(MainViewModel main, Printout printout, Deck deck, Paper paper)
    {
        _main = main;
        Printout = printout;
        _deck = deck;
        _paper = paper;
    }

    public Printout Printout { get; }
    public bool IsProxies => Printout == Printout.Proxies;
    public string Title => IsProxies ? "Print proxies" : "Translation sheet";
    public bool IsEmpty => _deck.Cards.Count == 0;

    /// <summary>Last paper picked for a PDF, kept for the next dialog.</summary>
    public static Paper LastPaper { get; set; } = Pdf.Paper.A4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(About), nameof(IsA4), nameof(IsLetter))]
    private Paper _paper;

    public bool IsA4 => Paper == Pdf.Paper.A4;
    public bool IsLetter => Paper == Pdf.Paper.Letter;

    [RelayCommand]
    private void PickA4() => Paper = LastPaper = Pdf.Paper.A4;

    [RelayCommand]
    private void PickLetter() => Paper = LastPaper = Pdf.Paper.Letter;

    public string About
    {
        get
        {
            if (!IsProxies)
                return $"Each different card of \"{_deck.Name}\" with its picture, stats and English effect text, to keep next to the deck while playing.";
            var (cols, rows) = Printouts.ProxyGrid(Paper);
            return $"Every card of \"{_deck.Name}\" ({_deck.Cards.Count} cards) at Weiss Schwarz size (59 × 86 mm), {cols * rows} per page, with crop marks. Print at actual size (100 %), not \"fit to page\".";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanDismiss), nameof(IsIdle))]
    private bool _running;

    /// <summary>The written file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDone), nameof(IsAsking))]
    private string? _done;

    public bool IsIdle => !Running;
    public bool IsDone => Done is not null;
    public bool IsAsking => Done is null;
    public override bool CanDismiss => !Running;
    public bool CanSave => !Running && !IsEmpty;

    /// <summary>Where the PDF of the deck is written: the Downloads folder.</summary>
    private string PdfPath() =>
        Path.Combine(Shell.Downloads(), $"{DeckFiles.FileStemFor(_deck.Name)} - {(IsProxies ? "proxies" : "translation")}.pdf");

    [RelayCommand]
    private async Task Save()
    {
        if (!CanSave) return;
        Running = true;
        var (printout, paper, deck, path) = (Printout, Paper, _deck, PdfPath());
        try
        {
            Done = await Task.Run(() =>
            {
                var bytes = printout == Printout.Proxies ? Printouts.Proxies(deck, paper) : Printouts.Translation(deck, paper);
                Errors.WithContext($"Writing {path}", () => File.WriteAllBytes(path, bytes));
                return path;
            });
        }
        catch (Exception e)
        {
            Close();
            _main.Notify(Notice.FromException("Could not write the PDF", e));
        }
        finally
        {
            Running = false;
        }
    }

    [RelayCommand]
    private async Task Open()
    {
        if (Done is not null && !await Shell.OpenPath(Done))
            _main.Notify(new Notice.Error("Could not open the PDF", $"No program opened {Done}."));
    }

    [RelayCommand]
    private async Task ShowFolder()
    {
        if (Done is not null && Path.GetDirectoryName(Done) is { } dir && !await Shell.OpenPath(dir))
            _main.Notify(new Notice.Error("Could not open the PDF", $"No program opened {dir}."));
    }
}
