using Baba.Application.Companies;
using Baba.Domain.Accounting;
using Baba.Localization;

namespace Baba.Application.Accounting;

/// <summary>Reads and writes the table of exchange rates. Implemented by Infrastructure.</summary>
public interface ICurrencyRateStore
{
    Task<IReadOnlyList<CurrencyRate>> ListAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CurrencyRate rate, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record CurrencyRateInput(string CurrencyCode, DateOnly Date, decimal Rate);

public sealed record CurrencyRateDto(Guid Id, string CurrencyCode, DateOnly Date, decimal Rate);

/// <summary>A rate found for a date: its value, and the date it was set for (null when the currency is the company's own, which is always 1).</summary>
public sealed record RateOnDate(decimal Rate, DateOnly? RateDate, bool Found);

/// <summary>
/// The exchange-rate table (brief section 5): one rate per currency and date, converting the currency into the company's base
/// currency. Documents and vouchers in a foreign currency start from the latest rate on or before their date.
/// </summary>
public sealed class ExchangeRateService(ICurrencyRateStore rates, ICompanyFiles files)
{
    public const decimal MaxRate = 1_000_000m;

    public async Task<IReadOnlyList<CurrencyRateDto>> ListAsync(string? currencyCode = null, CancellationToken cancellationToken = default) =>
        (await rates.ListAsync(cancellationToken))
            .Where(r => string.IsNullOrEmpty(currencyCode) || string.Equals(r.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.CurrencyCode, StringComparer.Ordinal).ThenByDescending(r => r.Date)
            .Select(ToDto).ToList();

    /// <summary>Adds the rate for a currency and date, or changes it if there already is one for that day.</summary>
    public async Task<CurrencyRateDto> SetAsync(CurrencyRateInput input, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var code = (input.CurrencyCode ?? "").Trim().ToUpperInvariant();

        var issues = new List<ValidationIssue>();
        if (CurrencyCatalog.Find(code) is null)
            issues.Add(new ValidationIssue("currency", "rate.currency-unknown"));
        else if (code == company.BaseCurrencyCode)
            issues.Add(new ValidationIssue("currency", "rate.currency-is-base"));
        if (input.Date == default)
            issues.Add(new ValidationIssue("date", "rate.date-required"));
        if (input.Rate <= 0 || input.Rate > MaxRate || FxRate.ToScaled(input.Rate) <= 0)
            issues.Add(new ValidationIssue("rate", "rate.rate-invalid"));
        if (issues.Count > 0)
            throw new ValidationException(issues);

        var existing = (await rates.ListAsync(cancellationToken)).FirstOrDefault(r => r.CurrencyCode == code && r.Date == input.Date);
        var rate = existing ?? new CurrencyRate { CurrencyCode = code, Date = input.Date };
        rate.Rate = input.Rate;
        await rates.SaveAsync(rate, cancellationToken);
        return ToDto(rate);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if ((await rates.ListAsync(cancellationToken)).All(r => r.Id != id))
            throw new NotFoundException("rate");
        await rates.DeleteAsync(id, cancellationToken);
    }

    /// <summary>The latest rate on or before a date. The company's own currency is always 1; a currency with no rate yet is "not found".</summary>
    public async Task<RateOnDate> RateOnAsync(string currencyCode, DateOnly date, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var code = (currencyCode ?? "").Trim().ToUpperInvariant();
        if (code == company.BaseCurrencyCode)
            return new RateOnDate(1m, null, true);

        var found = (await rates.ListAsync(cancellationToken))
            .Where(r => r.CurrencyCode == code && r.Date <= date)
            .OrderByDescending(r => r.Date).FirstOrDefault();
        return found is null ? new RateOnDate(0m, null, false) : new RateOnDate(found.Rate, found.Date, true);
    }

    private static CurrencyRateDto ToDto(CurrencyRate r) => new(r.Id, r.CurrencyCode, r.Date, r.Rate);
}
