using Avalonia.Controls;
using Avalonia.Input;
using WeissSchwarzSimulatorTools.App.ViewModels;

namespace WeissSchwarzSimulatorTools.App.Views;

public partial class DetailPaneView : UserControl
{
    public DetailPaneView() => InitializeComponent();

    /// <summary>Escape unpins the card, wherever the focus is in the window.</summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top) top.KeyDown += OnWindowKeyDown;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top) top.KeyDown -= OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsEffectivelyVisible && DataContext is DetailPaneViewModel { Pinned: true } vm) vm.Pinned = false;
    }
}
