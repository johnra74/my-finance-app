using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyFinance.App.Services;
using MyFinance.Core.Entities;
using MyFinance.Core.Enums;
using MyFinance.Core.Primitives;
using MyFinance.Data.Services;

namespace MyFinance.App.ViewModels.Dialogs;

/// <summary>An account type paired with the label shown in the picker.</summary>
/// <param name="Type">The underlying type.</param>
/// <param name="Text">How it reads on screen.</param>
public readonly record struct AccountTypeOption(AccountType Type, string Text);

/// <summary>Creates a new account or edits an existing one.</summary>
public sealed partial class AccountEditorViewModel : DialogViewModel
{
    private readonly AccountService _accounts;
    private readonly int? _id;

    private AccountEditorViewModel(AccountService accounts, Account? existing)
    {
        _accounts = accounts;
        _id = existing?.Id;

        Types =
        [
            .. AccountTypeNames.Choosable.Select(t => new AccountTypeOption(t, AccountTypeNames.Describe(t))),
        ];

        if (existing is null)
        {
            _selectedType = Types[0];
            _openedOn = DateTime.Today;
            _openingBalanceText = "0.00";
            return;
        }

        _name = existing.Name;
        _institution = existing.Institution;
        _accountNumberMasked = existing.AccountNumberMasked;
        _openingBalanceText = existing.OpeningBalance.ToString("N", CultureInfo.CurrentCulture);
        _openedOn = existing.OpenedOn?.ToDateTime(TimeOnly.MinValue);
        _notes = existing.Notes;
        _isFavorite = existing.IsFavorite;
        _isClosed = existing.IsClosed;
        _sortOrder = existing.SortOrder;

        AccountTypeOption match = Types.FirstOrDefault(t => t.Type == existing.Type);
        _selectedType = match.Text is null ? Types[0] : match;
    }

    public static AccountEditorViewModel ForNew(AccountService accounts) => new(accounts, null);

    /// <summary>
    /// A new account with what a downloaded statement already told us filled in.
    /// </summary>
    /// <remarks>
    /// The opening balance is deliberately left at zero rather than seeded from the
    /// statement's closing balance. That figure is the position <em>after</em> the file's
    /// transactions, so using it and then importing them would count the whole statement
    /// twice. The file simply does not contain the balance before the period it covers.
    /// </remarks>
    public static AccountEditorViewModel ForNewFromStatement(
        AccountService accounts,
        string? suggestedName,
        string? institution,
        AccountType? type,
        string? maskedNumber,
        string? currencyCode)
    {
        var model = new AccountEditorViewModel(accounts, null)
        {
            Name = suggestedName ?? string.Empty,
            Institution = institution,
            AccountNumberMasked = maskedNumber,
        };

        if (type is AccountType resolved)
        {
            AccountTypeOption match = model.Types.FirstOrDefault(t => t.Type == resolved);
            if (match.Text is not null)
            {
                model.SelectedType = match;
            }
        }

        if (!string.IsNullOrWhiteSpace(currencyCode))
        {
            model._currencyCode = currencyCode.Trim();
        }

        return model;
    }

    public static AccountEditorViewModel ForExisting(AccountService accounts, Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new AccountEditorViewModel(accounts, account);
    }

    public override string Title => _id is null ? "New account" : "Account details";

    public IReadOnlyList<AccountTypeOption> Types { get; }

    /// <summary>
    /// Whether the type picker is live. It is frozen once the account has history, because
    /// changing it would reverse the meaning of the sign on every transaction already there.
    /// </summary>
    [ObservableProperty]
    private bool _canChangeType = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private AccountTypeOption _selectedType;

    [ObservableProperty]
    private string? _institution;

    [ObservableProperty]
    private string? _accountNumberMasked;

    /// <summary>Typed as text so a half-entered amount does not read as zero.</summary>
    [ObservableProperty]
    private string _openingBalanceText = "0.00";

    [ObservableProperty]
    private DateTime? _openedOn;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isClosed;

    private int _sortOrder;

    private string _currencyCode = "USD";

    /// <summary>
    /// Id of the account just saved, so a caller that opened this editor to create one can
    /// carry on with it. Null until Save succeeds.
    /// </summary>
    public int? SavedAccountId { get; private set; }

    /// <summary>Marks the type picker read-only. Set by the caller when history exists.</summary>
    public void FreezeType() => CanChangeType = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (!Money.TryParse(OpeningBalanceText, CultureInfo.CurrentCulture, out Money opening))
        {
            ErrorMessage = "The opening balance is not an amount.";
            return;
        }

        var draft = new AccountDraft
        {
            Id = _id,
            Name = Name,
            Type = SelectedType.Type,
            Institution = Institution,
            AccountNumberMasked = AccountNumberMasked,
            OpeningBalance = opening,
            CurrencyCode = _currencyCode,
            OpenedOn = OpenedOn is DateTime opened ? DateOnly.FromDateTime(opened) : null,
            IsFavorite = IsFavorite,
            IsClosed = IsClosed,
            SortOrder = _sortOrder,
            Notes = Notes,
        };

        IsBusy = true;

        try
        {
            if (_id is null)
            {
                SavedAccountId = await _accounts.CreateAsync(draft).ConfigureAwait(true);
            }
            else
            {
                await _accounts.UpdateAsync(draft).ConfigureAwait(true);
                SavedAccountId = _id;
            }

            Close(true);
        }
        catch (BookValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
