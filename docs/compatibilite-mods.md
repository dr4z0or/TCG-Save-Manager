# Compatibilité des mods — analyse des copies

Validation utilisateur : séparation des améliorations Shop C/Upstairs, couleurs différentes des meubles entre boutiques et correction du parcours après sauvegarde. Les snapshots Checkout/Claw liés aux backups natifs sont désormais conservés quand disponibles (tests hors jeu furniture-test-003). La validation exhaustive des cartes aux caisses, machines Claw et états historiques de secours reste à faire ; les composants sans compagnon historique dédié ne garantissent pas la correspondance exacte d'un backup ancien.

État courant 0.9 : aller-retour Shop C/Upstairs confirmé par l'utilisateur. Furniture est désormais intégré aux archives et réinitialisations, avec tests synthétiques réussis. Une instance-003 avec les trois mods est prête via Lancer-test-furniture.cmd. Le fonctionnement Furniture en partie, la reprise de versions antérieures et les backups restent à valider. Les analyses et étapes ci-dessous décrivent aussi les travaux précédents.

Le changement sans redémarrage du jeu de base a été confirmé par l'utilisateur. La copie habituelle instance-001 reste sans mods optionnels. Une copie distincte instance-002 inclut désormais Shop C et Upstairs pour essais interactifs ; leur chargement initial est confirmé, mais leur fonctionnement pendant les changements reste à valider. Aucun export ci-dessous n'est réinjecté dans le jeu.

## Fichiers identifiés dans les versions installées

| Mod | Fichiers par emplacement | Données supplémentaires |
|---|---|---|
| Shop C Expansion 1.5.4 | `ShopLotC_Save{slot}.json` | État de l'extension ; hooks de copie/sauvegarde à conserver |
| Upstairs 1.4.2 | `Upstairs/Upstairs_{slot}.json` | Migration possible depuis `Upstairs_Save{slot}.json` |
| Furniture Overhaul 1.6.1 | `FurnitureOverhaulPaint/slot{slot}.json`, backup ; `FurnitureOverhaulDisplayOnly/slot{slot}.json` ; `FurnitureOverhaulCheckout/slot{slot}.json` ; `FurnitureOverhaulClaw/slot{slot}.json` | Checkout et Claw possèdent aussi `{mainHash}.json` dans leur dossier |

Les méthodes CheckoutDisplay.GameHash et ClawSave.Hash normalisent CGameData par aller-retour Unity JsonUtility puis calculent le SHA-256 UTF-8 en minuscules. L'association au fichier par empreinte ne doit pas être remplacée par une hypothèse sur le numéro de slot. Le compagnon indique l'empreinte dans son champ `mainHash`.

## Export de contrôle réalisé

Source : `lab/instance-001/original-saves-copy`, déjà copiée au début du projet. Les originaux LocalLow n'ont pas été utilisés par cet export.

- Slot 0 : `lab/mod-audit-slot0` — 12 fichiers, contrôles SHA-256 réussis.
- Slot 3 : `lab/mod-audit-slot3` — 12 fichiers, contrôles SHA-256 réussis.
- Pour les deux slots, les compagnons Checkout et Claw pointent vers des fichiers présents, dont l'empreinte interne correspond à leur nom.
- Dans ces deux exemples, les empreintes déclarées correspondent aussi au SHA-256 des JSON principaux copiés. Cela n'établit pas que le JSON brut de toute autre version ou partie correspondra à la normalisation Unity.

Chaque export contient `files/`, un `manifest.json` avec les empreintes et associations, et un `rapport.md` signalant également les fichiers absents. Les sauvegardes de secours natives sont exportées, mais leurs compagnons historiques spécifiques n'ont pas encore été associés : ces exports ne constituent pas un système complet de restauration des mods.

Commande pour exporter un autre slot vers un nouveau dossier :

```powershell
.\.tools\dotnet\dotnet.exe .\src\TCG.SaveManager.Lab\bin\Release\net8.0\TCG.SaveManager.Lab.dll mod-audit 'C:\TCG-Modding\lab\instance-001\original-saves-copy' 1 'C:\TCG-Modding\lab\mod-audit-slot1'
```

L'outil exige une copie identifiée et refuse d'écraser un export. Il ne désérialise pas les fichiers `.gd`, n'installe aucun mod et ne restaure aucun fichier.

## Étapes nécessaires avant activation

Le prototype 0.8 intègre les compagnons Shop C/Upstairs au stockage actif, aux empreintes de session, au changement transactionnel et à la récupération après interruption. Tests synthétiques réussis dans `lab/companion-test-002`. Les réinitialisations Teardown/Reset sont ajoutées et les versions vérifiées avant les changements. Seul le profil dédié instance-002 autorise les deux DLL contrôlées par empreinte. Furniture reste couvert par les outils hors jeu uniquement.

L'inspection des hooks et caches des versions installées est maintenant décrite dans [mod-runtime-plan.md](mod-runtime-plan.md), avec les preuves IL reproductibles dans `mod-runtime-hooks.il.txt`. Les hooks LoadBackup ont été trouvés pour les six composants étudiés ; les nouvelles parties, les objets du monde et l'ordre de capture nécessitent encore l'adaptateur et les essais en jeu.

### Restauration hors jeu vérifiée

La commande `mod-restore-test <export> <slot cible> <nouveau dossier>` restaure uniquement dans un nouveau dossier directement sous une instance marquée ; elle refuse `saves`, `original-saves-copy` et toute sortie existante. Le manifeste, les tailles et SHA-256 sont vérifiés avant copie. Les chemins sont limités aux adaptateurs connus. Les compagnons Checkout/Claw doivent avoir leurs fichiers par empreinte présents et cohérents. La publication du dossier intervient après vérification des copies.

Essais réalisés : `lab/instance-001/mod-restore-slot0-to1` et `mod-restore-slot3-to0`, 12 fichiers chacun. Les fichiers natifs, Shop C, Upstairs (courant et legacy) et Furniture sont renommés selon le slot cible ; les noms par empreinte restent identiques. Aucun fichier n'est injecté dans les sauvegardes de travail du jeu.

Tests synthétiques complets réussis dans `lab/mod-restore-test-002` : remappage JSON/GD et compagnons, sauvegardes de secours copiées sans prétendre les associer à un état historique, conservation des fichiers par empreinte, refus d'écrasement, de chemins hors adaptateur, d'export altéré et de fichier lié manquant. Les sources restent inchangées. Le premier essai `mod-restore-test-001` s'est arrêté sur un défaut du traitement des exceptions dans le test, corrigé avant l'essai complet.

Cet outil ne traite pas les caches, l'ordre des hooks ou la normalisation Unity. Il ne constitue pas encore l'adaptateur actif du gestionnaire.

### Travail restant

1. Implémenter et vérifier les réinitialisations identifiées par l'analyse des hooks/caches, notamment lors d'une nouvelle partie et d'un changement de scène.
2. Ajouter les fichiers associés aux instantanés et aux transactions, avec le renommage des compagnons par slot et la conservation des noms par empreinte.
3. Traiter l'absence d'un compagnon pour une nouvelle boutique et les états précédents des backups sans récupérer un fichier d'une autre boutique.
4. Préparer une instance distincte incluant les dépendances nécessaires et tester successivement chaque mod.
5. Valider un aller-retour entre deux boutiques, puis la séparation auto/manuelle avec les extensions et les objets concernés.

Les données d'autres mods installés (ShopOS, Staff Overhaul, Enhanced Binder, etc.) nécessiteront leurs propres adaptateurs. Copier indistinctement toute la configuration BepInEx ne les désactive pas et ne garantit pas l'indépendance des boutiques.
