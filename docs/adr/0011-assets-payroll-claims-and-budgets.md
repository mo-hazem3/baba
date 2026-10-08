# ADR 0011: Fixed assets, payroll, expense claims and budgets

- Status: accepted
- Date: 2026-10-09

## Context

Phase 6 (brief sections 10.4 and 13) adds four modules whose "done when" is that monthly payroll and depreciation post by themselves.
The rules of earlier phases hold: only `PostingEngine` writes the ledger (through system vouchers the modules ask `VoucherService` for),
money is x10,000, nothing outside `Baba.Localization/<Country>/` names a country, and every list exports to Excel while the lists a user
would correct in Excel (assets, employees, budgets, opening stock) also import, all or nothing.

## Decision

**System vouchers, one kind per job** (all `IsSystemMade`, so nobody edits them by hand and the voucher screen shows them read-only with a
note): `Depreciation` DP-, `AssetDisposal` DS-, `Payroll` PR-, `SalaryPayment` SP-, `EndOfServiceAccrual` EA-, `ExpenseClaim` XC-,
`ClaimPayment` XP-. Each is made, replaced and deleted by the thing that owns it. New account roles give the defaults (accumulated
depreciation, depreciation expense, asset disposal gain or loss, salary expense, salaries payable, employer insurance expense,
insurance payable, end-of-service expense and provision, expense claims payable); the default chart has an account for each, and
screens let the user choose others.

**Fixed assets** keep a register entry per asset (cost, salvage, life or declining rate, three accounts, optional opening depreciation for
an asset already in use). `DepreciationEngine` is pure: given what has been depreciated and the month number it gives the month's
amount, so any missed month is caught up and the last straight-line month takes the rounding. A run posts one voucher per month, dated
the last day, for every asset that still has something to depreciate, and **stops at the first month that cannot be posted** (a locked
month), because later months follow on from it. The run for the months that have ended happens when a company is opened, so
depreciation posts by itself. The latest month can be taken back; an asset's terms cannot change once depreciation is posted. A disposal
first depreciates up to its month, then one voucher removes cost and depreciation, books proceeds and the gain or loss. The register
report checks cost and depreciation against the ledger accounts the assets use.

**Payroll** is a payslip per employee per month (`PayrollRun` > `Payslip` > `PayslipItem`), made from the employee's basic salary, the
allowances and deductions assigned to them (fixed, or a percentage of basic), prorated for joining or leaving in the month. **Social
insurance comes from the country's pack** (`IPayrollRules`: one scheme for nationals, one for foreigners, each with employee and employer
percentages and optional wage limits); the rates are *copied into the company's payroll settings the first time and are then the
company's own to correct*, because rates and limits change and the pack's numbers are defaults that must be checked with the authority.
Insurance is worked out on the items flagged insurable, within the limits. Posting a month makes one `Payroll` voucher (earnings and
employer insurance debited; net pay, insurance and deductions credited); editing a payslip of a posted month posts it again; **Pay**
clears what is owed to employees from a bank or cash account (`SalaryPayment`), and can be taken back. After the first month is made,
later months that have ended are made, and posted if the company chose that (default on), when a company is opened; it stops at the
first month that cannot be made. **End-of-service** is also pack data (`IEndOfServiceRules`: days of wage per year of service, with a
cap): the required provision is worked out for the active employees, the ledger's balance of the provision account is the amount already
set aside, and a monthly entry (or a release) brings them together; it is made once per month and by itself when a company is opened.
Leave is a balance (brought forward plus a twelfth of the yearly entitlement per whole month, less annual leave taken).

**Expense claims**: draft, submitted, approved (posts expenses against "expense claims payable"), rejected (can be changed and sent
again), paid (clears the payable from a bank or cash account); an approval or a payment can be taken back. Who may approve is not
decided here: that is the approval workflow of the users and permissions phase.

**Budgets** are `BudgetEntry` rows (fiscal year, revenue or expense account, optional cost center, month 1-12, amount). A budget is
saved whole (it replaces the year's entries for that cost center), can be copied from another year with a percentage, imported from
Excel (twelve months, or a yearly total spread evenly) and exported in the same layout. The budget-versus-actual report reads actuals
from the ledger's operating totals (or the cost center's totals), counting a budget month when its first day falls in the period.

## Consequences

- **Not built, and not claimed:** the official salary-transfer file formats (UAE WPS SIF, Saudi Mudad) need the banks' and ministries'
  specifications and a way to test them; the payroll summary report carries the bank details and exports to Excel or CSV as the list to
  give the bank. Social insurance and gratuity rules in the packs are defaults, tested for arithmetic, not verified against the
  authorities or the labour laws; `docs/countries` says so. No pro-rata depreciation inside the first month (a full month is taken),
  no revaluation or impairment, no leave encashment at termination, no overtime rules, no payroll tax (income tax withholding), no
  attachments (receipts) on claims yet.
- Payroll is a posting module only: it does not file anything with the social insurance authority.
- Companies made before this phase lack the new default accounts: choose them in the payroll settings and on assets, or give the roles to
  existing accounts in the chart.
