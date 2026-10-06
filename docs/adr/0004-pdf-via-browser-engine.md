# ADR 0004: PDFs are made by the browser engine from HTML and CSS

- Status: accepted
- Date: 2026-10-06

## Context

Printouts must render Arabic correctly (letters join, right-to-left, Arabic-Indic digits), print Arabic, English or both
side by side, and be editable as print templates (brief sections 6, 7 and 10.1). The brief says many PDF libraries
cannot do Arabic and asks for a check in Phase 0. The owner left the engine choice to us.

Options considered:

1. **Browser engine (HTML and CSS to PDF).** WebView2's print-to-PDF on desktop; headless Chromium in the cloud.
2. **QuestPDF** (pure .NET). Arabic works, but layouts are written in C# code, so print templates are not editable by
   users, and its license depends on company revenue, which is still an open question for Baba (brief section 15).

## Decision

Use the browser engine. The app builds an HTML page, and an `IPdfRenderer` turns it into a PDF.

- `IPdfRenderer` (Application) is implemented on desktop by `WebView2PdfRenderer`, which prints from a hidden WebView2
  window. The cloud edition will implement the same interface with headless Chromium.
- Printouts are HTML and CSS, so Arabic shaping and bidirectional text come from the same engine that draws the screen,
  bilingual side-by-side pages are ordinary CSS, and the print templates of Phase 1 can be edited as HTML.
- Fonts are embedded in the app (Noto Sans Arabic 400 and 700, SIL Open Font License, see `docs/licenses`) and written
  into each page as data URLs, so a PDF never depends on fonts installed on the computer.
- All user text is HTML-encoded by the page builder (`TestPageBuilder`), and numbers are isolated left to right so
  signs stay in front of digits inside Arabic text.

## Verified

`Baba.Desktop.exe --smoke-test` makes the Arabic, English and bilingual test pages as real PDFs, and the Desktop test
checks that each is a valid PDF with the Arabic font embedded. The pages were also inspected visually: letters join
correctly, tables mirror right-to-left (the "#" column on the right), digits are Arabic-Indic in the Arabic page,
negative amounts are red with a minus sign, and the bilingual page stacks English above Arabic.

## Consequences

- Needs the WebView2 runtime (present on current Windows 10 and 11; the installer will check for it).
- A hidden window is used for printing; renders run one at a time.
- Latin text falls back to Segoe UI, Tahoma or Arial (installed on Windows). For the cloud edition a Latin font (for
  example Noto Sans) must be embedded too, because servers may not have these fonts.
- Amount in words (brief section 5) is not part of the test page. It needs its own engine (Arabic and English, per
  currency) in Phase 1.
