using Baba.Application.Accounting;
using Baba.Application.Importing;
using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Application.Banking;

/// <summary>A bank or cash account with its balance and where its reconciliation stands.</summary>
public sealed record BankAccountDto(
    Guid AccountId,
    string Code,
    string NameAr,
    string NameEn,
    bool IsActive,
    decimal Balance,
    decimal ReconciledBalance,
    int Unreconciled,
    DateOnly? LastStatementDate,
    decimal? LastStatementBalance);

public sealed record StatementLineDto(Guid Id, DateOnly Date, string? Description, string? Reference, decimal Amount, bool Reconciled);

/// <summary>A statement line and the ledger entry that probably is the same thing (same amount, nearly the same day).</summary>
public sealed record SuggestedMatch(Guid StatementLineId, Guid EntryId);

public sealed record ReconciliationView(
    Guid AccountId,
    decimal OpeningBalance,
    DateOnly? LastStatementDate,
    IReadOnlyList<BankEntry> Entries,
    IReadOnlyList<StatementLineDto> StatementLines,
    IReadOnlyList<SuggestedMatch> Suggestions);

public sealed record CompleteReconciliationInput(DateOnly StatementDate, decimal StatementBalance, IReadOnlyList<Guid> EntryIds, IReadOnlyList<Guid>? StatementLineIds);

public sealed record ReconciliationDto(Guid Id, DateOnly StatementDate, decimal StatementBalance, DateTime CompletedAt);

/// <summary>
/// Bank and cash accounts, bank reconciliation and statement import (brief section 10.2). To reconcile, you enter the date and
/// closing balance on the bank's statement and tick the ledger entries that appear on it. The reconciliation can be finished only
/// when the entries ticked, added to what was reconciled before, equal the statement balance exactly.
/// </summary>
public sealed class BankService(
    IAccountStore accounts,
    IReconciliationStore reconciliations,
    ILedgerQuery ledger,
    ITabularReader reader,
    TimeProvider clock)
{
    /// <summary>How many days apart a statement line and a ledger entry may be and still look like the same thing.</summary>
    public const int MatchWindowDays = 10;

    public async Task<IReadOnlyList<BankAccountDto>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        var bank = (await accounts.ListAsync(cancellationToken)).Where(a => a.Role == AccountRole.CashOrBank && a.IsPosting).ToList();
        var totals = (await ledger.TotalsAsync(null, null, cancellationToken)).ToDictionary(t => t.AccountId);
        var summary = await reconciliations.SummaryAsync(cancellationToken);

        var result = new List<BankAccountDto>();
        foreach (var account in bank.OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase))
        {
            var (unreconciled, last) = summary.TryGetValue(account.Id, out var s) ? s : (0, null);
            var total = totals.GetValueOrDefault(account.Id);
            result.Add(new BankAccountDto(
                account.Id, account.Code, account.NameAr, account.NameEn, account.IsActive,
                total is null ? 0 : total.Debit - total.Credit,
                await reconciliations.ReconciledBalanceAsync(account.Id, cancellationToken),
                unreconciled, last?.StatementDate, last?.StatementBalance));
        }

        return result;
    }

    /// <summary>What the reconciliation screen shows: the entries still to tick up to the statement date, and the imported statement lines.</summary>
    public async Task<ReconciliationView> GetReconciliationAsync(Guid accountId, DateOnly statementDate, CancellationToken cancellationToken = default)
    {
        await RequireBankAccountAsync(accountId, cancellationToken);

        var entries = await reconciliations.UnreconciledEntriesAsync(accountId, statementDate, cancellationToken);
        var lines = await reconciliations.StatementLinesAsync(accountId, cancellationToken);
        var open = lines.Where(l => l.ReconciliationId is null).ToList();
        var last = (await reconciliations.ListAsync(accountId, cancellationToken)).OrderByDescending(r => r.StatementDate).FirstOrDefault();

        return new ReconciliationView(
            accountId,
            await reconciliations.ReconciledBalanceAsync(accountId, cancellationToken),
            last?.StatementDate,
            entries,
            lines.OrderBy(l => l.Date).Select(l => new StatementLineDto(l.Id, l.Date, l.Description, l.Reference, l.Amount, l.ReconciliationId is not null)).ToList(),
            Suggest(open, entries));
    }

    /// <summary>
    /// Pairs each open statement line with an entry of the same amount that is no more than <see cref="MatchWindowDays"/> days away,
    /// closest first, using each entry and each line once. These are only suggestions: the person ticks what they agree with.
    /// </summary>
    internal static IReadOnlyList<SuggestedMatch> Suggest(IReadOnlyList<BankStatementLine> lines, IReadOnlyList<BankEntry> entries)
    {
        var candidates = new List<(BankStatementLine Line, BankEntry Entry, int Days)>();
        foreach (var line in lines)
        {
            foreach (var entry in entries.Where(e => e.Amount == line.Amount))
            {
                var days = Math.Abs(line.Date.DayNumber - entry.Date.DayNumber);
                if (days <= MatchWindowDays)
                    candidates.Add((line, entry, days));
            }
        }

        var usedLines = new HashSet<Guid>();
        var usedEntries = new HashSet<Guid>();
        var matches = new List<SuggestedMatch>();
        foreach (var (line, entry, _) in candidates.OrderBy(c => c.Days).ThenBy(c => c.Line.Date).ThenBy(c => c.Entry.Date))
        {
            if (usedLines.Contains(line.Id) || usedEntries.Contains(entry.EntryId))
                continue;
            usedLines.Add(line.Id);
            usedEntries.Add(entry.EntryId);
            matches.Add(new SuggestedMatch(line.Id, entry.EntryId));
        }

        return matches;
    }

    /// <summary>Finishes a reconciliation. The ticked entries and the balance already reconciled must add up to the statement balance.</summary>
    public async Task<ReconciliationDto> CompleteAsync(Guid accountId, CompleteReconciliationInput input, CancellationToken cancellationToken = default)
    {
        await RequireBankAccountAsync(accountId, cancellationToken);

        var entryIds = (input.EntryIds ?? []).Distinct().ToList();
        var issues = new List<ValidationIssue>();
        var entries = (await reconciliations.UnreconciledEntriesAsync(accountId, input.StatementDate, cancellationToken)).ToDictionary(e => e.EntryId);
        if (entryIds.Any(id => !entries.ContainsKey(id)))
            issues.Add(new ValidationIssue("entries", "reconciliation.entry-unavailable"));

        var opening = await reconciliations.ReconciledBalanceAsync(accountId, cancellationToken);
        var ticked = entryIds.Where(entries.ContainsKey).Sum(id => entries[id].Amount);
        if (issues.Count == 0 && opening + ticked != input.StatementBalance)
            issues.Add(new ValidationIssue("statementBalance", "reconciliation.difference"));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        var reconciliation = new BankReconciliation
        {
            AccountId = accountId,
            StatementDate = input.StatementDate,
            StatementBalance = input.StatementBalance,
            CompletedAt = clock.GetUtcNow().UtcDateTime,
        };
        await reconciliations.CompleteAsync(reconciliation, entryIds, (input.StatementLineIds ?? []).Distinct().ToList(), cancellationToken);
        return new ReconciliationDto(reconciliation.Id, reconciliation.StatementDate, reconciliation.StatementBalance, reconciliation.CompletedAt);
    }

    public async Task<IReadOnlyList<ReconciliationDto>> HistoryAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await RequireBankAccountAsync(accountId, cancellationToken);
        return (await reconciliations.ListAsync(accountId, cancellationToken))
            .OrderByDescending(r => r.StatementDate).ThenByDescending(r => r.CompletedAt)
            .Select(r => new ReconciliationDto(r.Id, r.StatementDate, r.StatementBalance, r.CompletedAt))
            .ToList();
    }

    /// <summary>Takes back the latest reconciliation, for when a mistake is found. Its entries can be ticked again.</summary>
    public async Task UndoLastAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await RequireBankAccountAsync(accountId, cancellationToken);
        if (!await reconciliations.UndoLastAsync(accountId, cancellationToken))
            throw new ValidationException([new ValidationIssue("reconciliation", "reconciliation.none-to-undo")]);
    }

    // ---------------------------------------------------------------- Statement import

    /// <summary>
    /// Reads a bank statement from a CSV or Excel file. It needs a Date column and either an Amount column (money in positive, money
    /// out negative) or Debit and Credit columns as the bank prints them (debit = money out, credit = money in). Lines already imported
    /// are skipped, so overlapping statements can be loaded. If any row is wrong, nothing is imported.
    /// </summary>
    public async Task<ImportResult> ImportStatementAsync(Guid accountId, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        await RequireBankAccountAsync(accountId, cancellationToken);

        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            rows = reader.Read(fileName, content);
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or NotSupportedException or IOException)
        {
            return Fail(0, "import.unreadable");
        }

        var headerIndex = -1;
        int date = -1, description = -1, reference = -1, amount = -1, debit = -1, credit = -1;
        for (var i = 0; i < rows.Count && headerIndex < 0; i++)
        {
            date = ImportParsing.FindColumn(rows[i], "date", "transaction date", "value date", "booking date", "التاريخ", "تاريخ");
            if (date < 0)
                continue;
            headerIndex = i;
            description = ImportParsing.FindColumn(rows[i], "description", "details", "narration", "particulars", "memo", "البيان", "الوصف", "التفاصيل");
            reference = ImportParsing.FindColumn(rows[i], "reference", "ref", "cheque", "cheque no", "chq", "المرجع", "رقم المرجع");
            amount = ImportParsing.FindColumn(rows[i], "amount", "المبلغ");
            debit = ImportParsing.FindColumn(rows[i], "debit", "withdrawal", "withdrawals", "paid out", "money out", "مدين", "سحب", "مسحوبات");
            credit = ImportParsing.FindColumn(rows[i], "credit", "deposit", "deposits", "paid in", "money in", "دائن", "إيداع", "ايداع", "مودعات");
        }

        if (headerIndex < 0)
            return Fail(0, "statement.date-column-missing");
        if (amount < 0 && debit < 0 && credit < 0)
            return Fail(headerIndex + 1, "statement.amount-column-missing");

        var issues = new List<ImportIssue>();
        var parsed = new List<BankStatementLine>();
        var batch = Guid.CreateVersion7();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            var day = ImportParsing.ParseDate(ImportParsing.Cell(row, date));
            if (day is null)
            {
                issues.Add(new ImportIssue(i + 1, "statement.date-invalid"));
                continue;
            }

            decimal? signed;
            if (amount >= 0)
            {
                signed = ImportParsing.ParseAmount(ImportParsing.Cell(row, amount));
            }
            else
            {
                var moneyOut = ImportParsing.ParseAmount(ImportParsing.Cell(row, debit));
                var moneyIn = ImportParsing.ParseAmount(ImportParsing.Cell(row, credit));
                signed = moneyOut is null && moneyIn is null ? null : (moneyIn ?? 0) - Math.Abs(moneyOut ?? 0);
            }

            if (signed is null || signed == 0)
            {
                issues.Add(new ImportIssue(i + 1, "statement.amount-invalid"));
                continue;
            }

            parsed.Add(new BankStatementLine
            {
                AccountId = accountId,
                Date = day.Value,
                Description = Clean(ImportParsing.Cell(row, description)),
                Reference = Clean(ImportParsing.Cell(row, reference)),
                Amount = signed.Value,
                BatchId = batch,
            });
        }

        if (issues.Count > 0)
            return new ImportResult(0, 0, issues);
        if (parsed.Count == 0)
            return Fail(0, "statement.empty");

        var existing = (await reconciliations.StatementLinesAsync(accountId, cancellationToken))
            .Select(l => (l.Date, l.AmountScaled, l.Description, l.Reference)).ToHashSet();
        var fresh = parsed.Where(l => existing.Add((l.Date, l.AmountScaled, l.Description, l.Reference))).ToList();
        if (fresh.Count > 0)
            await reconciliations.AddStatementLinesAsync(fresh, cancellationToken);
        return new ImportResult(fresh.Count, parsed.Count - fresh.Count, []);
    }

    /// <summary>Removes the imported statement lines that no reconciliation has used (for example after loading the wrong file).</summary>
    public async Task<int> ClearStatementAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await RequireBankAccountAsync(accountId, cancellationToken);
        return await reconciliations.DeleteOpenStatementLinesAsync(accountId, cancellationToken);
    }

    private async Task RequireBankAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = (await accounts.ListAsync(cancellationToken)).FirstOrDefault(a => a.Id == accountId) ?? throw new NotFoundException("account");
        if (account.Role != AccountRole.CashOrBank)
            throw new ValidationException([new ValidationIssue("account", "bank.account-invalid")]);
    }

    private static ImportResult Fail(int row, string code) => new(0, 0, [new ImportIssue(row, code)]);

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
