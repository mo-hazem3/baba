using Baba.Application.Importing;
using Baba.Application.Printing;
using ClosedXML.Excel;

namespace Baba.Infrastructure.Printing;

/// <summary>
/// The Excel files people download to fill in before an import: the header row the importer expects (with Arabic names under the
/// English ones in a note row) and one example row. The importer accepts the names in English or Arabic, in any order.
/// </summary>
public sealed class ImportTemplates : IImportTemplates
{
    private static readonly Dictionary<string, (string[] Headers, string[] Example)> Templates = new()
    {
        ["accounts"] = (["Code", "Name", "Name (Arabic)", "Parent code", "Type", "Kind", "Special use"], ["91", "Petty cash", "صندوق المصروفات", "9", "", "posting", "Cash"]),
        ["parties"] = (["Code", "Name", "Name (Arabic)", "Phone", "Email", "Address", "Tax number", "Credit limit", "Payment terms"], ["", "Gulf Traders", "الخليج للتجارة", "+965 5555 1111", "", "", "", "1500", "30"]),
        ["statement"] = (["Date", "Description", "Amount"], ["2026-10-06", "Transfer from customer", "1250.500"]),
        ["products"] = (["Code", "Name", "Name (Arabic)", "Unit", "Sale price", "Purchase price", "Revenue account", "Expense account", "Tax code", "Stock item", "Barcode", "Reorder level"], ["P001", "Consulting hour", "ساعة استشارة", "hour", "90", "", "512", "", "", "no", "", ""]),
        ["assets"] = (["Code", "Name", "Name (Arabic)", "Type", "Acquisition date", "Cost", "Salvage value", "Useful life (months)", "Method", "Annual rate (%)", "Asset account", "Accumulated depreciation account", "Depreciation expense account", "Depreciation so far", "Depreciated through"], ["A001", "Laptop", "حاسوب محمول", "Fixed asset", "2026-01-15", "1200", "0", "36", "Straight line", "", "121", "123", "426", "0", ""]),
        ["employees"] = (["Code", "Name", "Name (Arabic)", "Job title", "ID number", "National", "Join date", "Basic salary", "Bank", "Account number", "Annual leave days"], ["E001", "Sara Ahmed", "سارة أحمد", "Accountant", "290010100123", "yes", "2025-03-01", "750", "Gulf Bank", "KW81GBKU0000000000001234560101", "30"]),
        ["opening-stock"] = (["Product code", "Warehouse code", "Quantity", "Unit cost"], ["P001", "MAIN", "10", "25.5"]),
        ["exchange-rates"] = (["Currency", "Date", "Rate"], ["USD", "2026-10-01", "0.3075"]),
        ["journal"] = (["Entry", "Date", "Account", "Debit", "Credit", "Description", "Customer", "Cost center"], ["1", "2026-10-02", "111", "500", "", "Cash in", "", ""]),
        ["opening-balances"] = (["Account", "Debit", "Credit", "Customer", "Cost center"], ["111", "1000", "", "", ""]),
    };

    public ExportFile? Build(string key)
    {
        if (!Templates.TryGetValue(key, out var template))
            return null;

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Template");
        for (var c = 0; c < template.Headers.Length; c++)
        {
            var header = sheet.Cell(1, c + 1);
            header.Value = template.Headers[c];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF1F6");
            sheet.Cell(2, c + 1).Value = template.Example[c]; // text, so a code like 0123 keeps its zeros; the importer reads numbers from text
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ExportFile($"{key}-template.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", stream.ToArray());
    }
}
