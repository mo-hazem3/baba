using Baba.Application.Trade;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;
using Baba.Infrastructure.CompanyFiles;
using Microsoft.EntityFrameworkCore;

namespace Baba.Infrastructure.Trade;

public sealed class DocumentStore(ICompanyDbContextFactory contexts) : IDocumentStore
{
    public async Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var document = await context.Documents.AsNoTracking().Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        document?.Lines.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
        return document;
    }

    public async Task<IReadOnlyList<Document>> SearchAsync(DocumentSearch search, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var query = context.Documents.AsNoTracking().Include(d => d.Lines).AsQueryable();
        if (search.Kind is { } kind) query = query.Where(d => d.Kind == kind);
        if (search.Status is { } status) query = query.Where(d => d.Status == status);
        if (search.PartyId is { } party) query = query.Where(d => d.PartyId == party);
        if (search.From is { } from) query = query.Where(d => d.Date >= from);
        if (search.To is { } to) query = query.Where(d => d.Date <= to);

        var found = await query
            .OrderByDescending(d => d.Date).ThenByDescending(d => d.CreatedAt)
            .Take(Math.Clamp(search.Limit, 1, 10_000))
            .ToListAsync(cancellationToken);
        foreach (var document in found)
            document.Lines.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));
        return found;
    }

    public async Task SaveAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        foreach (var document in documents)
        {
            var stored = await context.Documents.Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == document.Id, cancellationToken);
            if (stored is null)
            {
                context.Documents.Add(document);
                continue;
            }

            // Copy the new values onto the stored row and match lines by id, so the audit log shows only what really changed.
            context.Entry(stored).CurrentValues.SetValues(document);
            var incoming = document.Lines.ToDictionary(l => l.Id);
            foreach (var old in stored.Lines.Where(l => !incoming.ContainsKey(l.Id)).ToList())
                context.DocumentLines.Remove(old);
            foreach (var line in document.Lines)
            {
                var existing = stored.Lines.FirstOrDefault(l => l.Id == line.Id);
                if (existing is null)
                    context.DocumentLines.Add(line);
                else
                    context.Entry(existing).CurrentValues.SetValues(line);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var stored = await context.Documents.Include(d => d.Lines).SingleAsync(d => d.Id == id, cancellationToken);
        context.Documents.Remove(stored); // tracked, so the document and its lines are written to the audit log
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> NextNumberAsync(DocumentKind kind, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var startMonth = await context.Companies.Select(c => c.FiscalYearStartMonth).SingleAsync(cancellationToken);
        var fiscalYear = FiscalYear.Of(date, startMonth);
        var name = kind.ToString();

        var sequence = await context.NumberSequences.SingleOrDefaultAsync(s => s.Kind == name && s.FiscalYear == fiscalYear, cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence { Kind = name, FiscalYear = fiscalYear };
            context.NumberSequences.Add(sequence);
        }

        sequence.LastNumber++;
        await context.SaveChangesAsync(cancellationToken);
        return DocumentNumber.Format(kind, fiscalYear, sequence.LastNumber);
    }

    public async Task<IReadOnlySet<Guid>> ProductIdsInUseAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contexts.Create();
        var used = await context.DocumentLines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).Distinct().ToListAsync(cancellationToken);
        return used.ToHashSet();
    }
}
