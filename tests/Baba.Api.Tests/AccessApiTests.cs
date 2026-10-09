using System.Net;
using System.Net.Http.Json;
using Baba.Api.Endpoints;
using Baba.Api.Errors;
using Baba.Api.Security;
using Baba.Application.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Baba.Api.Tests;

/// <summary>Sign-in, and what each role may reach (brief section 10.4).</summary>
public class AccessApiTests : ApiFixture
{
    private const string AdminPassword = "owner password";

    private async Task<SessionInfo> TurnOnAsync() =>
        await ReadAsync<SessionInfo>(await Client.PostAsJsonAsync("/api/session/turn-on", new TurnOnAccountsRequest("owner", "The Owner", AdminPassword)));

    private async Task<HttpResponseMessage> SignInAsync(string name, string password) =>
        await Client.PostAsJsonAsync("/api/session/sign-in", new SignInInput(name, password));

    private async Task<UserDto> AddUserAsync(string name, string roleName, string password = "first password")
    {
        var roles = await ReadAsync<List<RoleDto>>(await Client.GetAsync("/api/roles"));
        var role = roles.Single(r => r.NameEn == roleName);
        return await ReadAsync<UserDto>(await Client.PostAsJsonAsync("/api/users", new UserInput(name, name, role.Id, password)));
    }

    private async Task SignedInAsAsync(string name, string password)
    {
        var response = await SignInAsync(name, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<ApiProblem> ProblemAsync(HttpResponseMessage response) => await ReadAsync<ApiProblem>(response);

    [Fact]
    public async Task A_company_without_users_asks_nobody_to_sign_in_and_allows_everything()
    {
        await CreateCompanyAsync();

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/users")).StatusCode);
        var session = await ReadAsync<SessionInfo>(await Client.GetAsync("/api/session"));
        Assert.False(session.AccountsOn);
        Assert.Contains("payroll.Approve", session.Permissions);
    }

    [Fact]
    public async Task Once_accounts_are_on_everything_but_signing_in_needs_a_signed_in_person()
    {
        await CreateCompanyAsync();
        var admin = await TurnOnAsync();
        Assert.True(admin.SignedIn);
        Assert.Equal("The Owner", admin.DisplayName);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/accounts")).StatusCode);

        await Client.PostAsync("/api/session/sign-out", null);

        var refused = await Client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("SignInRequired", (await ProblemAsync(refused)).Problem);
        var session = await ReadAsync<SessionInfo>(await Client.GetAsync("/api/session"));
        Assert.True(session.AccountsOn);
        Assert.False(session.SignedIn);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/company")).StatusCode); // the company's name is shown on the sign-in screen

        var wrong = await SignInAsync("owner", "not the password");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains((await ProblemAsync(wrong)).Issues!, i => i.Code == "auth.wrong-credentials");
        Assert.Equal(HttpStatusCode.BadRequest, (await SignInAsync("nobody", AdminPassword)).StatusCode); // the same answer for a name that does not exist
        await SignedInAsAsync("OWNER", AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/accounts")).StatusCode);
    }

    [Fact]
    public async Task A_role_reaches_only_its_areas_and_is_told_so_plainly()
    {
        await CreateCompanyAsync();
        await TurnOnAsync();
        await AddUserAsync("samira", "Sales");
        await Client.PostAsync("/api/session/sign-out", null);
        await SignedInAsAsync("samira", "first password");
        // An administrator set the password: the person chooses their own first.
        Assert.Equal("PasswordChangeRequired", (await ProblemAsync(await Client.GetAsync("/api/products"))).Problem);
        var changed = await Client.PostAsJsonAsync("/api/session/password", new ChangePasswordRequest("first password", "my own password"));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/products")).StatusCode);          // sales sees products
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/documents")).StatusCode);         // and its own documents
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/accounts")).StatusCode);          // a pick-list every form needs
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync("/api/payroll/runs/run-due", null)).StatusCode); // the program's own run

        foreach (var forbidden in new[] { "/api/vouchers", "/api/dashboard", "/api/users", "/api/employees", "/api/reports/trial-balance", "/api/payroll/runs", "/api/audit-log" })
        {
            var response = await Client.GetAsync(forbidden);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, forbidden + " answered " + response.StatusCode);
            var problem = await ProblemAsync(response);
            Assert.Equal("Forbidden", problem.Problem);
            Assert.Equal("forbidden.View", problem.Issues!.Single().Code);
        }

        var cannotChange = await Client.PostAsJsonAsync("/api/vouchers/draft", new { });
        Assert.Equal(HttpStatusCode.Forbidden, cannotChange.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.DeleteAsync($"/api/products/{Guid.NewGuid()}")).StatusCode); // sales may add and change products, not delete
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/reports/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("/api/reports/employees")).StatusCode);
    }

    [Fact]
    public async Task A_viewer_can_read_but_not_change_and_a_custom_role_gets_exactly_what_was_ticked()
    {
        await CreateCompanyAsync();
        await TurnOnAsync();
        await AddUserAsync("viewer1", "Viewer");
        var clerkRole = await ReadAsync<RoleDto>(await Client.PostAsJsonAsync("/api/roles", new RoleInput("", "Clerk", ["payroll.View", "payroll.Edit"])));
        await ReadAsync<UserDto>(await Client.PostAsJsonAsync("/api/users", new UserInput("clerk1", "Clerk", clerkRole.Id, "first password")));

        await Client.PostAsync("/api/session/sign-out", null);
        await SignedInAsAsync("viewer1", "first password");
        await Client.PostAsJsonAsync("/api/session/password", new ChangePasswordRequest("first password", "viewer password"));
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/reports/trial-balance")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/vouchers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsJsonAsync("/api/vouchers/draft", new { })).StatusCode);

        await Client.PostAsync("/api/session/sign-out", null);
        await SignedInAsAsync("clerk1", "first password");
        await Client.PostAsJsonAsync("/api/session/password", new ChangePasswordRequest("first password", "clerk password"));
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsync($"/api/payroll/runs/{Guid.NewGuid()}/post", null)).StatusCode); // approving needs Approve
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("/api/reports/trial-balance")).StatusCode);
    }

    [Fact]
    public async Task Closing_and_reopening_the_company_asks_everyone_to_sign_in_again()
    {
        await CreateCompanyAsync();
        await TurnOnAsync();

        await Client.PostAsync("/api/company/close", null);
        var reopened = await Client.PostAsJsonAsync("/api/company/open", new Baba.Api.Contracts.OpenCompanyRequest(NewPath(), Password));
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/accounts")).StatusCode);
        await SignedInAsAsync("owner", AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/accounts")).StatusCode);
    }

    [Fact]
    public async Task A_forgotten_administrator_password_can_be_replaced_by_whoever_knows_the_file_password()
    {
        await CreateCompanyAsync();
        await TurnOnAsync();
        await Client.PostAsync("/api/session/sign-out", null);

        var wrong = await Client.PostAsJsonAsync("/api/session/recover", new RecoverRequest("not the file password", "owner", "The Owner", "a new password"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains((await ProblemAsync(wrong)).Issues!, i => i.Code == "auth.file-password-wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/accounts")).StatusCode);

        var recovered = await Client.PostAsJsonAsync("/api/session/recover", new RecoverRequest(Password, "owner", "The Owner", "a new password"));
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/accounts")).StatusCode);
        await Client.PostAsync("/api/session/sign-out", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await SignInAsync("owner", AdminPassword)).StatusCode); // the old password no longer works
        await SignedInAsAsync("owner", "a new password");
    }

    [Fact]
    public async Task Every_route_of_the_api_has_a_permission_rule()
    {
        var routes = App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        Assert.True(routes.Count > 150, "The API has far more routes than that: the check found " + routes.Count);

        var missing = new List<string>();
        foreach (var route in routes)
        {
            var path = "/" + System.Text.RegularExpressions.Regex.Replace(route.RoutePattern.RawText!.TrimStart('/'), @"\{[^}]*:guid\}", "00000000-0000-0000-0000-000000000001");
            path = System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*:int\}", "2026");
            path = System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*\}", "sample");
            var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var method in methods)
            {
                if (PermissionMap.Resolve(method, path) is null)
                    missing.Add($"{method} {path}");
            }
        }

        Assert.True(missing.Count == 0, "These routes have no permission rule in PermissionMap: " + string.Join(", ", missing));
    }
}
