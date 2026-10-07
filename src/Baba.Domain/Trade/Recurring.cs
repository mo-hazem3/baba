namespace Baba.Domain.Trade;

public enum RecurrenceFrequency
{
    Weekly,
    Monthly,
    Quarterly,
    Yearly,
}

public enum RecurringTemplate
{
    /// <summary>A sales or purchase document (usually an invoice) that is made again each time.</summary>
    Document,

    /// <summary>A voucher (a journal entry, a payment or a receipt) that is made again each time.</summary>
    Voucher,
}

/// <summary>
/// Something that Baba makes again on a schedule (brief section 10.3): a monthly rent invoice, a standing journal entry. It keeps a copy
/// of the document or voucher as it was when the schedule was set up. Each run makes a new one from the copy, dated the day it was due,
/// either as a draft to check or issued straight away.
/// </summary>
public sealed class RecurringSchedule : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = "";
    public RecurringTemplate Template { get; set; }

    /// <summary>The kind of document or voucher that is made, by name (for lists).</summary>
    public string TemplateKind { get; set; } = "";

    /// <summary>The copy, as the JSON the API takes to make one.</summary>
    public string PayloadJson { get; set; } = "";

    public RecurrenceFrequency Frequency { get; set; }
    public DateOnly NextRunDate { get; set; }

    /// <summary>The day of the month the schedule started on, so a schedule that began on the 31st runs on the last day of shorter months and goes back to the 31st.</summary>
    public int AnchorDay { get; set; }

    public DateOnly? EndDate { get; set; }

    /// <summary>Issue (post) what is made, instead of leaving it as a draft.</summary>
    public bool AutoIssue { get; set; }

    public bool IsActive { get; set; } = true;
    public DateOnly? LastRunDate { get; set; }
    public int RunCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public static class Recurrence
{
    /// <summary>The run after <paramref name="current"/>. Months keep the anchor day where the month is long enough.</summary>
    public static DateOnly Next(DateOnly current, RecurrenceFrequency frequency, int anchorDay)
    {
        if (frequency == RecurrenceFrequency.Weekly)
            return current.AddDays(7);

        var months = frequency switch { RecurrenceFrequency.Monthly => 1, RecurrenceFrequency.Quarterly => 3, _ => 12 };
        var first = new DateOnly(current.Year, current.Month, 1).AddMonths(months);
        return new DateOnly(first.Year, first.Month, Math.Min(anchorDay, DateTime.DaysInMonth(first.Year, first.Month)));
    }
}
