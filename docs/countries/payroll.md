# Payroll defaults in the country packs (to verify)

These are the starting values each pack gives a company's payroll settings. They are **defaults to check with the
authority and the labour law, not verified rules**: rates, wage limits and the gratuity formulas change. The company can correct the
insurance rates and limits in Payroll settings; the end-of-service formula is in the pack (`Localization/<country>/`).

| Country | Insurance for nationals (employee / employer) | Others | Wage limits | End-of-service gratuity |
|---|---|---|---|---|
| Kuwait | 10.5% / 11.5% (PIFSS) | none | ceiling 2,750 | 15 days of wage a year for 5 years, then a month a year (26-day wage), capped at 18 months |
| Saudi Arabia | 9.75% / 11.75% (GOSI annuities and SANED) | employer 2% (occupational hazards) | 1,500 to 45,000 | half a month a year for 5 years, then a month a year |
| UAE | 5% / 12.5% (GPSSA) | none | ceiling 50,000 | 21 days a year for 5 years, then 30 (30-day wage), capped at 24 months |
| Egypt | 11% / 18.75% | none | left empty: enter the current limits | none (no gratuity in Egyptian law) |

Wage for the gratuity is the basic salary plus allowances flagged "counts for end-of-service". Insurance is worked out on the basic salary
plus allowances flagged "counts for social insurance", within the limits.

**Not built:** the official salary-transfer files (UAE WPS SIF, Saudi Mudad) and any filing with the insurance authorities. They need the
banks' or ministries' specifications and a way to test them. The payroll summary report (Excel, CSV, PDF) has each employee's bank and
account number for the list a bank asks for.
