using System.Text.Json;
using System.Text.Json.Serialization;
using Baba.Application.Accounting;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;

namespace Baba.Application.Trade;

public interface IRecurringStore
{
    Task<IReadOnlyList<RecurringSchedule>> ListAsync(CancellationToken cancellationToken = default);
    Task<RecurringSchedule?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(RecurringSchedule schedule, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringSchedule schedule, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>When and how often a schedule runs. The thing that is made again comes from the document or voucher it is set up from.</summary>
public sealed record RecurringInput(string Name, RecurrenceFrequency Frequency, DateOnly NextRunDate, DateOnly? EndDate, bool AutoIssue);

public sealed record RecurringDto(
    Guid Id,
    string Name,
    RecurringTemplate Template,
    string TemplateKind,
    RecurrenceFrequency Frequency,
    DateOnly NextRunDate,
    DateOnly? EndDate,
    bool AutoIssue,
    bool IsActive,
    DateOnly? LastRunDate,
    int RunCount);

/// <summary>What one run made, or why it stopped.</summary>
public sealed record RecurringRunItem(
    Guid ScheduleId, string Name, string TemplateKind, DateOnly Date, Guid? CreatedId, string? Number, bool Issued, string? ProblemCode);

public sealed record RecurringRunResult(IReadOnlyList<RecurringRunItem> Items);

/// <summary>
/// Recurring invoices and entries (brief section 10.3). A schedule is set up from an existing document or voucher. Running the due
/// schedules makes one new document or voucher for each missed date (up to a limit), so nothing is skipped when the app was not
/// opened for a while. A run that cannot be made (for example the month is locked) stops that schedule and says why; the others go on.
/// </summary>
public sealed class RecurringService(
    IRecurringStore schedules, DocumentService documents, VoucherService vouchers, Companies.ICompanyFiles files, TimeProvider clock)
{
    /// <summary>The most dates one schedule catches up in a single run.</summary>
    public const int MaxCatchUp = 24;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<RecurringDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await schedules.ListAsync(cancellationToken)).OrderBy(s => s.NextRunDate).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToList();

    public async Task<RecurringDto> CreateFromDocumentAsync(Guid documentId, RecurringInput input, CancellationToken cancellationToken = default)
    {
        var document = await documents.GetAsync(documentId, cancellationToken) ?? throw new NotFoundException("document");
        var template = new DocumentInput(
            document.Kind, document.Date, null, document.PartyId, document.CurrencyCode, null, document.Reference, document.Memo, document.DiscountPercent,
            document.Lines.Select(l => new DocumentLineInput(null, l.ProductId, l.AccountId, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.CostCenterId)).ToList());
        return await AddAsync(RecurringTemplate.Document, document.Kind.ToString(), JsonSerializer.Serialize(template, Json), input, cancellationToken);
    }

    public async Task<RecurringDto> CreateFromVoucherAsync(Guid voucherId, RecurringInput input, CancellationToken cancellationToken = default)
    {
        var voucher = await vouchers.GetAsync(voucherId, cancellationToken) ?? throw new NotFoundException("voucher");
        if (voucher.Kind.IsSystemMade() || voucher.Kind == VoucherKind.Opening)
            throw Refused("template", "recurring.template-unavailable");

        var template = new VoucherInput(
            voucher.Kind, voucher.Date, voucher.CashAccountId, voucher.Reference, voucher.Memo,
            voucher.Lines.Select(l => new VoucherLineInput(null, l.AccountId, l.Description, l.Debit, l.Credit, l.PartyId, l.CostCenterId)).ToList(),
            files.Current is { } company && voucher.CurrencyCode == company.BaseCurrencyCode ? null : voucher.CurrencyCode);
        return await AddAsync(RecurringTemplate.Voucher, voucher.Kind.ToString(), JsonSerializer.Serialize(template, Json), input, cancellationToken);
    }

    public async Task<RecurringDto> UpdateAsync(Guid id, RecurringInput input, CancellationToken cancellationToken = default)
    {
        var schedule = await schedules.FindAsync(id, cancellationToken) ?? throw new NotFoundException("recurring");
        Check(input);
        schedule.Name = input.Name.Trim();
        schedule.Frequency = input.Frequency;
        if (schedule.NextRunDate != input.NextRunDate)
        {
            schedule.NextRunDate = input.NextRunDate;
            schedule.AnchorDay = input.NextRunDate.Day;
        }

        schedule.EndDate = input.EndDate;
        schedule.AutoIssue = input.AutoIssue;
        await schedules.UpdateAsync(schedule, cancellationToken);
        return ToDto(schedule);
    }

    public async Task<RecurringDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var schedule = await schedules.FindAsync(id, cancellationToken) ?? throw new NotFoundException("recurring");
        schedule.IsActive = active;
        await schedules.UpdateAsync(schedule, cancellationToken);
        return ToDto(schedule);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await schedules.FindAsync(id, cancellationToken) ?? throw new NotFoundException("recurring");
        await schedules.DeleteAsync(id, cancellationToken);
    }

    /// <summary>Makes what is due up to today (or the given day), for every active schedule.</summary>
    public async Task<RecurringRunResult> RunDueAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default)
    {
        var today = asOf ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var items = new List<RecurringRunItem>();

        foreach (var schedule in await schedules.ListAsync(cancellationToken))
        {
            for (var made = 0; made < MaxCatchUp && schedule.IsActive && schedule.NextRunDate <= today && (schedule.EndDate is null || schedule.NextRunDate <= schedule.EndDate); made++)
            {
                var date = schedule.NextRunDate;
                try
                {
                    var (id, number, issued) = await MakeAsync(schedule, date, cancellationToken);
                    items.Add(new RecurringRunItem(schedule.Id, schedule.Name, schedule.TemplateKind, date, id, number, issued, null));
                }
                catch (ValidationException e)
                {
                    // This schedule waits (the next run tries the same date again); the others go on.
                    items.Add(new RecurringRunItem(schedule.Id, schedule.Name, schedule.TemplateKind, date, null, null, false, e.Issues.FirstOrDefault()?.Code ?? "recurring.failed"));
                    break;
                }

                schedule.LastRunDate = date;
                schedule.RunCount++;
                schedule.NextRunDate = Recurrence.Next(date, schedule.Frequency, schedule.AnchorDay);
                await schedules.UpdateAsync(schedule, cancellationToken);
            }
        }

        return new RecurringRunResult(items);
    }

    // ---------------------------------------------------------------- Helpers

    private async Task<(Guid Id, string? Number, bool Issued)> MakeAsync(RecurringSchedule schedule, DateOnly date, CancellationToken cancellationToken)
    {
        if (schedule.Template == RecurringTemplate.Document)
        {
            var template = JsonSerializer.Deserialize<DocumentInput>(schedule.PayloadJson, Json)!;
            // The rate and the due date follow the new date: the latest rate on it, and the party's payment terms.
            var input = template with { Date = date, DueDate = null, ExchangeRate = null };
            var made = schedule.AutoIssue ? await documents.IssueAsync(null, input, cancellationToken) : await documents.SaveDraftAsync(null, input, cancellationToken);
            return (made.Id, made.Number, schedule.AutoIssue);
        }
        else
        {
            var template = JsonSerializer.Deserialize<VoucherInput>(schedule.PayloadJson, Json)!;
            var input = template with { Date = date, ExchangeRate = null };
            var made = schedule.AutoIssue ? await vouchers.SaveAndPostAsync(null, input, cancellationToken) : await vouchers.SaveDraftAsync(null, input, cancellationToken);
            return (made.Id, made.Number, schedule.AutoIssue);
        }
    }

    private async Task<RecurringDto> AddAsync(RecurringTemplate template, string kind, string payload, RecurringInput input, CancellationToken cancellationToken)
    {
        Check(input);
        var company = files.Current ?? throw new Companies.CompanyFileException(Companies.CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var schedule = new RecurringSchedule
        {
            CompanyId = company.Id,
            Name = input.Name.Trim(),
            Template = template,
            TemplateKind = kind,
            PayloadJson = payload,
            Frequency = input.Frequency,
            NextRunDate = input.NextRunDate,
            AnchorDay = input.NextRunDate.Day,
            EndDate = input.EndDate,
            AutoIssue = input.AutoIssue,
        };
        await schedules.AddAsync(schedule, cancellationToken);
        return ToDto(schedule);
    }

    private static void Check(RecurringInput input)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(input.Name))
            issues.Add(new("name", "recurring.name-required"));
        if (input.NextRunDate == default)
            issues.Add(new("nextRunDate", "recurring.next-date-required"));
        else if (input.EndDate is { } end && end < input.NextRunDate)
            issues.Add(new("endDate", "recurring.end-before-next"));
        if (issues.Count > 0)
            throw new ValidationException(issues);
    }

    private static RecurringDto ToDto(RecurringSchedule s) =>
        new(s.Id, s.Name, s.Template, s.TemplateKind, s.Frequency, s.NextRunDate, s.EndDate, s.AutoIssue, s.IsActive, s.LastRunDate, s.RunCount);

    private static ValidationException Refused(string field, string code) => new([new ValidationIssue(field, code)]);
}
