using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Localization;

namespace Baba.Application.Reporting;

/// <summary>
/// The reports that read the customer and supplier sub-ledger and the cost center tags (brief section 10.2): a party's statement,
/// the aging of what customers owe and what is owed to suppliers, and the profit per cost center. Like every report they are
/// computed from the ledger entries on the spot.
/// </summary>
public sealed partial class ReportService
{
    // ---------------------------------------------------------------- Statement of one customer or supplier

    /// <summary>
    /// Every entry of one customer or supplier with a running balance, after an opening balance. A customer's balance is what they owe
    /// (debits less credits), a supplier's is what is owed to them (credits less debits).
    /// </summary>
    public async Task<ReportResult> PartyStatementAsync(Guid partyId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var party = (await parties.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == partyId) ?? throw new NotFoundException("party");
        var sign = party.Kind == PartyKind.Customer ? 1m : -1m;

        var entries = await ledger.PartyEntriesAsync(partyId, to, cancellationToken);
        var before = from is { } start ? entries.Where(e => e.Date < start).ToList() : [];
        var inRange = entries.Where(e => from is null || e.Date >= from).ToList();

        var rows = new List<ReportRow>();
        var running = before.Sum(e => e.Debit - e.Credit) * sign;
        rows.Add(new ReportRow([ReportCell.Blank, ReportCell.Blank, Label(ReportLabels.Opening), ReportCell.Blank, ReportCell.Blank, Amount(running)], 0, RowStyle.Subtotal));

        foreach (var entry in inRange)
        {
            running += (entry.Debit - entry.Credit) * sign;
            rows.Add(new ReportRow(
                [new(Date: entry.Date), new(entry.VoucherNumber), new(entry.Description), Money(entry.Debit), Money(entry.Credit), Amount(running)],
                0, RowStyle.Normal, new ReportLink(ReportLink.Voucher, entry.VoucherId)));
        }

        rows.Add(new ReportRow(
            [ReportCell.Blank, ReportCell.Blank, Label(ReportLabels.Closing), Amount(inRange.Sum(e => e.Debit)), Amount(inRange.Sum(e => e.Credit)), Amount(running)],
            0, RowStyle.Total));

        var title = (ReportLabels.PartyStatement.En + ": " + party.Code + " " + party.NameEn, ReportLabels.PartyStatement.Ar + ": " + party.Code + " " + party.NameAr);
        var columns = new List<ReportColumn>
        {
            Column("date", ColumnKind.Date, ReportLabels.Date), Column("voucher", ColumnKind.Text, ReportLabels.Voucher),
            Column("description", ColumnKind.Text, ReportLabels.Description), Column("debit", ColumnKind.Amount, ReportLabels.Debit),
            Column("credit", ColumnKind.Amount, ReportLabels.Credit), Column("balance", ColumnKind.Amount, ReportLabels.Balance),
        };
        return Result("party-statement", title, ReportLabels.Range(from, to), company, currency, columns, rows, []);
    }

    // ---------------------------------------------------------------- Aging

    /// <summary>
    /// Who owes what, and for how long. The entries on receivable accounts (for customers) or payable accounts (for suppliers) are
    /// grouped by party. Credits (payments) are applied to the oldest debits first, and what is left is aged from its due date: the
    /// entry's date plus the party's payment terms. A credit with nothing to pay off is shown as a negative amount (an advance).
    /// A payment that was set against specific invoices pays exactly those, and only the rest goes by the oldest-first rule.
    /// </summary>
    public async Task<ReportResult> AgingAsync(PartyKind side, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var role = side == PartyKind.Customer ? AccountRole.Receivable : AccountRole.Payable;
        var allParties = (await parties.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var accountRoles = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id, a => a.Role);

        var entries = (await ledger.PartyEntriesAsync(null, asOf, cancellationToken))
            .Where(e => accountRoles.GetValueOrDefault(e.AccountId) == role)
            .GroupBy(e => e.PartyId);

        var allocated = await allocations.ListAsync(cancellationToken);
        var lines = new List<AgingLine>();
        foreach (var group in entries)
        {
            if (!allParties.TryGetValue(group.Key, out var party))
                continue;

            var line = Age(party, group, side, asOf, allocated);
            if (line.Total != 0 || line.Buckets.Any(b => b != 0))
                lines.Add(line);
        }

        lines.Sort((a, b) => string.Compare(a.Party.Code, b.Party.Code, StringComparison.OrdinalIgnoreCase));

        var rows = new List<ReportRow>();
        foreach (var line in lines)
        {
            var cells = new List<ReportCell> { new(line.Party.Code), new(line.Party.NameEn, line.Party.NameAr) };
            cells.AddRange(line.Buckets.Select(Money));
            cells.Add(Amount(line.Total));
            if (side == PartyKind.Customer)
                cells.Add(line.Party.CreditLimitScaled == 0 ? ReportCell.Blank : Amount(line.Party.CreditLimit));
            rows.Add(new ReportRow(cells, 0, RowStyle.Normal, new ReportLink(ReportLink.Party, line.Party.Id)));
        }

        var totals = new List<ReportCell> { ReportCell.Blank, Label(ReportLabels.Totals) };
        for (var i = 0; i < 5; i++)
            totals.Add(Amount(lines.Sum(l => l.Buckets[i])));
        totals.Add(Amount(lines.Sum(l => l.Total)));
        if (side == PartyKind.Customer)
            totals.Add(ReportCell.Blank);
        rows.Add(new ReportRow(totals, 0, RowStyle.Total));

        var columns = new List<ReportColumn>
        {
            Column("code", ColumnKind.Text, ReportLabels.Code),
            Column("name", ColumnKind.Text, side == PartyKind.Customer ? ReportLabels.Customer : ReportLabels.Supplier),
            Column("notDue", ColumnKind.Amount, ReportLabels.NotDue),
            Column("d30", ColumnKind.Amount, ReportLabels.Overdue1To30),
            Column("d60", ColumnKind.Amount, ReportLabels.Overdue31To60),
            Column("d90", ColumnKind.Amount, ReportLabels.Overdue61To90),
            Column("over90", ColumnKind.Amount, ReportLabels.OverdueOver90),
            Column("total", ColumnKind.Amount, ReportLabels.Total),
        };
        var checks = new List<ReportCheck>();
        if (side == PartyKind.Customer)
        {
            columns.Add(Column("creditLimit", ColumnKind.Amount, ReportLabels.CreditLimit));
            checks.Add(Check(ReportLabels.WithinCreditLimits, lines.All(l => l.Party.CreditLimitScaled == 0 || l.Total <= l.Party.CreditLimit)));
        }

        var title = side == PartyKind.Customer ? ReportLabels.AgingReceivable : ReportLabels.AgingPayable;
        return Result(side == PartyKind.Customer ? "aging-receivable" : "aging-payable", title, ReportLabels.AsOf(asOf), company, currency, columns, rows, checks);
    }

    private sealed record AgingLine(Party Party, decimal[] Buckets, decimal Total);

    /// <summary>Ages one party's entries: oldest debits are paid off first, the rest is placed in a bucket by days past due.</summary>
    private static AgingLine Age(Party party, IEnumerable<PartyEntry> entries, PartyKind side, DateOnly asOf, IReadOnlyList<Domain.Trade.Allocation> allocated)
    {
        // "Owed" is positive: a debit for a customer, a credit for a supplier. Exchange differences only settle what the allocations
        // below already pair off, so they are left out.
        var items = entries
            .Where(e => e.Kind != VoucherKind.FxSettlement)
            .Select(e => (e.VoucherId, e.Date, Amount: side == PartyKind.Customer ? e.Debit - e.Credit : e.Credit - e.Debit))
            .ToList();
        foreach (var a in allocated)
        {
            var invoice = items.FindIndex(x => x.VoucherId == a.InvoiceVoucherId);
            var payment = items.FindIndex(x => x.VoucherId == a.PaymentVoucherId);
            if (invoice < 0 || payment < 0)
                continue; // the invoice or the payment is not within the dates of this report yet

            items[invoice] = (items[invoice].VoucherId, items[invoice].Date, items[invoice].Amount - a.InvoiceBase);
            items[payment] = (items[payment].VoucherId, items[payment].Date, items[payment].Amount + a.PaymentBase);
        }

        var owed = items.Select(x => (x.Date, x.Amount)).ToList();
        var credits = -owed.Where(x => x.Amount < 0).Sum(x => x.Amount);
        var buckets = new decimal[5];

        foreach (var (date, amount) in owed.Where(x => x.Amount > 0).OrderBy(x => x.Date))
        {
            var applied = Math.Min(credits, amount);
            credits -= applied;
            var open = amount - applied;
            if (open == 0)
                continue;

            var daysPastDue = asOf.DayNumber - date.AddDays(party.PaymentTermsDays).DayNumber;
            buckets[daysPastDue switch { <= 0 => 0, <= 30 => 1, <= 60 => 2, <= 90 => 3, _ => 4 }] += open;
        }

        buckets[0] -= credits; // an advance with nothing to pay off is a negative amount that is not due
        return new AgingLine(party, buckets, buckets.Sum());
    }

    // ---------------------------------------------------------------- Cost centers

    /// <summary>Revenue, expenses and profit of every cost center that has entries, each linking to its profit and loss.</summary>
    public async Task<ReportResult> CostCenterSummaryAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var types = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id, a => a.Type);
        var all = (await costCenters.ListAsync(cancellationToken)).ToDictionary(c => c.Id);
        var totals = await ledger.CostCenterTotalsAsync(from, to, cancellationToken);

        var rows = new List<ReportRow>();
        decimal revenue = 0, expenses = 0;
        foreach (var group in totals.GroupBy(t => t.CostCenterId).Where(g => all.ContainsKey(g.Key)).OrderBy(g => all[g.Key].Code, StringComparer.OrdinalIgnoreCase))
        {
            var cc = all[group.Key];
            var income = group.Where(t => types.GetValueOrDefault(t.AccountId) == AccountType.Revenue).Sum(t => t.Credit - t.Debit);
            var cost = group.Where(t => types.GetValueOrDefault(t.AccountId) == AccountType.Expense).Sum(t => t.Debit - t.Credit);
            revenue += income;
            expenses += cost;
            rows.Add(new ReportRow(
                [new(cc.Code), new(cc.NameEn, cc.NameAr), Amount(income), Amount(cost), Amount(income - cost)],
                0, RowStyle.Normal, new ReportLink(ReportLink.CostCenter, cc.Id, from, to)));
        }

        rows.Add(new ReportRow([ReportCell.Blank, Label(ReportLabels.Totals), Amount(revenue), Amount(expenses), Amount(revenue - expenses)], 0, RowStyle.Total));

        var columns = new List<ReportColumn>
        {
            Column("code", ColumnKind.Text, ReportLabels.Code), Column("name", ColumnKind.Text, ("Cost center", "مركز التكلفة")),
            Column("revenue", ColumnKind.Amount, ReportLabels.Revenues), Column("expenses", ColumnKind.Amount, ReportLabels.Expenses),
            Column("profit", ColumnKind.Amount, ReportLabels.Profit),
        };
        return Result("cost-centers", ReportLabels.CostCenters, ReportLabels.Range(from, to), company, currency, columns, rows, []);
    }

}
