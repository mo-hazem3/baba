# 4 · Accountant

**Written for:** the person who keeps the books: records money in and out, checks the bank, prepares the reports and closes the month and the year.

**Your rights:** you can see, add, change, delete and approve everything about the money. You can only *look at* Settings, and you cannot manage users or see the audit log. (The owner can give you more.)

If you are new to Baba, do [the 30-minute tour](02-thirty-minute-tour.md) first.

---

## The one idea behind everything: draft and posted

| State | Meaning | Number? | In the reports? |
|---|---|---|---|
| **Draft** | A note to yourself. You can change it freely. | No | No |
| **Posted** | Real. It is in the books. | Yes (for example RV-2026-0001) | Yes |

- **Save as draft** keeps your work without recording it.
- **Save and post** (vouchers) or **Issue and post** (invoices) records it and gives it a number.
- A posted entry **can still be corrected** unless its month is **locked**. Every correction is written in the audit log.
- If your company asks for approval and you do not have the Approve right, you will see **Send for approval** instead. You do have the right unless the owner removed it.

---

## Every day: money in, money out

### Receipts: money coming in

1. Menu **Receipts**, then **+ New receipt**.
2. **Date** (today by default), **Received into** (the bank or cash account).
3. In the table: for each source of the money choose the **Account** (for example "Sales" or "Capital"), type the **Amount**. A description is optional.
4. If the account is **receivable** (money from a customer), you must choose the **Customer / supplier**: Baba needs to know whose debt this settles.
5. **Save and post**.

💡 You can type in the account box: part of the code or of the name is enough. Press **F2** to search accounts in a bigger window.

### Payments: money going out

Same, with **+ New payment** and **Paid from**. Choose the expense accounts and amounts. For a supplier's debt choose the payable account and the supplier.

### Journal vouchers: everything else

**+ New journal voucher** has **Debit** and **Credit** columns. The totals must be equal; Baba tells you the difference if not. Use it for adjustments and anything that is not a receipt or payment.

### Transfers: between your own accounts

**Transfers**, **+ New transfer**: choose where the money comes from and where it goes and the amount. Nothing is earned or spent, so profit does not change.

### Correcting and deleting

- Open the voucher from its list and change it, then **Save and post** again.
- **Delete** removes it. A deleted draft can be brought back with **Undo** for a few seconds. A deleted posted voucher is gone from the books; the audit log keeps the record.
- ⚠️ Baba refuses to change a voucher in a **locked month**, a voucher already used in a **bank reconciliation** (undo the reconciliation first), or a payment already set against invoices (delete it and make it again). The message says which.
- Vouchers that Baba made itself (from invoices, payroll, depreciation, stock) open **read-only** with a note. Change the invoice or run, not the voucher.

---

## Customers and suppliers

- **Customers** and **Suppliers**: **+ New customer**. Set **payment terms** (days until due) and an optional **credit limit**. The invoice form warns you if a customer goes over the limit.
- Open a customer to see their **statement**: every invoice and payment, and the balance.
- **Reports** → **Customers aging** and **Suppliers aging** show who owes what and for how long (30, 60, 90+ days). Payments are matched to the **oldest** debt first.
- A customer or supplier that has been used cannot be deleted. Use **Deactivate** to switch them off.

---

## Sales and purchases

(The sales staff do this too; their guide is [Sales](05-sales.md).)

- **Sales**: quotes, sales orders, delivery notes, invoices, credit notes. **Purchases**: purchase orders, goods received, supplier bills (**New bill**), debit notes.
- **Quotes, orders and delivery notes post nothing.** An **invoice or bill posts** when you **Issue and post**: to the customer or supplier and to revenue or expenses, with tax and, if you use stock, the cost of goods sold.
- **Turn this into:** moves a quote to an order to an invoice without retyping.
- **Receive payment** (sales) and **Pay supplier** (purchases): choose the customer or supplier, type how much goes to each unpaid invoice (or **Pay in full**), choose the bank, then **Record receipt** or **Record payment**. If a foreign-currency rate changed, Baba books the exchange gain or loss for you.
- An invoice that has been paid or credited is **frozen**. To change it, first delete the payment or credit note.
- In some countries an **issued invoice can never be changed or deleted** (the tax authority's rule). The page tells you; make a **credit note** instead.

### Tax (VAT)

- **Tax codes** (menu, only in countries with tax) shows the codes of your country with their rates. Choose the accounts they post to, and which code new lines start with. A line **keeps the rate it was written with**, so changing a code never changes an old invoice.
- **Reports** → **Tax return** shows tax on sales and purchases for the dates you choose, and **checks itself against the books**. A green tick means it agrees.
- Tax invoices print with the QR code where your country needs it.

### Foreign currencies

- **Exchange rates**: for each foreign currency type how much your own currency it is worth, from a date on.
- A voucher or invoice in that currency starts from the latest rate on or before its date. You may change the rate for that one document.

---

## Bank and cash

- **Bank and cash** lists your accounts with the **balance in the books** and how far each has been **checked**.
- **Reconcile** when the statement comes:
  1. Type the **Statement date** and the **Closing balance on the statement**.
  2. Optional: **Load statement from a file…** (Excel or CSV from your bank). Baba suggests matches.
  3. **Tick** each entry that appears on the statement. Entries not on it yet stay unticked for next time.
  4. When **Difference** is 0, **Finish reconciliation**.
- Checked vouchers become locked. **Undo the latest** if you made a mistake.

---

## Cost centers and projects (if switched on)

Tag any voucher or invoice line with a **cost center** (a branch, a project). **Reports** → **Cost centers** and **Profit and loss** for one cost center show the result for each.

---

## Recurring invoices and entries

Open any invoice or journal voucher and click **Repeat…**. Choose how often (every week, month, 3 months, year), the next date, an optional last date, and whether it is **issued automatically** or left as a **draft for you to check**. Baba makes whatever is due **when the program is opened**, including months you missed. See them in **Recurring**: pause, resume, stop, or **Make what is due now**.

---

## Reports

Menu **Reports**. Choose the dates at the top. **Click any amount** to see the entries behind it.

| Report | Answers |
|---|---|
| **Trial balance** | Does everything add up? Every account's total. |
| **Profit and loss** | Did we earn or lose? Compare with last year, or for one cost center. |
| **Balance sheet** | What we have, what we owe, what is the owners' as of a date. |
| **Account statement**, **General ledger**, **Journal** | The detail behind the numbers. |
| **Customer or supplier statement**, **Customers aging**, **Suppliers aging** | Who owes what. |
| **Tax return** | Tax on sales and purchases, checked against the books. |
| **Stock valuation**, **Stock movements**, **Products to reorder** | What is in stock and what it is worth. |
| **Fixed asset register** | Equipment, depreciation, book value. |
| **Payroll summary**, **Leave balances**, **End-of-service provision** | Staff costs and what is owed to staff. |
| **Budget versus actual** | How the year compares with the plan. |

### Printing and Excel

On every report and every list there is **Export**. Choose the **language** of the printout (Arabic, English, or both), then **PDF** (opens in a window to print or save), **Excel** (real numbers and real dates, ready to add up) or **CSV**.

> Reports that check themselves (stock valuation, fixed assets, tax return) show **✓** when they agree with the ledger and a warning when they do not. Never ignore the warning.

---

## Stock, assets, payroll, claims and budgets

Done by you or by the people below. Short version; the pages have **Help**.

### Stock (see also [Storekeeper](06-storekeeper.md))
Invoices and bills move stock and the cost of goods sold **by themselves**. Cost is the **weighted average**. **Stock valuation** must equal the stock account in the books; if it does not, the report says so.

### Fixed assets
**Fixed assets** → **+ New asset**: cost, life, the accounts. Baba posts each month's **depreciation by itself** for months that have ended when you open the company; **Post depreciation** runs it to a date. **Sell or scrap** takes an asset off the books and records the gain or loss. **Undo last month** takes back the latest month.

### Employees and payroll
1. **Employees** → **+ New employee**: name, join date, basic salary, **National** for citizens (their country's social insurance applies), allowances and deductions.
2. **Payroll** → **+ New payroll month** → **Make payslips**. Change a payslip for overtime or a deduction if needed.
3. **Post to the books**, then **Pay salaries** (choose the bank).
4. After the first month, later months are made and posted for you when the company is opened.
5. **Payslip** opens a printable PDF for each employee.

⚠️ The social insurance rates and end-of-service rules Baba starts with are **defaults**. **Check them against the current law** before the first real payroll, and correct them in the payroll settings.

### Expense claims
**Expense claims** → **New claim**: choose the employee, add a line for each receipt. **Send for approval**, then **Approve** (or **Reject** with a reason) and **Pay**.

### Budgets
**Budgets**: choose the year, add accounts, type a yearly total to spread it over twelve months or change single months. Then **Reports** → **Budget versus actual**.

---

## Opening balances, the month and the year

- **Opening balances**: the starting figures, once. There is only one such entry; you can correct it later.
- **Lock the month** when it is final: **Settings** → **Locked months** → **Lock**. (Unlocking needs the Approve right.)
- **Year-end**: when the year is over and everything is posted, **Year-end** → **Close year**. Baba puts the profit or loss into retained earnings, locks the twelve months and saves a copy of the file first. **Reopen** the latest closed year if you find a mistake.

---

## Bringing lists in from Excel

**Import…** on the pages **Chart of accounts**, **Customers**, **Suppliers**, **Products and prices**, **Employees**, **Fixed assets**, **Budgets**, **Exchange rates**, **Opening balances**, **Stock** (opening stock), and for **journal entries**. Always download the **template** first. **Everything is checked first; if one line is wrong nothing is imported** and Baba says which line.

---

## Checklist when something does not add up

1. **Trial balance**: do debits equal credits? (They always should.)
2. **Draft vouchers**: the Summary warns about drafts. Unposted drafts are not in the reports.
3. **Dates**: is the date in the right month, and is that month the one you are reading?
4. **Currency**: a foreign-currency voucher uses the rate of its date.
5. **Click the amount** in the report to see the entries behind it.
6. **Audit log** (ask the owner): who changed what, and when.
