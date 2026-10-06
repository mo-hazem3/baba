namespace Baba.Domain.Accounting;

/// <summary>
/// One thing wrong with a voucher. <see cref="Field"/> says where (for example <c>lines[2].account</c>, zero-based like the
/// screen's rows) and <see cref="Code"/> is a stable, translatable code such as <c>line.account-required</c>.
/// </summary>
public sealed record PostingIssue(string Field, string Code);

/// <summary>What the posting engine needs to know besides the voucher itself.</summary>
public sealed class PostingContext(
    IReadOnlyDictionary<Guid, Account> accounts,
    Currency baseCurrency,
    Currency voucherCurrency,
    Func<DateOnly, bool> isDateOpen)
{
    public IReadOnlyDictionary<Guid, Account> Accounts { get; } = accounts;

    /// <summary>The company's base currency: the currency reports are in.</summary>
    public Currency BaseCurrency { get; } = baseCurrency;

    /// <summary>The currency the voucher is written in (its minor units decide how many decimals amounts may have).</summary>
    public Currency VoucherCurrency { get; } = voucherCurrency;

    /// <summary>False for dates inside a locked period.</summary>
    public Func<DateOnly, bool> IsDateOpen { get; } = isDateOpen;
}

/// <summary>
/// The heart of the app (brief section 3). Every business document says how it turns into ledger entries, and this is the only
/// code that makes them:
/// <list type="bullet">
/// <item>Payment: debit each line's account, credit the bank or cash account for the total.</item>
/// <item>Receipt: debit the bank or cash account for the total, credit each line's account.</item>
/// <item>Journal: the lines as written, which must balance.</item>
/// </list>
/// Drafts make no entries. Posting makes them, and editing a posted voucher makes a fresh set. Entries are never edited.
/// </summary>
public static class PostingEngine
{
    /// <summary>What must be true to save a voucher at all (even as a draft): at least one complete line, sensible numbers.</summary>
    public static IReadOnlyList<PostingIssue> ValidateDraft(Voucher voucher, PostingContext context)
    {
        var issues = new List<PostingIssue>();

        if (voucher.Date == default)
            issues.Add(new("date", "date.required"));

        if (voucher.Lines.Count == 0)
            issues.Add(new("lines", "lines.required"));

        ValidateCurrency(voucher, context, issues);

        for (var i = 0; i < voucher.Lines.Count; i++)
            ValidateLine(voucher.Kind, voucher.Lines[i], i, context, issues);

        return issues;
    }

    /// <summary>What must be true to post: a valid draft, balanced, in an open period, with usable accounts.</summary>
    public static IReadOnlyList<PostingIssue> ValidateForPosting(Voucher voucher, PostingContext context)
    {
        var issues = ValidateDraft(voucher, context).ToList();

        if (voucher.Date != default && !context.IsDateOpen(voucher.Date))
            issues.Add(new("date", "date.locked-period"));

        for (var i = 0; i < voucher.Lines.Count; i++)
        {
            if (context.Accounts.TryGetValue(voucher.Lines[i].AccountId, out var account))
            {
                if (!account.IsPosting)
                    issues.Add(new($"lines[{i}].account", "line.account-not-posting"));
                else if (!account.IsActive)
                    issues.Add(new($"lines[{i}].account", "line.account-inactive"));
            }
        }

        if (voucher.Kind is VoucherKind.Payment or VoucherKind.Receipt)
            ValidateCashAccount(voucher, context, issues);

        if (voucher.Kind == VoucherKind.Journal && voucher.Lines.Count > 0)
        {
            if (voucher.Lines.Sum(l => l.DebitScaled) != voucher.Lines.Sum(l => l.CreditScaled))
                issues.Add(new("balance", "balance.unbalanced"));
            else if (issues.Count == 0 && BaseTotals(voucher, context) is var (debit, credit) && debit != credit)
                issues.Add(new("balance", "balance.unbalanced-base"));
        }

        return issues;
    }

    /// <summary>Makes the ledger entries for a voucher that passed <see cref="ValidateForPosting"/>.</summary>
    public static IReadOnlyList<LedgerEntry> GenerateEntries(Voucher voucher, PostingContext context)
    {
        var entries = new List<LedgerEntry>();
        var rate = voucher.ExchangeRate;
        long totalScaled = 0;
        long totalBaseScaled = 0;

        foreach (var line in voucher.Lines.OrderBy(l => l.LineNumber))
        {
            var baseDebit = ToBase(line.DebitScaled, rate, context.BaseCurrency);
            var baseCredit = ToBase(line.CreditScaled, rate, context.BaseCurrency);
            entries.Add(LedgerEntry.Create(
                voucher.CompanyId, voucher.Id, voucher.Date, line.AccountId, line.Description ?? voucher.Memo, entries.Count,
                line.DebitScaled, line.CreditScaled, voucher.CurrencyCode, voucher.ExchangeRateScaled, baseDebit, baseCredit));

            totalScaled += voucher.Kind == VoucherKind.Payment ? line.DebitScaled : line.CreditScaled;
            totalBaseScaled += voucher.Kind == VoucherKind.Payment ? baseDebit : baseCredit;
        }

        // The bank or cash side is the total of the lines, in both currencies, so the voucher always balances exactly.
        if (voucher.Kind == VoucherKind.Payment)
        {
            entries.Add(LedgerEntry.Create(
                voucher.CompanyId, voucher.Id, voucher.Date, voucher.CashAccountId!.Value, voucher.Memo, entries.Count,
                0, totalScaled, voucher.CurrencyCode, voucher.ExchangeRateScaled, 0, totalBaseScaled));
        }
        else if (voucher.Kind == VoucherKind.Receipt)
        {
            entries.Add(LedgerEntry.Create(
                voucher.CompanyId, voucher.Id, voucher.Date, voucher.CashAccountId!.Value, voucher.Memo, entries.Count,
                totalScaled, 0, voucher.CurrencyCode, voucher.ExchangeRateScaled, totalBaseScaled, 0));
        }

        return entries;
    }

    private static void ValidateCurrency(Voucher voucher, PostingContext context, List<PostingIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(voucher.CurrencyCode))
        {
            issues.Add(new("currency", "currency.required"));
            return;
        }

        if (voucher.ExchangeRateScaled <= 0)
            issues.Add(new("exchangeRate", "exchange-rate.invalid"));
        else if (voucher.CurrencyCode == context.BaseCurrency.Code && voucher.ExchangeRateScaled != FxRate.One)
            issues.Add(new("exchangeRate", "exchange-rate.base-must-be-one"));
    }

    private static void ValidateLine(VoucherKind kind, VoucherLine line, int index, PostingContext context, List<PostingIssue> issues)
    {
        string Field(string name) => $"lines[{index}].{name}";

        if (line.AccountId == Guid.Empty)
            issues.Add(new(Field("account"), "line.account-required"));
        else if (!context.Accounts.ContainsKey(line.AccountId))
            issues.Add(new(Field("account"), "line.account-unknown"));

        if (line.DebitScaled < 0 || line.CreditScaled < 0)
        {
            issues.Add(new(Field("amount"), "line.amount-negative"));
            return;
        }

        if (line.DebitScaled == 0 && line.CreditScaled == 0)
        {
            issues.Add(new(Field("amount"), "line.amount-required"));
            return;
        }

        var wrongSide = kind switch
        {
            VoucherKind.Payment => line.CreditScaled != 0,
            VoucherKind.Receipt => line.DebitScaled != 0,
            _ => false,
        };
        if (wrongSide)
            issues.Add(new(Field("amount"), "line.amount-wrong-side"));
        else if (line.DebitScaled != 0 && line.CreditScaled != 0)
            issues.Add(new(Field("amount"), "line.amount-both-sides"));

        // No silent rounding: an amount must fit the currency (2 decimals, or 3 for dinars).
        if (Money.Round(line.Debit, context.VoucherCurrency) != line.Debit || Money.Round(line.Credit, context.VoucherCurrency) != line.Credit)
            issues.Add(new(Field("amount"), "line.amount-decimals"));
    }

    private static void ValidateCashAccount(Voucher voucher, PostingContext context, List<PostingIssue> issues)
    {
        if (voucher.CashAccountId is not { } id || id == Guid.Empty)
        {
            issues.Add(new("cashAccount", "cash-account.required"));
            return;
        }

        if (!context.Accounts.TryGetValue(id, out var account) || account.Role != AccountRole.CashOrBank
            || !account.IsPosting || !account.IsActive)
        {
            issues.Add(new("cashAccount", "cash-account.invalid"));
        }
    }

    private static (long Debit, long Credit) BaseTotals(Voucher voucher, PostingContext context)
    {
        var rate = voucher.ExchangeRate;
        return (
            voucher.Lines.Sum(l => ToBase(l.DebitScaled, rate, context.BaseCurrency)),
            voucher.Lines.Sum(l => ToBase(l.CreditScaled, rate, context.BaseCurrency)));
    }

    /// <summary>An amount in the base currency: converted at the voucher's rate and rounded to the base currency's minor units.</summary>
    private static long ToBase(long scaled, decimal rate, Currency baseCurrency) =>
        scaled == 0 ? 0 : Scaled.ToScaled(Money.Round(Scaled.ToDecimal(scaled) * rate, baseCurrency));
}
