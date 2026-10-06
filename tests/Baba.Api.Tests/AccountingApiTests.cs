using System.Net;
using System.Net.Http.Json;
using System.Text;
using Baba.Api.Endpoints;
using Baba.Api.Errors;
using Baba.Application.Accounting;
using Baba.Application.Printing;
using Baba.Application.Reporting;
using Baba.Domain;
using Baba.Domain.Accounting;

namespace Baba.Api.Tests;

public class AccountingApiTests : ApiFixture
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly Dictionary<string, AccountDto> _accounts = [];

    private HttpClient Http => Client;

    private async Task<HttpResponseMessage> PostAsync<T>(string url, T body) => await Http.PostAsJsonAsync(url, body, Json);

    private async Task StartCompanyAsync()
    {
        Assert.Equal(HttpStatusCode.OK, (await CreateCompanyAsync()).StatusCode);
        foreach (var account in await ReadAsync<List<AccountDto>>(await Http.GetAsync("/api/accounts")))
            _accounts[account.Code] = account;
    }

    private Guid Id(string code) => _accounts[code].Id;

    private VoucherInput Payment(DateOnly date, params (string Code, decimal Amount)[] lines) => new(
        VoucherKind.Payment, date, Id("111"), "CHQ-7", "rent",
        lines.Select(l => new VoucherLineInput(null, Id(l.Code), null, l.Amount, 0)).ToList());

    private static readonly DateOnly Oct6 = new(2026, 10, 6);

    private async Task<VoucherDto> PostVoucherAsync(VoucherInput input, Guid? id = null)
    {
        var response = await PostAsync("/api/vouchers/post", new SaveVoucherRequest(id, input));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<VoucherDto>(response);
    }

    // ------------------------------------------------------------------ Chart of accounts

    [Fact]
    public async Task The_chart_can_be_listed_added_to_edited_deactivated_and_deleted_over_http()
    {
        await StartCompanyAsync();
        Assert.Equal(AccountRole.CashOrBank, _accounts["111"].Role);

        var created = await PostAsync("/api/accounts", new AccountInput("4281", "التدريب", "Training", Id("42"), AccountType.Expense, true, AccountRole.None));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var account = await ReadAsync<AccountDto>(created);

        var renamed = await Http.PutAsJsonAsync($"/api/accounts/{account.Id}", new AccountInput("4281", "التدريب", "Staff training", Id("42"), AccountType.Expense, true, AccountRole.None), Json);
        Assert.Equal("Staff training", (await ReadAsync<AccountDto>(renamed)).NameEn);

        var inactive = await ReadAsync<AccountDto>(await PostAsync($"/api/accounts/{account.Id}/active", new SetActiveRequest(false)));
        Assert.False(inactive.IsActive);

        Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"/api/accounts/{account.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.DeleteAsync($"/api/accounts/{account.Id}")).StatusCode);
    }

    [Fact]
    public async Task Chart_mistakes_come_back_as_field_problems_the_screen_can_translate()
    {
        await StartCompanyAsync();

        var response = await PostAsync("/api/accounts", new AccountInput("422", "x", "x", Id("42"), AccountType.Expense, true, AccountRole.None));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ApiProblem>(response);
        Assert.Equal("Validation", problem.Problem);
        Assert.Contains(problem.Issues!, i => i is { Field: "code", Code: "account.code-duplicate" });
    }

    [Fact]
    public async Task An_account_with_entries_cannot_be_deleted_but_its_entries_can_be_moved()
    {
        await StartCompanyAsync();
        await PostVoucherAsync(Payment(Oct6, ("424", 12m)));

        var blocked = await Http.DeleteAsync($"/api/accounts/{Id("424")}");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains((await ReadAsync<ApiProblem>(blocked)).Issues!, i => i.Code == "account.has-entries");

        var moved = await ReadAsync<MoveEntriesResult>(await PostAsync($"/api/accounts/{Id("424")}/move-entries", new MoveEntriesRequest(Id("427"))));
        Assert.Equal(1, moved.VouchersMoved);
        Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"/api/accounts/{Id("424")}")).StatusCode);
    }

    // ------------------------------------------------------------------ Vouchers

    [Fact]
    public async Task A_voucher_can_be_saved_as_a_draft_posted_edited_listed_opened_and_deleted()
    {
        await StartCompanyAsync();

        var draft = await ReadAsync<VoucherDto>(await PostAsync("/api/vouchers/draft", new SaveVoucherRequest(null, Payment(Oct6, ("422", 100m)))));
        Assert.Equal((VoucherStatus.Draft, null), (draft.Status, draft.Number));

        var posted = await ReadAsync<VoucherDto>(await PostAsync($"/api/vouchers/{draft.Id}/post", new { }));
        Assert.Equal((VoucherStatus.Posted, "PV-2026-0001"), (posted.Status, posted.Number));

        var edited = await PostVoucherAsync(Payment(Oct6, ("422", 80m), ("423", 20m)), posted.Id);
        Assert.Equal(("PV-2026-0001", 100m, 2), (edited.Number, edited.Total, edited.Lines.Count));

        var list = await ReadAsync<List<VoucherSummary>>(await Http.GetAsync("/api/vouchers?kind=Payment&status=Posted&from=2026-10-01&to=2026-10-31"));
        var row = Assert.Single(list);
        Assert.Equal((posted.Id, "PV-2026-0001", 100m, 2), (row.Id, row.Number, row.Total, row.LineCount));
        Assert.Empty(await ReadAsync<List<VoucherSummary>>(await Http.GetAsync("/api/vouchers?kind=Receipt")));

        var opened = await ReadAsync<VoucherDto>(await Http.GetAsync($"/api/vouchers/{posted.Id}"));
        Assert.Equal("CHQ-7", opened.Reference);

        Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"/api/vouchers/{posted.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync($"/api/vouchers/{posted.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.DeleteAsync($"/api/vouchers/{posted.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_voucher_that_cannot_be_posted_says_which_row_and_why()
    {
        await StartCompanyAsync();
        var input = Payment(Oct6, ("422", 10m), ("423", 0m)) with { CashAccountId = null };

        var response = await PostAsync("/api/vouchers/post", new SaveVoucherRequest(null, input));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var issues = (await ReadAsync<ApiProblem>(response)).Issues!;
        Assert.Contains(issues, i => i is { Field: "lines[1].amount", Code: "line.amount-required" });
        Assert.Contains(issues, i => i is { Field: "cashAccount", Code: "cash-account.required" });
        Assert.Empty(await ReadAsync<List<VoucherSummary>>(await Http.GetAsync("/api/vouchers")));
    }

    [Fact]
    public async Task A_locked_month_refuses_new_vouchers_until_it_is_unlocked()
    {
        await StartCompanyAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync("/api/periods/lock", new LockPeriodRequest(new DateOnly(2026, 10, 1), true))).StatusCode);
        var months = await ReadAsync<List<PeriodDto>>(await Http.GetAsync("/api/periods?fiscalYear=2026"));
        Assert.Equal(12, months.Count);
        Assert.Equal([10], months.Where(m => m.IsLocked).Select(m => m.Start.Month));

        var refused = await PostAsync("/api/vouchers/post", new SaveVoucherRequest(null, Payment(Oct6, ("422", 5m))));
        Assert.Contains((await ReadAsync<ApiProblem>(refused)).Issues!, i => i.Code == "date.locked-period");

        await PostAsync("/api/periods/lock", new LockPeriodRequest(new DateOnly(2026, 10, 1), false));
        await PostVoucherAsync(Payment(Oct6, ("422", 5m)));

        var notMonthStart = await PostAsync("/api/periods/lock", new LockPeriodRequest(new DateOnly(2026, 10, 15), true));
        Assert.Equal(HttpStatusCode.BadRequest, notMonthStart.StatusCode);
    }

    // ------------------------------------------------------------------ Reports and exports

    [Fact]
    public async Task Reports_are_computed_from_the_ledger_and_drill_down_to_statements()
    {
        await StartCompanyAsync();
        await PostVoucherAsync(Payment(Oct6, ("422", 750m)));

        var trialBalance = await ReadAsync<ReportResult>(await Http.GetAsync("/api/reports/trial-balance?from=2026-10-01&to=2026-10-31"));
        Assert.Equal("Trial balance", trialBalance.TitleEn);
        Assert.All(trialBalance.Checks, c => Assert.True(c.Passed));
        var rent = trialBalance.Rows.Single(r => r.Cells[0].Text == "422");
        Assert.Equal(ReportLink.Account, rent.Link!.Kind);

        var statement = await ReadAsync<ReportResult>(await Http.GetAsync($"/api/reports/statement-of-account?accountId={rent.Link.Id}&from=2026-10-01&to=2026-10-31"));
        Assert.Equal(750m, statement.Rows[^1].Cells[5].Amount);

        var profit = await ReadAsync<ReportResult>(await Http.GetAsync("/api/reports/profit-and-loss?from=2026-10-01&to=2026-10-31&comparison=PreviousYear"));
        Assert.Equal(["code", "name", "amount", "previous"], profit.Columns.Select(c => c.Key));

        var balanceSheet = await ReadAsync<ReportResult>(await Http.GetAsync("/api/reports/balance-sheet?asOf=2026-10-31"));
        Assert.All(balanceSheet.Checks, c => Assert.True(c.Passed));

        foreach (var key in new[] { "general-ledger", "journal", "chart-of-accounts", "vouchers" })
            Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync($"/api/reports/{key}")).StatusCode);
    }

    [Fact]
    public async Task Report_requests_that_do_not_make_sense_are_refused_clearly()
    {
        await StartCompanyAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync("/api/reports/no-such-report")).StatusCode);
        var noAccount = await Http.GetAsync("/api/reports/statement-of-account");
        Assert.Equal(HttpStatusCode.BadRequest, noAccount.StatusCode);
        Assert.Contains((await ReadAsync<ApiProblem>(noAccount)).Issues!, i => i.Code == "report.account-required");
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync($"/api/reports/statement-of-account?accountId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Reports_export_to_excel_and_csv_and_pdf_needs_a_pdf_engine()
    {
        await StartCompanyAsync();
        await PostVoucherAsync(Payment(Oct6, ("422", 750m)));

        var csv = await Http.GetAsync("/api/reports/journal/export?format=Csv&layout=English&from=2026-10-01&to=2026-10-31");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Contains("filename=", csv.Content.Headers.ContentDisposition!.ToString());
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.Contains("PV-2026-0001", text);

        var xlsx = await Http.GetAsync("/api/reports/trial-balance/export?format=Xlsx&layout=Arabic");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        Assert.Equal((byte)'P', (await xlsx.Content.ReadAsByteArrayAsync())[0]); // an .xlsx file is a zip ("PK")

        Assert.Equal(HttpStatusCode.NotImplemented, (await Http.GetAsync("/api/reports/journal/export?format=Pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await Http.GetAsync($"/api/vouchers/{Guid.NewGuid()}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync("/api/reports/nothing/export?format=Csv")).StatusCode);
    }

    // ------------------------------------------------------------------ Branding and the dashboard

    [Fact]
    public async Task The_print_template_and_the_logo_can_be_set_read_and_removed()
    {
        await StartCompanyAsync();
        var settings = await ReadAsync<PrintSettingsDto>(await Http.GetAsync("/api/branding"));
        Assert.True(settings.ShowLogo);
        Assert.False(settings.HasLogo);

        var saved = await Http.PutAsJsonAsync("/api/branding", new PrintSettingsInput(
            true, true, false, true, true, "Block 5", "قطعة ٥", null, null, PrintLayout.Arabic, true), Json);
        var dto = await ReadAsync<PrintSettingsDto>(saved);
        Assert.Equal((PrintLayout.Arabic, false, "Block 5"), (dto.DefaultLayout, dto.ShowStamp, dto.HeaderTextEn));

        var upload = new HttpRequestMessage(HttpMethod.Put, "/api/branding/logo") { Content = new ByteArrayContent(Png) };
        upload.Headers.Add("X-File-Name", "logo.png");
        Assert.Equal(HttpStatusCode.NoContent, (await Http.SendAsync(upload)).StatusCode);

        var picture = await Http.GetAsync("/api/branding/logo");
        Assert.Equal("image/png", picture.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await picture.Content.ReadAsByteArrayAsync());
        Assert.True((await ReadAsync<PrintSettingsDto>(await Http.GetAsync("/api/branding"))).HasLogo);

        Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync("/api/branding/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync("/api/branding/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync("/api/branding/banner")).StatusCode);
    }

    [Fact]
    public async Task A_picture_that_is_too_big_or_not_a_picture_is_refused()
    {
        await StartCompanyAsync();

        var notAPicture = await Http.PutAsync("/api/branding/stamp", new ByteArrayContent(Encoding.UTF8.GetBytes("<svg onload=alert(1)>")));
        Assert.Equal(HttpStatusCode.BadRequest, notAPicture.StatusCode);
        Assert.Contains((await ReadAsync<ApiProblem>(notAPicture)).Issues!, i => i.Code == "image.unsupported-type");

        var huge = new byte[BrandingService.MaxImageBytes + 5000];
        Png.CopyTo(huge, 0);
        var tooLarge = await Http.PutAsync("/api/branding/logo", new ByteArrayContent(huge));
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Contains((await ReadAsync<ApiProblem>(tooLarge)).Issues!, i => i.Code == "image.too-large");
    }

    [Fact]
    public async Task The_dashboard_summarises_the_books()
    {
        await StartCompanyAsync();
        var empty = await ReadAsync<DashboardDto>(await Http.GetAsync("/api/dashboard"));
        Assert.Equal(("KWD", 3, 0m), (empty.CurrencyCode, empty.MinorUnits, empty.Cash));

        await PostVoucherAsync(Payment(DateOnly.FromDateTime(DateTime.Now), ("422", 25m)));
        var dashboard = await ReadAsync<DashboardDto>(await Http.GetAsync("/api/dashboard"));

        Assert.Equal(-25m, dashboard.Cash); // paid out of cash that had nothing in it yet
        Assert.Equal(-25m, dashboard.ProfitThisMonth);
    }

    [Fact]
    public async Task Everything_needs_an_open_company()
    {
        // No company has been created in this test.
        foreach (var url in new[] { "/api/accounts", "/api/vouchers", "/api/dashboard", "/api/reports/trial-balance", "/api/periods?fiscalYear=2026" })
        {
            var response = await Http.GetAsync(url);
            Assert.True(response.StatusCode is HttpStatusCode.Conflict, $"{url} answered {response.StatusCode}");
            Assert.Equal("NoCompanyOpen", (await ReadAsync<ApiProblem>(response)).Problem);
        }
    }
}

public class AccountingPdfApiTests : ApiFixture
{
    private readonly StubRenderer _renderer = new();

    protected override IPdfRenderer? PdfRenderer => _renderer;

    [Fact]
    public async Task With_a_pdf_engine_vouchers_and_reports_download_as_pdf_in_the_chosen_language()
    {
        await CreateCompanyAsync();
        var accounts = await ReadAsync<List<AccountDto>>(await Client.GetAsync("/api/accounts"));
        Guid Id(string code) => accounts.Single(a => a.Code == code).Id;
        var input = new VoucherInput(VoucherKind.Payment, new DateOnly(2026, 10, 6), Id("111"), null, null, [new(null, Id("422"), null, 750m, 0)]);
        var voucher = await ReadAsync<VoucherDto>(await Client.PostAsJsonAsync("/api/vouchers/post", new SaveVoucherRequest(null, input), Json));

        var voucherPdf = await Client.GetAsync($"/api/vouchers/{voucher.Id}/pdf?layout=Arabic");
        Assert.Equal("application/pdf", voucherPdf.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", _renderer.Html);
        Assert.Contains("فقط سبعمائة وخمسون ديناراً كويتياً لا غير", _renderer.Html);

        var reportPdf = await Client.GetAsync("/api/reports/trial-balance/export?format=Pdf&layout=English");
        Assert.Equal("application/pdf", reportPdf.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Trial balance", _renderer.Html);
        Assert.True(_renderer.Landscape);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/vouchers/{Guid.NewGuid()}/pdf")).StatusCode);
    }

    private sealed class StubRenderer : IPdfRenderer
    {
        public string? Html { get; private set; }
        public bool Landscape { get; private set; }

        public Task<byte[]> RenderAsync(string html, PdfOptions options, CancellationToken cancellationToken = default)
        {
            (Html, Landscape) = (html, options.Landscape);
            return Task.FromResult(Encoding.ASCII.GetBytes("%PDF-fake"));
        }
    }
}
