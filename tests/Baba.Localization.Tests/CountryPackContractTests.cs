using System.Text.RegularExpressions;
using Baba.Localization.Template;

namespace Baba.Localization.Tests;

/// <summary>Rules every country pack must follow, checked for every pack that is installed.</summary>
public class CountryPackContractTests
{
    private static readonly CountryPackRegistry Registry = CountryPackRegistry.Discover();

    public static TheoryData<string> PackCodes
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var pack in Registry.All)
                data.Add(pack.Identity.Code);
            return data;
        }
    }

    [Fact]
    public void At_least_one_pack_is_installed_and_the_template_is_not_registered()
    {
        Assert.NotEmpty(Registry.All);
        Assert.DoesNotContain(Registry.All, p => p is TemplatePack);
    }

    [Theory]
    [MemberData(nameof(PackCodes))]
    public void Identity_is_complete(string code)
    {
        var identity = Registry.Get(code).Identity;

        Assert.Matches("^[A-Z]{2}$", identity.Code);
        Assert.False(string.IsNullOrWhiteSpace(identity.NameEn));
        Assert.False(string.IsNullOrWhiteSpace(identity.NameAr));
        Assert.NotEmpty(identity.Languages);
    }

    [Theory]
    [MemberData(nameof(PackCodes))]
    public void Currency_has_names_in_both_languages_and_sensible_minor_units(string code)
    {
        var currency = Registry.Get(code).Currency;

        Assert.InRange(currency.Currency.MinorUnits, 0, 4);
        Assert.False(string.IsNullOrWhiteSpace(currency.MajorNameEn));
        Assert.False(string.IsNullOrWhiteSpace(currency.MajorNameAr));
        Assert.False(string.IsNullOrWhiteSpace(currency.MinorNameEn));
        Assert.False(string.IsNullOrWhiteSpace(currency.MinorNameAr));
    }

    [Theory]
    [MemberData(nameof(PackCodes))]
    public void Calendar_is_valid(string code)
    {
        var calendar = Registry.Get(code).Calendar;

        Assert.Contains(CalendarKind.Gregorian, calendar.Calendars);
        Assert.NotEmpty(calendar.DefaultWeekend);
        Assert.InRange(calendar.DefaultFiscalYearStartMonth, 1, 12);
    }

    [Theory]
    [MemberData(nameof(PackCodes))]
    public void Tax_codes_are_unique_and_valid(string code)
    {
        var taxCodes = Registry.Get(code).TaxCodes;

        Assert.Equal(taxCodes.Count, taxCodes.Select(t => t.Code).Distinct().Count());
        foreach (var tax in taxCodes)
        {
            Assert.InRange(tax.Rate, 0m, 100m);
            Assert.False(string.IsNullOrWhiteSpace(tax.NameEn));
            Assert.False(string.IsNullOrWhiteSpace(tax.NameAr));
            if (tax.EffectiveFrom is { } from && tax.EffectiveTo is { } to)
                Assert.True(from <= to, $"Tax code {tax.Code} ends before it starts.");
            if (tax.Category is TaxCategory.Zero or TaxCategory.Exempt or TaxCategory.OutOfScope)
                Assert.Equal(0m, tax.Rate);
        }
    }

    [Theory]
    [MemberData(nameof(PackCodes))]
    public void Registration_patterns_are_valid_regular_expressions(string code)
    {
        foreach (var rule in Registry.Get(code).TaxRegistration)
        {
            if (rule.Pattern is not null)
                _ = new Regex(rule.Pattern);
        }
    }

    [Theory]
    [MemberData(nameof(PackCodes))]
    public void Charts_of_accounts_are_consistent(string code)
    {
        var charts = Registry.Get(code).ChartsOfAccounts;
        Assert.NotEmpty(charts);

        foreach (var chart in charts)
        {
            var codes = chart.Accounts.Select(a => a.Code).ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());

            foreach (var account in chart.Accounts)
            {
                if (account.ParentCode is not null)
                    Assert.Contains(account.ParentCode, codes);
            }

            // Only group accounts can have children; entries are posted to leaf accounts.
            foreach (var posting in chart.Accounts.Where(a => a.IsPosting))
                Assert.DoesNotContain(chart.Accounts, a => a.ParentCode == posting.Code);
        }
    }

    [Theory]
    [MemberData(nameof(PackCodes))]
    public void Capabilities_match_the_optional_parts(string code)
    {
        var pack = Registry.Get(code);

        Assert.Equal(pack.TaxCodes.Count > 0, pack.Capabilities.HasTaxCodes);
        Assert.Equal(pack.EInvoicing is not null, pack.Capabilities.HasEInvoicing);
        Assert.Equal(pack.Payroll is not null, pack.Capabilities.HasPayroll);
    }

    [Fact]
    public void Two_packs_with_the_same_code_are_refused()
    {
        var pack = Registry.All.First();

        Assert.Throws<InvalidOperationException>(() => new CountryPackRegistry([pack, pack]));
    }

    [Fact]
    public void Looking_up_a_missing_country_is_clear_about_it()
    {
        Assert.Null(Registry.Find("ZZ"));
        Assert.Throws<KeyNotFoundException>(() => Registry.Get("ZZ"));
    }
}
