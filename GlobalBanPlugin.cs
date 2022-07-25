// ReSharper disable AnnotateNotNullParameter
// ReSharper disable AnnotateNotNullTypeMember

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.Core.Users;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Unturned.Plugins;
using OpenMod.Unturned.Users;
using Pustalorc.GlobalBan.API.Enums;
using Pustalorc.GlobalBan.API.External;
using Pustalorc.GlobalBan.API.Services;
using Pustalorc.GlobalBan.Database;
using Pustalorc.PlayerInfoLib.Unturned;
using Pustalorc.PlayerInfoLib.Unturned.API.Services;
using SDG.Unturned;
using Steamworks;
using Math = System.Math;

[assembly:
    PluginMetadata("Pustalorc.GlobalBan", Author = "Pustalorc, Nuage", DisplayName = "Global Ban",
        Website = "https://github.com/Pustalorc/GlobalBan/")]

namespace Pustalorc.GlobalBan
{
    public class GlobalBanPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<GlobalBanPlugin> m_Logger;
        private readonly IUserManager m_UserManager;
        private readonly IUnturnedUserDirectory m_UnturnedUserDirectory;
        private readonly GlobalBanDbContext m_GlobalBanDbContext;
        private readonly IGlobalBanRepository m_GlobalBanRepository;
        private readonly IPluginAccessor<PlayerInfoLibrary> m_PilPlugin;

        public GlobalBanPlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<GlobalBanPlugin> logger,
            IUserManager userManager,
            IUnturnedUserDirectory unturnedUserDirectory,
            IGlobalBanRepository globalBanRepository, IPluginAccessor<PlayerInfoLibrary> pilPlugin, 
            GlobalBanDbContext globalBanDbContext,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
            m_Logger = logger;
            m_UserManager = userManager;
            m_UnturnedUserDirectory = unturnedUserDirectory;
            m_GlobalBanDbContext = globalBanDbContext;
            m_GlobalBanRepository = globalBanRepository;
            m_PilPlugin = pilPlugin;
        }

        protected override async UniTask OnLoadAsync()
        {
            // Event nelson added to check if someone is banned yourself. Uses correct Banned removal if isBanned is returned to true.
            Provider.onCheckBanStatusWithHWID += CheckBanned;
            // Event nelson added to interupt vanilla bans. This is used for integration with other plugins
            Provider.onBanPlayerRequestedV2 += RequestBan;

            await m_GlobalBanDbContext.Database.MigrateAsync();

            m_Logger.LogInformation("Global Ban for Unturned by Pustalorc was loaded correctly.");
        }

        protected override UniTask OnUnloadAsync()
        {
            Provider.onCheckBanStatusWithHWID -= CheckBanned;
            Provider.onBanPlayerRequestedV2 -= RequestBan;

            m_Logger.LogInformation("Global Ban for Unturned by Pustalorc was unloaded correctly.");

            return UniTask.CompletedTask;
        }

        private void CheckBanned(SteamPlayerID playerId, uint remoteIp, ref bool isBanned, ref string banReason,
            ref uint banRemainingDuration)
        {
            var steamId = playerId.steamID.m_SteamID;
            var hwid = string.Join("", playerId.hwid);
            var server = m_PilPlugin.Instance.LifetimeScope.Resolve<IPlayerInfoRepository>().GetCurrentServer();

            switch (m_GlobalBanRepository.CheckBan(steamId, remoteIp, hwid))
            {
                case BanType.Id:
                    var now = DateTime.Now;

                    var bans = m_GlobalBanRepository.FindBansInEffect(steamId.ToString(), BanSearchMode.Id)
                        .OrderByDescending(k => k.TimeOfBan.AddSeconds(k.Duration).Subtract(now).TotalSeconds).ToList();
                    var ban = bans.FirstOrDefault();
                    if (ban == null) return;

                    isBanned = true;
                    banReason = ban.Reason;
                    var remainingDuration = ban.TimeOfBan.AddSeconds(ban.Duration).Subtract(DateTime.Now).TotalSeconds;
                    banRemainingDuration =
                        (uint) Math.Min(uint.MaxValue,
                            remainingDuration); // Makes sure that the maximum number sent back is uint.MaxValue, even if remaining duration is higher (it would overflow otherwise and give the wrong duration)

                    if (!bans.Any(k => k.Hwid.Equals(hwid) && k.Ip == remoteIp))
                    {
                        m_GlobalBanRepository.BanPlayer(server?.Id ?? 0, steamId, remoteIp, hwid, banRemainingDuration,
                            0, m_StringLocalizer["internal:new_ip_or_hwid_ban_reason"]);

                        var translation = m_StringLocalizer["commands:ban:banned",
                            new {Player = playerId.characterName, Reason = banReason}];
                        m_UserManager.BroadcastAsync(translation);
                        m_Logger.LogInformation(translation);
                        SendWebhook(WebhookType.BanEvading, playerId.characterName,
                            m_StringLocalizer["internal:ban_evading_admin_name"], banReason, steamId.ToString(),
                            banRemainingDuration);
                    }

                    break;
                case BanType.Ip:
                case BanType.Hwid:
                    isBanned = true;
                    banReason = m_StringLocalizer["internal:ban_evading_reason"];
                    banRemainingDuration = uint.MaxValue;

                    m_GlobalBanRepository.BanPlayer(server?.Id ?? 0, steamId, remoteIp, hwid, uint.MaxValue, 0,
                        banReason);

                    var translated = m_StringLocalizer["commands:ban:banned",
                        new {Player = playerId.characterName, Reason = banReason}];
                    m_UserManager.BroadcastAsync(translated);
                    m_Logger.LogInformation(translated);
                    SendWebhook(WebhookType.BanEvading, playerId.characterName,
                        m_StringLocalizer["internal:ban_evading_admin_name"], banReason, steamId.ToString(),
                        uint.MaxValue);
                    break;
                default:
                    return;
            }
        }
        
        private void RequestBan(CSteamID instigator, CSteamID playerToBan, uint ipToBan, IEnumerable<byte[]> hwidsToBan,
            ref string reason, ref uint duration, ref bool shouldVanillaBan)
        {
            shouldVanillaBan = false;

            // Get config option
            var shouldIpAndHwidBan = m_Configuration.GetSection("commands:ban:ban_hwid_and_ip").Get<bool>();

            // Try to find user to be banned
            var pilRepo = m_PilPlugin.Instance.LifetimeScope.Resolve<IPlayerInfoRepository>();

            var user = m_UnturnedUserDirectory.FindUser(playerToBan);
            var pData = pilRepo.FindPlayer(playerToBan.ToString(), UserSearchMode.FindById);

            var adminId = instigator.m_SteamID;
            string characterName;
            var ip = 0u;
            var hwid = "";

            if (shouldIpAndHwidBan)
            {
                ip = ipToBan;
                if (hwidsToBan != null)
                    hwid = string.Join("", hwidsToBan.First()); // Obsolete - Should Implement Multiple HWIDs
            }

            if (user is UnturnedUser player)
            {
                characterName = player.DisplayName;
            }
            else if (pData != null)
            {
                characterName = pData.CharacterName;
            }
            else
            {
                characterName = playerToBan.ToString();
            }

            var server = pilRepo.GetCurrentServer();
            m_GlobalBanRepository.BanPlayer(server?.Id ?? 0, playerToBan.m_SteamID, ip, hwid, duration, adminId,
                reason);

            Provider.ban(playerToBan, reason, duration);

            var translated = m_StringLocalizer["commands:ban:banned", new { Player = characterName, Reason = reason }];
            m_UserManager.BroadcastAsync(translated);
            m_Logger.LogInformation(translated);

            var externalAdmin = m_StringLocalizer["webhooks:ban:by_external", new { ExternalId = adminId.ToString() }];
            SendWebhook(WebhookType.Ban, playerToBan.ToString(), externalAdmin, reason,
                playerToBan.ToString(), duration);
        }

        public async Task SendWebhookAsync(WebhookType webhookType, string playerName, string adminName, string reason,
            string playerId, uint duration)
        {
            await Discord.SendWebhookPostAsync(m_Configuration[$"webhooks:{webhookType.ToString().ToLower()}:url"],
                Discord.BuildDiscordEmbed(m_StringLocalizer[$"webhooks:{webhookType.ToString().ToLower()}:title"],
                    m_StringLocalizer[$"webhooks:{webhookType.ToString().ToLower()}:description",
                        new {Player = playerName, Reason = reason}], m_StringLocalizer["webhooks:global:displayname"],
                    m_Configuration["webhooks:image_url"],
                    m_Configuration.GetSection($"webhooks:{webhookType.ToString().ToLower()}:color").Get<int>(),
                    BuildFields(webhookType, adminName, reason, playerId, duration)));
        }

        public void SendWebhook(WebhookType webhookType, string playerName, string adminName, string reason,
            string playerId, uint duration)
        {
            Discord.SendWebhookPost(m_Configuration[$"webhooks:{webhookType.ToString().ToLower()}:url"],
                Discord.BuildDiscordEmbed(m_StringLocalizer[$"webhooks:{webhookType.ToString().ToLower()}:title"],
                    m_StringLocalizer[$"webhooks:{webhookType.ToString().ToLower()}:description",
                        new {Player = playerName, Reason = reason}], m_StringLocalizer["webhooks:global:displayname"],
                    m_Configuration["webhooks:image_url"],
                    m_Configuration.GetSection($"webhooks:{webhookType.ToString().ToLower()}:color").Get<int>(),
                    BuildFields(webhookType, adminName, reason, playerId, duration)));
        }

        private object[] BuildFields(WebhookType webhookType, string adminName, string reason,
            string playerId, uint duration)
        {
            var fields = Array.Empty<object>();

            switch (webhookType)
            {
                case WebhookType.BanEvading:
                    fields = new[]
                    {
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:steam64id"], playerId, true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:time"],
                            DateTime.Now.ToString(CultureInfo.InvariantCulture), true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:reason"], reason, false),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:duration"], duration.ToString(),
                            true)
                    };
                    break;
                case WebhookType.Ban:
                    fields = new[]
                    {
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:steam64id"], playerId, true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:ban:banned_by"], adminName, true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:time"],
                            DateTime.Now.ToString(CultureInfo.InvariantCulture), false),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:reason"], reason, true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:duration"], duration.ToString(),
                            true)
                    };
                    break;
                case WebhookType.Kick:
                    fields = new[]
                    {
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:steam64id"], playerId, true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:kick:kicked_by"], adminName, true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:time"],
                            DateTime.Now.ToString(CultureInfo.InvariantCulture), false),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:reason"], reason, true)
                    };
                    break;
                case WebhookType.Unban:
                    fields = new[]
                    {
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:steam64id"], playerId, true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:unban:unbanned_by"], adminName, true),
                        Discord.BuildDiscordField(m_StringLocalizer["webhooks:global:time"],
                            DateTime.Now.ToString(CultureInfo.InvariantCulture), false)
                    };
                    break;
            }

            return fields;
        }
    }
}
