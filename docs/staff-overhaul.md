# Staff Overhaul 2.1.1 — analyse de compatibilité

Le journal Steam confirme : « staff companion missing for native save 1300162716c3 (slot 0): staff data started over for this save ». Le signalement No job correspond donc à une réinitialisation des données Staff lors de cet import. Cela n'établit pas que tous les postes observés viennent de cette seule cause.

Le snapshot d'origine correspondant existe dans StaffOverhaulSnapshots/1300162716c3e41d3c41cee588423912fcc7913f6cdb8fc5673ff1acef7469c3.txt. Son corps contient notamment job1, job2, rôle, zone, salaire, humeur et horaires. La première ligne associe game à l'empreinte native et body à l'empreinte du corps. Aucun fichier original n'a été changé lors de l'analyse.

La DLL installée expose com.qolmods.staffoverhaul version 2.1.1. StaffStore.GameHash normalise CGameData via Unity JsonUtility avant SHA-256. StaffStore.Load essaie aussi l'empreinte du JSON original capturé par NativeStaffLoadSource. Il existe des marqueurs initialized-slotN.txt : ne pas les traiter comme des profils d'employés indépendants.

Staff.Load appelle Staff.Reset avant StaffStore.Load. Reset nettoie les records, le cycle de vie, les horaires et les zones en mémoire. La compatibilité doit couvrir les fichiers correspondants au JSON courant et de secours, vérifier game et body, conserver les snapshots avec leur nom par empreinte, et retirer les snapshots de travail d'une autre boutique durant les transitions. Les caches StaffStore et la logique des marqueurs doivent encore être examinés avant intégration.

Ne pas remplacer automatiquement les données de la boutique déjà resauvegardée avec le snapshot ancien : cela pourrait annuler les nouveaux postes, salaires ou horaires. L'archive source et la copie originale permettent de préparer ultérieurement un nouvel import complet distinct.

Preuve reproductible : staff-runtime-audit.il.txt, produit par mod-hooks à partir des DLL lues sans exécuter leur code. Cet audit inclut les méthodes Staff et les autres adaptateurs ; il ne constitue pas encore un adaptateur activé.
