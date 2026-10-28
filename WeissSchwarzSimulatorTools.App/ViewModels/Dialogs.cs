// Dialogs drawn over the window (MainWindow shows MainViewModel.Dialog), one at a time.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>A dialog shown over the window. <see cref="Close"/> takes it off the screen.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    /// <summary>Set by the window: removes the dialog from the screen.</summary>
    public Action? Closer { get; set; }

    /// <summary>Whether Escape or a click outside may close it (not while work is running).</summary>
    public virtual bool CanDismiss => true;

    [RelayCommand]
    public void Close() => Closer?.Invoke();
}

/// <summary>Asks before an action that loses work: <paramref name="confirmLabel"/> runs <paramref name="confirmed"/>.</summary>
public sealed partial class ConfirmDialogViewModel(string title, string message, string confirmLabel, Action confirmed, Action? cancelled = null)
    : DialogViewModel
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public string ConfirmLabel { get; } = confirmLabel;

    [RelayCommand]
    private void Confirm()
    {
        Close();
        confirmed();
    }

    [RelayCommand]
    private void Cancel()
    {
        Close();
        cancelled?.Invoke();
    }
}
