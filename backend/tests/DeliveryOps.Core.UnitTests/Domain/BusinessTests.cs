using DeliveryOps.Core.Domain.Entities;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class BusinessTests
{
    [Fact]
    public void Create_NormalizesCodeAndActivatesBusiness()
    {
        Business business = Business.Create("Örnek Restoran", "  ornek-01 ");

        Assert.Equal("Örnek Restoran", business.Name);
        Assert.Equal("ORNEK-01", business.Code);
        Assert.True(business.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_RejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => Business.Create(name, "ORNEK"));
    }
}
