# ADR 0009: Tax codes, VAT on documents, the tax return, and e-invoicing

- Status: accepted
- Date: 2026-10-07

## Context

Phase 4 (brief sections 8, 10.3 and 13) adds tax to the documents of ADR 0008, a tax return, and e-invoicing. The rule of ADR 0001
holds: nothing outside a country's pack names a country, so everything below is data in the pack plus generic code that follows what
the pack says.

## Decision

**Tax codes belong to the company and come from the pack.** A `TaxCode` has a code, names, a rate (percent x 10,000), a treatment
(standard, zero, exempt, out of scope), optional effective dates, and two accounts: *output* (tax payable, a liability) for sales and
*input* (tax receivable, an asset) for purchases, so one code serves both directions. The codes of the company's country are copied from
its pack the first time they are asked for (and any missing ones are added later), pointing at the accounts with the new roles
`TaxPayable` and `TaxReceivable` (the default chart has them: 214 and 116). A code from the pack keeps its rate, treatment and dates; the
company can rename it, choose its accounts, switch it off, make one the default, and add codes of its own. A code that is used on a
document or a product cannot be deleted, only switched off.

**A line keeps the rate it was written with** (`DocumentLine.TaxCodeId` and `TaxRateScaled`). Changing or retiring a code never changes an
old document, and nothing needs to look up rates when totals are worked out. A code must exist, be on (unless the line already had it)
and apply on the document's date.

**Prices are tax-exclusive and tax is worked out line by line.** `DocumentMath` takes each line's posted amount (after the document
discount and its rounding remainder) and charges the line's rate on it, rounded to the currency's decimals; `Total` is net plus tax. The
invoice's voucher gets one tax line per tax account, on the same side as the lines the tax was charged on, so credit and debit notes
reverse tax with no special case, and the party's control line (which absorbs base-currency rounding) still balances the voucher.
Tax on a foreign-currency invoice is posted in both currencies like every other line. An invoice with a taxed line needs the code's
account for its direction (`line.tax-account-missing` otherwise); a draft does not.

**The tax return is built from the issued documents** (invoices and notes, notes counting negatively), by tax code, in the company's
currency with each line converted at its document's rate, and it checks itself against the ledger: the tax accounts must have moved by
exactly the tax the documents say (a manual entry on a tax account fails the check on purpose). The layout is generic (sales and
purchases by code, output tax, input tax, net payable). Each pack reports `ITaxReturnDefinition` (name and filing frequency) so the UI
offers the return only where there is one; the authorities' official box layouts are not mapped yet.

**Printed invoices become tax invoices** when any line carries a tax code: tax columns, net / tax / total, "Tax invoice" or "Tax credit
note" in the layout's language(s), and the company's tax numbers named by the pack's registration rules.

**E-invoicing is a pack part with a deliberately small contract.** `IEInvoicingProvider` names the provider, says which company tax number
is the seller's, and builds the text of the QR code of an issued invoice (`BuildQrCode`). `DocumentRules.SubmittedDocumentsAreImmutable`
makes an issued invoice or note read-only in a country that says so (brief section 8): it cannot be edited or deleted
(`document.immutable`), only reversed with a credit or debit note. Until invoices are really sent to an authority, "issued" stands for
"submitted". A document remembers when it was first issued (`IssuedAt`, UTC), because the QR code carries it. The QR code is printed on
issued sales invoices and credit notes in the company's own currency, drawn as SVG by `IQrImageMaker`.

**Excel is a first-class format** (owner requirement): every list added here exports to Excel, CSV or PDF through the same report-table
path, with real numbers and dates; products, exchange rates, journal entries and opening balances import from Excel or CSV, all or
nothing, with a downloadable Excel template for each. Importers ask the services (not the stores) so a country's tax codes exist before
they are looked up.

## Consequences

- Saudi Arabia is the only pack with an e-invoicing provider, and what it does is the QR code of ZATCA's "Guide to Developed FATOORA
  Compliant QR Code" (tested against the guide's own worked example, byte for byte). **Not built, and not claimed:** the integration
  phase (signed UBL 2.1 XML, the hash chain, cryptographic stamp onboarding, clearance and reporting through ZATCA's API), Egypt's ETA
  e-invoice and e-receipt, and the UAE's e-invoicing. Each needs the authority's credentials and test system to be checked, and each is
  added to its pack's provider, with its own phase of checks. See `docs/countries/sa.md`.
- Withholding tax is not implemented (`IWithholdingRules` is still empty).
- Companies made before this phase have no accounts with the tax roles: choose the tax accounts on the Tax codes screen.
