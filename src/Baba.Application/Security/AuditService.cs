using System.Text.Json;
using Baba.Application.Companies;
using Baba.Application.Reporting;
using Baba.Domain;
using Baba.Localization;

namespace Baba.Application.Security;

public sealed record AuditSearch(DateOnly? From = null, DateOnly? To = null, string? User = null, string? Entity = null, int? Limit = null);

public interface IAuditStore
{
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<AuditLogEntry>> SearchAsync(AuditSearch search, int limit, CancellationToken cancellationToken = default);
}

/// <summary>One changed value. A created record has no "before", a deleted one no "after".</summary>
public sealed record AuditChange(string Field, string? Before, string? After);

public sealed record AuditEntryDto(
    Guid Id,
    DateTime At,
    string UserId,
    AuditAction Action,
    /// <summary>What kind of record, as the program names it (Voucher, Party ...).</summary>
    string EntityName,
    string EntityLabelEn,
    string EntityLabelAr,
    Guid EntityId,
    /// <summary>A number, code or name that says which record it was, when the log has one.</summary>
    string? Reference,
    IReadOnlyList<AuditChange> Changes);

/// <summary>
/// Who changed what and when, in words a person can read (brief section 10.4: an audit log viewer with filters). The log itself is written
/// automatically on every save; this only reads it.
/// </summary>
public sealed class AuditService(IAuditStore store, ICompanyFiles files)
{
    private const int DefaultLimit = 500;
    private const int MaxLimit = 5000;

    // The names people know the records by. Anything not listed shows its program name split into words.
    private static readonly Dictionary<string, (string En, string Ar)> Labels = new()
    {
        ["Voucher"] = ("Voucher", "سند"),
        ["VoucherLine"] = ("Voucher line", "بند سند"),
        ["Account"] = ("Account", "حساب"),
        ["Party"] = ("Customer or supplier", "عميل أو مورد"),
        ["CostCenter"] = ("Cost center", "مركز تكلفة"),
        ["Document"] = ("Sales or purchase document", "مستند بيع أو شراء"),
        ["DocumentLine"] = ("Document line", "بند مستند"),
        ["Product"] = ("Product", "منتج"),
        ["PriceList"] = ("Price list", "قائمة أسعار"),
        ["TaxCode"] = ("Tax code", "رمز ضريبة"),
        ["Warehouse"] = ("Warehouse", "مستودع"),
        ["StockDocument"] = ("Stock document", "مستند مخزون"),
        ["FixedAsset"] = ("Fixed asset", "أصل ثابت"),
        ["Employee"] = ("Employee", "موظف"),
        ["PayrollRun"] = ("Payroll run", "مسير رواتب"),
        ["ExpenseClaim"] = ("Expense claim", "مطالبة مصروفات"),
        ["BudgetEntry"] = ("Budget figure", "رقم موازنة"),
        ["AppUser"] = ("User", "مستخدم"),
        ["Role"] = ("Role", "دور"),
        ["ApprovalRequest"] = ("Approval request", "طلب موافقة"),
        ["SecuritySettings"] = ("Security settings", "إعدادات الأمان"),
        ["Company"] = ("Company details", "بيانات الشركة"),
        ["Period"] = ("Locked month", "شهر مقفل"),
        ["CurrencyRate"] = ("Exchange rate", "سعر صرف"),
        ["BankReconciliation"] = ("Bank reconciliation", "مطابقة بنكية"),
    };

    private static readonly string[] ReferenceFields = ["Number", "Code", "UserName", "NameEn", "NameAr", "DisplayName", "Name"];

    public async Task<IReadOnlyList<AuditEntryDto>> ListAsync(AuditSearch search, CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(search.Limit ?? DefaultLimit, 1, MaxLimit);
        var entries = (await store.SearchAsync(search, limit, cancellationToken)).Select(ToDto).ToList();

        // A change only logs what changed, so it often has no name or number: borrow it from another entry about the same record.
        var known = entries.Where(e => e.Reference is not null).GroupBy(e => e.EntityId).ToDictionary(g => g.Key, g => g.First().Reference);
        return entries.Select(e => e.Reference is null && known.TryGetValue(e.EntityId, out var reference) ? e with { Reference = reference } : e).ToList();
    }

    /// <summary>The log as a table, so the usual exports (Excel, CSV, PDF) work for it.</summary>
    public async Task<ReportResult> ReportAsync(AuditSearch search, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var currency = CurrencyCatalog.Find(company.BaseCurrencyCode)?.Currency ?? new Currency(company.BaseCurrencyCode, 2);
        var columns = new[]
        {
            new ReportColumn("at", ColumnKind.Text, "When", "الوقت"),
            new ReportColumn("user", ColumnKind.Text, "Who", "المستخدم"),
            new ReportColumn("action", ColumnKind.Text, "What happened", "ما حدث"),
            new ReportColumn("record", ColumnKind.Text, "Record", "السجل"),
            new ReportColumn("reference", ColumnKind.Text, "Number or name", "الرقم أو الاسم"),
            new ReportColumn("changes", ColumnKind.Text, "Changes", "التغييرات"),
        };

        var rows = (await ListAsync(search, cancellationToken)).Select(e => new ReportRow(
        [
            new(e.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm")),
            new(e.UserId),
            new(ActionEn(e.Action), ActionAr(e.Action)),
            new(e.EntityLabelEn, e.EntityLabelAr),
            new(e.Reference),
            new(string.Join("; ", e.Changes.Select(c => c.Before is null ? $"{c.Field} = {c.After}" : c.After is null ? $"{c.Field} (was {c.Before})" : $"{c.Field}: {c.Before} → {c.After}"))),
        ])).ToList();

        return new ReportResult(
            "audit-log", "Audit log", "سجل المراجعة", "Who changed what, newest first", "من غيّر ماذا، الأحدث أولاً",
            company.NameEn, company.NameAr, company.BaseCurrencyCode, currency.MinorUnits, columns, rows, []);
    }

    private static string ActionEn(AuditAction action) => action switch { AuditAction.Created => "Added", AuditAction.Deleted => "Deleted", _ => "Changed" };

    private static string ActionAr(AuditAction action) => action switch { AuditAction.Created => "إضافة", AuditAction.Deleted => "حذف", _ => "تعديل" };

    private static AuditEntryDto ToDto(AuditLogEntry entry)
    {
        var before = Parse(entry.BeforeJson);
        var after = Parse(entry.AfterJson);
        var fields = before.Keys.Concat(after.Keys).Distinct().ToList();
        var changes = fields
            .Select(f => new AuditChange(f, before.GetValueOrDefault(f), after.GetValueOrDefault(f)))
            .Where(c => entry.Action != AuditAction.Updated || c.Before != c.After)
            .Where(c => entry.Action == AuditAction.Updated || c.Field is not ("Id" or "CompanyId") && (c.Before ?? c.After) is not null)
            .ToList();

        var source = entry.Action == AuditAction.Deleted ? before : after.Count > 0 ? after : before;
        var reference = ReferenceFields.Select(f => source.GetValueOrDefault(f)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        var (en, ar) = Labels.TryGetValue(entry.EntityName, out var label) ? label : (Words(entry.EntityName), Words(entry.EntityName));
        return new AuditEntryDto(entry.Id, entry.At, entry.UserId, entry.Action, entry.EntityName, en, ar, entry.EntityId, reference, changes);
    }

    private static string Words(string pascal) => System.Text.RegularExpressions.Regex.Replace(pascal, "(?<=[a-z])(?=[A-Z])", " ");

    private static Dictionary<string, string?> Parse(string? json)
    {
        var result = new Dictionary<string, string?>();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => property.Value.GetString(),
                    _ => property.Value.ToString(),
                };
            }
        }
        catch (JsonException)
        {
            // A damaged row is shown without its details rather than hiding the rest of the log.
        }

        return result;
    }
}
