using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using WeissSchwarzSimulatorTools.App.ViewModels;

namespace WeissSchwarzSimulatorTools.App.Views;

public partial class CardBrowserView : UserControl
{
    /// <summary>Shown on the right of the cards: the deck being built, in the deck editor.</summary>
    public static readonly StyledProperty<Control?> SideContentProperty =
        AvaloniaProperty.Register<CardBrowserView, Control?>(nameof(SideContent));

    public Control? SideContent
    {
        get => GetValue(SideContentProperty);
        set => SetValue(SideContentProperty, value);
    }

    public CardBrowserView()
    {
        InitializeComponent();
        // Ctrl+F: the search field.
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Control),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
            {
                Search.Focus();
                Search.SelectAll();
            }),
        });
    }

    private CardBrowserViewModel? Vm => DataContext as CardBrowserViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Ctrl+F works wherever the focus is.
        if (TopLevel.GetTopLevel(this) is { } top) top.KeyDown += OnWindowKeyDown;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top) top.KeyDown -= OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || e.KeyModifiers != KeyModifiers.Control || !IsEffectivelyVisible) return;
        Search.Focus();
        Search.SelectAll();
        e.Handled = true;
    }

    private void OnGridSizeChanged(object? sender, SizeChangedEventArgs e) => Vm?.SetViewportWidth(e.NewSize.Width - 16);

    private void OnSerieFlyoutOpened(object? sender, EventArgs e) => SerieSearch.Focus();

    private void OnSeriePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: Serie serie } list) return;
        list.SelectedItem = null;
        SeriePicker.Flyout?.Hide();
        Vm?.PickSerieCommand.Execute(serie);
    }
}
