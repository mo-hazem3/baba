# 3 · Owner and administrator

**Written for:** the person who owns the business or manages it, and who decides who else uses Baba.

You can do **everything** in Baba. This guide covers what only you do: setting up, deciding who can do what, protecting the data, and checking the work. For the daily accounting itself, read [the accountant guide](04-accountant.md); you can do all of it too.

---

## Your first week

| When | What |
|---|---|
| Day 1 | Install Baba, create the company, write the password on paper. ([First day](01-first-day.md)) |
| Day 1 | Make the first **backup**. Decide where copies will be kept. |
| Day 1 | Put in the **opening balances** with your accountant. |
| Day 2 | Import your **customers**, **suppliers** and **products** from Excel, or add them by hand. |
| Day 2 | Turn on **user accounts** and add your people (below). |
| Day 3 onward | Record every receipt, payment and invoice. Look at the **Summary** every morning. |
| End of the first month | Check the **bank reconciliation**, look at the reports, **lock the month**. ([Routines](08-routines.md)) |

---

## Deciding who can use Baba

At the start, anyone who opens the company file can do everything. That is fine if you are the only user. When others join, give each person their own sign-in.

### Turn on user accounts (once)

1. Menu: **Settings**. In the card **People and approval** click **Turn on user accounts…**
2. Type your **Full name**, a **User name** (what you will type to sign in, for example `samira`: letters and numbers, no spaces) and a **Password** twice.
3. Click **Turn on**.

✅ You are now the first **administrator** and you are signed in. From now on, after typing the file password everyone also types their own name and password.

⚠️ Keep your own password safe. **If you forget it**, whoever knows the **company file password** can set a new one: on the sign-in page click **I forgot the password**, type the file password, then your user name and a new password. That is why the file password must also be kept safe, and why only the owner should know it.

### Add a person

1. Menu: **Users and roles**. The tab **Users** shows everybody.
2. Click **+ New user**.
3. Type their **Full name** and **User name**. Choose their **Role** (below). Give a **Temporary password** (at least 8 characters).
4. Click **Save**.
5. Tell the person their user name and temporary password. The first time they sign in, Baba makes them **choose their own password**. After that, not even you can see it.

You can later **Edit** a person (change their role, or switch off **Can sign in** when they leave: what they recorded stays), **Reset password** (if they forgot), or **Delete**.

### Choose a role for each person

A **role** is a set of rights. Baba has five ready-made roles:

| Role | Good for | In short |
|---|---|---|
| **Administrator** | You | Everything, including people and the audit log. |
| **Accountant** | The bookkeeper | Everything about the money. Cannot manage people. Can only look at Settings. |
| **Sales** | Sales staff | Customers, quotes, orders, invoices, products. Can look at stock and tax. Cannot see the company's money reports. |
| **Storekeeper** | Warehouse | Everything about stock. Can look at products and documents. |
| **Viewer** | Partner, bank, auditor | Can open and read everything. Changes nothing. |

The company must always have **at least one active administrator**. Baba will not let you remove the last one, and nobody can delete themselves.

### Making your own role

Your business is not a template. Make a role that fits.

1. **Users and roles**, tab **Roles**, click **+ New role**.
2. Give it a name, for example `Cashier`.
3. (Optional) Under **Start from**, choose a ready-made role: Baba copies its ticks, and you change them.
4. In the table, tick what people with this role may do in each part of Baba. The columns mean:

| Column | Means |
|---|---|
| **See** | Open and read it, and its reports. |
| **Add and change** | Add new things and change them. |
| **Delete** | Delete things. |
| **Approve** | Make things final: post, approve, lock a month, close a year. When your company asks for approval, only people with this can post. |

💡 Ticking **Add and change**, **Delete** or **Approve** also ticks **See**, because nobody can change what they cannot see.

5. Click **Save**.

The ready-made roles cannot be changed, but you can click **Open** to look at them and copy one with **+ New role**. The table only shows the parts of Baba your company has switched on.

### Ask for approval before posting

If you do not want staff to post vouchers and invoices without a second pair of eyes:

1. **Settings**, card **People and approval**, switch on **Ask for approval before posting**.
2. From then on, people **without the Approve right** see the button **Send for approval** instead of **Save and post** (or **Issue and post**). Their work waits as a draft.
3. People **with the Approve right** (you, the accountant) open **Approvals**. A number on the menu item shows how many wait. Click the item to read it, then **Approve and post** (it is posted exactly as written) or **Send back** with a reason.
4. If the sender changes or deletes the draft after sending, the request is withdrawn, so you always approve what is in front of you.

⚠️ With approval on, only people with the Approve right can also change or delete something already posted, receive payments against invoices, and import journal entries.

You must have user accounts turned on first, because approval needs to know who is who.

---

## Watching what happens: the audit log

Menu: **Audit log**. A line for every change in the company: **When**, **Who**, **What happened** (Added, Changed, Deleted), and the **Record** (for example "Voucher RV-2026-0001"). Click the **+** at the left of a line to see **Field**, **Before** and **After**.

- Use **From** and **To** to choose dates, **Person** to see one person's work, **Kind of record** for vouchers only, and so on.
- Click **Export** to save it as Excel, CSV or PDF.
- Nobody can edit or delete the log. Passwords are never shown; only "Password: changed".

💡 When something looks wrong ("who deleted this invoice?"), this is where you look first.

---

## Protecting your company

### Backups

- **Back up now** on the **Summary** page makes a full copy in one click.
- Make one **every day** you record anything important, and keep **one copy outside this computer** (USB stick, cloud folder, a second computer).
- Baba also makes a copy by itself **before updating your file** to a newer version and **before closing a year**.
- To go back: **Menu**, **Close company**, then **Restore a backup…** on the welcome page. It makes a **new** company file from the backup.

### Locking a month

When a month is finished (the bank is reconciled, the tax return is filed), **lock it** so that nobody can add, change or delete anything dated in it.

1. **Settings**, card **Locked months**.
2. Next to the month click **Lock**.

To change something later you must first **Unlock** the month (an Approve right is needed). Everything is recorded in the audit log.

### Closing a year

Once the year is finished and everything is posted, go to **Year-end** and click **Close year**. Baba: moves the year's profit or loss into retained earnings, locks the twelve months, and saves a copy of the file first. If you find a mistake you can **Reopen** the latest closed year.

### Passwords: the rule of two

| Password | Who knows it | What it protects |
|---|---|---|
| **File password** | You (and maybe one trusted person) | The whole file. Needed to open the company at all. Cannot be recovered. |
| **User password** | Each person for themselves | Their own sign-in. Can always be reset by an administrator. |

---

## Things you can do from the Settings page

| Card | What it does |
|---|---|
| Language, **Text size**, number style | The look on this computer. **Hijri** dates can be shown next to normal dates. |
| **Printing** | Print a test page in Arabic, English or both. |
| **Optional parts of Baba** | Switch parts on or off. Switching off only hides the screens; nothing is deleted. |
| **People and approval** | User accounts and approval. |
| **Print template** and **Logo and stamp** | What your invoices look like: your logo, stamp and footer text. |
| **Locked months** | Lock and unlock months. |
| **Backups** | Information about backups. |

---

## The Summary page: your morning check

Open Baba each morning and read the four cards:

- **Cash and bank**: money you have.
- **Owed to you**: what customers still have to pay.
- **You owe**: what you still have to pay.
- **Profit this month**: the result so far this month.

Below them Baba shows warnings: **invoices past their due date**, **supplier bills past their due date**, **products at or below their reorder level**, **draft vouchers waiting to be posted**, and **items waiting for your approval**. Click the link in a warning to go straight to the list.

---

## Your decisions

| Decision | Advice |
|---|---|
| Who gets which role | Start small: give the least that does the job. You can always give more. |
| Use approval or not | Yes, if more than one person posts money entries and mistakes would be costly. No, if you are one or two people who trust each other. |
| How often to back up | Every day, plus one copy outside the computer. |
| When to lock a month | Right after the bank is reconciled and any tax return is filed. |
