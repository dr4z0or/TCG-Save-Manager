# Chargements et états en mémoire des mods

Inspection statique des DLL installées, sans exécution de leur code. Preuves complètes : [mod-runtime-hooks.il.txt](mod-runtime-hooks.il.txt), comprenant attributs Harmony, dépendances, champs statiques, IL et SHA-256 des DLL. Cette étude ne valide pas leur comportement en jeu.

## Hooks observés

| Composant | Sauvegarde | Chargement normal et secours | Réinitialisation observée |
|---|---|---|---|
| Shop C 1.5.4 | Postfix `CGameData.SaveGameData` → `ShopCSaveManager.Save(slot)` | Postfix `CSaveLoad.Load` / `LoadBackup`, si succès → `ShopCSaveManager.Load(slot)` | `Load` commence par `ShopCState.Reset` ; `CPlayerData.CreateDefaultData` appelle aussi Reset |
| Upstairs 1.4.2 | Postfix `CGameData.SaveGameData` → `UpSave.Save(slot)` | Postfix `CSaveLoad.Load` / `LoadBackup`, si succès → `UpSave.Load(slot)` | `Load` commence par `UpState.Reset` ; `CreateDefaultData` appelle aussi Reset |
| Checkout 1.6.1 | Capture pendant la collecte des meubles ; prefix `CSaveLoad.Save` → `StageSave` | Postfix Load / LoadBackup → `PrepareLoad(m_SavedGame, slot, contexte)` si succès | `PrepareLoad` remet `_loaded`, `_captured`, `_captureError` à null avant de chercher un compagnon par empreinte |
| Claw 1.6.1 | Prefix `CSaveLoad.Save` | Postfix Load / LoadBackup → `ClawSave.Load(slot)` si succès | `Load` remet `_loaded` à null et mémorise le slot avant la recherche par empreinte |
| Paint 1.6.1 | Prefix/finalizer Save, compteur de profondeur | Postfix Load / LoadBackup → `PrepareLoad(slot, backup, succès)` | Efface `_captured`, `_pending`, `_pendingFrom` avant le test de succès ; sélectionne le backup Paint si demandé et présent |
| DisplayOnly 1.6.1 | Prefix Save → `NoteSlot` puis `Save` | Postfix Load / LoadBackup → `PrepareLoad(slot)` si succès | Mémorise le slot et arme `_restorePending` ; la lecture est différée jusqu'aux meubles chargés |

Les hooks de secours sont présents pour ces composants. La présence des hooks ne prouve pas que les compagnons par slot correspondent à l'état ancien du backup natif : Checkout/Claw recalculent l'empreinte de la partie chargée, Paint dispose d'un backup spécifique, Shop C et Upstairs lisent leur fichier de slot.

## Points à traiter dans l'adaptateur

1. Archiver après le retour du `CGameManager.SaveGameData` extérieur : un postfix de `CSaveLoad.Save` peut précéder les postfix Shop C/Upstairs sur `CGameData.SaveGameData`. Le bouton manuel actuel archive après le retour de l'appel extérieur ; l'archivage automatique différé doit également vérifier la stabilité des compagnons.
2. Remplacer ensemble les fichiers natifs et les compagnons de la version sélectionnée, en incluant ceux-ci dans les copies de récupération. Une nouvelle boutique doit retirer les compagnons des slots de travail précédents. Les fichiers par empreinte doivent rester associés à la bonne archive ; les recherches de secours ne doivent pas pouvoir sélectionner une autre boutique.
3. Définir un reset explicite pour les nouvelles parties : les hooks Load ne sont pas appelés par une simple création de données. Aucun hook Furniture `CreateDefaultData` n'est présent dans les attributs inspectés. Cela indique un chemin à vérifier et à compléter, sans établir qu'un défaut se produit déjà en jeu. DisplayOnly doit notamment repartir avec un slot correct, Checkout/Claw/Paint sans état chargé d'une autre boutique.
4. Vérifier les objets du monde après retour au menu : `ShopCWorldBuilder.Teardown` et `UpWorld.Teardown` existent et sont utilisés lors du déchargement des plugins. `UpWorld.EnsureBuilt` peut retourner immédiatement si `_root` existe. Le changement de scène détruit normalement des objets Unity, mais l'effet sur les références statiques et les coroutines doit être testé dans une instance dédiée. Ne pas appeler `Plugin.OnDestroy` pour changer de boutique : cela enlèverait aussi les patches Harmony et les integrations.
5. Furniture `get_LoadedShelves` met en cache le ShelfManager pour la frame ; `ForgetShelves` invalide ce cache. `Update`, quand les meubles ne sont pas chargés, nettoie plusieurs états (TagLayer, SaveWatch, BoxLabel, RugResize, Refinish, RugStack) et `_sweepDone`. Cette voie doit être observée pendant un changement réel ; elle n'efface pas explicitement tous les snapshots des composants de sauvegarde.

## Validation encore nécessaire

Préparer une instance distincte et un adaptateur transactionnel avant d'activer les mods. Tester séparément : A → B → A, sauvegarde manuelle ancienne, auto distincte, boutique vierge après boutique équipée, backup natif, retour au titre puis nouvelle partie, récupération après interruption. Vérifier les pièces/étages, objets peints, présentoirs, cartes aux caisses, machines Claw et journaux. La copie de test actuelle reste sans mods optionnels.

Pour reproduire l'inspection :

```powershell
.\.tools\dotnet\dotnet.exe src/TCG.SaveManager.Sandbox/bin/Release/net8.0/TCG.SaveManager.Sandbox.dll mod-hooks 'D:\SteamLibrary\steamapps\common\TCG Card Shop Simulator\BepInEx\plugins' docs/mod-runtime-hooks.il.txt
```
