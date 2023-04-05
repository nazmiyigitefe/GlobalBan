using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MoreLinq;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using Pustalorc.GlobalBan.API.Enums;
using Pustalorc.GlobalBan.API.Services;
using Pustalorc.GlobalBan.Database;
using Pustalorc.PlayerInfoLib.Unturned;
using Pustalorc.PlayerInfoLib.Unturned.API.Services;
using Steamworks;

namespace Pustalorc.GlobalBan.Commands
{
    [Command("bans")]
    [CommandSyntax("[player]")]
    [CommandDescription("Lists the total amount of bans in the server, or of a specific player.")]
    public class CommandBans : Command
    {
        private readonly IUserManager m_UserManager;
        private readonly IPluginAccessor<PlayerInfoLibrary> m_PilPlugin;
        private readonly IGlobalBanRepository m_GlobalBanRepository;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly GlobalBanDbContext m_DbContext;

        public CommandBans(IStringLocalizer stringLocalizer, IPluginAccessor<PlayerInfoLibrary> pilPlugin, GlobalBanDbContext globalBanDbContext,
            IUserManager userManager, IGlobalBanRepository globalBanRepository, IServiceProvider serviceProvider) : base(serviceProvider)
        {
            m_UserManager = userManager;
            m_PilPlugin = pilPlugin;
            m_GlobalBanRepository = globalBanRepository;
            m_StringLocalizer = stringLocalizer;
            m_DbContext = globalBanDbContext;
        }

        protected override async Task OnExecuteAsync()
        {
            var actor = Context.Actor;

            if (!Context.Parameters.TryGet<string>(0, out var target))
            {
                var totalBans = await m_DbContext.PlayerBans.CountAsync();
                var inEffect = await m_DbContext.PlayerBans.Where(k => !k.IsUnbanned).ToListAsync();
                var totalBansInEffect =
                    inEffect.Count(k => DateTime.Now.Subtract(k.TimeOfBan).TotalSeconds <= k.Duration);

                await actor.PrintMessageAsync(m_StringLocalizer["commands:bans:global",
                    new {Total = totalBans, InEffect = totalBansInEffect}]);
                return;
            }

            var user = await m_UserManager.FindUserAsync(KnownActorTypes.Player, target, UserSearchMode.FindByNameOrId);
            var pData = await m_PilPlugin.Instance.LifetimeScope.Resolve<IPlayerInfoRepository>().FindPlayerAsync(target, UserSearchMode.FindByNameOrId);

            if (user is UnturnedUser || pData != null)
            {
                var player = user as UnturnedUser;

                var steamId = player?.SteamId ?? (CSteamID) pData.Id;
                var characterName = player?.DisplayName ?? pData.CharacterName;
                var hwid = player != null ?string.Join("", player.Player.SteamPlayer.playerID.GetHwids().ElementAt(0)) : pData.Hwid;
                var ip = player != null ? player.Player.SteamPlayer.getIPv4AddressOrZero() : (uint) pData.Ip;
                var idBans = await m_GlobalBanRepository.FindBansInEffectAsync(steamId.ToString(), BanSearchMode.Id);
                var ipBans = await m_GlobalBanRepository.FindBansInEffectAsync(ip.ToString(), BanSearchMode.Ip);
                var hwidBans = await m_GlobalBanRepository.FindBansInEffectAsync(hwid, BanSearchMode.Hwid);
                var bansInEffect = idBans.Concat(ipBans).Concat(hwidBans).DistinctBy(k => k.Id).ToList();

                var lastBan = bansInEffect.LastOrDefault();

                idBans = await m_GlobalBanRepository.FindBansAsync(steamId.ToString(), BanSearchMode.Id);
                ipBans = await m_GlobalBanRepository.FindBansAsync(ip.ToString(), BanSearchMode.Ip);
                hwidBans = await m_GlobalBanRepository.FindBansAsync(hwid, BanSearchMode.Hwid);
                var bans = idBans.Concat(ipBans).Concat(hwidBans).DistinctBy(k => k.Id).Count();

                await actor.PrintMessageAsync(m_StringLocalizer["commands:bans:count",
                    new {Player = characterName, Total = bans, InEffect = bansInEffect.Count}]);
                if (lastBan != null)
                    await actor.PrintMessageAsync(m_StringLocalizer["commands:bans:last_ban", new {Ban = lastBan}]);
            }
        }
    }
}
