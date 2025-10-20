# \# RPGStatusEffects

# 

# A Valheim mod that adds custom status effects, including a Taunt effect that forces enemies to attack the player using a craftable Taunt Hammer, and a Purity effect for additional gameplay enhancements.

# 

# \## Features

# \- \*\*Taunt Effect\*\*: Forces enemies to target the player for a configurable duration (default: 15 seconds).

# \- \*\*Taunt Hammer\*\*: Craftable weapon (`TauntHammer\_vad`) that applies the Taunt effect on hit.

# \- \*\*Purity Effect\*\*: A secondary status effect with configurable duration (default: 10 seconds).

# \- \*\*Configurable\*\*: Adjust taunt duration, purity duration, and hammer recipe via the config file.

# \- \*\*Server-Synced\*\*: Configs sync across multiplayer servers for consistent gameplay.

# 

# \## Installation

# 1\. Install \[BepInExPack for Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack\_Valheim/) (version 5.4.2202 or later).

# 2\. Download RPGStatusEffects from Thunderstore.

# 3\. Extract the `RPGStatusEffects` folder to your Valheim `BepInEx/plugins` directory.

# 4\. Run Valheim to generate the config file (`BepInEx/config/com.Fire.rpgstatuseffects.cfg`).

# 5\. (Optional) Edit the config to adjust settings.

# 

# \## Configuration

# Located in `BepInEx/config/com.Fire.rpgstatuseffects.cfg`:

# \- `\[General] VerboseLogging`: Enable/disable debug logs (default: false).

# \- `\[StatusEffects] TauntDuration`: Duration of the Taunt effect in seconds (default: 15).

# \- `\[StatusEffects] PurityDuration`: Duration of the Purity effect in seconds (default: 10).

# \- `\[Item\_Recipe\_TauntHammer] Recipe`: Crafting recipe for the Taunt Hammer (default: `Wood,10,5,LeatherScraps,5,2,SwordCheat,1,0`).

# 

# \## Commands

# \- `va\_status\_reload`: Reloads the mod’s configs in-game (admin only).

# 

# \## Compatibility

# \- Tested with Valheim version 0.217.46.

# \- May conflict with mods altering `MonsterAI` or status effects (e.g., EpicMMOSystem). Test with a clean install if issues arise.

# 

# \## Known Issues

# \- Enemy status effect icons may not display on the HUD due to Valheim limitations, but the player’s Taunting effect shows the timer.

# 

# \## Credits

# \- Developed by Fire-VA.

# \- Uses BepInEx and Harmony for modding support.

# 

# \## Support

# Report bugs or request features on the \[GitHub repository](https://github.com/fire-VA/RPGStatusEffects).

