# Wildlife bridge hot reload

The in-game bridge is a stable kernel. Its command modules live at:

`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Wildlife-Bridge-HotCommands.xml`

Update modules without restarting RimWorld:

```powershell
.\Set-WildlifeBridgeModules.ps1 -SourcePath .\Wildlife-Bridge-HotCommands.example.xml
```

Supported module steps:

- `Text`: return static text with optional `$argument` substitution.
- `Builtin`: compose an existing stable bridge command.
- `Query`: inspect live `animals`, `pawns`, `colonists`, `things`, `buildings`,
  `components`, `animaldefs`, `map`, `game`, `world`, or `settings`.
- `Invoke`: call an existing public method. The command must explicitly set
  `allowMutation="true"`.

Use `HOT_STATUS`, `RELOAD_BRIDGE`, and `RESTART_BRIDGE` for diagnostics and
transport recovery. Invalid module updates retain the last valid command set.

Player-interface assessment commands:

- `UI_STATE` reports the selected thing, visible command labels, and open windows.
- `SELECT_SIGN <species>` focuses the newest matching wildlife sign.
- `OPEN_TRAIL <species>` opens its reconstructed trail card.
- `OPEN_TRAIL_BOARD` opens the player-facing Trail Leads board.

New compiled RimWorld types and Harmony patches still require restarting the game.
