# ADR 0012: Users, roles, permissions, approval and the audit log

- Status: accepted
- Date: 2026-10-09

## Context

Phase 7 (brief sections 10.4 and 13) adds the people side: who may sign in, what each person may do, an approval step before vouchers and
invoices are posted, and a log of changes that can be read. The desktop edition has one window and one company file, protected by the file
password; the cloud edition will later run the same code for many people at once. People using Baba are not technical, so everything shown
is a plain sentence in their language, and a company that never turns accounts on must work exactly as before.

## Decision

**Accounts are optional and live inside the company file.** `AppUser` and `Role` rows are part of the file (so backups, restores and
copies carry them). A company with no users has no sign-in: everyone who opens the file can do everything, and the audit log records the
Windows user name as before. Turning accounts on (Settings, or Users and roles) makes the first user, an administrator, and signs them
in. From then on: the file password first, then the user's own name and password.

**Passwords** are PBKDF2 (SHA-256, 200,000 iterations, per-user salt), at least 8 characters and not equal to the user name. A password
set by an administrator is temporary (`MustChangePassword`): the person must choose their own before anything else works. Sign-in refuses
with the same message for an unknown name and a wrong password. A forgotten administrator password is replaced by whoever knows the
company file password (`/api/session/recover`): the file password is the master key and nothing else can open the file anyway. The audit
log never contains password hashes ("Password: changed" only).

**Roles are sets of permissions; a permission is area x action.** Areas are the parts of the program (accounting, reports, banking,
parties, trade, products, tax, inventory, assets, payroll, claims, budgets, settings, users); actions are View, Edit, Delete and
Approve (post, approve, lock, close: the steps that make something final). Five built-in roles (Administrator, Accountant, Sales,
Storekeeper, Viewer) cannot be changed; a company copies one to make its own. Areas of modules a company has switched off are left out
of the role screen. The last active administrator cannot be removed, demoted or deactivated, and nobody deletes themselves.

**Enforcement is on the server, deny by default.** `PermissionMap` gives every `/api` route a rule (open before sign-in, any signed-in
person, or area x action). A route without a rule is refused, and a test enumerates every route of the API so a new endpoint cannot be
forgotten. `AccessMiddleware` runs after the host token check. Lookup lists that other screens fill their pick-lists from (accounts,
products, tax codes ...) need only a sign-in to read. The program's own automatic runs (depreciation, payroll months, end-of-service
provision, recurring documents when a company opens) run under `AppSession.AsSystem()` and are not held back by the signed-in person's
role. The screens follow the same permissions (menu, "+ New" buttons, the Summary), but the server is what protects.

**The session is one signed-in person per running program** (`AppSession` singleton): the desktop app has one window. The cloud edition
replaces it with a per-request session behind the same `AccessService`; `ICurrentUser` already comes from the session, so `CreatedBy`
and the audit log name the signed-in person. Opening, creating or closing a company starts a new session. Everything the screens read
under one person is forgotten when the person changes (`useSessionUpdate`), so nobody sees figures they were not allowed to see.

**Approval is a company setting (`SecuritySettings.ApprovalRequired`), off by default, and needs accounts.** When on, only a person with
the area's Approve permission can post a voucher, issue an invoice or note, take a posting back to draft, or delete a posted one
(`AccessService.RequireDirectPostingAsync`, called by `VoucherService` and `DocumentService`). Anyone else saves a draft and sends it
(`ApprovalRequest`, kept in its own table: subject, state Pending/Approved/Rejected, note, who and when, decision note). An approver
approves, which posts the draft exactly as saved by the approver's own act, or sends it back with a reason. Changing or deleting the
voucher or document withdraws a waiting or rejected request, so an approver always approves the figures that are in front of them.
Quotes, orders and delivery notes post nothing and need no approval. Receipts and payments set against invoices (settlements), and
imports of journal entries and opening balances, go through the same gate and so need an approver while approval is on.

**The audit log viewer** reads `AuditLogEntry` (written automatically on every save) in plain words: record kind in both languages, a
number or name for the record, and the old and new value of each changed field. Filters: dates, person, kind of record; newest first, 500
at a time. It exports to Excel, CSV and PDF like every other list (`audit-log` report key).

## Consequences

- A company that does not turn accounts on notices nothing, and its audit log still names the Windows user.
- Adding an endpoint without a `PermissionMap` rule fails the route-coverage test; adding a permission area is one line in
  `PermissionAreas` plus its translations (a test checks both languages).
- Not done and not claimed: single sign-on or other identity providers, two-factor sign-in, password expiry and lock-out after failed
  attempts, per-record (row-level) permissions, approval with several steps or limits by amount, e-mail or other notifications of
  waiting approvals (the Summary and the menu show a count), approval of expense claims and payroll beyond their own Approve permission,
  amounts in the audit log shown as money (they appear as the stored scaled numbers), and individual buttons hidden on forms for
  read-only roles (the server refuses and the screen says so in words; only "+ New" and the menu follow the role).
- The cloud edition needs a per-request session, a real user directory and rate limiting on sign-in; the contracts here (`ICurrentUser`,
  `AccessService`, `PermissionMap`) are where those plug in.
