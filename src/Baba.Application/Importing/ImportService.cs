using Baba.Application.Accounting;
using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Application.Importing;

/// <summary>
/// Bringing a chart of accounts or a list of customers or suppliers in from a CSV or Excel file (brief section 10.2), for example from
/// the spreadsheet or the old program a business used before. The first row names the columns, in English or Arabic. An import is all or
/// nothing: if any row is wrong, nothing is added and every wrong row is named.
/// </summary>
public sealed class ImportService(IAccountStore accounts, IPartyStore parties, ITabularReader reader)
{
    // ---------------------------------------------------------------- Chart of accounts

    /// <summary>
    /// Columns: code, name (English), name (Arabic), parent code, type (Asset, Liability, Equity, Revenue, Expense), kind (group or
    /// posting), special use. Only the code and one name are needed. A parent is another account in the file or one that exists; a row
    /// that names no type takes its parent's. An account that is the parent of another is a group, the others take entries.
    /// </summary>
    public async Task<ImportResult> ImportAccountsAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        if (!TryRead(fileName, content, out var rows))
            return Fail("import.unreadable");

        var header = FindHeader(rows, out var headerIndex, "code", "account code", "الرمز", "رمز الحساب");
        if (header is null)
            return Fail("accounts.code-column-missing");

        int code = ImportParsing.FindColumn(header, "code", "account code", "الرمز", "رمز الحساب");
        int nameEn = ImportParsing.FindColumn(header, "name", "name en", "english name", "name (english)", "الاسم بالانجليزية", "الاسم (بالانجليزية)", "الاسم (بالإنجليزية)");
        int nameAr = ImportParsing.FindColumn(header, "name ar", "arabic name", "name (arabic)", "الاسم", "الاسم بالعربية", "الاسم (بالعربية)");
        int parent = ImportParsing.FindColumn(header, "parent", "parent code", "الحساب الاب", "رمز الحساب الرئيسي", "الاب");
        int type = ImportParsing.FindColumn(header, "type", "account type", "النوع", "نوع الحساب");
        int kind = ImportParsing.FindColumn(header, "kind", "level", "group", "الصنف", "المستوى");
        int role = ImportParsing.FindColumn(header, "role", "special use", "استخدام خاص", "الدور");

        var existing = (await accounts.ListAsync(cancellationToken)).ToList();
        var pending = new List<(int Row, string Code, string NameEn, string NameAr, string ParentCode, string Type, string Kind, string Role)>();
        var issues = new List<ImportIssue>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;
            pending.Add((i + 1, ImportParsing.Cell(row, code), ImportParsing.Cell(row, nameEn), ImportParsing.Cell(row, nameAr),
                ImportParsing.Cell(row, parent), ImportParsing.Cell(row, type), ImportParsing.Cell(row, kind), ImportParsing.Cell(row, role)));
        }

        if (pending.Count == 0)
            return Fail("import.empty");

        var parentCodes = pending.Select(p => p.ParentCode).Where(c => c.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var all = new List<Account>(existing);
        var added = new List<Account>();
        var byCode = existing.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);

        // Add each account once its parent is there, so the file may list them in any order.
        while (pending.Count > 0)
        {
            var ready = pending.Where(p => p.ParentCode.Length == 0 || byCode.ContainsKey(p.ParentCode)).ToList();
            if (ready.Count == 0)
            {
                issues.AddRange(pending.Select(p => new ImportIssue(p.Row, "account.parent-unknown")));
                break;
            }

            foreach (var item in ready)
            {
                pending.Remove(item);
                var parentAccount = item.ParentCode.Length > 0 ? byCode[item.ParentCode] : null;

                var accountType = ParseType(item.Type) ?? parentAccount?.Type;
                if (accountType is null)
                {
                    issues.Add(new ImportIssue(item.Row, item.Type.Length == 0 ? "account.type-required" : "account.type-unknown"));
                    continue;
                }

                var account = new Account
                {
                    Code = item.Code.Trim(),
                    NameEn = item.NameEn.Trim(),
                    NameAr = item.NameAr.Trim(),
                    ParentId = parentAccount?.Id,
                    Type = accountType.Value,
                    IsPosting = ParseKind(item.Kind) ?? !parentCodes.Contains(item.Code.Trim()),
                    Role = ParseRole(item.Role, out var roleKnown),
                };
                if (!roleKnown)
                {
                    issues.Add(new ImportIssue(item.Row, "account.role-unknown"));
                    continue;
                }

                if (account.NameAr.Length == 0) account.NameAr = account.NameEn;
                if (account.NameEn.Length == 0) account.NameEn = account.NameAr;

                var problems = ChartRules.Validate(account, all, hasEntries: false);
                if (problems.Count > 0)
                {
                    issues.AddRange(problems.Select(p => new ImportIssue(item.Row, p.Code)));
                    continue;
                }

                all.Add(account);
                added.Add(account);
                byCode[account.Code] = account;
            }
        }

        if (issues.Count > 0)
            return new ImportResult(0, 0, issues.OrderBy(i => i.Row).ToList());

        foreach (var account in added)
            await accounts.AddAsync(account, cancellationToken);
        return new ImportResult(added.Count, 0, []);
    }

    private static AccountType? ParseType(string text) => ArabicText.Normalize(text).Replace(" ", "") switch
    {
        "asset" or "assets" or "اصول" or "اصل" => AccountType.Asset,
        "liability" or "liabilities" or "خصوم" or "التزامات" => AccountType.Liability,
        "equity" or "حقوقالملكيه" or "حقوقملكيه" => AccountType.Equity,
        "revenue" or "revenues" or "income" or "ايرادات" or "ايراد" => AccountType.Revenue,
        "expense" or "expenses" or "مصروفات" or "مصروف" or "مصاريف" => AccountType.Expense,
        _ => null,
    };

    private static bool? ParseKind(string text) => ArabicText.Normalize(text).Replace(" ", "") switch
    {
        "" => null,
        "posting" or "detail" or "account" or "فرعي" or "تفصيلي" or "حساب" or "no" or "0" or "false" => true,
        "group" or "header" or "main" or "رئيسي" or "مجموعه" or "yes" or "1" or "true" => false,
        _ => null,
    };

    private static AccountRole ParseRole(string text, out bool known)
    {
        known = true;
        switch (ArabicText.Normalize(text).Replace(" ", ""))
        {
            case "" or "none" or "-" or "بلا":
                return AccountRole.None;
            case "cashorbank" or "cash" or "bank" or "صندوق" or "بنك" or "صندوقاوبنك":
                return AccountRole.CashOrBank;
            case "receivable" or "receivables" or "مدينون" or "عملاء":
                return AccountRole.Receivable;
            case "payable" or "payables" or "دائنون" or "موردون":
                return AccountRole.Payable;
            case "retainedearnings" or "الارباحالمبقاه" or "ارباحمبقاه":
                return AccountRole.RetainedEarnings;
            default:
                known = false;
                return AccountRole.None;
        }
    }

    // ---------------------------------------------------------------- Customers and suppliers

    /// <summary>
    /// Columns: code, name (English), name (Arabic), phone, email, address, tax number, credit limit, payment terms (days), and
    /// optionally kind (customer or supplier). Without a kind column every row is <paramref name="defaultKind"/>. A missing code is
    /// filled in with the next free C001 or S001.
    /// </summary>
    public async Task<ImportResult> ImportPartiesAsync(PartyKind defaultKind, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        if (!TryRead(fileName, content, out var rows))
            return Fail("import.unreadable");

        var header = FindHeader(rows, out var headerIndex, "name", "name en", "english name", "name (english)", "الاسم", "الاسم بالعربية", "الاسم (بالعربية)", "الاسم (بالإنجليزية)", "الاسم بالانجليزية", "الاسم (بالانجليزية)");
        if (header is null)
            return Fail("parties.name-column-missing");

        int code = ImportParsing.FindColumn(header, "code", "الرمز");
        int nameEn = ImportParsing.FindColumn(header, "name", "name en", "english name", "name (english)", "الاسم بالانجليزية", "الاسم (بالانجليزية)", "الاسم (بالإنجليزية)");
        int nameAr = ImportParsing.FindColumn(header, "name ar", "arabic name", "name (arabic)", "الاسم", "الاسم بالعربية", "الاسم (بالعربية)");
        int phone = ImportParsing.FindColumn(header, "phone", "mobile", "tel", "الهاتف", "الجوال");
        int email = ImportParsing.FindColumn(header, "email", "e-mail", "البريد الالكتروني", "البريد");
        int address = ImportParsing.FindColumn(header, "address", "العنوان");
        int tax = ImportParsing.FindColumn(header, "tax number", "tax id", "vat", "registration number", "الرقم الضريبي", "الرقم التجاري");
        int limit = ImportParsing.FindColumn(header, "credit limit", "limit", "الحد الائتماني");
        int terms = ImportParsing.FindColumn(header, "payment terms", "terms", "payment terms days", "days", "شروط السداد");
        int kindColumn = ImportParsing.FindColumn(header, "kind", "type", "النوع", "الصنف");

        var all = (await parties.ListAsync(cancellationToken)).ToList();
        var added = new List<Party>();
        var issues = new List<ImportIssue>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;

            var kind = defaultKind;
            var kindText = ArabicText.Normalize(ImportParsing.Cell(row, kindColumn)).Replace(" ", "");
            if (kindText is "customer" or "عميل")
                kind = PartyKind.Customer;
            else if (kindText is "supplier" or "vendor" or "مورد")
                kind = PartyKind.Supplier;
            else if (kindText.Length > 0)
            {
                issues.Add(new ImportIssue(i + 1, "party.kind-unknown"));
                continue;
            }

            decimal limitValue = 0;
            var limitText = ImportParsing.Cell(row, limit);
            if (limitText.Length > 0 && ImportParsing.ParseAmount(limitText) is not { } parsedLimit)
            {
                issues.Add(new ImportIssue(i + 1, "party.credit-limit-invalid"));
                continue;
            }
            else if (limitText.Length > 0)
            {
                limitValue = ImportParsing.ParseAmount(limitText)!.Value;
            }

            var termsDays = 0;
            var termsText = ImportParsing.Cell(row, terms);
            if (termsText.Length > 0 && !int.TryParse(ArabicText.Digits(termsText), out termsDays))
            {
                issues.Add(new ImportIssue(i + 1, "party.terms-invalid"));
                continue;
            }

            var party = new Party
            {
                Kind = kind,
                Code = ImportParsing.Cell(row, code),
                NameEn = ImportParsing.Cell(row, nameEn),
                NameAr = ImportParsing.Cell(row, nameAr),
                Phone = Clean(ImportParsing.Cell(row, phone)),
                Email = Clean(ImportParsing.Cell(row, email)),
                Address = Clean(ImportParsing.Cell(row, address)),
                TaxNumber = Clean(ImportParsing.Cell(row, tax)),
                CreditLimit = kind == PartyKind.Customer ? limitValue : 0,
                PaymentTermsDays = termsDays,
            };
            if (party.NameAr.Length == 0) party.NameAr = party.NameEn;
            if (party.NameEn.Length == 0) party.NameEn = party.NameAr;
            if (party.Code.Length == 0)
                party.Code = NextCode(all, kind);

            var problems = PartyService.Validate(party, all);
            if (problems.Count > 0)
            {
                issues.AddRange(problems.Select(p => new ImportIssue(i + 1, p.Code)));
                continue;
            }

            all.Add(party);
            added.Add(party);
        }

        if (issues.Count > 0)
            return new ImportResult(0, 0, issues);
        if (added.Count == 0)
            return Fail("import.empty");

        foreach (var party in added)
            await parties.AddAsync(party, cancellationToken);
        return new ImportResult(added.Count, 0, []);
    }

    private static string NextCode(IReadOnlyList<Party> all, PartyKind kind)
    {
        var prefix = kind == PartyKind.Customer ? "C" : "S";
        var highest = all.Where(p => p.Kind == kind)
            .Select(p => int.TryParse(new string(p.Code.SkipWhile(c => !char.IsDigit(c)).ToArray()), out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefix}{highest + 1:000}";
    }

    // ---------------------------------------------------------------- Shared

    private bool TryRead(string fileName, byte[] content, out IReadOnlyList<IReadOnlyList<string>> rows)
    {
        try
        {
            rows = reader.Read(fileName, content);
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or NotSupportedException or IOException)
        {
            rows = [];
            return false;
        }
    }

    private static IReadOnlyList<string>? FindHeader(IReadOnlyList<IReadOnlyList<string>> rows, out int index, params string[] requiredAnyOf)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (ImportParsing.FindColumn(rows[i], requiredAnyOf) >= 0)
            {
                index = i;
                return rows[i];
            }
        }

        index = -1;
        return null;
    }

    private static ImportResult Fail(string code) => new(0, 0, [new ImportIssue(0, code)]);

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
