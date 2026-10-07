# ADR 0007: Tier 1 — sub-ledgers, bank reconciliation, opening balances and year-end

- Status: accepted
- Date: 2026-10-07

## Context

Phase 2 (brief sections 10.2 and 13) adds what a small business needs on top of the core books: who owes what, bank
reconciliation, opening balances, closing a year, cost centers, backups and import. It builds on ADR 0006 (only the posting
engine makes ledger entries; balances are never stored), so every decision below keeps that: new features read the ledger or
add tags and rules to it, and none keeps a second copy of a balance.

## Decision

**Customers, suppliers and cost centers are tags on voucher lines, not extra ledgers.** A voucher line and its ledger entries
carry an optional `PartyId` and `CostCenterId`. The posting engine requires a party on every line of a receivable or payable
account (that is what makes it a sub-ledger) and refuses one on any other account, and checks that a party or cost center
exists and is active. A draft may leave the party out; posting may not. Statements, balances and aging per party are queries
over the tagged entries, so they always agree with the general ledger. A party or cost center that has been used can be switched
off but not deleted; a party keeps its kind (customer or supplier) once it has entries.

**Aging pays off the oldest debts first.** A party's debits (what they owe) are cleared by their credits in date order, and what
remains is placed in a bucket by days past its due date (the entry date plus the party's payment terms): not due, 1–30, 31–60,
61–90, over 90. A credit with nothing to clear is an advance and shows as a negative amount that is not due. Until invoices
exist (Phase 3) there are no documents to allocate against, so this is the standard treatment of unallocated payments; Phase 3
allocations will replace the first-in-first-out rule for allocated items.

**Transfers, opening balances and year-end closing are voucher kinds** (`Transfer` TV-, `Opening` OB-, `Closing` CL-), so they are
numbered, printed, audited and drilled into like every other voucher. A transfer is a payment whose one line must be another bank
or cash account. There is one opening voucher; it takes balance sheet accounts only and is dated the day before the books start.
The closing entry is made only by closing a year (`voucher.system-generated` otherwise).

**Closing a year** brings every revenue and expense account of that fiscal year to zero with one balanced entry dated the last
day, puts the profit or loss into the retained earnings account (found by its role, not its code), locks the twelve months, and
first saves a copy of the file (`<name>.before-close-<year>-<time>.baba`). It refuses a year that has not ended, has drafts, is
already closed, has nothing to close, or when no retained earnings account exists. Reopening deletes the closing entry and unlocks
the months, and is allowed only for the latest closed year. Profit and loss, the dashboard and the year list read
`OperatingTotals`, which leaves closing entries out, so a closed year still shows what it earned; the trial balance and the balance
sheet include them, so retained earnings and the balance sheet are right after closing.

**Reconciliation is a checking list, not a balance.** `BankReconciliation` records that on a date the bank said the balance was X;
`ReconciledEntry` marks each ledger entry that was checked (once only). A reconciliation can be finished only when the previously
reconciled balance plus the ticked entries equals the statement balance exactly (whole 1/10,000 units, no tolerance). A voucher
with a reconciled entry cannot be changed or deleted, and the entries of such an account cannot be moved, until the latest
reconciliation is undone (only the latest can be). An imported statement (`BankStatementLine`) only helps: it is matched to entries
of the same amount within 10 days, closest first, each used once, as suggestions the person ticks.

**Imports are all or nothing.** The chart of accounts, customers and suppliers, and bank statements are read from CSV (UTF-8,
comma, semicolon or tab) or Excel (`ITabularReader`, ClosedXML in Infrastructure). Column names may be English or Arabic, dates may
be day-first in the common styles, numbers may use either decimal sign or Arabic digits. Every row is checked before anything is
written, and every wrong row is named by its number and a translatable code. The file goes up as the request body with its name
percent-encoded in a header (file names may be Arabic); only the ending is used.

**Restoring a backup copies it** to a new file chosen by the user (never overwriting), after which it is opened with its own
password like any company file. **Optional modules are switched in Settings** and only hide screens; nothing recorded is deleted.

**Downloads are saved through the native Save dialog.** The desktop host handles WebView2's `DownloadStarting` for Excel and CSV.
The desktop smoke test checks that a download made by the page reaches that handler and is saved. The real-window script cannot
drive the dialog: attached over the DevTools protocol, Playwright takes downloads itself, so the event never fires. The files are
checked there by fetching them instead.

## Consequences

- Phase 3 documents (invoices, bills) post lines on receivable and payable accounts with a party, so they fit the sub-ledger with
  no change, and their allocations will refine aging.
- Multi-currency parties and revaluation are not handled: balances are in the base currency until the multi-currency work.
- Closing and reopening are exact inverses only while nothing is posted into the closed year in between; the locked months and the
  `year.later-year-closed` rule protect that.
- The reconciliation's one-balance model suits one statement at a time per account; interleaving statements out of date order is
  possible but the difference check still keeps every finished reconciliation exact.
