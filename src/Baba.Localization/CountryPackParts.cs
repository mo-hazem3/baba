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
    bool IsPosting,
    AccountRole Role = AccountRole.None);

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
/// <summary>How often a tax return is filed.</summary>
public enum TaxReturnFrequency
{
    Monthly,
    Quarterly,
}

/// <summary>
/// The tax return of a country: its name and how often it is filed. The return itself is built from the company's documents and tax codes
/// (a generic layout by tax code); the official box layout of each authority is not mapped yet.
/// </summary>
public interface ITaxReturnDefinition
{
    string NameEn { get; }
    string NameAr { get; }
    TaxReturnFrequency Frequency { get; }
}

public sealed record TaxReturnDefinition(string NameEn, string NameAr, TaxReturnFrequency Frequency) : ITaxReturnDefinition;

/// <summary>What a country's e-invoicing needs to know about an invoice that was issued.</summary>
public sealed record EInvoiceFacts(
    string SellerNameEn,
    string SellerNameAr,
    string SellerTaxNumber,
    DateTimeOffset IssuedAt,
    decimal TotalWithTax,
    decimal TaxTotal);

/// <summary>
/// A country's e-invoicing (the authority's electronic invoice rules). <see cref="BuildQrCode"/> makes the text printed as a QR code on
/// a tax invoice. Submitting invoices to the authority is added to a country's provider when it can be checked against the authority's
/// own test system.
/// </summary>
public interface IEInvoicingProvider
{
    string NameEn { get; }
    string NameAr { get; }

    /// <summary>Which of the company's tax numbers (a <see cref="TaxRegistrationRule.Key"/>) is the seller's number on the invoice.</summary>
    string SellerTaxNumberKey { get; }

    /// <summary>The text of the QR code of an issued invoice, or null when it cannot be made (a field is missing).</summary>
    string? BuildQrCode(EInvoiceFacts facts);
}

public interface IWithholdingRules { }

/// <summary>
/// A social insurance scheme: what the employee and the employer each pay as a percentage of the insurable wage, within optional
/// limits (a wage under the floor is charged as the floor, a wage over the ceiling as the ceiling). These are defaults copied into the
/// company's payroll settings, where the accountant can correct them: rates and limits change and must be checked with the authority.
/// </summary>
public sealed record SocialInsuranceScheme(
    string NameEn,
    string NameAr,
    decimal EmployeePercent,
    decimal EmployerPercent,
    decimal? MonthlyFloor,
    decimal? MonthlyCeiling);

/// <summary>The end-of-service gratuity a country's labour law gives an employee, as a function of the wage and the years served.</summary>
public interface IEndOfServiceRules
{
    string NameEn { get; }
    string NameAr { get; }

    /// <summary>What the employee would be owed if the employer ended the contract today (the amount to keep as a provision).</summary>
    decimal Gratuity(decimal monthlyWage, decimal yearsOfService);
}

/// <summary>
/// What a country decides about payroll (brief section 10.4): social insurance for nationals and for foreigners, and end-of-service.
/// Null for a part means the country has none of it.
/// </summary>
public interface IPayrollRules
{
    SocialInsuranceScheme? InsuranceForNationals { get; }
    SocialInsuranceScheme? InsuranceForForeigners { get; }
    IEndOfServiceRules? EndOfService { get; }
}

public sealed record PayrollRules(
    SocialInsuranceScheme? InsuranceForNationals,
    SocialInsuranceScheme? InsuranceForForeigners,
    IEndOfServiceRules? EndOfService) : IPayrollRules;

/// <summary>
/// Gratuity worked out from days of wage per year of service: some days for each of the first five years and some for each year after,
/// with an optional cap in months of wage. Used by the packs that pay a gratuity (the day counts are each country's own).
/// </summary>
public sealed record DaysPerYearGratuity(
    string NameEn,
    string NameAr,
    decimal DaysFirstFiveYears,
    decimal DaysAfterFiveYears,
    decimal DaysInMonth,
    decimal? CapInMonths) : IEndOfServiceRules
{
    public decimal Gratuity(decimal monthlyWage, decimal yearsOfService)
    {
        if (monthlyWage <= 0 || yearsOfService <= 0)
            return 0;

        var dayWage = monthlyWage / DaysInMonth;
        var first = Math.Min(yearsOfService, 5m);
        var after = Math.Max(yearsOfService - 5m, 0m);
        var amount = dayWage * (first * DaysFirstFiveYears + after * DaysAfterFiveYears);
        return CapInMonths is { } cap ? Math.Min(amount, monthlyWage * cap) : amount;
    }
}

public interface IStatutoryReports { }

/// <summary>What a pack supports. The UI shows country-dependent screens from these flags, never from a country code.</summary>
public sealed record CountryCapabilities(
    bool HasTaxCodes,
    bool HasTaxReturn,
    bool HasEInvoicing,
    bool HasWithholding,
    bool HasPayroll,
    bool HasStatutoryReports);
