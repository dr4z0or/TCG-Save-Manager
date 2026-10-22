# Installation — état du prototype 0.9

Ce document décrit l'ancien paquet de laboratoire. Pour le paquet bêta autonome, consulter [installation-beta.md](installation-beta.md).

Le paquet actuel est destiné aux copies de test du laboratoire. Ce n'est pas encore une version installable dans le jeu principal.

## Contenu du paquet

- BepInEx/plugins/TCGSaveManagerDiagnostics/TCG.SaveManager.Diagnostics.dll
- BepInEx/plugins/TCGSaveManagerDiagnostics/TCG.SaveManager.Core.dll
- INSTALLATION.md : ces instructions.
- SHA256.txt : empreintes des DLL.

Les DLL du jeu, BepInEx et les mods optionnels ne sont pas distribués dans ce paquet.

## Utiliser la version préparée

Dans ce projet, fermer le jeu et utiliser le lanceur correspondant :

- Lancer-test.cmd : jeu de base.
- Lancer-test-extensions.cmd : Shop C et Upstairs.
- Lancer-test-furniture.cmd : Shop C, Upstairs et Furniture.

Les lanceurs copient automatiquement les dernières DLL compilées. Aucune extraction manuelle du paquet n'est nécessaire pour ces instances.

Les boutiques du laboratoire restent dans lab/instance-00N/campaigns et les copies de récupération dans recovery. Conserver l'instance complète pour garder ses sauvegardes et métadonnées. L'import lit original-saves-copy et crée une copie distincte ; il ne déplace pas la source.

## Travail nécessaire avant installation dans le jeu principal

Le plugin refuse actuellement un dossier de sauvegardes sans les marqueurs du laboratoire. La redirection de Application.persistentDataPath, la protection contre Steam Cloud et la préparation/récupération au démarrage sont assurées par des DLL copiées modifiées et des scripts locaux. Ces DLL modifiées ne doivent jamais être copiées dans Steam.

La version destinée au jeu principal devra assurer cette isolation au démarrage avec un mécanisme propre à BepInEx, avant que les autres mods mémorisent leurs chemins. Elle devra initialiser son espace de sauvegarde, conserver les originaux, récupérer les transactions interrompues et contrôler les accès au cloud. Il faut valider cela dans une nouvelle copie avant de proposer l'installation réelle.

## Validation disponible

L'utilisateur a confirmé les boutiques indépendantes, les chargements auto et les anciennes versions manuelles, les améliorations Shop C/Upstairs, les couleurs Furniture, l'import et la disparition des freezes en déplacement. Les tests automatisés couvrent notamment les archives, les transactions et leur récupération, les compagnons Furniture et le sondage sans normalisation répétée.

Les cartes aux caisses et les machines Claw n'ont pas été vérifiées en jeu. Les mods autres que Shop C 1.5.4, Upstairs 1.4.2 et Furniture 1.6.1 ne disposent pas d'adaptateur. Une ancienne partie contenant d'autres objets moddés peut échouer à charger même si l'import de ses fichiers réussit.
