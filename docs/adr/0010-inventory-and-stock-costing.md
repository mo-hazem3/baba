# ADR 0010: Inventory, warehouses and weighted-average stock costing

- Status: accepted
- Date: 2026-10-07

## Context

Phase 5 (brief sections 10.4 and 13) adds stock to the documents of ADR 0008: items that are counted, several warehouses, transfers,
counts and adjustments, automatic cost of sales, reorder levels, and a valuation that agrees with the ledger ("done when stock and COGS
reconcile to the ledger"). The rule of ADR 0006 holds: only `PostingEngine` creates ledger entries, so stock never writes the ledger
itself; it asks `VoucherService` for system vouchers like invoices do.

## Decision

**Stock is the sum of its movements; nothing is stored as a balance.** A `StockMovement` is one signed quantity of one product in one
warehouse on one date, with its value in the company's currency. On hand is a sum, a valuation is a sum, and a correction to an old
document simply changes some movements. There are two owners of movements: an invoice or note (`DocumentId`) and a stock document
(`StockDocumentId`: opening stock `OS-`, adjustment `AD-`, transfer `TR-`).

**Costing is weighted average, worked out by replaying.** `StockEngine.Replay` (pure, in the domain) takes every movement of a product in
order (date, then ins before outs so a sale and the purchase covering it on the same day work, then the time the owner was created, then
id) and gives each movement its value by its cost mode: *Given* (a purchase or a return to a supplier: the document's own amount),
*OutAtAverage* (a sale or a loss: quantity times the running average), *InAtAverage* (stock found in a count with no cost given),
*InAtSourceCost* (a credit note: what the original sale cost, however the average has moved since), *Transfer* (out and in at the average,
net zero). Taking out all that remains takes all the remaining value, so no fraction of the smallest unit is ever left behind. A
purchase entered late and dated earlier therefore changes the cost of every later sale, and the engine is the single place that knows how.
FIFO is not built; the brief makes it optional and the replay design is where it would go.

**Costs are derived, and the derived vouchers follow them.** After any change `StockService` replays the products touched, and for every
owner whose movement values changed it regenerates that owner's cost voucher: invoices get one `StockCost` voucher (`CG-`: debit cost of
sales, credit stock, per product accounts) and stock documents get one `StockAdjustment` voucher (`SK-`: against the stock gains and
losses account or the one the user chose). Both are system vouchers (`IsSystemMade`), deleted and rewritten with their owner.

**Nothing is half done.** Before an invoice is posted or a stock document saved, `CheckAsync` replays the proposed change and refuses it
if it would leave any warehouse or the product short at any date (`stock.insufficient`, or `stock.would-leave-later-sales-short` when the
shortage is in a later sale), or would change the value of a movement whose voucher falls in a locked month (`stock.locked-period`).
Deleting follows the same path. Stock cannot go below zero, per warehouse.

**Accounts** have roles: `Inventory` (asset), `CostOfSales` (expense) and `InventoryAdjustment` (stock gains and losses, an expense
account of the default chart). A product may name its own stock and cost-of-sales accounts. Stock that is bought or sent back to a
supplier is always posted to the stock account whatever was chosen on the line, so the stock and the ledger cannot drift apart. The
stock valuation report checks that its total equals the movement of all stock accounts, and says so on the page.

**One default warehouse** (`MAIN`) is made the first time warehouses are asked for, so a company with one place for its stock never has to
think about warehouses; a document without a warehouse uses it. A warehouse that has had stock cannot be deleted, only switched off.

**Services are never stock.** Only products marked as stock items move stock; a product that has had stock cannot stop being one.

**Excel in and out** (owner requirement): the valuation, movements, reorder list, warehouses, stock documents and products (with their
stock columns) export to Excel, CSV and PDF through the same report-table path, and stock items and opening stock import from Excel or
CSV, all or nothing, with a template (the opening stock file becomes one opening-stock document dated the day before the books start).

## Consequences

- Weighted average only. A product costed by FIFO or batches (with expiry or serial numbers) needs a different engine and a place to
  record the batch; both are future work and are not claimed.
- A change that alters old costs rewrites old cost vouchers. That is why locked months are checked first and why a change that would
  reach a locked month is refused rather than half applied.
- Landed costs, manufacturing, stock reservations, and barcode scanning at the point of sale are not part of this phase. The barcode is
  stored and unique, and imports can use it, so a scanner screen can be added later without a data change.
- Companies made before this phase have no inventory module switched on and no stock accounts with the new roles: switch the module on
  in Settings and choose the accounts on the product or in the chart.
