# 6 · Storekeeper

**Written for:** the person who looks after the goods: what is on the shelves, where, and how much it is worth.

**Your rights (ready-made Storekeeper role):** you can see, add, change, delete and approve everything about **stock and warehouses**. You can **look at** products, and at the sales and purchase documents. You cannot see the bank, the reports about money, customers' balances, payroll or users.

Your menu: **Summary**, **Products and prices**, **Sales** and **Purchases** (to read), **Stock**, **Warehouses**.

---

## The idea in one minute

- A **warehouse** is a place where you keep goods: "Main store", "Shop", "Van".
- A **stock item** is a product you count. (Services such as "consulting hour" are not counted.)
- Baba always knows **how many** of each item are in each warehouse and **what they are worth**.
- Stock moves in two ways:
  1. **By itself**: when your company **issues a sales invoice**, stock goes **out**. When it issues a **supplier bill**, stock comes **in**. Credit and debit notes put it back.
  2. **By you**: with the three **stock documents** below.
- Value uses the **average cost**: when you buy more at a different price, Baba works out the new average.

---

## Setting up (done once)

### Warehouses

1. Menu **Warehouses**. A warehouse called **MAIN** exists already and is the **default**: documents use it unless you pick another.
2. **+ New warehouse**: a **code** (for example SHOP) and a name. Save.
3. You can **Make default**, **Deactivate** (stop using, keep the history), or **Delete** (only if no stock has ever moved through it).

⚠️ There must always be one active warehouse, and it must be the default.

### Products you count

Menu **Products and prices** → open the product. Tick that it is a **stock item**. You may also set a **reorder level** (the number at which Baba should warn you) and a **barcode** (Baba keeps it; there is no scanning screen yet).

### The stock you already have: opening stock

1. Menu **Stock**, then **+ New** and choose **Opening stock**.
2. Choose the warehouse, add a line for each product with the **Quantity** and the **Cost per unit**.
3. Choose **Other side of the entry**: the account that takes the other side (your accountant will tell you; usually the opening balances or capital).
4. **Save**. ✅ **On hand** now shows what you entered, with **Average cost** and **Value**.

If you have a list in Excel: on the **Stock** page click **Import opening stock**, download the template, fill it in and import it. (Everything is checked first; if one line is wrong nothing is imported.)

---

## Counting the shelves

Do this whenever you count, for example at the end of each month. Baba books **only the difference** between what you counted and what the books say.

1. **Stock**, **+ New** → **Count or adjust stock**.
2. Choose the warehouse. Click **Load the stock of this warehouse**: every item appears with what Baba thinks is **On hand**.
3. In **Counted** type the number you actually counted. (Or switch **How to enter** to **Change in quantity** and type +5 or −2.)
4. Choose the **Other side of the entry** (often "stock differences": ask the accountant).
5. **Save**.

✅ The **On hand** column shows what you counted. The accountant sees the gain or loss.

---

## Moving goods between warehouses

**Stock** → **+ New** → **Transfer stock**: choose **From warehouse** and **To warehouse**, add the products and quantities, **Save**.

Nothing is earned or lost, so **nothing is posted to the money books**. Only the places change.

---

## Looking at stock

- **Stock** tab **On hand**: every item and warehouse with quantity, average cost and value. The bar shows the **Total value of stock**.
- Tab **Stock documents**: everything you recorded by hand: opening stock, counts, transfers.
- **Open the stock valuation report**: the same figures as a report that **checks itself against the books**. If the stock in Baba and the stock account in the books disagree, the report says so; tell the accountant.
- Click **Export** for **Excel**, **CSV** or **PDF**.

### The reorder warning

On the **Summary** page, if some products are at or below their **reorder level**, you see a yellow note: "N products are at or below their reorder level." Click the link to open **Products to reorder**, your shopping list.

---

## What Baba will not let you do (and what it says)

| You try to... | Baba says... | What to do |
|---|---|---|
| Sell or move more than there is | "There is not enough stock for this quantity." | Check the quantity and the warehouse. Count again if needed. |
| Change something dated in a **locked month** | That month is locked. | Ask the owner or accountant to unlock it. |
| Delete a warehouse that has had stock | Stock has moved through it. | Deactivate it instead. |
| Use a service as a stock item | Choose a product that is a stock item. | Tick **stock item** on the product, or choose another. |

---

## Good habits

- **Count one warehouse at a time** and enter the count the same day.
- Do a **count at the end of every month**, before the accountant locks the month.
- Enter **supplier deliveries** when they arrive: ask the accountant (or purchases) to issue the supplier bill with the right warehouse, so stock goes up by itself.
- If a number looks wrong, do not guess. Ask the accountant to open **Reports** → **Stock movements**, which lists every movement of an item with its date and document.
