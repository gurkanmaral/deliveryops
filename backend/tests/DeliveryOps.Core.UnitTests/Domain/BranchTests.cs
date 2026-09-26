using DeliveryOps.Core.Domain.Entities;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class BranchTests
{
    [Fact]
    public void Create_RejectsIncompleteCoordinates()
    {
        Assert.Throws<ArgumentException>(() =>
            Branch.Create(Guid.NewGuid(), "Merkez", "Kadıköy", 40.99, null));
    }

    [Theory]
    [InlineData(91, 29)]
    [InlineData(41, 181)]
    [InlineData(0, 0)]
    public void Update_RejectsUnsafeCoordinates(double latitude, double longitude)
    {
        Branch branch = Branch.Create(Guid.NewGuid(), "Merkez", "Kadıköy", 41, 29);

        Assert.ThrowsAny<ArgumentException>(() =>
            branch.Update("Merkez", "Kadıköy", latitude, longitude));
    }
}
