# Global Ban

This repository replaces the old Rocketmod 4 version that is the base of this forked repository.
With this plugin, you can ban players off your Unturned 3 server without having to ban them in all servers one by one.


## Current Features
| Command & Arguments     | Description                                                                                                                                                                                     |
|-------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `/ban <player> [time] [reason]` | Bans a player for `[time]` (seconds) with the specified reason. Note that the reason does not have to be in double quotes (`"reason"`).                                                         |
| `/banhistory [player]`  | Displays the last 4 bans of a player or yourself. (Cannot display bans of console, so this will instead throw a friendly exception).                                                            |
| `/bans [player]`        | Displays the total number of bans in the server as well as how many are active. If a player is specified, it will instead display the number of bans they have, as well as how many are active. |
| `/kick <player> [reason]`       | Kicks a player with the specified reason.                                                                                                                                                       |
| `/slay <player> [reason]`       | Kills and permanently bans the player for the specified reason.                                                                                                                                 |
| `/unban <player>`               | Unbans the specific player. This can also be their IP or their HWID as displayed in the DB.                                                                                                     |


`/ban` has a configuration option that allows it to no longer provide IP & HWID bans, but `/slay` will always support IP & HWID banning.


If a player goes on an alt account and they have their IP / HWID banned, this alt account will also be banned automatically and permanently.


Discord webhook support is also present for the following commands: `/ban`, `/kick`, `/slay`, `/unban`.
It is also available when the player tries to evade a ban.


## Database Stored Data

The database currently stores the following values:
- Ban Entry ID (Id)
- Steam 64 ID of the banned player (PlayerId)
- IP Address (IP)
- Hardware ID (HWID)
- Steam 64 ID of the Admin (AdminId)
- The reason of the ban (Reason)
- The duration of the ban (Duration)
- The Server ID related to the ban [See PIL's Servers table] (ServerId)
- The Date when the player has been banned (TimeOfBan)
- If this ban no longer counts no matter of the time remaining (IsUnbanned)

## Installation

 - `openmod install Pustalorc.GlobalBan`
 - Configure the plugin.
