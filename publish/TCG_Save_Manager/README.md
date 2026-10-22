# TCG Save Manager

**Separate shops, each with its own saves.** Play as many independent shops as you like in TCG Card Shop Simulator. Each shop keeps its own automatic save and **3 manual slots**, just like the game, and you switch between them from the main menu without restarting.

*Français plus bas.*

## Features

- **My shops** screen that replaces the game's load and save menus: one card per shop with its day, level and money, a search box, and a **Continue** button that resumes the latest save.
- **3 named manual slots per shop.** Overwriting a slot keeps the previous version as a one-time backup.
- **Switch shops without restarting** the game, from the main menu.
- **Import your existing saves**, from the game's own save folder or **any other folder (USB key, backup, another PC)**. The source is only read, and each slot becomes a separate shop. Before importing, you see which mods the save uses and which ones are missing.
- Rename and delete shops. A shop's size on disk is shown.
- **Mod data follows each shop:** Shop C Expansion, Upstairs, Furniture Overhaul, Staff Overhaul, Enhanced Prefab Loader (binder cards of custom editions) and Collection Tracker are saved with the shop they belong to.
- Interface in **English or French** (follows the game's language).

## Installation

**r2modman / Thunderstore Mod Manager:** install *TCG Save Manager*, then launch the game from the manager.

**Manual:** BepInEx 5 must already be installed. Close the game, then extract the archive's `BepInEx` folder into the game folder (where `Card Shop Simulator.exe` is). It contains:

```
BepInEx/plugins/TCGSaveManager/TCG.SaveManager.Diagnostics.dll
BepInEx/plugins/TCGSaveManager/TCG.SaveManager.Core.dll
BepInEx/patchers/TCGSaveManager/TCG.SaveManager.Preloader.dll
```

The preloader (in `patchers`) is required: it moves the game's saves to Save Manager's folder before the game starts.

## First start

Save Manager keeps its shops in **`%LOCALAPPDATA%\TCGSM`** (type it in the Explorer address bar). Your original saves in `%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator` are **not moved and not modified**.

1. At the main menu, open **Load Game**: the *My shops* screen appears with an empty first shop.
2. To keep playing your current game, click **Import a save**, choose the slot (Auto or Manual 1–3), give the shop a name and confirm. Then click **Continue** on the new shop. Saves on a USB key or in a backup folder: click **Other folder…** in the import window.

## Use

- **Main menu → Load Game**: choose a shop and click **Continue**, or **Saves…** to load a specific slot or the previous version of a slot.
- **In game → Save**: choose one of the 3 slots of the current shop and give it a name.
- **New Game** asks for the new shop's name.
- To delete a shop, open **Saves…** on its card. The active shop cannot be deleted.

## Settings

`BepInEx/config/local.tcg.savemanager.diagnostics.cfg`:

| Setting | Default | |
|---|---|---|
| `Interface.Language` | `auto` | Language of the TCG mods' menus: `auto` (game language), `en` or `fr`. Also used by TCG Custom Game Modes and TCG Mod Profiles. |
| `Interface.ToggleKey` | none | Optional keyboard shortcut to open or close the shop manager (it opens from the game's Load and Save menus). |

## Good to know

- **Back up `%LOCALAPPDATA%\TCGSM`** to back up all your shops.
- The game's Steam Cloud save methods are disabled while Save Manager is installed, so cloud saves do not overwrite your shops.
- Switching shops without restarting resets the supported mods first. With a version of Furniture Overhaul or Staff Overhaul other than the tested one (1.6.1 and 2.1.1), the chosen shop is loaded at the next start instead: quit and relaunch the game.
- Data of other mods is not separated per shop.
- If a shop's save needs a mod that is missing (for example Furniture Overhaul), loading is refused with the list of missing mods.

## Uninstall

Close the game and remove the three DLLs (or uninstall from the mod manager). The game then uses its original save folder again, as it was before Save Manager. Your shops stay in `%LOCALAPPDATA%\TCGSM`.

## Troubleshooting

- **The game closes right away:** Save Manager refuses to start if `%LOCALAPPDATA%\TCGSM` exists but was not created by it. Rename that folder. Details are in `BepInEx/LogOutput.log`.
- **"Save Manager is inactive"** in the shop screen: the preloader is missing from `BepInEx/patchers`.

Works well with **TCG Custom Game Modes** (rules per shop) and **TCG Mod Profiles** (mods per shop).

---

## Français

Des boutiques séparées, chacune avec sa sauvegarde automatique et **3 emplacements manuels nommés**, comme dans le jeu. On change de boutique depuis le menu principal sans relancer le jeu.

- Installation : r2modman, ou extraire le dossier `BepInEx` de l'archive dans le dossier du jeu.
- Les boutiques sont dans `%LOCALAPPDATA%\TCGSM`. Les sauvegardes d'origine du jeu ne sont ni déplacées ni modifiées.
- Pour reprendre votre partie actuelle : menu principal → **Charger la partie** → **Importer une sauvegarde** ; **Autre dossier…** permet d'importer depuis une clé USB ou une copie. Les mods utilisés par la sauvegarde (et ceux qui manquent) sont affichés avant l'import.
- Le gestionnaire s'ouvre depuis les menus Charger et Sauvegarder du jeu. L'interface suit la langue du jeu (réglage `Interface.Language`).
