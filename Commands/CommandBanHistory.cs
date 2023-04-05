using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Localization;
using OpenMod.API.Permissions;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Core.Permissions;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using Pustalorc.GlobalBan.API.Enums;
using Pustalorc.GlobalBan.API.Services;
using Pustalorc.PlayerInfoLib.Unturned;
using Pustalorc.PlayerInfoLib.Unturned.API.Services;
using Steamworks;

namespace Pustalorc.GlobalBan.Commands
{
    [Command("banhistory")]
    [CommandSyntax("[player]")]
    [CommandDescription("Lists the latest 4 bans of a player, or self if none is specified.")]
    [RegisterCommandPermission("other", Description = "Permission to see the bans of other players")]
    public class CommandBanHistory : Command
    {
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly IUserManager m_UserManager;
        private readonly IPluginAccessor<PlayerInfoLibrary> m_PilPlugin;
        private readonly IGlobalBanRepository m_GlobalBanRepository;

        public CommandBanHistory(IStringLocalizer stringLocalizer, IUserManager userManager, IPluginAccessor<PlayerInfoLibrary> pilPlugin, IGlobalBanRepository globalBanRepository,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            m_UserManager = userManager;
            m_StringLocalizer = stringLocalizer;
            m_PilPlugin = pilPlugin;
            m_GlobalBanRepository = globalBanRepository;
        }

        protected override async Task OnExecuteAsync()
        {
            var actor = Context.Actor;

            // Get target player
            if (!Context.Parameters.TryGet<string>(0, out var target))
            {
                if (!(actor is UnturnedUser actorUser))
                    throw new CommandWrongUsageException(Context);

                var bans = await m_GlobalBanRepository.FindBansAsync(actorUser.SteamId.ToString(), BanSearchMode.Id);

                var latestBans = bans.OrderByDescending(k => k.TimeOfBan).Take(4);
                foreach (var ban in latestBans)
                    await actor.PrintMessageAsync(m_StringLocalizer["commands:banhistory:format",
                        new {Ban = ban, Player = actorUser.DisplayName}]);
                return;
            }

            var user = await m_UserManager.FindUserAsync(KnownActorTypes.Player, target, UserSearchMode.FindByNameOrId);
            var pData = await m_PilPlugin.Instance.LifetimeScope.Resolve<IPlayerInfoRepository>().FindPlayerAsync(target, UserSearchMode.FindByNameOrId);

            if (!(user is UnturnedUser) && pData == null)
            {
                if (!(actor is UnturnedUser actorUser))
                    throw new CommandWrongUsageException(Context);

                var bans = await m_GlobalBanRepository.FindBansAsync(actorUser.SteamId.ToString(), BanSearchMode.Id);

                var latestBans = bans.OrderByDescending(k => k.TimeOfBan).Take(4);
                foreach (var ban in latestBans)
                    await actor.PrintMessageAsync(m_StringLocalizer["commands:banhistory:format",
                        new {Ban = ban, Player = actorUser.DisplayName}]);
                return;
            }

            if (await CheckPermissionAsync("other") != PermissionGrantResult.Grant)
                throw new NotEnoughPermissionException(Context, "other");

            UnturnedUser player = null;
            if (user is UnturnedUser unturnedUser)
                player = unturnedUser;

            var steamId = player?.SteamId ?? (CSteamID) pData.Id;
            var characterName = player?.DisplayName ?? pData.CharacterName;

            var allBans = await m_GlobalBanRepository.FindBansAsync(steamId.ToString(), BanSearchMode.Id);

            var lastBans = allBans.OrderByDescending(k => k.TimeOfBan).Take(4);
            foreach (var ban in lastBans)
                await actor.PrintMessageAsync(m_StringLocalizer["commands:banhistory:format",
                    new {Ban = ban, Player = characterName}]);
        }
    }
}
