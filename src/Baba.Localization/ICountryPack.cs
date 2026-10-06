namespace Baba.Localization;

/// <summary>
/// Everything the app needs to know about one country. This is the only way the core
/// talks about a country: no code outside <c>Baba.Localization/&lt;Country&gt;/</c> may name or
/// check a country. To support a new need, add it here and let every pack implement it
/// (possibly as "not applicable").
/// </summary>
public interface ICountryPack
{
    CountryIdentity Identity { get; }
    CurrencyRules Currency { get; }
    CalendarRules Calendar { get; }

    /// <summary>Empty when the country has no such tax; the design still supports adding codes later.</summary>
    IReadOnlyList<TaxCodeDefinition> TaxCodes { get; }
    IReadOnlyList<TaxRegistrationRule> TaxRegistration { get; }

    /// <summary>The chart(s) offered when creating a company. The first one is the default.</summary>
    IReadOnlyList<ChartOfAccountsTemplate> ChartsOfAccounts { get; }

    DocumentRules DocumentRules { get; }
    IReadOnlyList<TermOverride> Translations { get; }

    // Optional parts: null means "not supported in this country".
    ITaxReturnDefinition? TaxReturn { get; }
    IEInvoicingProvider? EInvoicing { get; }
    IWithholdingRules? Withholding { get; }
    IPayrollRules? Payroll { get; }
    IStatutoryReports? StatutoryReports { get; }

    CountryCapabilities Capabilities { get; }
}
