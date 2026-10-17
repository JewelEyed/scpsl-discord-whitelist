namespace ScpSlWhitelist;

public class Config
{
    public bool IsEnabled { get; set; } = true;
    public bool Debug { get; set; }

    /// <summary>
    /// Discord bot token.
    /// </summary>
    public string DiscordToken { get; set; } = string.Empty;

    /// <summary>
    /// Optional Discord webhook URL that logs when someone is whitelisted.
    /// </summary>
    public string LogWebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// When enabled, only whitelisted players can connect.
    /// </summary>
    public bool WhitelistEnabled { get; set; } = true;

    /// <summary>
    /// When false (default), an empty whitelist allows everyone, when true, deny.
    /// </summary>
    public bool DenyWhenEmpty { get; set; }

    /// <summary>
    /// Message shown to rejected players.
    /// </summary>
    public string RejectMessage { get; set; } = "You are not whitelisted on this server, check the Discord for information.";
}
