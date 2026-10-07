# ADR 0008: Sales and purchases — documents, settlements, exchange differences and recurring schedules

- Status: accepted
- Date: 2026-10-07

## Context

Phase 3 (brief sections 5, 10.3 and 13) adds quotes, orders, delivery notes, invoices, credit and debit notes, products and
price lists, receipts and payments set against invoices, foreign currencies with exchange gains and losses, and recurring
invoices and entries. ADR 0006 still holds: only the posting engine makes ledger entries and balances are never stored. So a
document is never a second copy of the books; it is something people write, and the books are made from it.

## Decision

**Documents are separate from vouchers.** A `Document` (kind, number, date, party, currency, rate, discount, lines of quantity x
price less a discount) is its own table. Quotes, orders, delivery notes and goods receipts post nothing; they are numbered
`QT/SO/DL/PO/GR-<fiscal year>-0001` when first issued. An invoice or a credit or debit note posts through a **system voucher** of
its own kind (`SalesInvoice SI-`, `SalesCreditNote SC-`, `PurchaseInvoice PI-`, `PurchaseDebitNote PD-`) that the document makes
and keeps in step: editing an issued invoice regenerates the entries, deleting it deletes them, and the document takes the number
of its voucher. People cannot save or delete these vouchers by hand (`voucher.system-generated`). The voucher's first line is the
customer's or supplier's control account for the whole document (party tagged) and it absorbs the rounding when the other lines are
converted to the company's currency, so the voucher always balances exactly. The document discount is shared over the lines by
percentage, with any rounding remainder on the biggest line (`DocumentMath`), so the posted amounts add up to the total.

**Conversions are one click and keep the chain.** Quote -> order or invoice, order -> delivery note or invoice, delivery note ->
invoice; purchases mirror it; an invoice -> credit note. The result is a new draft with the same lines. A quote, order or delivery
note that was converted is used up (`Converted`) until what it became is deleted. An invoice is not used up (it can have several
notes). A credit or debit note takes the invoice's own exchange rate, so it reverses it exactly.

**Products, price lists and pricing.** A product has a code, names, unit, sale and purchase prices and default accounts; one that
has been used can be switched off but not deleted. A price list is per customer and has an optional currency. `PricingService`
uses the customer's list when it is in the document's currency and lists the product, otherwise the product's own price converted
at the document's rate; purchases always use the purchase price. There is no stock yet (Phase 5).

**Settlements.** `SettlementService` makes an ordinary receipt (customer) or payment (supplier) voucher, plus `Allocation` rows
that say how much of which invoice it pays, in the invoice's currency, with what that part was worth in the company's currency at
the invoice's rate (`InvoiceBase`) and at the payment's rate (`PaymentBase`). Outstanding per invoice is total minus allocations
minus the notes made from it. When the two company-currency amounts differ, a system voucher of kind `FxSettlement` (`FX-`) books
the realized gain or loss between the party's account and the exchange-differences account (found by its role; `fx.account-required`
when there is none, checked before anything is posted). A failure after the payment was made deletes it again. Deleting the receipt
or payment removes its allocations (foreign-key cascade) and its exchange-difference voucher. A receipt or payment with allocations
cannot be edited (`voucher.allocated`), and an invoice that has allocations or notes cannot be edited or deleted
(`document.has-payments`, `document.has-notes`): take the payment or note away first. A payment is in the currency of the invoices
it pays; invoices in different currencies are paid separately.

**Aging uses allocations.** Reports pair each allocated invoice entry with its payment entry (reducing them by `InvoiceBase` and
`PaymentBase`), ignore `FxSettlement` entries (the allocations already net them out), and apply the oldest-first rule only to what
is left. Totals agree with the general ledger. The dashboard counts overdue invoices and bills from the same outstanding amounts.

**Recurring schedules.** `RecurringSchedule` keeps a JSON copy of a document or voucher (as the API input) with a frequency
(weekly, monthly, quarterly, yearly), the next date, an optional last date and whether to issue or leave drafts. Running the due
schedules makes one item per missed date (up to 24 per schedule per run), dated the date it was due, with the latest exchange rate on
that date and the party's payment terms. A run that fails stops that schedule at the same date and reports why (for example a locked
month); the others carry on. The web app runs what is due once each time a company is first shown, and there is a button.
Monthly dates keep the day the schedule started on where the month is long enough.

**Printing.** Documents print through the same browser-engine path as vouchers (ADR 0004): `TradePrintBuilder` makes the HTML (both
languages, party details, lines, discount, total, amount in words, signature boxes, stamp), `TradePrintService` renders it.

## Consequences

- An invoice can always be traced from the ledger (voucher `DocumentId`) and back (`Document.VoucherId`); neither link is a foreign
  key so a deleted document cannot be blocked by the books, and the voucher is deleted by the document, never the other way round.
- Tax lines are not here: Phase 4 adds tax codes per country pack and the e-invoicing formats. Documents have no tax columns yet,
  and printed invoices say "Sales invoice", not "Tax invoice".
- First-in-first-out remains for payments that were not set against an invoice (plain receipts, advances).
- Part-payments are in the invoice's currency; a payment that is worth less or more in the company's currency is never adjusted by
  hand: the difference is always the system voucher.
