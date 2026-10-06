# Adding a country to Baba

A country is one folder: `src/Baba.Localization/<Code>/`. Nothing else in the app may name or check a country.
The core only talks to the `ICountryPack` contract, and the UI only reacts to the capabilities a pack reports.

## Checklist

1. **Research** and write `docs/countries/<code>.md`: currency, taxes, e-invoicing, payroll, statutory reports,
   and links to the official sources. Tax and e-invoicing rules change often, so verify against the official
   authority and a local accountant before building the tax module.
2. **Copy** `src/Baba.Localization/Template/` to `src/Baba.Localization/<Code>/`. Rename the namespace and the
   class, then **delete the `[ExcludeFromDiscovery]` attribute**. The pack is found automatically; there is no
   registration step.
3. **Fill in the data.** Only identity, currency and calendar are required. Everything else defaults to
   "not applicable" and can be overridden: tax codes, tax registration numbers, chart of accounts, document rules,
   translations, and the optional parts (tax return, e-invoicing, withholding, payroll, statutory reports).
4. **Add tests** in `tests/Baba.Localization.Tests/<Code>/`. The shared contract tests already run for every
   pack; add tests for that country's rates, number formats, rounding, and (later) tax return totals and
   amount in words in each language.
5. **Run `dotnet test`.** The architecture tests fail if any code outside `Baba.Localization` names a country or
   uses a pack, and if one pack uses another.
6. **If something outside the pack had to change**, stop. Extend the `ICountryPack` contract instead (every pack
   then implements it, possibly as "not applicable") and record why in an ADR.

## Removing a country

Delete its folder and its test folder. The build and every other country keep working.

## Rules of thumb

- Rates are percentages (`14` means 14%) and carry `EffectiveFrom` / `EffectiveTo` dates, so a change in rate never
  rewrites history.
- Money rounding follows the currency's minor units (`CurrencyRules.Currency.MinorUnits`), for example 3 for the dinar.
- Prefer data over code. If a country needs logic (for example an e-invoicing provider), put it in that country's
  folder behind the optional interface.
