using Baba.Domain.Accounting;

namespace Baba.Domain.Inventory;

/// <summary>How the value of a movement is found.</summary>
public enum CostMode
{
    /// <summary>The movement says what it is worth: a purchase at its invoice amount, an opening at its cost.</summary>
    Given,

    /// <summary>Stock going out leaves at the average cost of what is on hand at that moment.</summary>
    OutAtAverage,

    /// <summary>Stock coming in without a cost (a count that found more) comes in at the average cost at that moment.</summary>
    InAtAverage,

    /// <summary>Stock taken back from a customer comes in at what it cost when it was sold, so the profit on the sale is reversed exactly.</summary>
    InAtSourceCost,

    /// <summary>A transfer between warehouses: changes where the stock is (and so what each warehouse holds), never what all of it is worth.</summary>
    Transfer,
}

public sealed record MovementInput(
    Guid Id,
    Guid ProductId,
    Guid WarehouseId,
    DateOnly Date,
    DateTime SourceCreatedAt,
    long QuantityScaled,
    CostMode Mode,
    long GivenValueScaled = 0,
    /// <summary>The invoice or stock document this movement belongs to.</summary>
    Guid OwnerId = default,
    /// <summary>For <see cref="CostMode.InAtSourceCost"/>: the invoice whose cost to use.</summary>
    Guid? SourceOwnerId = null);

/// <summary>Where, and when, a product would have gone below zero.</summary>
public sealed record StockShortfall(Guid ProductId, Guid WarehouseId, DateOnly Date, long QuantityScaled, Guid MovementId);

public sealed record ReplayResult(IReadOnlyDictionary<Guid, long> Values, StockShortfall? Shortfall);

/// <summary>
/// Works out what every movement is worth, with weighted-average costing (brief section 10.4), by going through the movements of each product
/// in date order: stock coming in adds its quantity and value to what is on hand, stock going out takes away the average cost of what is on
/// hand. Doing it again from the start whenever anything changes is what makes a purchase entered late (or edited) correct all the sales after
/// it. Of two movements on one day the one coming in goes first (so a sale and the purchase that covers it the same day work), then the
/// older document. A product or warehouse that would go below zero is reported, so the change that caused it can be refused.
/// </summary>
public static class StockEngine
{
    public static ReplayResult Replay(IEnumerable<MovementInput> movements, Currency currency)
    {
        var values = new Dictionary<Guid, long>();
        StockShortfall? firstShortfall = null;

        foreach (var product in movements.GroupBy(m => m.ProductId))
        {
            var ordered = product
                .OrderBy(m => m.Date)
                .ThenBy(m => m.QuantityScaled < 0 ? 1 : 0) // coming in before going out
                .ThenBy(m => m.SourceCreatedAt)
                .ThenBy(m => m.Id)
                .ToList();

            decimal quantity = 0, value = 0, lastAverage = 0; // what is on hand, in units and in the company's currency
            var perWarehouse = new Dictionary<Guid, long>();
            var soldCost = new Dictionary<Guid, (decimal Quantity, decimal Value)>(); // by invoice: what the stock it took out cost

            foreach (var m in ordered)
            {
                var units = Scaled.ToDecimal(m.QuantityScaled);
                decimal worth;
                switch (m.Mode)
                {
                    case CostMode.Transfer:
                        // The stock keeps its cost when it moves: out of one warehouse at the average, into the other at the same, so the total never changes.
                        worth = Money.Round((quantity > 0 ? value / quantity : lastAverage) * units, currency);
                        break;
                    case CostMode.Given:
                        worth = Scaled.ToDecimal(m.GivenValueScaled);
                        break;
                    case CostMode.OutAtAverage:
                        // Taking out everything takes out all the value, so no fraction of a unit is ever left behind.
                        worth = quantity > 0 ? (-units >= quantity ? -value : -Money.Round(value * -units / quantity, currency)) : 0;
                        break;
                    case CostMode.InAtSourceCost when m.SourceOwnerId is { } source && soldCost.TryGetValue(source, out var sold) && sold.Quantity != 0:
                        worth = Money.Round(sold.Value * units / sold.Quantity, currency);
                        break;
                    default: // InAtAverage, or a return whose sale is not known
                        worth = Money.Round((quantity > 0 ? value / quantity : lastAverage) * units, currency);
                        break;
                }

                if (m.Mode != CostMode.Transfer)
                {
                    quantity += units;
                    value += worth;
                    if (quantity > 0)
                        lastAverage = value / quantity;
                    if (m.Mode == CostMode.OutAtAverage && m.OwnerId != default)
                    {
                        var sold = soldCost.GetValueOrDefault(m.OwnerId);
                        soldCost[m.OwnerId] = (sold.Quantity - units, sold.Value - worth); // kept positive: what was taken out
                    }
                }

                values[m.Id] = Scaled.ToScaled(worth);

                // Each warehouse must hold its own stock too.
                var held = perWarehouse.GetValueOrDefault(m.WarehouseId) + m.QuantityScaled;
                perWarehouse[m.WarehouseId] = held;
                if ((held < 0 || (m.Mode != CostMode.Transfer && quantity < 0)) && firstShortfall is null)
                    firstShortfall = new StockShortfall(m.ProductId, m.WarehouseId, m.Date, held < 0 ? held : Scaled.ToScaled(quantity), m.Id);
            }
        }

        return new ReplayResult(values, firstShortfall);
    }
}
