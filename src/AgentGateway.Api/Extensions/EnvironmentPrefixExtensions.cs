namespace AgentGateway.Api.Extensions;

/// <summary>
/// The product was renamed from TelegramGateway. Running installations keep their TELEGRAMGATEWAY_ variables; both prefixes are
/// read and AGENTGATEWAY_ is added last, so it wins when one setting is given under both names.
/// </summary>
public static class EnvironmentPrefixExtensions
{
    public const string CurrentPrefix = "AGENTGATEWAY_";
    public const string LegacyPrefix = "TELEGRAMGATEWAY_";

    public static IConfigurationBuilder AddGatewayEnvironmentVariables(this IConfigurationBuilder configuration)
        => configuration.AddEnvironmentVariables(LegacyPrefix).AddEnvironmentVariables(CurrentPrefix);

    /// <summary>Names only: values of these variables include secrets.</summary>
    public static IReadOnlyList<string> LegacyVariableNames() => Environment.GetEnvironmentVariables().Keys.Cast<string>()
        .Where(name => name.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
}
