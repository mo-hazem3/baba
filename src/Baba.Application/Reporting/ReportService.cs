using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Localization;

namespace Baba.Application.Reporting;

/// <summary>
/// The six core reports (brief section 10.1): trial balance, profit and loss, balance sheet, statement of account, general ledger
/// and journal. Every one is computed from the ledger entries on the spot (balances are never stored), takes a date range or an
/// "as of" date, and lets you drill down: figure, statement of account, voucher.
/// </summary>
public sealed partial class ReportService(
    IAccountStore accounts, IPartyStore parties, ICostCenterStore costCenters, ILedgerQuery ledger, ICompanyFiles files)
{
    // ---------------------------------------------------------------- Trial balance

    public async Task<ReportResult> TrialBalanceAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var tree = new AccountTree(await accounts.ListAsync(cancellationToken));

        var opening = from is { } start
            ? Totals(await ledger.TotalsAsync(null, start.AddDays(-1), cancellationToken))
            : new Dictionary<Guid, (decimal Debit, decimal Credit)>();
        var movement = Totals(await ledger.TotalsAsync(from, to, cancellationToken));

        // Opening and closing are net balances (shown on the debit or the credit side); movement is the gross debits and credits.
        var rolledOpening = tree.Rollup(a => opening.GetValueOrDefault(a.Id).Debit - opening.GetValueOrDefault(a.Id).Credit, (x, y) => x + y, 0m);
        var rolledMovement = tree.Rollup(a => movement.GetValueOrDefault(a.Id), (x, y) => (x.Debit + y.Debit, x.Credit + y.Credit), (Debit: 0m, Credit: 0m));

        var rows = new List<ReportRow>();
        var visible = VisibleAccounts(tree, a =>
            rolledOpening[a.Id] != 0 || rolledMovement[a.Id] != (0m, 0m)
            || rolledOpening[a.Id] + rolledMovement[a.Id].Debit - rolledMovement[a.Id].Credit != 0);

        decimal openingDebit = 0, openingCredit = 0, movementDebit = 0, movementCredit = 0, closingDebit = 0, closingCredit = 0;
        foreach (var (account, level) in tree.Walk(tree.Roots).Where(x => visible.Contains(x.Account.Id)))
        {
            var openingNet = rolledOpening[account.Id];
            var m = rolledMovement[account.Id];
            var closingNet = openingNet + m.Debit - m.Credit;

            if (account.IsPosting)
            {
                openingDebit += Math.Max(openingNet, 0); openingCredit += Math.Max(-openingNet, 0);
                movementDebit += m.Debit; movementCredit += m.Credit;
                closingDebit += Math.Max(closingNet, 0); closingCredit += Math.Max(-closingNet, 0);
            }

            rows.Add(new ReportRow(
                [
                    new(account.Code), Name(account),
                    Money(Side(openingNet, debit: true)), Money(Side(openingNet, debit: false)),
                    Money(m.Debit), Money(m.Credit),
                    Money(Side(closingNet, debit: true)), Money(Side(closingNet, debit: false)),
                ],
                level,
                account.IsPosting ? RowStyle.Normal : RowStyle.Group,
                new ReportLink(ReportLink.Account, account.Id, from, to)));
        }

        rows.Add(new ReportRow(
            [
                ReportCell.Blank, Label(ReportLabels.Totals),
                Amount(openingDebit), Amount(openingCredit), Amount(movementDebit), Amount(movementCredit), Amount(closingDebit), Amount(closingCredit),
            ],
            0, RowStyle.Total));

        var columns = new List<ReportColumn>
        {
            Column("code", ColumnKind.Text, ReportLabels.Code), Column("name", ColumnKind.Text, ReportLabels.Account),
            Column("openingDebit", ColumnKind.Amount, ("Opening debit", "افتتاحي مدين")), Column("openingCredit", ColumnKind.Amount, ("Opening credit", "افتتاحي دائن")),
            Column("movementDebit", ColumnKind.Amount, ("Movement debit", "حركة مدين")), Column("movementCredit", ColumnKind.Amount, ("Movement credit", "حركة دائن")),
            Column("closingDebit", ColumnKind.Amount, ("Closing debit", "ختامي مدين")), Column("closingCredit", ColumnKind.Amount, ("Closing credit", "ختامي دائن")),
        };

        var range = ReportLabels.Range(from, to);
        return Result("trial-balance", ReportLabels.TrialBalance, range, company, currency, columns, rows,
            [
                Check(ReportLabels.DebitsEqualCredits, openingDebit == openingCredit && movementDebit == movementCredit && closingDebit == closingCredit),
            ]);
    }

    // ---------------------------------------------------------------- Profit and loss

    public Task<ReportResult> ProfitAndLossAsync(
        DateOnly? from, DateOnly? to, Comparison comparison = Comparison.None, CancellationToken cancellationToken = default) =>
        ProfitAndLossCoreAsync(from, to, comparison, ReportLabels.ProfitAndLoss, (f, t) => ledger.TotalsAsync(f, t, cancellationToken), cancellationToken);

    /// <summary>The profit and loss of the entries tagged with one cost center or project (brief section 10.2).</summary>
    public async Task<ReportResult> ProfitAndLossAsync(
        Guid costCenterId, DateOnly? from, DateOnly? to, Comparison comparison = Comparison.None, CancellationToken cancellationToken = default)
    {
        var costCenter = (await costCenters.ListAsync(cancellationToken)).FirstOrDefault(c => c.Id == costCenterId) ?? throw new NotFoundException("cost-center");
        var title = (ReportLabels.ProfitAndLoss.En + ": " + costCenter.Code + " " + costCenter.NameEn,
                     ReportLabels.ProfitAndLoss.Ar + ": " + costCenter.Code + " " + costCenter.NameAr);
        return await ProfitAndLossCoreAsync(from, to, comparison, title, (f, t) => ledger.TotalsForCostCenterAsync(costCenterId, f, t, cancellationToken), cancellationToken);
    }

    private async Task<ReportResult> ProfitAndLossCoreAsync(
        DateOnly? from, DateOnly? to, Comparison comparison, (string En, string Ar) reportTitle,
        Func<DateOnly?, DateOnly?, Task<IReadOnlyList<AccountTotal>>> totals, CancellationToken cancellationToken)
    {
        var (company, currency) = Company();
        var tree = new AccountTree(await accounts.ListAsync(cancellationToken));

        var current = Totals(await totals(from, to));
        var previous = comparison == Comparison.PreviousYear
            ? Totals(await totals(from?.AddYears(-1), to?.AddYears(-1)))
            : null;

        var rows = new List<ReportRow>();
        var net = (Current: 0m, Previous: 0m);
        foreach (var (type, title, total) in new[]
                 {
                     (AccountType.Revenue, ReportLabels.Revenues, ReportLabels.TotalRevenues),
                     (AccountType.Expense, ReportLabels.Expenses, ReportLabels.TotalExpenses),
                 })
        {
            var sign = type == AccountType.Revenue ? -1m : 1m; // revenue grows with credits, expenses with debits
            var section = BuildSection(tree, type, current, previous, sign, from, to, rows, title, total);
            net = type == AccountType.Revenue ? section : (net.Current - section.Current, net.Previous - section.Previous);
        }

        rows.Add(new ReportRow(AmountCells(Label(ReportLabels.NetProfit), net.Current, net.Previous, previous is not null), 0, RowStyle.Total));

        return Result("profit-and-loss", reportTitle, ReportLabels.Range(from, to), company, currency,
            AmountColumns(previous is not null), rows, []);
    }

    // ---------------------------------------------------------------- Balance sheet

    public async Task<ReportResult> BalanceSheetAsync(
        DateOnly asOf, Comparison comparison = Comparison.None, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var tree = new AccountTree(await accounts.ListAsync(cancellationToken));

        var current = Totals(await ledger.TotalsAsync(null, asOf, cancellationToken));
        var previous = comparison == Comparison.PreviousYear
            ? Totals(await ledger.TotalsAsync(null, asOf.AddYears(-1), cancellationToken))
            : null;
        var withComparison = previous is not null;

        var rows = new List<ReportRow>();
        var assets = BuildSection(tree, AccountType.Asset, current, previous, 1m, null, asOf, rows, ReportLabels.Assets, ReportLabels.TotalAssets);
        var liabilities = BuildSection(tree, AccountType.Liability, current, previous, -1m, null, asOf, rows, ReportLabels.Liabilities, ReportLabels.TotalLiabilities);

        // Equity, plus the profit that has not been closed into retained earnings yet (there is no year-end close in Phase 1).
        var equity = BuildSection(tree, AccountType.Equity, current, previous, -1m, null, asOf, rows, ReportLabels.Equity, null);
        var profit = (
            Current: -NetOfType(tree, current, AccountType.Revenue) - NetOfType(tree, current, AccountType.Expense),
            Previous: previous is null ? 0m : -NetOfType(tree, previous, AccountType.Revenue) - NetOfType(tree, previous, AccountType.Expense));
        rows.Add(new ReportRow(AmountCells(Label(ReportLabels.ProfitNotClosed), profit.Current, profit.Previous, withComparison), 1));
        var totalEquity = (equity.Current + profit.Current, equity.Previous + profit.Previous);
        rows.Add(new ReportRow(AmountCells(Label(ReportLabels.TotalEquity), totalEquity.Item1, totalEquity.Item2, withComparison), 0, RowStyle.Total));

        var rightSide = (liabilities.Current + totalEquity.Item1, liabilities.Previous + totalEquity.Item2);
        rows.Add(new ReportRow(AmountCells(Label(ReportLabels.TotalLiabilitiesAndEquity), rightSide.Item1, rightSide.Item2, withComparison), 0, RowStyle.Total));

        return Result("balance-sheet", ReportLabels.BalanceSheet, ReportLabels.AsOf(asOf), company, currency,
            AmountColumns(withComparison), rows,
            [Check(ReportLabels.AssetsEqualLiabilitiesAndEquity, assets.Current == rightSide.Item1 && assets.Previous == rightSide.Item2)]);
    }

    // ---------------------------------------------------------------- Statement of account, general ledger, journal

    /// <summary>
    /// Every entry of one account (and the accounts below it, for a group) with a running balance, after an opening balance.
    /// The balance reads positive on the account's normal side (debit for assets and expenses, credit for the rest).
    /// </summary>
    public async Task<ReportResult> StatementOfAccountAsync(
        Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var all = await accounts.ListAsync(cancellationToken);
        var account = all.FirstOrDefault(a => a.Id == accountId) ?? throw new NotFoundException("account");
        var tree = new AccountTree(all);
        var members = Domain.Accounting.ChartRules.WithDescendants(accountId, all);
        var memberIds = members.Select(a => a.Id).ToHashSet();

        var rows = new List<ReportRow>();
        await AddStatementAsync(rows, tree, account, memberIds, isGroup: !account.IsPosting, from, to, withHeading: false, cancellationToken);

        var title = (ReportLabels.StatementOfAccount.En + ": " + account.Code + " " + account.NameEn,
                     ReportLabels.StatementOfAccount.Ar + ": " + account.Code + " " + account.NameAr);
        return Result("statement-of-account", title, ReportLabels.Range(from, to), company, currency, StatementColumns(), rows, []);
    }

    /// <summary>The statement of account of every account that has activity or an opening balance, one after the other.</summary>
    public async Task<ReportResult> GeneralLedgerAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var all = await accounts.ListAsync(cancellationToken);
        var tree = new AccountTree(all);

        var opening = from is { } start ? Totals(await ledger.TotalsAsync(null, start.AddDays(-1), cancellationToken)) : new();
        var movement = Totals(await ledger.TotalsAsync(from, to, cancellationToken));
        var active = all.Where(a => a.IsPosting && (movement.ContainsKey(a.Id) || opening.GetValueOrDefault(a.Id) != (0m, 0m)))
            .OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<ReportRow>();
        foreach (var account in active)
            await AddStatementAsync(rows, tree, account, [account.Id], isGroup: false, from, to, withHeading: true, cancellationToken);

        return Result("general-ledger", ReportLabels.GeneralLedger, ReportLabels.Range(from, to), company, currency, StatementColumns(), rows, []);
    }

    /// <summary>Every entry of every voucher in date order, as a journal.</summary>
    public async Task<ReportResult> JournalAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var tree = new AccountTree(await accounts.ListAsync(cancellationToken));
        var lines = await ledger.LinesAsync(null, from, to, cancellationToken);

        var rows = lines.Select(line =>
        {
            var account = tree.Find(line.AccountId);
            return new ReportRow(
                [
                    new(Date: line.Date), new(line.VoucherNumber), AccountCell(account), new(line.Description ?? line.VoucherMemo),
                    Money(line.Debit), Money(line.Credit),
                ],
                0, RowStyle.Normal, new ReportLink(ReportLink.Voucher, line.VoucherId));
        }).ToList();

        rows.Add(new ReportRow(
            [ReportCell.Blank, ReportCell.Blank, Label(ReportLabels.Totals), ReportCell.Blank, Amount(lines.Sum(l => l.Debit)), Amount(lines.Sum(l => l.Credit))],
            0, RowStyle.Total));

        var columns = new List<ReportColumn>
        {
            Column("date", ColumnKind.Date, ReportLabels.Date), Column("voucher", ColumnKind.Text, ReportLabels.Voucher),
            Column("account", ColumnKind.Text, ReportLabels.Account), Column("description", ColumnKind.Text, ReportLabels.Description),
            Column("debit", ColumnKind.Amount, ReportLabels.Debit), Column("credit", ColumnKind.Amount, ReportLabels.Credit),
        };

        return Result("journal", ReportLabels.Journal, ReportLabels.Range(from, to), company, currency, columns, rows,
            [Check(ReportLabels.DebitsEqualCredits, lines.Sum(l => l.Debit) == lines.Sum(l => l.Credit))]);
    }

    // ---------------------------------------------------------------- Building blocks

    private async Task AddStatementAsync(
        List<ReportRow> rows, AccountTree tree, Account account, HashSet<Guid> memberIds, bool isGroup,
        DateOnly? from, DateOnly? to, bool withHeading, CancellationToken cancellationToken)
    {
        var sign = account.IsDebitNormal ? 1m : -1m; // balance = (debit - credit) for debit-normal accounts, the reverse otherwise

        decimal running = 0;
        if (from is { } start)
        {
            var before = await ledger.TotalsAsync(null, start.AddDays(-1), cancellationToken);
            running = before.Where(t => memberIds.Contains(t.AccountId)).Sum(t => t.Debit - t.Credit) * sign;
        }

        if (withHeading)
            rows.Add(new ReportRow([new(account.Code), Name(account), ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, ReportCell.Blank], 0, RowStyle.Heading,
                new ReportLink(ReportLink.Account, account.Id, from, to)));

        rows.Add(new ReportRow(
            [ReportCell.Blank, ReportCell.Blank, Label(ReportLabels.Opening), ReportCell.Blank, ReportCell.Blank, Amount(running)],
            0, RowStyle.Subtotal));

        decimal debit = 0, credit = 0;
        foreach (var line in await ledger.LinesAsync(memberIds, from, to, cancellationToken))
        {
            running += (line.Debit - line.Credit) * sign;
            debit += line.Debit;
            credit += line.Credit;

            var description = line.Description ?? line.VoucherMemo;
            if (isGroup && tree.Find(line.AccountId) is { } posting)
                description = $"{posting.Code} {posting.NameEn}" + (description is null ? "" : " — " + description);

            rows.Add(new ReportRow(
                [new(Date: line.Date), new(line.VoucherNumber), new(description), Money(line.Debit), Money(line.Credit), Amount(running)],
                0, RowStyle.Normal, new ReportLink(ReportLink.Voucher, line.VoucherId)));
        }

        rows.Add(new ReportRow(
            [ReportCell.Blank, ReportCell.Blank, Label(ReportLabels.Closing), Amount(debit), Amount(credit), Amount(running)],
            0, RowStyle.Total));
    }

    /// <summary>One section of a statement: the tree of one account type with its rolled-up amounts, then an optional total row.</summary>
    private static (decimal Current, decimal Previous) BuildSection(
        AccountTree tree, AccountType type,
        IReadOnlyDictionary<Guid, (decimal Debit, decimal Credit)> current,
        IReadOnlyDictionary<Guid, (decimal Debit, decimal Credit)>? previous,
        decimal sign, DateOnly? from, DateOnly? to, List<ReportRow> rows,
        (string En, string Ar) title, (string En, string Ar)? total)
    {
        decimal Own(IReadOnlyDictionary<Guid, (decimal Debit, decimal Credit)> source, Account a) =>
            (source.GetValueOrDefault(a.Id).Debit - source.GetValueOrDefault(a.Id).Credit) * sign;

        var currentRolled = tree.Rollup(a => Own(current, a), (x, y) => x + y, 0m);
        var previousRolled = previous is null ? null : tree.Rollup(a => Own(previous, a), (x, y) => x + y, 0m);
        var withComparison = previous is not null;

        var roots = tree.Roots.Where(a => a.Type == type).ToList();
        var visible = VisibleAccounts(tree, a => currentRolled[a.Id] != 0 || (previousRolled?[a.Id] ?? 0) != 0, roots);

        rows.Add(new ReportRow(AmountCells(Label(title), null, null, withComparison), 0, RowStyle.Heading));
        foreach (var (account, level) in tree.Walk(roots).Where(x => visible.Contains(x.Account.Id)))
        {
            rows.Add(new ReportRow(
                AmountCells(AccountCell(account), currentRolled[account.Id], previousRolled?[account.Id] ?? 0, withComparison, includeCode: true, account),
                level + 1, account.IsPosting ? RowStyle.Normal : RowStyle.Group,
                new ReportLink(ReportLink.Account, account.Id, from, to)));
        }

        var sum = (Current: roots.Sum(r => currentRolled[r.Id]), Previous: roots.Sum(r => previousRolled?[r.Id] ?? 0));
        if (total is { } label)
            rows.Add(new ReportRow(AmountCells(Label(label), sum.Current, sum.Previous, withComparison), 0, RowStyle.Subtotal));
        return sum;
    }

    private static decimal NetOfType(AccountTree tree, IReadOnlyDictionary<Guid, (decimal Debit, decimal Credit)> totals, AccountType type) =>
        tree.Roots.Where(a => a.Type == type).Sum(root => tree.Walk([root]).Where(x => x.Account.IsPosting)
            .Sum(x => totals.GetValueOrDefault(x.Account.Id).Debit - totals.GetValueOrDefault(x.Account.Id).Credit));

    /// <summary>Accounts that pass the test, plus the groups above them.</summary>
    private static HashSet<Guid> VisibleAccounts(AccountTree tree, Func<Account, bool> hasValue, IEnumerable<Account>? from = null)
    {
        var visible = new HashSet<Guid>();
        foreach (var (account, _) in tree.Walk(from ?? tree.Roots))
        {
            if (!account.IsPosting || !hasValue(account))
                continue;

            for (var node = account; node is not null; node = node.ParentId is { } p ? tree.Find(p) : null)
                visible.Add(node.Id);
        }

        return visible;
    }

    private (CompanyInfo Company, Currency Currency) Company()
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var currency = CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);
        return (company, currency);
    }

    private static Dictionary<Guid, (decimal Debit, decimal Credit)> Totals(IEnumerable<AccountTotal> totals) =>
        totals.ToDictionary(t => t.AccountId, t => (t.Debit, t.Credit));

    private static ReportResult Result(
        string key, (string En, string Ar) title, (string En, string Ar) subtitle, CompanyInfo company, Currency currency,
        IReadOnlyList<ReportColumn> columns, IReadOnlyList<ReportRow> rows, IReadOnlyList<ReportCheck> checks) =>
        new(key, title.En, title.Ar, subtitle.En, subtitle.Ar, company.NameEn, company.NameAr, company.BaseCurrencyCode, currency.MinorUnits, columns, rows, checks);

    private static ReportColumn Column(string key, ColumnKind kind, (string En, string Ar) title) => new(key, kind, title.En, title.Ar);

    private static ReportCheck Check((string En, string Ar) text, bool passed) => new(text.En, text.Ar, passed);

    private static ReportCell Label((string En, string Ar) text) => new(text.En, text.Ar);

    private static ReportCell Name(Account a) => new(a.NameEn, a.NameAr);

    private static ReportCell AccountCell(Account? a) => a is null ? ReportCell.Blank : new($"{a.Code} {a.NameEn}", $"{a.Code} {a.NameAr}");

    /// <summary>An amount, with zero shown as an empty cell.</summary>
    private static ReportCell Money(decimal value) => value == 0 ? ReportCell.Blank : new(Amount: value);

    private static ReportCell Amount(decimal value) => new(Amount: value);

    /// <summary>The part of a net balance that belongs on the debit (positive) or credit (negative) side, or nothing.</summary>
    private static decimal Side(decimal net, bool debit) => debit ? Math.Max(net, 0) : Math.Max(-net, 0);

    private static IReadOnlyList<ReportCell> AmountCells(
        ReportCell first, decimal? current, decimal? previous, bool withComparison, bool includeCode = false, Account? account = null)
    {
        var cells = new List<ReportCell>();
        if (includeCode)
            cells.Add(new(account!.Code));
        else
            cells.Add(ReportCell.Blank);

        cells.Add(first with { Text = includeCode ? account!.NameEn : first.Text, TextAr = includeCode ? account!.NameAr : first.TextAr });
        cells.Add(current is null ? ReportCell.Blank : Amount(current.Value));
        if (withComparison)
            cells.Add(previous is null ? ReportCell.Blank : Amount(previous.Value));
        return cells;
    }

    private static IReadOnlyList<ReportColumn> AmountColumns(bool withComparison)
    {
        var columns = new List<ReportColumn>
        {
            Column("code", ColumnKind.Text, ReportLabels.Code), Column("name", ColumnKind.Text, ReportLabels.Account),
            Column("amount", ColumnKind.Amount, ReportLabels.Amount),
        };
        if (withComparison)
            columns.Add(Column("previous", ColumnKind.Amount, ReportLabels.PreviousYear));
        return columns;
    }

    private static IReadOnlyList<ReportColumn> StatementColumns() =>
    [
        Column("date", ColumnKind.Date, ReportLabels.Date), Column("voucher", ColumnKind.Text, ReportLabels.Voucher),
        Column("description", ColumnKind.Text, ReportLabels.Description), Column("debit", ColumnKind.Amount, ReportLabels.Debit),
        Column("credit", ColumnKind.Amount, ReportLabels.Credit), Column("balance", ColumnKind.Amount, ReportLabels.Balance),
    ];
}
