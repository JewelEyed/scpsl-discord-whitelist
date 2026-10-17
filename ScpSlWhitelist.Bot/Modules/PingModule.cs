using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace ScpSlWhitelist.Bot.Modules;

public class PingModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("ping", "Check whether the bot is responsive.")]
    public InteractionMessageProperties Ping() => Response("Pong!");

    private static InteractionMessageProperties Response(string description) => new()
    {
        Embeds = [new EmbedProperties { Description = description }],
        Flags = MessageFlags.Ephemeral,
    };
}
