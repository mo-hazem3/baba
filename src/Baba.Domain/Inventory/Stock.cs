using Baba.Domain.Accounting;

namespace Baba.Domain.Inventory;

/// <summary>A place stock is kept (brief section 10.4): a shop, a store room, a van.</summary>
public sealed class Warehouse : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsActive { get; set; } = true;

    /// <summary>The warehouse a document starts with (at most one).</summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum StockMovementKind
{
    /// <summary>Stock bought on a purchase invoice.</summary>
    Purchase,

    /// <summary>Stock sent back to a supplier on a debit note.</summary>
    PurchaseReturn,

    /// <summary>Stock sold on a sales invoice.</summary>
    Sale,

    /// <summary>Stock taken back from a customer on a credit note.</summary>
    SaleReturn,

    /// <summary>Stock that was there when the books started.</summary>
    Opening,

    /// <summary>A correction found by counting (more or less than the books said).</summary>
    Adjustment,

    TransferOut,
    TransferIn,
}

/// <summary>
/// One change of the quantity of one product in one warehouse. Quantity and value are signed (in is positive, out is negative); the value is in
/// the company's currency. Stock on hand and its value are always the sum of the movements: nothing is stored as a balance. Movements are made
/// by the invoices and stock documents that cause them and are worked out again, in date order, whenever any of them changes
/// (<see cref="StockEngine"/>).
/// </summary>
public sealed class StockMovement : Entity, ICompanyScoped, INotAudited
{
    public Guid CompanyId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateOnly Date { get; set; }
    public StockMovementKind Kind { get; set; }

    /// <summary>Ten-thousandths of the product's unit; positive in, negative out.</summary>
    public long QuantityScaled { get; set; }

    /// <summary>The cost of the movement in the company's currency, in ten-thousandths; the same sign as the quantity.</summary>
    public long ValueScaled { get; set; }

    /// <summary>How the value is found, kept so the movements can be worked out again (<see cref="StockEngine"/>).</summary>
    public CostMode Mode { get; set; }

    /// <summary>For <see cref="CostMode.Given"/>: the value the document says. The value above is this one then.</summary>
    public long GivenValueScaled { get; set; }

    /// <summary>For stock taken back: the invoice whose cost it comes back at.</summary>
    public Guid? SourceOwnerId { get; set; }

    /// <summary>The invoice or note that made the movement, or null for a stock document.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>The opening, adjustment or transfer that made the movement, or null for an invoice or note.</summary>
    public Guid? StockDocumentId { get; set; }

    /// <summary>When the invoice or stock document was created: of two movements on one day, the older document comes first.</summary>
    public DateTime SourceCreatedAt { get; set; }

    public decimal Quantity
    {
        get => Scaled.ToDecimal(QuantityScaled);
        set => QuantityScaled = Scaled.ToScaled(value);
    }

    public decimal Value
    {
        get => Scaled.ToDecimal(ValueScaled);
        set => ValueScaled = Scaled.ToScaled(value);
    }
}

public enum StockDocumentKind
{
    /// <summary>The stock on hand when the books start.</summary>
    Opening,

    /// <summary>More or less stock than the books say (found by a count, a loss, a find).</summary>
    Adjustment,

    /// <summary>Stock moved from one warehouse to another. Changes no value.</summary>
    Transfer,
}

/// <summary>An opening, an adjustment or a transfer of stock (brief section 10.4). Opening and adjustments post to the ledger; transfers do not.</summary>
public sealed class StockDocument : Entity, ICompanyScoped, IAuditable
{
    public Guid CompanyId { get; set; }
    public StockDocumentKind Kind { get; set; }

    /// <summary>OS-2026-0001, AD-2026-0001 or TR-2026-0001.</summary>
    public string Number { get; set; } = "";

    public DateOnly Date { get; set; }
    public string? Memo { get; set; }

    /// <summary>The other side of the stock account for an opening or an adjustment (stock gains and losses, or the opening balances account).</summary>
    public Guid? CounterAccountId { get; set; }

    /// <summary>The ledger voucher of an opening or adjustment.</summary>
    public Guid? VoucherId { get; set; }

    public List<StockDocumentLine> Lines { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class StockDocumentLine : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid StockDocumentId { get; set; }
    public int LineNumber { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }

    /// <summary>For a transfer, the warehouse the stock goes to.</summary>
    public Guid? ToWarehouseId { get; set; }

    /// <summary>An opening: the quantity there. An adjustment: the change (negative for a loss). A transfer: the quantity moved.</summary>
    public long QuantityScaled { get; set; }

    /// <summary>The cost of one unit in the company's currency, for stock coming in. Left empty, stock comes in at the current average cost.</summary>
    public long? UnitCostScaled { get; set; }

    public decimal Quantity
    {
        get => Scaled.ToDecimal(QuantityScaled);
        set => QuantityScaled = Scaled.ToScaled(value);
    }

    public decimal? UnitCost
    {
        get => UnitCostScaled is { } v ? Scaled.ToDecimal(v) : null;
        set => UnitCostScaled = value is { } d ? Scaled.ToScaled(d) : null;
    }
}
