using Baba.Domain.Trade;

namespace Baba.Application.Trade;

public sealed record DocumentSearch(
    DocumentKind? Kind = null,
    DocumentStatus? Status = null,
    Guid? PartyId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Limit = 2000);

/// <summary>Reads and writes sales and purchase documents. Implemented by Infrastructure.</summary>
public interface IDocumentStore
{
    /// <summary>The document with its lines (in line order), or null.</summary>
    Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Documents with their lines, newest first.</summary>
    Task<IReadOnlyList<Document>> SearchAsync(DocumentSearch search, CancellationToken cancellationToken = default);

    /// <summary>Saves new or changed documents together, in one transaction (a conversion changes two). Lines are matched by id.</summary>
    Task SaveAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The next number for a kind of document that does not post (QT-2026-0001, SO-2026-0001 ...), counted per fiscal year.</summary>
    Task<string> NextNumberAsync(DocumentKind kind, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>The products that document lines (including drafts) point at, so they cannot simply be deleted.</summary>
    Task<IReadOnlySet<Guid>> ProductIdsInUseAsync(CancellationToken cancellationToken = default);
}

public interface IProductStore
{
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IPriceListStore
{
    /// <summary>Price lists with their lines.</summary>
    Task<IReadOnlyList<PriceList>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves a new or changed price list; its lines are replaced by the ones given.</summary>
    Task SaveAsync(PriceList priceList, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The price lists that customers use, so they cannot simply be deleted.</summary>
    Task<IReadOnlySet<Guid>> PriceListIdsInUseAsync(CancellationToken cancellationToken = default);
}
