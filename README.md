# TCG Save Manager

A BepInEx mod for **TCG Card Shop Simulator**. Interface in English, or in French when the game is in French.

**Separate shops, each with its own saves.** Play as many independent shops as you like in TCG Card Shop Simulator. Each shop keeps its own automatic save and **3 manual slots**, just like the game, and you switch between them from the main menu without restarting.

Full description, settings and FAQ: [mod page](publish/TCG_Save_Manager/README.md) · changes: [CHANGELOG](publish/TCG_Save_Manager/CHANGELOG.md).

## Install

**Download:** [Nexus Mods](https://www.nexusmods.com/tcgcardshopsimulator/mods/1927) (Thunderstore / r2modman: coming soon). Requires BepInEx 5 (BepInExPack).

Add-ons built on Save Manager's shops: [TCG Custom Game Modes](https://github.com/dr4z0or/TCG-Custom-Game-Modes) (rules per shop) and [TCG Mod Profiles](https://github.com/dr4z0or/TCG-Mod-Profiles) (mods per shop).

## Building from source (Windows, .NET SDK 8)

1. Copy `local.props.example` to `local.props` and set `GameDir` to your game folder (BepInEx 5 installed).
2. `dotnet build src/TCG.SaveManager.Diagnostics -c Release` (plugin and its core library) and `dotnet build src/TCG.SaveManager.Preloader -c Release` (patcher).
3. Offline tests: `dotnet run --project src/TCG.SaveManager.Lab -- <suite> <new empty folder>` (suites: `self-test`, `campaign-test`, `history-test`, `companion-test`, `furniture-test`, `required-mods-test`, `import-test`, `native-import-test`, `polling-test`, `profile-test`, `mod-restore-self-test`); interface texts: `dotnet run --project src/TCG.Lang.Check -- src`.

Install: `TCG.SaveManager.Diagnostics.dll` and `TCG.SaveManager.Core.dll` in `BepInEx/plugins/TCGSaveManager`, `TCG.SaveManager.Preloader.dll` in `BepInEx/patchers/TCGSaveManager`.

## Other mods by dr4z0or

- [TCG Custom Game Modes](https://github.com/dr4z0or/TCG-Custom-Game-Modes)
- [TCG Mod Profiles](https://github.com/dr4z0or/TCG-Mod-Profiles)
- [TCG Card Translations](https://github.com/dr4z0or/TCG-Card-Translations)
- [Mods Translator](https://github.com/dr4z0or/Mods-Translator)

## License

[MIT](LICENSE). Not affiliated with OPNeon Games.

---

**En français :** Boutiques séparées, chacune avec sa sauvegarde automatique et 3 emplacements manuels ; changement de boutique depuis le menu principal sans redémarrer ; import depuis le jeu ou n'importe quel dossier. Les joueurs le téléchargent sur [Nexus Mods](https://www.nexusmods.com/tcgcardshopsimulator/mods/1927) (bientôt sur Thunderstore) ; ce dépôt contient le code source.
