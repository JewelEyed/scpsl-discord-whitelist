using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

using ScpSlWhitelist.Bot.Modules;

var token = Environment.GetEnvironmentVariable("SCP_SL_DISCORD_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Missing environment variable SCP_SL_DISCORD_TOKEN.");
    return 1;
}

using var client = new GatewayClient(new BotToken(token), new GatewayClientConfiguration
{
    Intents = GatewayIntents.AllNonPrivileged,
});

var applicationCommandService = new ApplicationCommandService<ApplicationCommandContext>();
applicationCommandService.AddModule<PingModule>();
applicationCommandService.AddModule<WhitelistModule>();

ulong? guildId = ulong.TryParse(Environment.GetEnvironmentVariable("SCP_SL_DISCORD_GUILD_ID"), out var g) ? g : null;

client.Ready += async ready =>
{
    Console.WriteLine("Discord bot connected and ready.");
    await applicationCommandService.RegisterCommandsAsync(client.Rest, ready.ApplicationId, guildId);
};

client.GuildUserAdd += async userAdd => {
    var dms = await userAdd.GetDMChannelAsync();
    await dms.SendMessageAsync(new MessageProperties
    {
        Embeds = [new EmbedProperties { Color = new(255, 192, 203), Description = "Welcome, for SL whitelisting, please link your Steam account to your Discord, and run /whitelist-me." }],
    });
};

client.InteractionCreate += async interaction =>
{
    if (interaction is ApplicationCommandInteraction applicationCommandInteraction)
        await applicationCommandService.ExecuteAsync(
            new ApplicationCommandContext(applicationCommandInteraction, client),
            serviceProvider: null);
};

await client.StartAsync();

var exit = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exit.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => exit.TrySetResult();

await exit.Task;
return 0;
