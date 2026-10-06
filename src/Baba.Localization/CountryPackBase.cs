namespace Baba.Localization;

/// <summary>
/// Starting point for a country pack. A new pack only has to supply identity, currency and
/// calendar; everything else has a safe "not applicable" default that it can override.
/// </summary>
public abstract class CountryPackBase : ICountryPack
{
    public abstract CountryIdentity Identity { get; }
    public abstract CurrencyRules Currency { get; }
    public abstract CalendarRules Calendar { get; }

    public virtual IReadOnlyList<TaxCodeDefinition> TaxCodes => [];
    public virtual IReadOnlyList<TaxRegistrationRule> TaxRegistration => [];

    public virtual IReadOnlyList<ChartOfAccountsTemplate> ChartsOfAccounts => [DefaultChartOfAccounts.Template];

    public virtual DocumentRules DocumentRules => new(SubmittedDocumentsAreImmutable: false, BilingualPrintRequired: false);
    public virtual IReadOnlyList<TermOverride> Translations => [];

    public virtual ITaxReturnDefinition? TaxReturn => null;
    public virtual IEInvoicingProvider? EInvoicing => null;
    public virtual IWithholdingRules? Withholding => null;
    public virtual IPayrollRules? Payroll => null;
    public virtual IStatutoryReports? StatutoryReports => null;

    public CountryCapabilities Capabilities => new(
        HasTaxCodes: TaxCodes.Count > 0,
        HasTaxReturn: TaxReturn is not null,
        HasEInvoicing: EInvoicing is not null,
        HasWithholding: Withholding is not null,
        HasPayroll: Payroll is not null,
        HasStatutoryReports: StatutoryReports is not null);
}
