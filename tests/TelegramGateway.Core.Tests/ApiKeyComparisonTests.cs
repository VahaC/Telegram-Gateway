using TelegramGateway.Core.Security;

namespace TelegramGateway.Core.Tests;

public sealed class ApiKeyComparisonTests
{
    [Theory]
    [InlineData("correct", "correct", true)]
    [InlineData("correct", "Correct", false)]
    [InlineData("short", "a much longer key", false)]
    [InlineData("", "configured key", false)]
    [InlineData("ключ", "ключ", true)]
    public void Matches_credentials_uses_exact_comparison(string supplied, string expected, bool matches)
    {
        //Arrange
        //Act
        var result = ApiKeyComparison.Matches(supplied, expected);
        //Assert
        Assert.Equal(matches, result);
    }
}
