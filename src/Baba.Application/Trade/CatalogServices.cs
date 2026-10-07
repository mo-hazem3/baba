using Baba.Application.Accounting;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Domain.Accounting;
using Baba.Domain.Trade;
using Baba.Localization;

namespace Baba.Application.Trade;

public sealed record ProductInput(
    string Code, string NameAr, string NameEn, string? Unit, decimal SalePrice, decimal PurchasePrice, Guid? SalesAccountId, Guid? PurchaseAccountId, Guid? TaxCodeId = null);

public sealed record ProductDto(
    Guid Id, string Code, string NameAr, string NameEn, string? Unit, decimal SalePrice, decimal PurchasePrice,
    Guid? SalesAccountId, Guid? PurchaseAccountId, bool IsActive, bool InUse, Guid? TaxCodeId = null);

/// <summary>
/// Products and services (brief section 10.3). A product that has been used on a document can be switched off but not deleted, so old
/// documents still say what was sold.
/// </summary>
public sealed class ProductService(IProductStore products, IDocumentStore documents, IAccountStore accounts, ITaxCodeStore taxCodes)
{
    public async Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var inUse = await documents.ProductIdsInUseAsync(cancellationToken);
        return (await products.ListAsync(cancellationToken))
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToDto(p, inUse.Contains(p.Id))).ToList();
    }

    public async Task<ProductDto> CreateAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        var all = await products.ListAsync(cancellationToken);
        var product = new Product();
        Apply(product, input);
        Throw(await ValidateAsync(product, all, cancellationToken));
        await products.AddAsync(product, cancellationToken);
        return ToDto(product, inUse: false);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, ProductInput input, CancellationToken cancellationToken = default)
    {
        var all = await products.ListAsync(cancellationToken);
        var product = all.FirstOrDefault(p => p.Id == id) ?? throw new NotFoundException("product");
        var changed = new Product { Id = product.Id, CompanyId = product.CompanyId, IsActive = product.IsActive };
        Apply(changed, input);
        Throw(await ValidateAsync(changed, all, cancellationToken));

        product.Code = changed.Code;
        product.NameAr = changed.NameAr;
        product.NameEn = changed.NameEn;
        product.Unit = changed.Unit;
        product.SalePriceScaled = changed.SalePriceScaled;
        product.PurchasePriceScaled = changed.PurchasePriceScaled;
        product.SalesAccountId = changed.SalesAccountId;
        product.PurchaseAccountId = changed.PurchaseAccountId;
        product.TaxCodeId = changed.TaxCodeId;
        await products.UpdateAsync(product, cancellationToken);
        return ToDto(product, (await documents.ProductIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task<ProductDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var product = (await products.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == id) ?? throw new NotFoundException("product");
        product.IsActive = active;
        await products.UpdateAsync(product, cancellationToken);
        return ToDto(product, (await documents.ProductIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if ((await products.ListAsync(cancellationToken)).All(p => p.Id != id))
            throw new NotFoundException("product");
        if ((await documents.ProductIdsInUseAsync(cancellationToken)).Contains(id))
            throw new ValidationException([new ValidationIssue("product", "product.in-use")]);
        await products.DeleteAsync(id, cancellationToken);
    }

    private async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(Product product, IReadOnlyList<Product> all, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        if (product.Code.Length == 0)
            issues.Add(new("code", "product.code-required"));
        else if (all.Any(p => p.Id != product.Id && string.Equals(p.Code, product.Code, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("code", "product.code-duplicate"));
        if (product.NameAr.Length == 0 && product.NameEn.Length == 0)
            issues.Add(new("name", "product.name-required"));
        if (product.SalePriceScaled < 0 || product.PurchasePriceScaled < 0)
            issues.Add(new("salePrice", "product.price-negative"));

        var chart = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id);
        if (product.SalesAccountId is { } sales && (!chart.TryGetValue(sales, out var s) || !s.IsPosting))
            issues.Add(new("salesAccount", "product.account-invalid"));
        if (product.PurchaseAccountId is { } purchase && (!chart.TryGetValue(purchase, out var p2) || !p2.IsPosting))
            issues.Add(new("purchaseAccount", "product.account-invalid"));
        if (product.TaxCodeId is { } taxCodeId && (await taxCodes.ListAsync(cancellationToken)).All(c => c.Id != taxCodeId))
            issues.Add(new("taxCode", "product.tax-code-unknown"));
        return issues;
    }

    private static void Apply(Product product, ProductInput input)
    {
        product.Code = input.Code?.Trim() ?? "";
        product.NameAr = input.NameAr?.Trim() ?? "";
        product.NameEn = input.NameEn?.Trim() ?? "";
        product.Unit = string.IsNullOrWhiteSpace(input.Unit) ? null : input.Unit.Trim();
        product.SalePrice = input.SalePrice;
        product.PurchasePrice = input.PurchasePrice;
        product.SalesAccountId = input.SalesAccountId == Guid.Empty ? null : input.SalesAccountId;
        product.PurchaseAccountId = input.PurchaseAccountId == Guid.Empty ? null : input.PurchaseAccountId;
        product.TaxCodeId = input.TaxCodeId == Guid.Empty ? null : input.TaxCodeId;
        if (product.NameAr.Length == 0) product.NameAr = product.NameEn;
        if (product.NameEn.Length == 0) product.NameEn = product.NameAr;
    }

    private static ProductDto ToDto(Product p, bool inUse) =>
        new(p.Id, p.Code, p.NameAr, p.NameEn, p.Unit, p.SalePrice, p.PurchasePrice, p.SalesAccountId, p.PurchaseAccountId, p.IsActive, inUse, p.TaxCodeId);

    private static void Throw(IReadOnlyCollection<ValidationIssue> issues)
    {
        if (issues.Count > 0)
            throw new ValidationException(issues.ToList());
    }
}

public sealed record PriceListLineInput(Guid ProductId, decimal Price);

public sealed record PriceListInput(string NameAr, string NameEn, string? CurrencyCode, IReadOnlyList<PriceListLineInput> Lines);

public sealed record PriceListDto(Guid Id, string NameAr, string NameEn, string CurrencyCode, bool IsActive, IReadOnlyList<PriceListLineInput> Lines, bool InUse);

/// <summary>Price lists (brief section 10.3): a named set of prices, in the company's currency or in another one, that a customer can be given.</summary>
public sealed class PriceListService(IPriceListStore priceLists, IProductStore products, ICompanyFiles files)
{
    public async Task<IReadOnlyList<PriceListDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var inUse = await priceLists.PriceListIdsInUseAsync(cancellationToken);
        return (await priceLists.ListAsync(cancellationToken))
            .OrderBy(l => l.NameEn, StringComparer.OrdinalIgnoreCase)
            .Select(l => ToDto(l, inUse.Contains(l.Id))).ToList();
    }

    public async Task<PriceListDto> CreateAsync(PriceListInput input, CancellationToken cancellationToken = default)
    {
        var list = new PriceList();
        await ApplyAsync(list, input, cancellationToken);
        await priceLists.SaveAsync(list, cancellationToken);
        return ToDto(list, inUse: false);
    }

    public async Task<PriceListDto> UpdateAsync(Guid id, PriceListInput input, CancellationToken cancellationToken = default)
    {
        var list = (await priceLists.ListAsync(cancellationToken)).FirstOrDefault(l => l.Id == id) ?? throw new NotFoundException("price-list");
        await ApplyAsync(list, input, cancellationToken);
        await priceLists.SaveAsync(list, cancellationToken);
        return ToDto(list, (await priceLists.PriceListIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task<PriceListDto> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var list = (await priceLists.ListAsync(cancellationToken)).FirstOrDefault(l => l.Id == id) ?? throw new NotFoundException("price-list");
        list.IsActive = active;
        await priceLists.SaveAsync(list, cancellationToken);
        return ToDto(list, (await priceLists.PriceListIdsInUseAsync(cancellationToken)).Contains(id));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if ((await priceLists.ListAsync(cancellationToken)).All(l => l.Id != id))
            throw new NotFoundException("price-list");
        if ((await priceLists.PriceListIdsInUseAsync(cancellationToken)).Contains(id))
            throw new ValidationException([new ValidationIssue("priceList", "price-list.in-use")]);
        await priceLists.DeleteAsync(id, cancellationToken);
    }

    private async Task ApplyAsync(PriceList list, PriceListInput input, CancellationToken cancellationToken)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var issues = new List<ValidationIssue>();
        var nameAr = input.NameAr?.Trim() ?? "";
        var nameEn = input.NameEn?.Trim() ?? "";
        if (nameAr.Length == 0 && nameEn.Length == 0)
            issues.Add(new("name", "price-list.name-required"));

        var currency = (input.CurrencyCode ?? "").Trim().ToUpperInvariant();
        if (currency == company.BaseCurrencyCode)
            currency = "";
        if (currency.Length > 0 && CurrencyCatalog.Find(currency) is null)
            issues.Add(new("currency", "price-list.currency-unknown"));

        var known = (await products.ListAsync(cancellationToken)).Select(p => p.Id).ToHashSet();
        var lines = input.Lines ?? [];
        for (var i = 0; i < lines.Count; i++)
        {
            if (!known.Contains(lines[i].ProductId))
                issues.Add(new($"lines[{i}].product", "price-list.product-unknown"));
            else if (lines.Take(i).Any(l => l.ProductId == lines[i].ProductId))
                issues.Add(new($"lines[{i}].product", "price-list.product-twice"));
            if (lines[i].Price < 0)
                issues.Add(new($"lines[{i}].price", "price-list.price-negative"));
        }

        if (issues.Count > 0)
            throw new ValidationException(issues);

        list.NameAr = nameAr.Length == 0 ? nameEn : nameAr;
        list.NameEn = nameEn.Length == 0 ? nameAr : nameEn;
        list.CurrencyCode = currency;
        list.Lines = lines.Select(l => new PriceListLine { PriceListId = list.Id, ProductId = l.ProductId, Price = l.Price }).ToList();
    }

    private static PriceListDto ToDto(PriceList l, bool inUse) =>
        new(l.Id, l.NameAr, l.NameEn, l.CurrencyCode, l.IsActive, l.Lines.Select(x => new PriceListLineInput(x.ProductId, x.Price)).ToList(), inUse);
}

/// <summary>The price and defaults to put on a document line when a product is chosen.</summary>
public sealed record ProductPrice(Guid ProductId, decimal Price, string Source, string? Unit, Guid? AccountId, string NameAr, string NameEn, Guid? TaxCodeId = null);

/// <summary>
/// Finds the price of a product for a document (brief section 10.3). A customer's price list wins when it is in the document's currency
/// and lists the product; otherwise the product's own price is used, converted from the company's currency at the document's rate.
/// Purchases always use the product's purchase price.
/// </summary>
public sealed class PricingService(IProductStore products, IPriceListStore priceLists, IPartyStore parties, ICompanyFiles files)
{
    public async Task<ProductPrice> PriceAsync(
        Guid productId, Guid? partyId, string? currencyCode, decimal exchangeRate, bool forSale, CancellationToken cancellationToken = default)
    {
        var company = files.Current ?? throw new CompanyFileException(CompanyFileProblem.NoCompanyOpen, "No company is open.");
        var product = (await products.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == productId) ?? throw new NotFoundException("product");
        var currency = string.IsNullOrWhiteSpace(currencyCode) ? company.BaseCurrencyCode : currencyCode.Trim().ToUpperInvariant();

        if (forSale && partyId is { } id)
        {
            var listId = (await parties.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == id)?.PriceListId;
            var list = listId is null ? null : (await priceLists.ListAsync(cancellationToken)).FirstOrDefault(l => l.Id == listId && l.IsActive);
            var listCurrency = list is null ? "" : (list.CurrencyCode.Length == 0 ? company.BaseCurrencyCode : list.CurrencyCode);
            var line = list?.Lines.FirstOrDefault(l => l.ProductId == productId);
            if (line is not null && listCurrency == currency)
                return Result(product, line.Price, "priceList", forSale);
        }

        var basePrice = forSale ? product.SalePrice : product.PurchasePrice;
        var price = currency == company.BaseCurrencyCode || exchangeRate <= 0 ? basePrice : Math.Round(basePrice / exchangeRate, 4, MidpointRounding.AwayFromZero);
        return Result(product, price, "product", forSale);
    }

    private static ProductPrice Result(Product p, decimal price, string source, bool forSale) =>
        new(p.Id, price, source, p.Unit, forSale ? p.SalesAccountId : p.PurchaseAccountId, p.NameAr, p.NameEn, p.TaxCodeId);
}
