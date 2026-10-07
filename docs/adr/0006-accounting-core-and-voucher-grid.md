# ADR 0006: The accounting core, and the voucher entry grid

- Status: accepted
- Date: 2026-10-07

## Context

Phase 1 (brief sections 10.1, 11 and 13) turns the empty company file into books: a chart of accounts, three kinds of
voucher, a posting engine, locked months, six reports with drill-down, print templates and exports. It is the base every
later module (sales, purchases, inventory, payroll) will post through, so the rules have to be simple and hard to
bypass. The Phase 0 spike on the voucher grid (Ant Design table or AG Grid) was left open for this phase.

## Decision

**One way into the ledger.** `PostingEngine` (Domain) is the only code that creates `LedgerEntry` rows (the factory is
`internal`). A draft voucher creates no entries. Posting creates them; editing a posted voucher deletes and re-creates its
entries in one transaction; deleting removes them. Account balances are never stored: every report is computed from the
entries, so a balance can never disagree with the entries behind it.

**Money and rates are whole numbers.** Amounts are stored as `long` scaled by 10,000 (as in ADR 0002), exchange rates
as `long` scaled by 1,000,000. SQL sums are therefore exact. Each entry keeps the amount in the voucher currency and in
the company currency; the second is computed once, rounded half away from zero to the currency's minor units, when
the voucher is posted, so reports in the company currency always add up exactly. A voucher must balance in both.

**Numbers.** `PV-`, `RV-`, `JV-` + fiscal year + a four-digit counter, assigned when a voucher is first posted and never
reused or renumbered (a deleted posted voucher leaves a gap, which is how auditors expect it). The fiscal year is named
by the calendar year it starts in. Drafts have no number.

**Periods.** Only locked months are stored (`Period` rows); a month with no row is open. A locked month refuses adding,
changing and deleting posted vouchers dated in it, and moving a voucher into or out of it.

**Chart rules.** Accounts form a tree of any depth. Only "posting" accounts take entries; groups do not. An account with
entries cannot be deleted or change type, and cannot become a group; its entries can be moved to another account of the
same type (`MoveEntries`), after which it can be deleted. Special uses (`AccountRole`: cash or bank, receivable, payable,
retained earnings) mark the accounts the modules need to find without looking at names, so a country's chart can name
them anything. The default chart comes from the country pack; the core knows no country.

**Reports are one generic shape.** Every report returns a `ReportResult` (bilingual column titles, rows with a level and
a style, optional drill-down link per row, and self-checks such as "debits equal credits"). The screen, PDF, Excel and
CSV all render that same object, so they cannot disagree, and a new report is a new function, not a new screen.

**The voucher grid is our own small editable table on Ant Design inputs, not AG Grid.** The grid needs eight lines at a
time, an account box that finds accounts by code or Arabic or English name, Enter and Tab moving cell to cell, arrows moving
between rows, F2 for a full account search and a new row appearing at the bottom. That is a plain HTML table with Ant
Design `Select` and `InputNumber` cells and about 150 lines of keyboard handling (`VoucherLinesGrid`). It mirrors in
Arabic with logical CSS only, needs no license (AG Grid's useful features are commercial), adds no download weight, and is
covered by unit tests of the key handling and by the end-to-end test. If a later phase needs thousands of rows (stock
counts, bank statement matching) that screen can use a virtualised grid without touching this one.

**Hijri dates are display only and off by default** (brief section 6 leaves entry as an open question). The setting lives
on the computer (like language and text size), dates are always stored and entered as Gregorian, and the Hijri date is
computed in the browser with the Umm al-Qura calendar when the switch is on.

## Consequences

- Phase 2 and later modules post by calling `PostingEngine`; nothing writes entries directly. `LedgerEntry` has no public
  constructor or setters and its factory is `internal` to Domain, so other projects cannot create one.
- Reports on large files read the whole ledger for the period. This is fine for SME volumes; add indexes (the date and
  account indexes exist) or period balance snapshots only when a real file shows it is slow.
- Deleting posted vouchers is allowed (with a gap in the numbers and an audit-log record) until the owner decides the policy
  (brief section 15); the UI offers an undo straight after.
- Exchange rates are entered per voucher. Daily rate tables and revaluation arrive with the multi-currency work.
