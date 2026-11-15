# Rapport détaillé : snapshot `take_screenshot`

Date des mesures : 24 octobre 2026.  
Cible : Infinite Magicraid, fenêtre `2582×1496` pixels physiques.  
Point de départ : le journal MCP affichait

`response take_screenshot png 2582x1496 5469730 bytes 313 ms`

Objectif retenu pendant toute l’itération :

- le temps mesuré autour de l’outil doit descendre à 10 ms ou moins en régime stable ;
- l’image reste à la résolution de `GetWindowRect` (pas de réduction par défaut) ;
- le format par défaut reste PNG ;
- une fenêtre qui recouvre la cible ne doit pas apparaître ;
- ce qui est derrière la cible ne doit pas apparaître ;
- le cadre de la fenêtre (barre de titre, bordure, boutons) doit être dans l’image ;
- l’origine `(0, 0)` reste le coin haut-gauche de `GetWindowRect`, pour que les clics suivants tombent au bon endroit.

Le chronomètre du journal entoure l’outil seulement. La recherche de fenêtre, la copie des pixels, le test de noirceur et l’encodage y sont. Le transport HTTP et le base64 de la réponse n’y sont pas.

Machine utilisée pour les bancs : Intel i7-7700K, 4 cœurs / 8 threads, AVX2, SSSE3, SSE4.1. Système 10.0.26200.

## 1. D’où venaient les 313 ms

Trois coûts se cumulaient.

1. **Recherche de fenêtre.** `WindowSearch.Enumerate` appelait `Process.GetProcessById` pour chaque fenêtre de premier niveau. Ce seul passage coûtait de l’ordre de 100 ms. `GetWindowText` et `Process.MainWindowTitle` peuvent aussi bloquer si le thread UI de la fenêtre est occupé.
2. **Mauvais pixels, puis un second blit.** `PrintWindow` sur Infinite Magicraid retourne `FALSE` tout de suite et ne remplit pas la bitmap. Le repli était souvent un `BitBlt` de l’écran sur le rectangle de la fenêtre. Ce blit montre la fenêtre telle qu’elle est composée à l’écran, donc aussi tout ce qui est peint par-dessus.
3. **Encodage PNG GDI+.** Le fichier faisait environ 5,4 Mo (`5 469 730` octets). L’encodage compressé était plus long que la copie des pixels.

`ImageContentBlock.FromBytes` a été mesuré autour de 0,01 ms. Ce n’était pas le problème.

## 2. Contrainte de justesse, avant la vitesse

Le blit d’écran a été écarté comme chemin principal parce qu’il inclut les fenêtres qui recouvrent la cible.

Vérification faite avec deux fenêtres sans bordure : une rouge, et une bleue topmost qui la chevauche. Le premier essai a échantillonné le pixel `(30, 40)` de la fenêtre rouge. Ce point était en dehors de l’intersection, donc le test passait pour une mauvaise raison. Le test corrigé calcule l’intersection des deux `GetWindowRect` et lit le centre. Le pixel reste rouge (`R` environ 170–190, `B` 0–40) et la méthode enregistrée est celle de la fenêtre, pas celle de l’écran.

`GetWindowDC` + `BitBlt` (`SRCCOPY`) sur cette paire de fenêtres ne copie pas la fenêtre du dessus. C’est pour cela que ce chemin a d’abord été gardé.

## 3. Recherche de fenêtre

Mesures approximatives sur la machine :

| Opération | Ordre de grandeur |
| --- | --- |
| `EnumWindows` seul | 0,12 ms |
| `Process.GetProcesses` | 10 ms |
| `Process.GetProcessById` par fenêtre | assez pour expliquer une grande part des 313 ms |

Ce qui a été gardé :

- énumérer les fenêtres sans résoudre le processus de chacune ;
- filtrer d’abord le titre, et ne résoudre le nom de processus que pour les titres qui correspondent ;
- lire le nom par `OpenProcess` + `QueryFullProcessImageName` (`PROCESS_QUERY_LIMITED_INFORMATION`) et le mettre en cache ;
- lire le titre avec `InternalGetWindowText`, qui ne reste pas bloqué sur un thread UI occupé ;
- après un succès, réutiliser la fenêtre si `IsWindow` est vrai, si le pid n’a pas changé, si le nom de processus correspond encore, et si le titre contient encore le filtre. Dans ce cas on ne ré-énumère pas ;
- n’appeler `Process.GetProcesses` que lorsqu’aucun candidat n’a été trouvé.

Le test `ACachedWindowDoesNotEnumerateAgain` couvre le cache. Le test de fenêtre bloquée vérifie que l’énumération rend le titre en moins de 2 secondes.

En régime stable, la recherche sur Infinite Magicraid est d’environ 0,1 ms. Elle n’est plus le sujet.

## 4. Encodage : ce qui a été mesuré, et ce qui a été écarté

La copie de la fenêtre, une fois le locator réglé, coûtait environ 3,5 à 4 ms. Le reste du budget de 10 ms était l’encodage d’une image pleine résolution.

Essais écartés, sur cette même image `2582×1496` :

| Encodeur | Résultat |
| --- | --- |
| PNG GDI+ (le chemin d’origine) | environ 5,4 Mo, trop lent pour le budget |
| PNG et JPEG Skia | plus lents que le PNG stocké |
| fpng compressé | environ 21 ms |
| swizzle OpenMP | plus lent, et variable |
| CRC calculé ligne par ligne sur le même thread que le swizzle | remplissage d’environ 6,4 ms, médiane à chaud d’environ 12,5 ms |

Le CRC par ligne a été écrit, mesuré, puis retiré. Le chaînage utile est un CRC par bande de lignes contiguës, pas un CRC par ligne.

Une allocation fraîche du tampon de sortie (environ 11,6 Mo) à l’intérieur du chronomètre coûte environ 5 ms de fautes de page. Le premier appel à froid peut donc rester autour de 16 ms. Le régime stable réutilise un tampon déjà touché. Le tampon suivant est préparé après l’encodage, pas pendant, pour qu’une allocation ou un GC de 11,6 Mo ne s’intercale pas dans le blit.

### PNG stocké, retenu pour l’encodage

Le plus rapide à pleine résolution sur ce CPU est un PNG non compressé :

- couleur RGB 8 bits, type 2 ;
- un filtre 0 par ligne ;
- un bloc zlib « stored » par ligne (`0x78 0x01`) ;
- swizzle BGRA vers RGB en SSSE3 quand le CPU l’a (`pshufb`), sinon en scalaire ;
- Adler-32 et CRC-32 via les routines de fpng (1.0.6, licence Unlicense, compilé `/O2`, sans `/openmp`, sans `/arch:AVX2`).

Longueur pour `2582×1496` : **11 597 055 octets**. La formule, partagée par le C# et `snap.cpp` :

- `rowBytes = width * 3 + 1`
- `raw = rowBytes * height`
- `blocks = height` (une ligne tient dans un bloc stored, `rowBytes ≤ 65535`)
- `zlibLength = 2 + raw + blocks * 5 + 4`
- `pngLength = 57 + zlibLength`

Les 57 octets sont la signature (8), IHDR (25), l’en-tête IDAT (12) et IEND (12). Le CRC IDAT couvre les quatre octets `IDAT` et toute la charge zlib, y compris l’Adler. L’Adler part de 1. Le CRC part de 0.

Un encodage fusionné, CRC unique à la fin, tournait autour de 6,0 ms. C’est le coût d’encodage qui reste une fois les pixels en mémoire.

`snapenc.dll` est produit avant la compilation de l’application. S’il manque, le chemin rapide se ferme et le repli GDI+ reste disponible. Les pixels du PNG stocké sont vérifiés contre la source, y compris une largeur qui n’est pas un multiple de 4, et le PNG produit bande par bande est identique au PNG produit en un seul appel.

## 5. Chemin rapide par device context de fenêtre

Avant la correction du cadre, le chemin par défaut était :

1. `GetWindowDC` + `BitBlt` dans un DIB 32 bits top-down réutilisé ;
2. encodage PNG stocké ;
3. si la zone cliente est noire (luminance moyenne sous 2,0, en ignorant la barre de titre), `PrintWindow` avec le flag 0, puis le flag 2 (`PW_RENDERFULLCONTENT`) ;
4. blit d’écran seulement si aucune fenêtre visible au-dessus ne coupe la cible.

Le test de noirceur sur le jeu coûte environ 0,01 ms. Il s’arrête tôt dès que les échantillons restants, même s’ils étaient nuls, ne peuvent plus faire passer la moyenne sous le seuil.

Sur Infinite Magicraid, `PrintWindow` (flags 0 et 2) a été re-mesuré en processus DPI-aware : retour `FALSE` en 0 ms, bitmap laissée noire. Ce n’est pas un chemin utilisable pour ce jeu. Il reste le repli pour les fenêtres dont le device context est noir (Chrome, Electron, et cas semblables).

### Chevauchement blit / encodage

Après le PNG stocké, une capture à chaud se décomposait ainsi (médiane de 8 échantillons, 40 ms de pause entre chaque, deux tours de chauffe écartés) :

| Étape | Médiane |
| --- | --- |
| total outil | 10,16 ms |
| recherche | 0,09 ms |
| capture | 10,05 ms |
| blit | 3,70 ms |
| test de noirceur | 0,01 ms |
| encodage | 6,51 ms |
| construction du bloc image | 0,01 ms |

Blit et encodage étaient en série, et la somme dépassait 10 ms. Les bandes de 160 lignes font les deux en même temps : le thread appelant copie la bande suivante pendant qu’un thread `snapshot-encode` swizzle, calcule l’Adler et le CRC de la bande précédente. Le CRC d’une bande est un seul appel sur des octets contigus.

Médianes ensuite, toujours sur le jeu `2582×1496`, méthode `windowDc` :

| Série | Échantillons (ms) | Médiane | Capture |
| --- | --- | --- | --- |
| 1 | 6,56 6,63 6,68 7,51 7,68 8,17 8,94 9,17 | 7,59 | 7,49 |
| 2 | 6,48 7,27 7,35 8,40 9,01 9,23 9,39 10,09 | 8,70 | 8,58 |

C’était sous 10 ms, et le pixel couvert restait celui de la fenêtre cible. Ce chemin a été gardé comme repli. Il ne résout pas le cadre.

## 6. Le cadre manquant, et le fond qui fuyait

Un snapshot `windowDc` du jeu montrait le contenu du jeu, mais :

- la barre de titre et les boutons n’y étaient pas ;
- une bande rouge et noire, le décor derrière la fenêtre, occupait le haut de l’image.

Géométrie physique, processus DPI-aware, au moment de la mesure :

| Mesure | Valeur |
| --- | --- |
| `GetWindowRect` | `2582×1496` à `(629, 332)` |
| zone cliente | `2560×1440`, décalage `(11, 45)` dans la fenêtre |
| style | caption et bordure présents, pas layered, pas `WS_EX_NOREDIRECTIONBITMAP` |
| `DWMWA_EXTENDED_FRAME_BOUNDS` | `2564×1487`, marges gauche 9, haut 0, droite 9, bas 9 |

La fenêtre a donc une vraie barre de titre. Le device context de fenêtre ne la contient pas. DWM dessine le cadre au-dessus de la surface de redirection. Dans cette surface, la zone non cliente contient ce qui est derrière la fenêtre. `BitBlt` de ce DC copie cette fuite. C’est le problème de sécurité : l’image révèle le contenu situé derrière la cible.

Comparaison du haut de la fenêtre, même instant, même rectangle :

| Source | Haut de l’image | Temps du probe |
| --- | --- | --- |
| device context de fenêtre | bandes du fond, puis le jeu plus bas | 15 ms (DIB créé dans la mesure) |
| `PrintWindow` 0 et 2 | noir, appel en échec | 0 ms |
| blit d’écran | le jeu tel qu’il est composé, et aussi la fenêtre Cursor qui recouvrait la droite | 95 ms à froid pour le rectangle entier |

Le blit d’écran aurait rendu le cadre, mais il aurait aussi rendu Cursor. Le texte du chat était lisible dans la bande de droite. Ce chemin reste interdit comme source principale.

`DwmGetDxSharedSurface` n’a pas été retenu : c’est l’API non documentée de la surface de redirection, celle qui a déjà le même trou de cadre que le device context.

## 7. Windows Graphics Capture

`Windows.Graphics.Capture` sur le HWND produit la visuelle DWM de cette fenêtre seule.

Probe DPI-aware, même fenêtre :

| Mesure | Valeur |
| --- | --- |
| taille de l’item | `2564×1487`, égale au cadre étendu |
| création de l’item, premier appel | environ 100 ms |
| première frame après `StartCapture` | 9 à 13 ms |
| copie CPU via `SoftwareBitmap.CreateCopyFromSurfaceAsync` | 21 à 28 ms |
| attente de la frame suivante, session déjà ouverte | environ 11 ms |
| pixel `(8, 8)`, milieu du haut, et haut droite | BGRA `243,243,243,255` |

`243,243,243` est le gris de la barre de titre, sur toute la largeur, y compris sous l’endroit où Cursor recouvrait l’écran. L’image montrait l’icône, le titre `InfiniteMagicraid`, et les boutons réduire, agrandir, fermer. Le jeu commence sous cette barre. Les bandes du fond n’y sont pas.

La copie `SoftwareBitmap` a été écartée pour le chemin chaud : 21 ms rien que pour ramener les pixels. Le chemin retenu crée une texture staging D3D11 (`B8G8R8A8`, formats 87, 90 ou 91), fait `CopyResource`, puis `Map`. La session, le device et le pool restent ouverts pour la fenêtre et sa taille. La frame la plus récente est conservée, donc l’appel suivant ne paie pas la création.

La bordure jaune de capture est désactivée (`IsBorderRequired = false`). Le curseur n’est pas capturé.

### Placement dans `GetWindowRect`

L’item fait `2564×1487`. Le contrat des clics exige `2582×1496`. La visuelle est copiée à l’offset du cadre étendu, ici `(9, 0)`. Les 9 pixels de marge invisible à gauche, à droite et en bas ne sont pas lus à l’écran : les recopier montrerait encore le bureau. Ils sont remplis en répétant le pixel de bord du cadre. L’origine de l’image ne bouge pas. Un clic dans cette marge tombe toujours sur le rectangle de fenêtre qui sert au redimensionnement.

Si la visuelle est trop petite (moins de la moitié de la fenêtre) ou si la frame ne correspond pas à la taille attendue, l’appel échoue et le repli `windowDc` reprend.

L’encodage est de nouveau croisé avec la copie : des bandes de 160 lignes sont écrites dans le DIB, et le thread d’encodage traite la bande précédente pendant que la suivante est copiée depuis la texture mappée.

## 8. Solution retenue

Pour un PNG entier, sans région et sans `maxWidth` :

1. recherche de fenêtre via le cache décrit plus haut ;
2. copie `windowVisual` (Windows Graphics Capture), session réutilisée ;
3. marges invisibles remplies depuis le bord du cadre ;
4. PNG stocké, bandes de 160 lignes encodées en parallèle de la copie ;
5. si la zone cliente est noire, ou si la visuelle n’est pas disponible : device context de fenêtre (`windowDc`), avec le même encodage croisé ;
6. sinon `PrintWindow` 0, puis `PrintWindow` 2 ;
7. blit d’écran seulement si la cible n’est pas couverte. S’il est couvert et que tout le reste est noir, l’image noire est rendue avec l’avertissement déjà existant, plutôt que les pixels de la fenêtre du dessus.

JPEG, région et `maxWidth` utilisent la même visuelle quand elle est disponible, puis l’encodage GDI+. Ils sont corrects, et plus lents. Ce n’est pas le chemin par défaut.

Méthodes possibles dans les métadonnées : `windowVisual`, `windowDc`, `printWindow`, `printWindowFull`, `screen`.

Le snapshot live repris après branchement, via `http://127.0.0.1:5000/mcp`, a renvoyé :

`captureMethod: windowVisual`, `2582×1496`, `11 597 055` octets, échelle 1.

L’image montre la barre de titre et les boutons. Une fenêtre posée au-dessus du jeu n’y est pas. Le test d’intersection rouge sous bleu passe encore, avec la méthode `windowVisual`.

### Temps après la solution complète

Même protocole : 10 itérations, les deux premières écartées, 40 ms hors chronomètre, médiane des 8 restantes. Jeu `2582×1496`.

| Série | Échantillons (ms) | Médiane | Part capture |
| --- | --- | --- | --- |
| visuelle, copie puis encodage en série | — | 10,79 | 10,63 |
| visuelle, encodage croisé avec la copie | 6,92 … 11,67 | 8,37 | 8,19 |
| même chemin, autre passage | 8,66 … 11,06 | 10,16 | 10,03 |
| même chemin, passage suivant | 7,30 7,49 7,51 7,68 8,26 8,73 9,40 9,68 | 7,97 | 7,89 |

La série à 10,79 est celle d’avant le croisement encodage / copie. Elle a été remplacée. Le régime stable se place en général entre 8 et 10 ms. Un passage a médiane 10,16 ms : la variance vient surtout de la lecture GPU, pas de la recherche de fenêtre. Le premier appel après création de la session reste plus lent (création de l’item autour de 100 ms dans le probe, plus la première frame).

## 9. Ce qui n’a pas été changé

- pas de JPEG par défaut, et pas de baisse de résolution pour tenir le temps ;
- pas de blit d’écran comme source normale ;
- les coordonnées de clic restent les pixels de l’image, origine en haut à gauche de `GetWindowRect` ;
- `PrintWindow` reste le repli quand la zone cliente est noire ;
- le fichier PNG est plus gros (11,6 Mo au lieu de 5,4 Mo) parce que c’est l’encodage pleine résolution qui tenait dans le temps.

## 10. Fichiers principaux

| Rôle | Fichier |
| --- | --- |
| ordre des méthodes | `src/mcp_overlord/Services/CaptureService.cs` |
| copie visuelle, staging D3D11, marges | `src/mcp_overlord/Services/Native/WindowVisualCapture.cs` |
| repli DC, bandes, encodage croisé | `src/mcp_overlord/Services/Native/FastWindowSnapshot.cs` |
| PNG stocké, tampon réchauffé | `src/mcp_overlord/Services/Native/NativeStoredPng.cs` |
| encodeur natif | `src/mcp_overlord/Native/snap.cpp`, `fpng.cpp` |
| cache de fenêtre | `src/mcp_overlord/Services/WindowLocator.cs`, `src/automation_util/WindowSearch.cs`, `ProcessNameLookup.cs` |
| détection d’une fenêtre qui recouvre | `src/mcp_overlord/Services/Native/WindowOverlap.cs` |
