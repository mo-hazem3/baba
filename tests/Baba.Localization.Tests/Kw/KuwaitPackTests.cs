using Baba.Domain;
using Baba.Localization.Kw;

namespace Baba.Localization.Tests.Kw;

public class KuwaitPackTests
{
    private readonly KuwaitPack _pack = new();

    [Fact]
    public void Uses_the_dinar_with_three_decimals() =>
        Assert.Equal(("KWD", 3), (_pack.Currency.Currency.Code, _pack.Currency.Currency.MinorUnits));

    [Fact]
    public void Has_no_vat_yet_but_still_reports_that_clearly()
    {
        Assert.Empty(_pack.TaxCodes);
        Assert.False(_pack.Capabilities.HasTaxCodes);
        Assert.False(_pack.Capabilities.HasEInvoicing);
    }

    [Fact]
    public void Amounts_round_to_fils()
    {
        var money = Money.Of(1.2345m, _pack.Currency.Currency);

        Assert.Equal(1.235m, money.Amount);
    }
}
