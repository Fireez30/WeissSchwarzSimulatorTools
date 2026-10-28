using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WeissSchwarzSimulatorTools.App.ViewModels;

namespace WeissSchwarzSimulatorTools.App.Views;

public partial class DeckEditorPanel : UserControl
{
    public DeckEditorPanel()
    {
        InitializeComponent();
        // A new, unnamed deck: the cursor goes to the name field.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is DeckEditorViewModel { Name.Length: 0 })
                Dispatcher.UIThread.Post(() => NameField.Focus(), DispatcherPriority.Background);
        };
    }

    /// <summary>The detail pane shows the card of the row under the pointer.</summary>
    private void OnRowEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: EditorCardViewModel row } && DataContext is DeckEditorViewModel editor)
            editor.HoverCommand.Execute(row.Card);
    }
}
