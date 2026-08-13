namespace CiDemo.Tests;

public class PercentageCalculatorTests
{
    [Theory]
    [InlineData(200, 25, 150)]
    [InlineData(100, 0, 100)]
    [InlineData(100, 100, 0)]
    public void ApplyDiscount_ReturnsDiscountedAmount(decimal amount, decimal discountPercent, decimal expected)
    {
        var result = PercentageCalculator.ApplyDiscount(amount, discountPercent);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ApplyDiscount_NegativeAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PercentageCalculator.ApplyDiscount(-1, 10));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ApplyDiscount_DiscountOutOfRange_Throws(decimal discountPercent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PercentageCalculator.ApplyDiscount(100, discountPercent));
    }

    [Theory]
    [InlineData(25, 200, 12.5)]
    [InlineData(50, 50, 100)]
    public void PercentageOf_ReturnsExpectedPercentage(decimal part, decimal whole, decimal expected)
    {
        var result = PercentageCalculator.PercentageOf(part, whole);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void PercentageOf_ZeroWhole_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => PercentageCalculator.PercentageOf(10, 0));
    }

    [Theory]
    [InlineData(70, 70, true)]
    [InlineData(75, 70, true)]
    [InlineData(69.9, 70, false)]
    public void MeetsThreshold_ComparesCorrectly(decimal measuredPercent, decimal thresholdPercent, bool expected)
    {
        var result = PercentageCalculator.MeetsThreshold(measuredPercent, thresholdPercent);

        Assert.Equal(expected, result);
    }
}
