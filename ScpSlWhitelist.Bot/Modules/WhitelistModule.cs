using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Net.Http.Json;

namespace ScpSlWhitelist.Bot.Modules;

public class WhitelistModule : ApplicationCommandModule<ApplicationCommandContext>
{
    // Smallest possible SteamID64
    private const long MinSteamId64 = 76561197960265728;

    [SlashCommand("whitelist-me", "Add yourself to the SCP:SL server whitelist.")]
    public async Task<InteractionMessageProperties> WhitelistMeAsync(string steamId)
    {
        var normalizedId = ParseSteamId(steamId);
        if (normalizedId is null)
            return Response($"'{steamId}' does not look like a valid SteamID64");

        var discordUserId = Context.Interaction.User.Id.ToString();
        var lines = await ReadLinesAsync();
        var claims = await ReadClaimsAsync();

        if (claims.TryGetValue(discordUserId, out var claimed) && claimed != normalizedId)
            return Response($"You've already linked `{claimed}` with this Discord account. Ask a server admin to change it.");

        if (claimed is null && claims.ContainsValue(normalizedId))
            return Response($"`{normalizedId}` is already linked to another Discord account.");

        if (lines.Contains(normalizedId))
        {
            if (claimed is null)
            {
                claims[discordUserId] = normalizedId;
                await WriteClaimsAsync(claims);
                Log($"{Context.Interaction.User.Username} ({discordUserId}) linked existing entry: {normalizedId}");
            }
            return Response($"`{normalizedId}` is already whitelisted.");
        }

        lines.Add(normalizedId);
        claims[discordUserId] = normalizedId;
        await WriteLinesAsync(lines);
        await WriteClaimsAsync(claims);
        Log($"{Context.Interaction.User.Username} ({discordUserId}) added themselves: {normalizedId}");
        await SendWebhookLogAsync(Context.Interaction.User, normalizedId, lines.Count);
        return Response($"Added `{normalizedId}` to the whitelist — you can now join the server.");
    }

    [SlashCommand("whitelist-add", "Add a player to the SCP:SL server whitelist.")]
    public async Task<InteractionMessageProperties> AddAsync(string steamId)
    {
        if (GetPermissionError() is { } error)
            return Response(error);

        var normalized = ParseSteamId(steamId);
        if (normalized is null)
            return Response($"'{steamId}' does not look like a valid SteamID64 (17 digits starting with 7656119...).");

        var lines = await ReadLinesAsync();
        if (lines.Contains(normalized))
            return Response($"`{normalized}` is already whitelisted.");

        lines.Add(normalized);
        await WriteLinesAsync(lines);
        Log($"{Context.Interaction.User.Username} ({Context.Interaction.User.Id}) added: {normalized}");
        await SendWebhookLogAsync(Context.Interaction.User, normalized, lines.Count);
        return Response($"Added `{normalized}` to the whitelist ({lines.Count} entr{(lines.Count == 1 ? "y" : "ies")}).");
    }

    [SlashCommand("whitelist-remove", "Remove a player from the SCP:SL server whitelist.")]
    public async Task<InteractionMessageProperties> RemoveAsync(string steamId)
    {
        if (GetPermissionError() is { } error)
            return Response(error);

        var normalized = ParseSteamId(steamId);
        if (normalized is null)
            return Response($"'{steamId}' does not look like a valid SteamID64 (17 digits starting with 7656119...).");

        var lines = await ReadLinesAsync();
        if (!lines.Remove(normalized))
            return Response($"`{normalized}` is not on the whitelist.");

        var claims = await ReadClaimsAsync();
        foreach (var key in claims.Where(kv => kv.Value == normalized).Select(kv => kv.Key).ToList())
            claims.Remove(key);
        if (claims.Count > 0 || File.Exists(GetClaimsPath()))
            await WriteClaimsAsync(claims);

        await WriteLinesAsync(lines);
        Log($"{Context.Interaction.User.Username} ({Context.Interaction.User.Id}) removed: {normalized}");
        return Response($"Removed `{normalized}` from the whitelist ({lines.Count} entr{(lines.Count == 1 ? "y" : "ies")}).");
    }

    [SlashCommand("whitelist-list", "List everyone on the SCP:SL server whitelist.")]
    public async Task<InteractionMessageProperties> ListAsync()
    {
        if (GetPermissionError() is { } error)
            return Response(error);

        var lines = await ReadLinesAsync();
        if (lines.Count == 0)
            return Response("The whitelist is empty.");

        lines.Sort();
        var body = string.Join('\n', lines.Select(id => $"`{id}`"));
        if (body.Length > 4000)
            body = string.Join('\n', lines.Take(100).Select(id => $"`{id}`")) + $"\n... and {lines.Count - 100} more";
        return Response($"Whitelisted ({lines.Count}):\n{body}");
    }

    private static InteractionMessageProperties Response(string description) => new()
    {
        Embeds = [new EmbedProperties { Description = description }],
        Flags = MessageFlags.Ephemeral,
    };

    private static void Log(string message) => Console.WriteLine($"[Whitelist] {message}");

    private static readonly HttpClient Http = new();

    private static async Task SendWebhookLogAsync(User actor, string steamId, int totalEntries)
    {
        var url = Environment.GetEnvironmentVariable("SCP_SL_WEBHOOK_URL");
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        description = $"**{actor.Username}** (`{actor.Id}`) added `{steamId}` to the whitelist ({totalEntries} entr{(totalEntries == 1 ? "y" : "ies")}).",
                        color = 0xFFC0CB,
                    },
                },
            };

            using var response = await Http.PostAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode)
                Log($"Webhook log failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            Log($"Webhook log failed: {ex.Message}");
        }
    }

    private string? GetPermissionError()
    {
        if (Context.Interaction.User is not GuildInteractionUser { Permissions: var permissions })
            return "This command can only be used inside a server.";

        if ((permissions & Permissions.ManageGuild) == 0)
            return "You need the **Manage Server** permission to manage the whitelist.";

        return null;
    }

    private static string? ParseSteamId(string steamId)
    {
        if (!ulong.TryParse(steamId.Trim(), out var value) || value < MinSteamId64)
            return null;

        return value.ToString();
    }

    private static string GetWhitelistPath()
    {
        var path = Environment.GetEnvironmentVariable("SCP_SL_WHITELIST_FILE");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Missing environment variable SCP_SL_WHITELIST_FILE. Start the bot through the ScpSlWhitelist plugin.");

        return path;
    }

    private static string GetClaimsPath() => GetWhitelistPath() + ".claims";

    private static async Task<Dictionary<string, string>> ReadClaimsAsync()
    {
        var path = GetClaimsPath();
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        var claims = new Dictionary<string, string>();
        foreach (var raw in await File.ReadAllLinesAsync(path))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
                claims[parts[0]] = parts[1];
        }

        return claims;
    }

    private static async Task WriteClaimsAsync(Dictionary<string, string> claims)
    {
        var path = GetClaimsPath();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var temp = path + ".tmp";
        await File.WriteAllLinesAsync(temp, claims.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value}"));
        File.Move(temp, path, true);
    }

    private static async Task<List<string>> ReadLinesAsync()
    {
        var path = GetWhitelistPath();
        if (!File.Exists(path))
            return new List<string>();

        var lines = await File.ReadAllLinesAsync(path);
        return lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
    }

    private static async Task WriteLinesAsync(List<string> lines)
    {
        var path = GetWhitelistPath();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Write to a temp file first so the server never reads a half-written whitelist.
        var temp = path + ".tmp";
        await File.WriteAllLinesAsync(temp, lines.OrderBy(id => id, StringComparer.Ordinal));
        File.Move(temp, path, true);
    }
}
