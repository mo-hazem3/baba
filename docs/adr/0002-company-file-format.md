# ADR 0002: Company file format (SQLCipher, rollback journal, scaled-integer money)

- Status: accepted
- Date: 2026-10-06

## Context

Each company is one `.baba` file that users copy, back up and email (brief sections 1 and 9). It must be encrypted
with its own password, work offline, and keep money sums exact. Phase 0 spike (a) checked the risky parts. The
checks are kept as tests in `tests/Baba.Infrastructure.Tests/Spikes/SqlCipherSpikeTests.cs`.

## Findings

- **SQLCipher works with EF Core.** `Microsoft.EntityFrameworkCore.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher`,
  initialised with `SQLitePCL.Batteries_V2.Init()`, and the key given as `Password=` in the connection string.
  The file header is not `SQLite format 3` and stored text is not readable in the file.
- **A wrong password is detected on the first read** with `SqliteException` error code 26 (`SQLITE_NOTADB`). That is
  the same error as for a file that is not a Baba file, so the UI says "wrong password, or not a Baba file".
- **New files default to WAL.** `Microsoft.Data.Sqlite` creates databases in WAL mode. That can leave `-wal` and
  `-shm` files beside the `.baba` file, and copying only the `.baba` file can miss recent changes.
- **Closing a company needs `SqliteConnection.ClearAllPools()`.** Connection pooling keeps the file open after the
  context is disposed. After clearing the pools the file can be moved or deleted.
- **EF Core 10 translates `SUM` over a plain decimal column** (stored as text) using a managed `ef_sum` function per
  row. It is exact, but it runs managed code for every row and is not plain SQL.

## Decision

1. Company files are SQLite databases encrypted with SQLCipher. The password is never stored; the app warns that a
   forgotten password cannot be recovered.
2. **Journal mode is `DELETE` (the rollback journal), set when a file is created and again on every open.** A closed
   company is always one self-contained file. We do not use WAL on desktop.
3. **Money is stored as a scaled integer (amount x 10,000) in a `long` column.** The scaled `long` is the only mapped
   property; the decimal view is a computed, unmapped property. SQL sums, groups and raw-SQL reports use the
   integer column, which stays exact and is fast, and `Money` handles currency rounding in memory.
   Plain decimal columns must not be summed in queries.
4. Closing a company disposes its contexts, calls `SqliteConnection.ClearAllPools()`, and releases the file lock.

## Consequences

- Reports that sum money must query the scaled column (the repositories will hide this). Four decimal places are
  enough for amounts in every supported currency (up to 3 minor units).
- Exchange rates, quantities and unit costs need more precision than 4 decimals. They are not stored as scaled
  amounts. A separate ADR will set their scale when the first one is added (Phase 1 and later).
- On PostgreSQL (Phase 8) the same value is `numeric(19,4)`; the conversion is confined to the Infrastructure layer.
