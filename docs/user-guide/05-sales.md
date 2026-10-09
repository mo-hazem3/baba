# 5 · Sales

**Written for:** people who deal with customers: write quotes, make orders, send invoices, and keep the list of products and prices.

**Your rights (ready-made Sales role):** you can see and add and change **customers**, **sales and purchase documents**, and **products and price lists**. You can **look at** stock, tax and settings. You cannot see the company's money reports, the bank, the payroll or the users. You cannot **delete** things. (The owner can change this.)

Your menu is therefore short: **Summary**, **Customers** (and **Suppliers**), **Sales**, **Purchases**, **Products and prices**, and possibly **Stock**.

---

## The path of a sale

```
Quote  →  Sales order  →  Delivery note  →  Invoice  →  Payment
(price)   (they agreed)    (goods sent)      (they owe)   (they paid)
```

You can start anywhere. Many businesses only use **Invoice**. Each step is **turned into** the next one with one click, without retyping.

| Document | What it is | Does it change the books? |
|---|---|---|
| **Quote** | A price you offer. | No |
| **Sales order** | The customer said yes. | No |
| **Delivery note** | Goods left your door. | No (stock moves when the invoice is issued) |
| **Invoice** | The customer owes you. | **Yes** when you **Issue and post** |
| **Credit note** | You take back (part of) an invoice. | **Yes** |

---

## Before your first invoice: customers and products

### Add a customer

1. Menu **Customers**, then **+ New customer**.
2. Type the **name** (Arabic, English, or both; one is enough). Phone, address, tax number if you have them.
3. **Payment terms**: how many days the customer has to pay (for example 30). The invoice's **due date** follows from it.
4. Optional: **credit limit** and a **price list** (special prices for this customer).
5. **Save**.

💡 You do not need to give a code. Baba numbers customers C001, C002...

### Add a product or service

1. Menu **Products and prices**, then **+ New product**.
2. Name, **Unit** (piece, hour, kg), **Sale price**, and the **Revenue account** (ask your accountant which one).
3. If the product is in stock, tick that it is a **stock item**.
4. If your country has tax, choose its **tax code**: lines of this product start with it.
5. **Save**.

Choosing a product on an invoice then fills in the name, the price and the account for you.

### Price lists

A **price list** gives chosen customers their own prices (a wholesale list, for example). On the tab **Price lists** click **+ New price list**, add the products and prices. Then open the customer and choose the list. On their invoices, the list's prices are used.

---

## Making an invoice

1. Menu **Sales**, then **+ New invoice**.
2. Choose the **Customer**. The due date fills in.
3. On each line choose the **Product** and type the **Quantity**. The price, the account and the tax fill in. You can change the price and give a **Discount %** on a line, or a **Discount on the whole document**.
4. Check the **Total** at the bottom (with the tax, if any).
5. Click one of:

| Button | Result |
|---|---|
| **Issue and post** | The invoice gets its number (for example SI-2026-0001) and the customer now owes you. |
| **Save as draft** | Kept for later. No number, nothing recorded. Good for an unfinished invoice. |
| **Send for approval** | You see this instead of **Issue and post** if your company asks for approval. A manager approves it and Baba issues it. Your draft waits in **Approvals**. |

6. **Print** opens the invoice as a PDF (with your logo and, where required, the QR code). Print it or save it and e-mail it.

✅ If you gave a customer a **credit limit**, Baba shows a note when the invoice goes over it.

### Changing or cancelling an invoice

- An **issued** invoice can be changed and issued again, **unless** it has been **paid or credited** (then it is frozen) or **your country's tax rule forbids it**.
- If it cannot be changed: open it and use **Turn this into:** → **Credit note**. The credit note cancels the invoice (all or part of it), and you issue a new correct invoice.
- You normally cannot **Delete** (the ready-made Sales role has no delete right). Ask the owner or the accountant.

---

## When the customer pays

You can **record that a customer paid**. Open **Sales** and click **Receive payment**: choose the customer, type how much goes to each unpaid invoice (or **Pay in full**), choose the bank account, and **Record receipt**. The invoice then shows **Paid**.

> If your company has switched on **approval**, this step may need the accountant instead. Baba tells you if so.

---

## Quotes and orders

1. **+ New quote**: the same screen as the invoice. Save it.
2. When the customer accepts, open it and click **Turn this into:** → **Sales order** (or straight to **Invoice**). Baba makes a **draft** of the next document with the same lines. Check it and issue it.
3. The quote and order cannot be changed after they were turned into the next document. Delete the next document to get the earlier one back.

---

## Finding things

- The **Sales** page has **tabs by kind of document**. Use the search box to find by number, reference or name.
- On a customer's page, **Statement** shows everything they bought and paid, and what they still owe.
- **Export** (top of any list) saves the list as Excel, CSV or PDF.

---

## Repeating invoices

For a monthly rent or a subscription: open the invoice and click **Repeat…**. Choose how often, the next date, and **Issue or post it automatically** (or leave each as a draft to check). Baba makes them when the program is opened. See and stop them in **Recurring**.

---

## What you will not see (and why)

- **Reports** and the money figures on the **Summary**: those belong to the accountant and the owner. Ask them if you need a figure.
- **Payroll**, **Bank and cash**, **Users and roles**.

If you need more rights for your job, ask the owner. They can make a role for you.
