using Baba.Application.Trade;
using Baba.Domain;
using Baba.Domain.Trade;
using Baba.Localization;

namespace Baba.Application.Reporting;

/// <summary>
/// The tax return (brief sections 8 and 10.3): for a period, what was sold and bought under each tax code, and the tax on it, taken from
/// the invoices and credit or debit notes that were issued. Credit and debit notes count against what they took back. Amounts are in the
/// company's currency, each line converted at its document's rate. The report checks itself against the ledger: the tax accounts must have
/// moved by exactly the tax the documents say (a manual entry on a tax account makes the check fail, on purpose).
/// </summary>
public sealed partial class ReportService
{
    private static (string En, string Ar) TaxReturnTitle => ("Tax return", "الإقرار الضريبي");

    public async Task<ReportResult> TaxReturnAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var (company, currency) = Company();
        var codes = (await taxCodes.ListAsync(cancellationToken)).ToDictionary(c => c.Id);

        var documentsInPeriod = new List<Document>();
        foreach (var kind in new[] { DocumentKind.SalesInvoice, DocumentKind.SalesCreditNote, DocumentKind.PurchaseInvoice, DocumentKind.PurchaseDebitNote })
            documentsInPeriod.AddRange(await documents.SearchAsync(new DocumentSearch(kind, DocumentStatus.Issued, null, from, to, 100_000), cancellationToken));

        // One bucket per side and tax code (Guid.Empty: lines with no tax code).
        var sales = new Dictionary<Guid, (decimal Base, decimal Tax)>();
        var purchases = new Dictionary<Guid, (decimal Base, decimal Tax)>();
        var expectedOutput = 0m;
        var expectedInput = 0m;

        foreach (var document in documentsInPeriod)
        {
            var docCurrency = CurrencyCatalog.Find(document.CurrencyCode)?.Currency ?? currency;
            var lines = document.Lines.OrderBy(l => l.LineNumber).ToList();
            var totals = DocumentMath.Compute(lines, document.DiscountPercent, docCurrency);
            var sign = document.Kind.IsNote() ? -1m : 1m;
            var side = document.Kind.IsSales() ? sales : purchases;
            var rate = document.ExchangeRate;

            for (var i = 0; i < lines.Count; i++)
            {
                var bucket = lines[i].TaxCodeId ?? Guid.Empty;
                var current = side.GetValueOrDefault(bucket);
                side[bucket] = (current.Base + sign * Domain.Money.Round(totals.PostedAmounts[i] * rate, currency), current.Tax + sign * Domain.Money.Round(totals.TaxAmounts[i] * rate, currency));
            }

            // What the ledger holds is one tax line per tax account, rounded once.
            foreach (var group in lines.Select((l, i) => (Line: l, Tax: totals.TaxAmounts[i]))
                         .Where(x => x.Tax != 0 && x.Line.TaxCodeId is not null && codes.ContainsKey(x.Line.TaxCodeId.Value))
                         .GroupBy(x => document.Kind.IsSales() ? codes[x.Line.TaxCodeId!.Value].OutputAccountId : codes[x.Line.TaxCodeId!.Value].InputAccountId))
            {
                var tax = sign * Domain.Money.Round(group.Sum(x => x.Tax) * rate, currency);
                if (document.Kind.IsSales()) expectedOutput += tax; else expectedInput += tax;
            }
        }

        var rows = new List<ReportRow>();
        decimal outputTax = 0, inputTax = 0;

        void Section((string En, string Ar) heading, (string En, string Ar) totalLabel, Dictionary<Guid, (decimal Base, decimal Tax)> side, ref decimal taxTotal)
        {
            rows.Add(new ReportRow([new(heading.En, heading.Ar), ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, ReportCell.Blank], 0, RowStyle.Heading));
            decimal baseTotal = 0, tax = 0;
            foreach (var (id, amounts) in side.OrderBy(entry => OrderOf(entry.Key, codes)).ThenBy(entry => entry.Key))
            {
                var code = codes.TryGetValue(id, out var found) ? found : null;
                rows.Add(new ReportRow(
                    [
                        code is null ? Label(TaxLabels.NoCode) : new(code.Code),
                        code is null ? ReportCell.Blank : new(code.NameEn, code.NameAr),
                        code is null ? ReportCell.Blank : new($"{code.Rate:0.##}%"),
                        Amount(amounts.Base),
                        Amount(amounts.Tax),
                    ], 0, RowStyle.Normal));
                baseTotal += amounts.Base;
                tax += amounts.Tax;
            }

            rows.Add(new ReportRow([Label(totalLabel), ReportCell.Blank, ReportCell.Blank, Amount(baseTotal), Amount(tax)], 0, RowStyle.Subtotal));
            taxTotal = tax;
        }

        static int OrderOf(Guid id, Dictionary<Guid, TaxCode> codes) => codes.TryGetValue(id, out var c) ? (int)c.Treatment : 99;

        Section(TaxLabels.Sales, TaxLabels.TotalSales, sales, ref outputTax);
        Section(TaxLabels.Purchases, TaxLabels.TotalPurchases, purchases, ref inputTax);

        rows.Add(new ReportRow([Label(TaxLabels.OutputTax), ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, Amount(outputTax)], 0, RowStyle.Normal));
        rows.Add(new ReportRow([Label(TaxLabels.InputTax), ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, Amount(inputTax)], 0, RowStyle.Normal));
        rows.Add(new ReportRow([Label(TaxLabels.NetTax), ReportCell.Blank, ReportCell.Blank, ReportCell.Blank, Amount(outputTax - inputTax)], 0, RowStyle.Total));

        // The ledger side of the check: tax collected on the output accounts and claimed on the input accounts.
        var outputAccounts = codes.Values.Select(c => c.OutputAccountId).OfType<Guid>().ToHashSet();
        var inputAccounts = codes.Values.Select(c => c.InputAccountId).OfType<Guid>().ToHashSet();
        var ledgerTotals = await ledger.OperatingTotalsAsync(from, to, cancellationToken);
        var ledgerOutput = ledgerTotals.Where(t => outputAccounts.Contains(t.AccountId)).Sum(t => t.Credit - t.Debit);
        var ledgerInput = ledgerTotals.Where(t => inputAccounts.Contains(t.AccountId)).Sum(t => t.Debit - t.Credit);
        var checks = new List<ReportCheck> { Check(TaxLabels.AgreesWithLedger, ledgerOutput == expectedOutput && ledgerInput == expectedInput) };

        var columns = new List<ReportColumn>
        {
            Column("code", ColumnKind.Text, ReportLabels.Code),
            Column("name", ColumnKind.Text, ReportLabels.Description),
            Column("rate", ColumnKind.Text, TaxLabels.Rate),
            Column("base", ColumnKind.Amount, TaxLabels.TaxableAmount),
            Column("tax", ColumnKind.Amount, TaxLabels.Tax),
        };
        return Result("tax-return", TaxReturnTitle, ReportLabels.Range(from, to), company, currency, columns, rows, checks);
    }
}

/// <summary>The words of the tax return, in both languages.</summary>
internal static class TaxLabels
{
    public static (string En, string Ar) Sales => ("Sales", "المبيعات");
    public static (string En, string Ar) Purchases => ("Purchases", "المشتريات");
    public static (string En, string Ar) TotalSales => ("Total sales", "إجمالي المبيعات");
    public static (string En, string Ar) TotalPurchases => ("Total purchases", "إجمالي المشتريات");
    public static (string En, string Ar) OutputTax => ("Tax on sales (output)", "الضريبة على المبيعات");
    public static (string En, string Ar) InputTax => ("Tax on purchases (input)", "الضريبة على المشتريات");
    public static (string En, string Ar) NetTax => ("Net tax payable (refundable if negative)", "صافي الضريبة المستحقة (مستردة إذا كانت سالبة)");
    public static (string En, string Ar) NoCode => ("No tax code", "بدون رمز ضريبة");
    public static (string En, string Ar) Rate => ("Rate", "النسبة");
    public static (string En, string Ar) TaxableAmount => ("Amount", "المبلغ");
    public static (string En, string Ar) Tax => ("Tax", "الضريبة");
    public static (string En, string Ar) AgreesWithLedger => ("Tax accounts agree with the ledger", "حسابات الضريبة تتفق مع دفتر الأستاذ");
}
