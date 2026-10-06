using Baba.Domain;

namespace Baba.Localization;

// The data parts of a country pack. Every part is plain data so most country
// changes need no new code (see docs/countries/README.md).

/// <summary>Who the country is. <see cref="Code"/> is the ISO 3166 alpha-2 code and the pack's unique key.</summary>
public sealed record CountryIdentity(
    string Code,
    string NameEn,
    string NameAr,
    IReadOnlyList<string> Languages);

/// <summary>Default currency, its minor units and the unit names used when writing amounts in words.</summary>
public sealed record CurrencyRules(
    Currency Currency,
    string MajorNameEn,
    string MajorNameAr,
    string MinorNameEn,
    string MinorNameAr);

public enum CalendarKind
{
    Gregorian,
    HijriUmmAlQura,
}

public sealed record CalendarRules(
    IReadOnlyList<CalendarKind> Calendars,
    IReadOnlyList<DayOfWeek> DefaultWeekend,
    int DefaultFiscalYearStartMonth);

public enum TaxCategory
{
    Standard,
    Zero,
    Exempt,
    OutOfScope,
}

/// <summary>A tax code with effective dates so a rate change never rewrites history. <c>Rate</c> is a percentage (14 means 14%).</summary>
public sealed record TaxCodeDefinition(
    string Code,
    string NameEn,
    string NameAr,
    decimal Rate,
    TaxCategory Category,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null);

/// <summary>A tax or registration number a company or party may need, with an optional validation pattern (regex).</summary>
public sealed record TaxRegistrationRule(
    string Key,
    string NameEn,
    string NameAr,
    string? Pattern,
    bool RequiredForCompany);

public sealed record AccountSeed(
    string Code,
    string NameEn,
    string NameAr,
    AccountType Type,
    string? ParentCode,
    bool IsPosting);

/// <summary>An account list used to seed a new company.</summary>
public sealed record ChartOfAccountsTemplate(
    string Key,
    string NameEn,
    string NameAr,
    IReadOnlyList<AccountSeed> Accounts);

/// <summary>Rules for how invoices and other documents behave in this country.</summary>
public sealed record DocumentRules(
    bool SubmittedDocumentsAreImmutable,
    bool BilingualPrintRequired);

/// <summary>A country-specific wording, for example what VAT is called locally.</summary>
public sealed record TermOverride(string Key, string En, string Ar);

// Optional parts. A pack returns null for a part that does not apply.
// Each is defined in full by the phase that needs it (tax return: Phase 4, e-invoicing: Phase 4,
// payroll: Phase 6) and is deliberately small until then.
public interface ITaxReturnDefinition { }

public interface IEInvoicingProvider { }

public interface IWithholdingRules { }

public interface IPayrollRules { }

public interface IStatutoryReports { }

/// <summary>What a pack supports. The UI shows country-dependent screens from these flags, never from a country code.</summary>
public sealed record CountryCapabilities(
    bool HasTaxCodes,
    bool HasTaxReturn,
    bool HasEInvoicing,
    bool HasWithholding,
    bool HasPayroll,
    bool HasStatutoryReports);
