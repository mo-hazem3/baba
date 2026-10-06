# ADR 0001: Country packs live in one project and are discovered automatically

- Status: accepted
- Date: 2026-10-06

## Context

Baba supports Egypt, Saudi Arabia, the UAE and Kuwait first, and more countries later. The brief (section 8)
requires that no code outside a country pack knows about a specific country, and that adding a country is easy.
We considered one project per country, but that adds solution and reference overhead for little gain.

## Decision

- All packs live in the single `Baba.Localization` project, one folder (and namespace) per country:
  `Baba.Localization/<Code>/`.
- The `ICountryPack` contract and `CountryPackBase` (safe "not applicable" defaults) are in the root namespace
  `Baba.Localization`.
- `CountryPackRegistry.Discover()` finds every concrete pack in the assembly. There is no registration list, so
  adding a country means adding a folder, and removing one means deleting it.
- The rule is enforced by tests in `tests/Baba.Architecture.Tests`: core assemblies may not depend on any
  `Baba.Localization.<Country>` namespace, packs may not depend on each other, and core source files may not contain
  country names, currency codes or quoted country codes.
- A `Template` pack (excluded from discovery) is the starting point for a new country.

## Consequences

- Adding a country needs no change outside its folder and tests (checklist: `docs/countries/README.md`).
- The architecture tests need the Windows-only desktop project, so they run on Windows (target `net10.0-windows`).
- If a country needs something the contract cannot express, the fix is a new capability on `ICountryPack`, not a
  country check in the core.
