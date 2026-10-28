using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SimuDeckParser.App.ViewModels;

namespace SimuDeckParser.App.Views;

public partial class DamageView : UserControl
{
    public DamageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            // New results: scroll down to them.
            if (DataContext is DamageViewModel vm)
                vm.ResultsShown += () => Dispatcher.UIThread.Post(() => Results.BringIntoView(), DispatcherPriority.Background);
        };
    }

    private DamageViewModel? Vm => DataContext as DamageViewModel;

    /// <summary>A stage card under the pointer: the detail pane shows it.</summary>
    private void OnSlotCardEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: SlotViewModel { Card: { } card } }) Vm?.Pane.Hover(card);
    }

    private void OnClimaxEntered(object? sender, PointerEventArgs e)
    {
        if (Vm?.Climax.Card is { } card) Vm.Pane.Hover(card);
    }
}
