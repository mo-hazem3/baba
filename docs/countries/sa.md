# Saudi Arabia (SA) — what Baba does and what it does not yet

Starting point only: verify every rule against ZATCA and a local accountant before relying on it (brief section 8).

## Done and checked

| Topic | What Baba does | How it was checked |
|---|---|---|
| Currency | Saudi riyal, 2 decimals, halala | pack tests |
| VAT | Standard 15% (from 1 July 2020), zero-rated, exempt, out of scope as tax codes | pack tests; tax tests (posting, discount, foreign currency, credit notes) |
| VAT number | 15 digits starting and ending with 3 | pack test |
| Tax return | Generic VAT return by tax code for a date range, checked against the ledger; quarterly in the pack | tax tests, browser test |
| Tax invoice | Taxed invoices print as "Tax invoice" / "فاتورة ضريبية" with tax columns and both parties' tax numbers; both languages are available as the print layout | print tests |
| QR code | The five-field TLV QR code of ZATCA's *Guide to Developed FATOORA Compliant QR Code* (Nov 2021): 1 seller name, 2 VAT number, 3 time stamp (UTC), 4 total with VAT, 5 VAT total; UTF-8, one-byte tag and length, base64; printed as an SVG QR code on issued sales invoices and credit notes in riyals | the guide's own worked example, byte for byte (`ZatcaEInvoicingTests`) |
| Locked invoices | An issued invoice or note cannot be edited or deleted; reverse it with a credit note | tests |

## Not built, and not claimed

The **integration phase** of ZATCA's e-invoicing (Fatoora), in force in waves from 1 January 2023:

- the signed XML invoice (UBL 2.1 with ZATCA's fields), including QR tags 6–9 (hash, signature, public key, the stamp's signature),
- the invoice counter and the previous-invoice hash chain,
- onboarding a device for a cryptographic stamp (CSR, compliance checks, OTP from the Fatoora portal),
- **clearance** of standard (B2B) tax invoices before they are issued, and **reporting** of simplified (B2C) invoices within 24 hours, through ZATCA's API.

These need ZATCA's onboarding credentials, certificates and sandbox, none of which can be exercised from the development machine, so no
code for them has been written rather than code that looks right and has never been accepted. Phase 4's goal in the brief ("sandbox
e-invoice submission passes") is therefore **not met for any country**. Next steps, in order: get sandbox access, read the current
*XML Implementation Standard*, *Security Features Implementation Standards* and *Detailed Technical Guidelines*, then add to
`ZatcaEInvoicing` (build XML, hash, sign, submit, store the response, UUID and status on the document).

Also not built: Zakat, withholding rules, the official VAT return box layout.

## Sources (read 2026-10-07)

- ZATCA, *Guide to Developed FATOORA Compliant QR Code*, 18 Nov 2021 — https://zatca.gov.sa/en/E-Invoicing/SystemsDevelopers/Documents/QRCodeCreation.pdf
- ZATCA, *Electronic Invoice Security Features Implementation Standards* v1.2 (19 May 2023) — https://zatca.gov.sa/ar/E-Invoicing/SystemsDevelopers/Documents/20230519_ZATCA_Electronic_Invoice_Security_Features_Implementation_Standards_vF.pdf
- ZATCA, *Electronic Invoice XML Implementation Standard* — https://zatca.gov.sa/ar/E-Invoicing/SystemsDevelopers/Documents/20220624_ZATCA_Electronic_Invoice_XML_Implementation_Standard_vF.pdf
- ZATCA, *E-invoicing Detailed Technical Guideline* — https://zatca.gov.sa/en/E-Invoicing/Introduction/Guidelines/Documents/E-invoicing-Detailed-Technical-Guideline.pdf
