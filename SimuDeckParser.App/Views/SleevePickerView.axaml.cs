using Avalonia.Controls;
using Avalonia.Input;
using SimuDeckParser.App.ViewModels;

namespace SimuDeckParser.App.Views;

public partial class SleevePickerView : UserControl
{
    public SleevePickerView() => InitializeComponent();

    private void OnAddKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SleevePickerViewModel { CanAdd: true } vm) vm.AddCommand.Execute(null);
    }
}
