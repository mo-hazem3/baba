using Baba.Domain.Accounting;

namespace Baba.Application.Accounting;

/// <summary>Reads and writes customers and suppliers. Implemented by Infrastructure.</summary>
public interface IPartyStore
{
    Task<IReadOnlyList<Party>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The parties that voucher lines (including drafts) point at, so they cannot simply be deleted.</summary>
    Task<IReadOnlySet<Guid>> PartyIdsInUseAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Party party, CancellationToken cancellationToken = default);
    Task UpdateAsync(Party party, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid partyId, CancellationToken cancellationToken = default);
}

public sealed record PartyInput(
    PartyKind Kind,
    string Code,
    string NameAr,
    string NameEn,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxNumber,
    decimal CreditLimit,
    int PaymentTermsDays,
    string? Notes);

/// <summary>A party as the screens see it. <see cref="Balance"/> is what the party owes (customers) or is owed (suppliers), in the base currency.</summary>
public sealed record PartyDto(
    Guid Id,
    PartyKind Kind,
    string Code,
    string NameAr,
    string NameEn,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxNumber,
    decimal CreditLimit,
    int PaymentTermsDays,
    bool IsActive,
    string? Notes,
    bool InUse,
    decimal Balance);

/// <summary>
/// Customers and suppliers (brief section 10.2). A party that has been used on a voucher can be switched off but not deleted, so
/// old statements still show who it was.
/// </summary>
public sealed class PartyService(IPartyStore parties, ILedgerQuery ledger)
{
    public const int MaxPaymentTermsDays = 365;

    public async Task<IReadOnlyList<PartyDto>> ListAsync(PartyKind? kind = null, CancellationToken cancellationToken = default)
    {
        var all = await parties.ListAsync(cancellationToken);
        var inUse = await parties.PartyIdsInUseAsync(cancellationToken);
        var balances = (await ledger.PartyTotalsAsync(null, cancellationToken)).ToDictionary(t => t.PartyId);

        return all.Where(p => kind is null || p.Kind == kind)
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToDto(p, inUse.Contains(p.Id), Balance(p, balances)))
            .ToList();
    }

    public async Task<PartyDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var party = (await parties.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == id) ?? throw NotFound();
        var balances = (await ledger.PartyTotalsAsync(null, cancellationToken)).ToDictionary(t => t.PartyId);
        return ToDto(party, (await parties.PartyIdsInUseAsync(cancellationToken)).Contains(id), Balance(party, balances));
    }

    public async Task<PartyDto> CreateAsync(PartyInput input, CancellationToken cancellationToken = default)
    {
        var all = await parties.ListAsync(cancellationToken);
        var party = new Party();
        Apply(party, input);

        Throw(Validate(party, all));
        await parties.AddAsync(party, cancellationToken);
        return ToDto(party, inUse: false, balance: 0);
    }

    public async Task<PartyDto> UpdateAsync(Guid id, PartyInput input, CancellationToken cancellationToken = default)
    {
        var all = await parties.ListAsync(cancellationToken);
        var party = all.FirstOrDefault(p => p.Id == id) ?? throw NotFound();
        var inUse = (await parties.PartyIdsInUseAsync(cancellationToken)).Contains(id);

        var changed = new Party { Id = party.Id, CompanyId = party.CompanyId, IsActive = party.IsActive };
        Apply(changed, input);
        var issues = Validate(changed, all).ToList();
        // A customer or supplier that has entries keeps its kind: the entries were made with that meaning.
        if (inUse && changed.Kind != party.Kind)
            issues.Add(new("kind", "party.kind-in-use"));
        Throw(issues);

        party.Kind = changed.Kind;
        party.Code = changed.Code;
        party.NameAr = changed.NameAr;
        party.NameEn = changed.NameEn;
        party.Phone = changed.Phone;
        party.Email = changed.Email;
        party.Address = changed.Address;
        party.TaxNumber = changed.TaxNumber;
        party.CreditLimitScaled = changed.CreditLimitScaled;
        party.PaymentTermsDays = changed.PaymentTermsDays;
        party.Notes = changed.Notes;
        await parties.UpdateAsync(party, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<PartyDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var party = (await parties.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == id) ?? throw NotFound();
        party.IsActive = active;
        await parties.UpdateAsync(party, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if ((await parties.ListAsync(cancellationToken)).All(p => p.Id != id))
            throw NotFound();
        if ((await parties.PartyIdsInUseAsync(cancellationToken)).Contains(id))
            Throw([new PostingIssue("party", "party.in-use")]);

        await parties.DeleteAsync(id, cancellationToken);
    }

    /// <summary>What the party owes (customer) or is owed (supplier): debits less credits for a customer, the reverse for a supplier.</summary>
    internal static decimal Balance(Party party, IReadOnlyDictionary<Guid, PartyTotal> totals) =>
        totals.TryGetValue(party.Id, out var t) ? (party.Kind == PartyKind.Customer ? t.Debit - t.Credit : t.Credit - t.Debit) : 0;

    internal static IReadOnlyList<PostingIssue> Validate(Party party, IReadOnlyList<Party> all)
    {
        var issues = new List<PostingIssue>();
        if (party.Code.Length == 0)
            issues.Add(new("code", "party.code-required"));
        else if (all.Any(p => p.Id != party.Id && string.Equals(p.Code, party.Code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "party.code-duplicate"));
        if (party.NameAr.Length == 0 && party.NameEn.Length == 0)
            issues.Add(new("name", "party.name-required"));
        if (party.CreditLimitScaled < 0)
            issues.Add(new("creditLimit", "party.credit-limit-negative"));
        if (party.PaymentTermsDays is < 0 or > MaxPaymentTermsDays)
            issues.Add(new("paymentTermsDays", "party.terms-invalid"));
        return issues;
    }

    private static void Apply(Party party, PartyInput input)
    {
        party.Kind = input.Kind;
        party.Code = input.Code?.Trim() ?? "";
        party.NameAr = input.NameAr?.Trim() ?? "";
        party.NameEn = input.NameEn?.Trim() ?? "";
        party.Phone = Clean(input.Phone);
        party.Email = Clean(input.Email);
        party.Address = Clean(input.Address);
        party.TaxNumber = Clean(input.TaxNumber);
        party.CreditLimit = input.CreditLimit;
        party.PaymentTermsDays = input.PaymentTermsDays;
        party.Notes = Clean(input.Notes);

        if (party.NameAr.Length == 0) party.NameAr = party.NameEn;
        if (party.NameEn.Length == 0) party.NameEn = party.NameAr;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static PartyDto ToDto(Party p, bool inUse, decimal balance) => new(
        p.Id, p.Kind, p.Code, p.NameAr, p.NameEn, p.Phone, p.Email, p.Address, p.TaxNumber,
        p.CreditLimit, p.PaymentTermsDays, p.IsActive, p.Notes, inUse, balance);

    private static void Throw(IReadOnlyCollection<PostingIssue> issues)
    {
        if (issues.Count > 0)
            throw new ValidationException(issues.Select(i => new ValidationIssue(i.Field, i.Code)).ToList());
    }

    private static NotFoundException NotFound() => new("party");
}
