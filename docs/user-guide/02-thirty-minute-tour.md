# 2 · A 30-minute tour

Before you put in real numbers, try Baba with **made-up numbers**. Make a company called "Practice Trading", follow the steps below, and delete the file when you are done. Nothing you do here touches any real company.

You need: Baba installed ([First day](01-first-day.md), Steps 1 and 2).

---

## Part 1 · Make the practice company (5 minutes)

1. Click **New company**.
2. Name: `Practice Trading`. Choose your country. Click **Next** until the **Modules** step and switch on **Customers and suppliers**, **Sales**, **Purchases**, **Bank and cash** and **Inventory**.
3. Password: use something simple like `practice-123`. Save as `C:\Books\Practice.baba`. Click **Create company**.

✅ The **Summary** page shows four cards at zero: **Cash and bank**, **Owed to you**, **You owe**, **Profit this month**.

---

## Part 2 · Money comes in and goes out (5 minutes)

**A. The owner puts money in.**

1. Menu: **Receipts**, then **+ New receipt**.
2. **Received into**: pick your bank (or cash) account.
3. In the first row choose the account **Capital** (type "capital" in the box to find it) and type the amount `10000`.
4. Click **Save and post**.

✅ You see "Posted as RV-…". The receipt has a number. Go to **Summary**: **Cash and bank** now shows 10,000.

> Words: **posting** means "make it real in the books". A **draft** (**Save as draft**) is only a note to yourself: it changes nothing until you post it.

**B. You pay the rent.**

1. Menu: **Payments**, then **+ New payment**.
2. **Paid from**: your bank account. First row: account **Rent**, amount `750`. Click **Save and post**.

✅ **Cash and bank** goes down by 750, and **Profit this month** shows −750.

---

## Part 3 · Sell something (8 minutes)

1. Menu: **Customers**, then **+ New customer**. Name: `First Customer`. Save.
2. Menu: **Products and prices**, then **+ New product**. Name: `Widget`, **Sale price** `50`, choose a **Revenue account**. If you switched on **Inventory**, tick the box that says it is a stock item. Save.
3. Menu: **Sales**, then **+ New invoice**.
4. Choose the customer. On the first line choose the product, type quantity `4`. The price and total fill in by themselves.
5. Click **Issue and post**.

✅ The invoice gets a number such as SI-… . On **Summary**, **Owed to you** shows 200 (plus tax if your country has it).

6. Open the invoice again and click **Print**. A window opens with the invoice as a PDF, ready to print or save.

**The customer pays.**

1. On the **Sales** page click **Receive payment**.
2. Choose the customer. The unpaid invoice appears. Click **Pay in full**.
3. Choose the bank account, click **Record receipt**.

✅ **Owed to you** goes back to 0 and **Cash and bank** goes up.

**You sent the wrong thing? Make a credit note.**
Open the invoice, use **Turn this into:** and choose **Credit note**. Check it and issue it. (In some countries a sent invoice cannot be changed at all; the credit note is then the correct way.)

---

## Part 4 · Look at the results (4 minutes)

1. Menu: **Reports**.
2. Open **Profit and loss**. Choose the dates at the top. ✅ You see your sales and your rent.
3. Click **any amount**. ✅ Baba shows the entries behind it. Click one to open the voucher.
4. Open **Customers aging**: who owes you, and for how long.
5. Click **Export** at the top right and choose **Excel**. Open the file in Excel. ✅ The numbers are real numbers you can add up.

---

## Part 5 · Check your bank (3 minutes, optional)

1. Menu: **Bank and cash**. Next to your bank account click **Reconcile**.
2. Type the statement date and the closing balance from the pretend bank statement.
3. Tick the entries that appear on the statement.
4. When **Difference** is zero, click **Finish reconciliation**.

> Words: **reconciling** means checking, line by line, that what you recorded agrees with what the bank says.

---

## Part 6 · People and approval (5 minutes)

Do this only if you want to see how several people share one company.

1. Menu: **Settings**. Find the card **People and approval**. Click **Turn on user accounts…**.
2. Type your name, a user name such as `owner`, and a password. Click **Turn on**. You are the first administrator and you are signed in.
3. Menu: **Users and roles**, then **+ New user**. Make `carl` with the role **Sales** and a temporary password.
4. Make another user, `ann`, with the role **Accountant**.
5. Back in **Settings**, switch on **Ask for approval before posting**.
6. Click **Menu**, then **Sign out**.
7. Sign in as `carl`. Baba asks him to choose his own password. ✅ His menu is shorter: he cannot see **Reports** or **Payroll**.
8. Sign out and sign in as `ann`, choose a password. ✅ She sees everything about the money except **Users and roles**.

**To see approval working:** as `ann` (who may approve) post anything normally. Then make a user with a custom role that can add vouchers but not approve (see [the owner guide](03-owner.md), "Making your own role"). That person sees the button **Send for approval** instead of **Save and post**. Ann then opens **Approvals** and clicks **Approve and post**.

---

## Part 7 · Look at the audit log, then make a backup (2 minutes)

1. Sign in as the owner. Menu: **Audit log**. ✅ A list of everything that happened, who did it, and when. Click the **+** at the left of a line to see the old and new values.
2. **Summary**, **Back up now**. Choose a place. ✅ You have a complete copy of the practice company.

---

## Clean up

Close the company (**Menu**, **Close company**). In Windows, delete `C:\Books\Practice.baba` and the backup file. Now start your real company ([First day](01-first-day.md), Step 3).
