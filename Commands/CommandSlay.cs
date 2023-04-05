using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Core.Console;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using Pustalorc.GlobalBan.API.Enums;
using Pustalorc.GlobalBan.API.Services;
using Pustalorc.PlayerInfoLib.Unturned;
using Pustalorc.PlayerInfoLib.Unturned.API.Services;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Command = OpenMod.Core.Commands.Command;

namespace Pustalorc.GlobalBan.Commands
{
    [Command("slay")]
    [CommandSyntax("<player> [reason]")]
    [CommandDescription(
        "Kills and permanently bans a player from the server. HWID & IP Bans will always apply with this command.")]
    public class CommandSlay : Command
    {
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly IUserManager m_UserManager;
        private readonly IPluginAccessor<GlobalBanPlugin> m_Plugin;
        private readonly IPluginAccessor<PlayerInfoLibrary> m_PilPlugin;
        private readonly IGlobalBanRepository m_GlobalBanRepository;
        private readonly ILogger<CommandSlay> m_Logger;

        public CommandSlay(IStringLocalizer stringLocalizer, IUserManager userManager,
            IPluginAccessor<GlobalBanPlugin> globalBanPlugin, IPluginAccessor<PlayerInfoLibrary> pilPlugin, IGlobalBanRepository globalBanRepository, ILogger<CommandSlay> logger, IServiceProvider serviceProvider) :
            base(serviceProvider)
        {
            m_UserManager = userManager;
            m_StringLocalizer = stringLocalizer;
            m_Plugin = globalBanPlugin;
            m_PilPlugin = pilPlugin;
            m_GlobalBanRepository = globalBanRepository;
            m_Logger = logger;
        }

        protected override async Task OnExecuteAsync()
        {
            var actor = Context.Actor;

            // Parse arguments
            var target = await Context.Parameters.GetAsync<string>(0);
            var reason = m_StringLocalizer["commands:global:no_reason_specified"].Value;
            if (Context.Parameters.Count >= 2)
                reason = Context.Parameters.GetArgumentLine(1);

            var pilRepository = m_PilPlugin.Instance.LifetimeScope.Resolve<IPlayerInfoRepository>();
            // Try to find user to ban
            var user = await m_UserManager.FindUserAsync(KnownActorTypes.Player, target, UserSearchMode.FindByNameOrId);
            var pData = await pilRepository.FindPlayerAsync(target, UserSearchMode.FindByNameOrId);
            var isId = ulong.TryParse(target, out var pId) && pId >= 76561197960265728 && pId <= 103582791429521408;

            if (!(user is UnturnedUser) && pData == null && !isId)
            {
                await actor.PrintMessageAsync(m_StringLocalizer["commands:global:playernotfound",
                    new {Input = target}]);
                return;
            }

            var adminId = ulong.TryParse(actor.Id, out var id) ? id : 0ul;
            CSteamID steamId;
            string characterName;
            string hwid;
            uint ip;

            if (user is UnturnedUser player)
            {
                steamId = player.SteamId;
                characterName = player.DisplayName;
                ip = player.Player.SteamPlayer.getIPv4AddressOrZero();
                hwid = string.Join("", player.Player.SteamPlayer.playerID.GetHwids().ElementAt(0));

                await UniTask.SwitchToMainThread();
                player.Player.Player.life.askDamage(101, Vector3.up * 101f, EDeathCause.KILL, ELimb.SKULL, (CSteamID) adminId,
                    out _);
                await UniTask.SwitchToThreadPool();
            }
            else if (pData != null)
            {
                steamId = (CSteamID) pData.Id;
                characterName = pData.CharacterName;
                ip = (uint) pData.Ip;
                hwid = pData.Hwid;
            }
            else
            {
                steamId = (CSteamID) pId;
                characterName = pId.ToString();
                ip = 0;
                hwid = "";
            }

            var server = await pilRepository.GetCurrentServerAsync();
            await m_GlobalBanRepository.BanPlayerAsync(server?.Id ?? 0, steamId.m_SteamID, ip, hwid, uint.MaxValue,
                adminId, reason);

            await UniTask.SwitchToMainThread();
            Provider.ban(steamId, reason, uint.MaxValue);
            await UniTask.SwitchToThreadPool();

            var translated = m_StringLocalizer["commands:ban:banned", new {Player = characterName, Reason = reason}];
            await m_UserManager.BroadcastAsync(translated);
            await actor.PrintMessageAsync(translated);
            if (!(actor is ConsoleActor))
                m_Logger.LogInformation(translated);

            if (m_Plugin.Instance != null)
                await m_Plugin.Instance.SendWebhookAsync(WebhookType.Ban, characterName, actor.DisplayName, reason,
                    steamId.ToString(), uint.MaxValue);
        }
    }
}
