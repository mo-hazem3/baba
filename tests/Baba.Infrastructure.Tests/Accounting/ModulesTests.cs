using Baba.Application;
using Baba.Application.Companies;
using Baba.Domain;
using Baba.Localization;

namespace Baba.Infrastructure.Tests.Accounting;

public class ModulesTests : AccountingFixture
{
    [Fact]
    public async Task Modules_can_be_switched_on_and_off_and_are_remembered_when_the_file_is_reopened()
    {
        var e = await NewEnvAsync();
        var service = new CompanyService(e.Files, CountryPackRegistry.Discover());

        var changed = await service.SetEnabledModulesAsync([ModuleKeys.CostCenters, ModuleKeys.BankAndCash, ModuleKeys.CostCenters]);

        Assert.Equal([ModuleKeys.BankAndCash, ModuleKeys.CostCenters], changed.EnabledModules); // in the catalog's order, once each
        Assert.Equal(changed.EnabledModules, service.Current!.EnabledModules);

        var path = e.Files.Current!.FilePath;
        e.Files.Close();
        var reopened = await e.Files.OpenAsync(path, Password);
        Assert.Equal([ModuleKeys.BankAndCash, ModuleKeys.CostCenters], reopened.EnabledModules);

        Assert.Empty((await service.SetEnabledModulesAsync([])).EnabledModules);
    }

    [Fact]
    public async Task An_unknown_module_is_refused()
    {
        var e = await NewEnvAsync();
        var service = new CompanyService(e.Files, CountryPackRegistry.Discover());

        var refused = await Assert.ThrowsAsync<ValidationException>(() => service.SetEnabledModulesAsync(["warp-drive"]));

        Assert.Contains(refused.Issues, i => i.Code == "module.unknown");
    }
}
