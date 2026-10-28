using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeissSchwarzSimulatorTools.App.Controls;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>
/// What the card browser needs from the deck editor to build a deck. Without a picker (the Cards
/// screen), clicks just pin cards in the side pane.
/// </summary>
public interface IPicker
{
    /// <summary>Copies of <paramref name="card"/> in the deck.</summary>
    int Copies(Card card);

    /// <summary>Adds a copy (a click on the card).</summary>
    void Add(Card card);

    /// <summary>Removes a copy (a right-click on the card).</summary>
    void Remove(Card card);

    /// <summary>The deck changed: copies shown on the cards must be updated.</summary>
    event Action? Changed;
}

/// <summary>
/// Side pane showing the hovered card, or the pinned one (click pins, Esc or the pin button
/// unpins). While building a deck, the picker turns it into a panel that also adds and removes copies.
/// </summary>
public sealed partial class DetailPaneViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCard), nameof(Copies), nameof(CopiesText), nameof(InDeck))]
    private Card? _card;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinnedKey), nameof(StateText))]
    private bool _pinned;

    public IPicker? Picker { get; }

    public DetailPaneViewModel(IPicker? picker = null)
    {
        Picker = picker;
        if (picker is not null)
            picker.Changed += () =>
            {
                OnPropertyChanged(nameof(Copies));
                OnPropertyChanged(nameof(CopiesText));
                OnPropertyChanged(nameof(InDeck));
            };
    }

    public bool HasCard => Card is not null;
    public bool IsPicking => Picker is not null;
    public string? PinnedKey => Pinned ? Card?.Key : null;
    public string StateText => Pinned ? "Pinned" : "Preview";
    public int Copies => Card is not null && Picker is not null ? Picker.Copies(Card) : 0;
    public bool InDeck => Copies > 0;
    public string CopiesText => $"×{Copies} in deck";

    public string EmptyText => Picker is not null
        ? "Hover a card to preview it.\nAdd or remove copies from here."
        : "Hover a card to preview it.\nClick to pin it here.";

    public void Clear()
    {
        Card = null;
        Pinned = false;
    }

    public void Hover(Card card)
    {
        if (!Pinned && Card?.Key != card.Key) Card = card;
    }

    /// <summary>Clicking the pinned card again unpins it.</summary>
    public void Click(Card card)
    {
        if (PinnedKey == card.Key)
        {
            Pinned = false;
            return;
        }
        Card = card;
        Pinned = true;
    }

    /// <summary>Shows <paramref name="card"/> pinned (a card clicked in the related cards window).</summary>
    public void Pin(Card card)
    {
        Card = card;
        Pinned = true;
    }

    partial void OnPinnedChanged(bool value) => RelatedCards.Pinned = PinnedKey;

    [RelayCommand]
    private void Unpin() => Pinned = false;

    [RelayCommand]
    private void Add()
    {
        if (Card is not null) Picker?.Add(Card);
    }

    [RelayCommand]
    private void Remove()
    {
        if (Card is not null) Picker?.Remove(Card);
    }

    [RelayCommand]
    private void ShowRelated()
    {
        if (Card is not null) RelatedCards.Open(Card);
    }
}
