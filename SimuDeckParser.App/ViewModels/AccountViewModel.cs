// The Encore Decks account: the sign-in form of the Settings screen, and the background work
// that links, restores and unlinks the account.
//
// The password only lives in the form until it is sent: it is then cleared from the field. The
// session that sign-in returns is kept in the system credential store (see Credentials).

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuDeckParser.EncoreDecks;

namespace SimuDeckParser.App.ViewModels;

public sealed partial class AccountViewModel(MainViewModel main) : ObservableObject
{
    /// <summary>The account decks are uploaded to; uploads are anonymous without one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinked), nameof(ShowForm), nameof(LinkedText))]
    private Account? _linked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLink))]
    private string _email = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLink))]
    private string _password = "";

    /// <summary>Signing in, then saving the session in the credential store.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLink))]
    private bool _signingIn;

    /// <summary>Reading the saved session at startup: an upload now would wrongly be anonymous.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForm))]
    private bool _restoring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool IsLinked => Linked is not null;
    public bool ShowForm => Linked is null && !Restoring;
    public bool HasError => Error is not null;
    public string LinkedText => Linked is null ? "" : $"Linked as {Linked.Name}";
    public bool CanLink => !SigningIn && Email.Trim().Length > 0 && Password.Length > 0;

    /// <summary>Reads the session of <paramref name="name"/> (the account linked last time) in the background.</summary>
    public async void Restore(string? name)
    {
        if (name is null) return;
        Restoring = true;
        try
        {
            var session = await Task.Run(() => Credentials.LoadSession(name));
            if (session is not null)
            {
                Linked = new Account(name, session);
            }
            else
            {
                // Nothing saved for this account any more: forget its name too.
                main.DeleteSetting(Settings.EncoreUser);
            }
        }
        catch (Exception e)
        {
            Error = $"Could not read the Encore Decks session: {e.Message}. Unlock the system keyring and restart, or link the account again.";
        }
        finally
        {
            Restoring = false;
        }
    }

    /// <summary>Signs in off the UI thread and saves the session in the credential store.</summary>
    [RelayCommand]
    private async Task Link()
    {
        if (!CanLink) return;
        var email = Email.Trim();
        var password = Password;
        Password = "";
        Error = null;
        SigningIn = true;
        try
        {
            var account = await Task.Run(async () =>
            {
                var account = await AccountApi.LoginAsync(email, password);
                try
                {
                    Credentials.SaveSession(account.Name, account.Session);
                }
                catch (Exception)
                {
                    // Not kept anywhere: end the session rather than leave it open.
                    await AccountApi.LogoutAsync(account.Session);
                    throw;
                }
                return account;
            });
            Email = "";
            main.SaveSetting(Settings.EncoreUser, account.Name);
            main.Notify(new Notice.Info($"Linked the Encore Decks account \"{account.Name}\"."));
            Linked = account;
        }
        catch (Exception e)
        {
            Error = $"Could not link the account: {e.Message}";
        }
        finally
        {
            SigningIn = false;
        }
    }

    /// <summary>
    /// Forgets the linked account: signs out of encoredecks.com and removes the session from the
    /// credential store, in the background.
    /// </summary>
    [RelayCommand]
    public async Task Unlink()
    {
        var account = Linked;
        Linked = null;
        main.DeleteSetting(Settings.EncoreUser);
        if (account is null) return;
        try
        {
            await Task.Run(async () =>
            {
                await AccountApi.LogoutAsync(account.Session);
                Credentials.DeleteSession(account.Name);
            });
        }
        catch (Exception e)
        {
            Error = $"Could not unlink completely: {e.Message}";
        }
    }
}
