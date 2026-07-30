# Types documentaires

Fiches des types que le service doit distinguer. Périmètre : les 14 types des priorités 1 et 2 de la liste métier. Chaque fiche donne une définition, les marqueurs qui portent le signal de classification, les règles de désambiguïsation avec les voisins, ce que montrent les exemples réels, et la spécification de génération correspondante.

Les fiches se recopient dans le pré-prompt du classifieur : elles sont écrites pour être lues par un modèle autant que par un humain.

| Code | Libellé | Fiche |
|---|---|---|
| `CARTE_VISITE` | Carte de visite | [écrite](#carte_visite-carte-de-visite) |
| `EVAL_FORM` | Évaluation de la formation | à écrire |
| `FEUILLE_EMAR` | Feuille d'émargement | [écrite](#feuille_emar-feuille-démargement) |
| `SEPA` | Mandat SEPA | [écrite](#sepa-mandat-sepa) |
| `OFF_STAG_EMP` | Offre de stage, alternance, emploi | [écrite](#off_stag_emp-offre-de-stage-alternance-emploi) |
| `RIB` | Relevé d'identité bancaire | [écrite](#rib-relevé-didentité-bancaire) |
| `RIB_SEPA` | Relevé d'identité bancaire et mandat SEPA | [écrite](#rib_sepa-relevé-didentité-bancaire-et-mandat-sepa) |
| `CONV_PROF` | Alternance, convention de formation en contrat de professionnalisation | à écrire |
| `APP_CONV_SFP` | Apprentissage, convention stagiaire de la formation professionnelle | à écrire |
| `CERTIF_FORM` | Certificat de formation | à écrire |
| `CERTIF_REAL` | Certificat de réalisation | à écrire |
| `CONT_APP_PRO` | Contrat d'apprentissage ou de professionnalisation | à écrire |
| `CONVENTION` | Convention | à écrire |
| `APP_CRFA_P2S` | Apprentissage, CERFA P2S | à écrire |

## `CARTE_VISITE`, carte de visite

### Définition

Carton de format carte de crédit portant l'identité professionnelle d'une personne : nom, fonction, employeur, coordonnées directes. Il n'a ni destinataire, ni date, ni objet, ni montant, ni signature.

### Marqueurs

Le signal ne vient pas du vocabulaire, il n'y en a presque pas, mais de la **combinaison d'entités sans texte liant** : un nom propre, une fonction, un ou deux numéros de téléphone, une adresse e-mail, une URL, parfois une adresse postale, le tout sans une seule phrase complète.

| Marqueur | Force |
|---|---|
| moins de 40 mots au total, aucune phrase verbale | fort, et c'est le marqueur principal |
| coexistence nom de personne + intitulé de fonction + téléphone + e-mail | fort |
| aucune date, aucun montant, aucun numéro de dossier | fort en négatif |
| numéro de mobile en `06` ou `07`, souvent avec un fixe en second | moyen |
| QR code, présent sur 6 des 14 exemples | moyen, invisible pour l'OCR |
| logo et nom d'entreprise dominants visuellement | moyen |

### Désambiguïsation

Le risque n'est pas la confusion avec un voisin proche : aucun autre type du périmètre ne ressemble à une carte de visite. Le risque est **l'absence de signal**. Une carte de visite produit si peu de texte que le classifieur peut y projeter n'importe quel type dont le vocabulaire apparaît par accident, ou répondre au hasard avec une confiance élevée.

| Confusion | Ce qui tranche |
|---|---|
| page presque vide, page de garde, annexe | la carte de visite porte des coordonnées structurées, une page vide n'en porte aucune |
| `PHOTO`, priorité 3 | même régime d'acquisition, photo au téléphone, mais la carte de visite contient du texte imprimé exploitable |
| en-tête d'un autre document, qui porte aussi logo et coordonnées | un en-tête est suivi d'un corps de document : destinataire, objet, dates, montants. La carte de visite n'a pas de corps |

**Règle opérationnelle :** en dessous de 40 mots extraits, avec des coordonnées et sans corps de document, répondre carte de visite. Ce type est aussi celui sur lequel un score de confiance bas est légitime, et c'est un bon révélateur de la calibration : un modèle qui répond avec 0,95 sur huit mots océrisés est mal calibré.

### Ce que montrent les 14 exemples réels

Fournis par le métier, non versionnés, dans `tmp/samples/Cartes de visites`. Les cartes sont citées par leur entreprise, jamais par la personne.

**Deux régimes d'acquisition, aucun n'est bureautique.** Sur les quatorze exemples, le document déposé en GED est toujours la reprise photographique d'un objet physique. Rien n'interdit pourtant qu'un export d'outil de mise en page soit déposé tel quel : quatorze exemples ne font pas une loi, et le corpus doit couvrir ce cas.

| Régime | Nombre | Caractéristiques |
|---|---|---|
| Photo au smartphone | 8, JPEG 2288x4080 ou 4080x2288 | carte posée sur un bureau clair, occupant de 15 % à 60 % du cadre, éclairage inégal avec reflets, modèle de l'appareil et horodatage incrustés par le téléphone dans deux coins |
| Scan à plat A4 | 6, PDF A4 portrait sans couche texte | carte dans un coin d'une page vide à 85 % ou plus, bord de couvercle visible en bande noire, légère rotation de scanner |

**Orientation.** Quatre des quatorze cartes sont **à 90°** dans leur cadre : une carte paysage photographiée en portrait, ou posée dans le sens de la largeur sur la vitre du scanner. Les autres présentent des rotations de quelques degrés. Aucune n'est parfaitement droite.

**Multipage.** Deux des six PDF ont **deux pages**, recto et verso scannés séparément. Sur l'un, la page qui porte le nom de la personne est la seconde ; la première ne contient que le logo, un QR code et une accroche. Sur l'autre, **le verso n'est pas une carte de visite** mais une publicité de l'entreprise, avec photos et slogan.

**Manuscrit, sur 2 exemplaires des 14.** Un scan porte un nom et un numéro ajoutés au stylo bleu par-dessus la carte imprimée. Sur une photo, la carte ne porte **que le logo de l'entreprise**, et le nom, l'e-mail et le téléphone sont entièrement manuscrits : tout ce qui identifie la personne échappe à un OCR de texte imprimé.

**Amplitude de contenu, d'un facteur cinq.** La plus pauvre porte un logo, un prénom et nom en minuscules, un e-mail et un mobile, soit huit mots. La plus riche porte deux établissements avec leurs adresses, deux fixes, deux URL, une accroche, deux logos de certification et la mention de l'organisme certificateur. Entre les deux : avec ou sans photo de la personne, avec ou sans adresse postale, avec ou sans identifiant de réseau social, avec ou sans logo.

**Aucune mise en page n'est partagée.** Sur quatorze cartes, quatorze compositions : texte noir sur fond blanc, texte blanc sur aplat de couleur pleine page, bandeau vertical coloré, logo centré en haut, logo en filigrane géant, colonne unique, deux colonnes séparées d'un filet. Deux cartes sont au format portrait au lieu du paysage habituel. La variété de templates n'est donc pas un raffinement du générateur, c'est la caractéristique première du type.

**Vocabulaire du secteur.** Plusieurs cartes viennent d'organismes de formation et portent `Qualiopi`, `NF Service Formation continue`, ou des accroches en anglais sur l'apprentissage. Le vocabulaire de la formation apparaît donc sur des cartes de visite, ce qui interdit de s'appuyer dessus pour classer.

### Spécification de génération

Ce que le générateur doit produire pour que le type soit représentatif. Chaque axe est tiré des observations ci-dessus.

1. **Presque jamais de carte cadrée seule.** Le document final est une page A4 très majoritairement vide avec la carte dans un coin, ou une photo où la carte occupe une fraction du cadre sur un fond de bureau. Une minorité, environ un sur sept, fait exception : l'export d'un outil de mise en page, page à la taille du carton et à texte natif. Aucun des quatorze exemples ne s'y trouve, mais rien ne l'empêche en production, et c'est le seul cas où l'OCR n'a rien à faire sur ce type.
2. **Trois chaînes de rendu distinctes**, scan à plat, photo au smartphone et fichier exporté, la photo restant majoritaire comme sur les exemples.
3. **Rotation obligatoire**, incluant les multiples de 90° et pas seulement de petits angles.
4. **Incrustations de l'appareil photo** sur la variante photo : modèle et horodatage dans les coins, en blanc.
5. **Templates nombreux et sans tronc commun**, portrait comme paysage, avec et sans aplat de couleur, texte clair sur fond sombre inclus.
6. **Blocs optionnels tirés indépendamment** : photo de la personne, QR code, adresse postale, second numéro, URL, identifiant de réseau social, logo de certification, accroche. C'est leur combinatoire qui produit l'amplitude observée.
7. **Variante recto verso sur deux pages**, dont un cas où la page nominative est la seconde et un cas où le verso est une publicité sans nom.
8. **Variante manuscrite**, jusqu'au cas extrême de la carte vierge de tout sauf le logo, complétée à la main.
9. **Longueur de texte tirée entre 8 et 60 mots**, et non centrée sur une valeur moyenne.

Le manifest enregistre pour chaque document le template, le régime d'acquisition, l'angle, les blocs présents et le nombre de mots, afin que le rapport puisse dire sur quel axe le classifieur décroche.

## `FEUILLE_EMAR`, feuille d'émargement

### Définition

Document qui atteste la présence effective de participants, et le plus souvent du formateur, à une session de formation professionnelle. La présence se matérialise par une grille de signatures manuscrites tirées en regard des noms des participants, découpée par créneau : demi-journée, journée, séance ou date. Il porte toujours l'identification de la session, dates, horaires, lieu, formateur, et il émane d'un organisme de formation.

### Marqueurs

Le signal ne vient pas d'un vocabulaire fixe, il est instable, mais de la **coexistence d'une liste nominative de personnes et d'une grille de signatures manuscrites découpée par créneau temporel**, sous des métadonnées de session de formation.

| Marqueur | Force |
|---|---|
| grille de signatures manuscrites en regard d'une liste de personnes nommées, découpée en créneaux, matin et après-midi, jours, ou dates | fort, et c'est le marqueur principal |
| métadonnées de session : intitulé de formation, numéro de session ou code stage, dates, horaires, lieu | fort |
| bloc de signature propre au formateur ou animateur, distinct de la grille des stagiaires | fort, présent sur presque tous les exemples |
| titre « feuille d'émargement » ou « feuille de présence » | moyen, il alterne librement et manque sur 2 des 16 exemples |
| numéro d'agrément ou de déclaration d'activité de l'organisme, mention Qualiopi | moyen |
| annotations manuscrites `ABSENT`, `RETARD`, `excusé`, en marge ou dans une case | moyen |
| colonnes entreprise, établissement, fonction, matricule, ou identifiant métier RPPS et ADELI | faible, très variable d'un template à l'autre |

### Désambiguïsation

Le risque n'est pas la confusion avec un voisin unique, aucun type du périmètre ne partage la grille de signatures par créneau. Le risque est double : **un titre trop instable pour servir de règle**, et une **frontière floue avec l'attestation individuelle de fin de formation**, d'autant que plusieurs feuilles portent la clause « en signant, j'atteste avoir reçu l'attestation de formation ».

| Confusion | Ce qui tranche |
|---|---|
| attestation ou certificat de formation, `CERTIF_FORM`, `CERTIF_REAL` | l'attestation nomme **une seule** personne et atteste un résultat ou un nombre d'heures. La feuille d'émargement liste **plusieurs** participants et matérialise leur présence par signature datée, créneau par créneau |
| convention, `CONVENTION` et ses spécialisations | la convention est un contrat rédigé en articles entre organisme, financeur et bénéficiaire. La feuille n'a pas de corps rédigé, seulement un en-tête de session et une grille signée |
| tableau ou formulaire générique | la feuille porte simultanément des noms de personnes, des signatures manuscrites et le vocabulaire d'une session de formation. Un tableau quelconque n'a pas ce faisceau |
| page presque vide, grille non remplie | une feuille peut arriver quasi vierge de signatures, ses lignes barrées en diagonale. L'en-tête de session et la structure de grille restent le signal, pas le remplissage |

**Règle opérationnelle :** en présence d'une liste nominative de participants et d'une grille de signatures découpée par créneau, sous un en-tête de session de formation, répondre feuille d'émargement, **sans exiger le mot « émargement » ni « présence »** qui manquent sur une partie des documents. La présence d'un bloc de signature du formateur distinct de la grille des stagiaires renforce fortement la décision.

### Ce que montrent les 16 exemples réels

Fournis par le métier, non versionnés, dans `tmp/samples/FEM`. Quinze PDF et un JPEG, tous issus de la formation professionnelle française.

**Le titre du document est instable, et parfois absent.** Sur les seize exemples, quatre libellés coexistent, « feuille d'émargement », « feuille de présence », « feuille collective d'émargement », « émargements par demi-journée ou soirée », et **deux documents ne portent aucun titre de ce genre** : l'un ouvre directement sur la session et la grille, l'autre est un récapitulatif mensuel d'heures. S'appuyer sur le titre seul manquerait ces deux cas et confondrait « présence » avec d'autres relevés.

**Cinq familles de mise en page, sans tronc commun.**

| Famille | Structure | Exemples |
|---|---|---|
| Séance unique | lignes stagiaires, deux colonnes de signature matin et après-midi | organisme éditeur, atelier artisanal, académie métier |
| Grille multi-jours | lignes stagiaires, colonnes par jour, chaque jour scindé ou non en demi-journées | semaine du lundi au vendredi, trois jours, jour 1 à 3 |
| Récapitulatif mensuel | **stagiaires en colonnes, dates en lignes**, suivi des heures cours, cumul et restant | formation en langue étalée sur un mois |
| Feuille collective | un bloc, colonnes par journée, colonne nombre d'heures total, cachet de l'organisme | séminaire sur deux jours |
| Format officiel sectoriel | deux tables séparées, intervenants et participants, créneaux matin, après-midi et **soirée**, clause d'attestation signée, mention CNIL, identifiants RPPS et ADELI | organisme de développement professionnel continu médical |

**Trois régimes d'acquisition.** Deux PDF sont à **texte natif**, produits par l'outil de l'organisme éditeur, avec des signatures incrustées comme images. Le reste est **numérisé** : scans à plat plus ou moins dégradés, dont un très bruité au fond gris, une **photo au smartphone** posée sur un bureau avec déformation de perspective, et un JPEG scanné. Quatre documents sont en **paysage tourné à 90°** dans leur fichier. Plusieurs sont **multipages**, de deux à trois pages, la continuation reprenant l'en-tête ou prolongeant la grille.

**Le bloc du formateur est presque universel.** Sur la quasi-totalité des exemples, le formateur ou animateur signe dans un cadre qui lui est propre, souvent dédoublé en matin et après-midi, séparé de la grille des stagiaires. C'est un marqueur plus stable que le titre.

**Blocs facultatifs, tirés indépendamment d'un template à l'autre :** colonne entreprise ou établissement, colonne fonction, matricule ou numéro de stagiaire, identifiant professionnel RPPS ou ADELI dans le secteur médical, case « document pédagogique reçu », ligne « remise d'un support pédagogique oui/non », suivi d'heures souscrites et de report du mois précédent, clause d'attestation avec signature du responsable, cachet de l'organisme, référence d'OPCO.

**Vocabulaire de conformité récurrent.** Plusieurs feuilles portent des consignes Qualiopi imprimées, du type « le formateur écrit la mention ABSENT ou RETARD en regard du nom concerné » ou « le formateur signe chaque feuille de présence ». Ces mentions et les annotations manuscrites `ABSENT`, `RETARD`, `excusé` qui en découlent sont un signal secondaire fréquent.

**Numéro d'agrément systématique.** L'organisme est presque toujours identifié par un numéro de déclaration d'activité ou d'agrément de formation, marqueur du contexte réglementaire du type.

**Un artefact de publipostage.** Un exemple laisse un champ de fusion non résolu visible dans le texte, `OrgFor_Nom` entre guillemets, trace d'une génération bureautique par modèle. Ce genre de résidu est un cas réel que le corpus peut reproduire.

### Spécification de génération

Ce que le générateur doit produire pour que le type soit représentatif. Chaque axe est tiré des observations ci-dessus.

1. **Cinq familles de mise en page distinctes**, séance unique, grille multi-jours, récapitulatif mensuel à stagiaires en colonnes, feuille collective, et format officiel à deux tables. La diversité de structure est la caractéristique première du type, pas un raffinement.
2. **Titre tiré indépendamment de la structure**, « feuille d'émargement », « feuille de présence », « feuille collective d'émargement », « émargements par demi-journée ou soirée », et **une fraction sans aucun titre de ce genre**, pour que le classifieur ne s'appuie pas sur le libellé.
3. **En-tête de session obligatoire** : intitulé de formation, numéro de session ou code stage, dates, horaires, lieu, organisme et numéro d'agrément ou de déclaration d'activité.
4. **Bloc de signature du formateur distinct de la grille des stagiaires**, présent sur la grande majorité des documents, souvent dédoublé matin et après-midi.
5. **Grille de signatures découpée par créneau**, avec les découpages observés : matin et après-midi, jours multiples, dates d'un mois, et le cas médical à trois créneaux dont la soirée.
6. **Signatures manuscrites tirées**, avec des cases vides et des lignes barrées en diagonale, jusqu'à des feuilles quasi vierges. Une minorité à signatures incrustées comme images, pour le cas du texte natif.
7. **Blocs facultatifs tirés indépendamment** : entreprise, établissement, fonction, matricule, identifiant RPPS ou ADELI, case document pédagogique reçu, suivi d'heures, clause d'attestation, cachet, référence d'OPCO.
8. **Annotations manuscrites d'absence** `ABSENT`, `RETARD`, `excusé`, en marge ou dans les cases, accompagnées le cas échéant de la consigne Qualiopi imprimée correspondante.
9. **Trois chaînes de rendu**, texte natif, scan à plat plus ou moins dégradé, et photo au smartphone, avec rotation incluant les multiples de 90°, et des documents multipages où l'en-tête se répète ou la grille se prolonge.
10. **Résidu de publipostage occasionnel**, un champ de fusion non résolu laissé visible, pour couvrir ce cas réel de génération bureautique.

Le manifest enregistre pour chaque document la famille de mise en page, le titre employé ou son absence, le régime d'acquisition, l'angle, le nombre de pages, les blocs présents et le taux de remplissage de la grille, afin que le rapport puisse dire sur quel axe le classifieur décroche.

## `OFF_STAG_EMP`, offre de stage, alternance, emploi

### Définition

Annonce de recrutement pour un stage, une alternance ou un emploi, publiée sur un site d'offres tiers puis imprimée en PDF depuis le navigateur. Le document porte un intitulé de poste rattaché à une entreprise, les conditions du poste (contrat, lieu, dates, rémunération), une description des missions et du profil recherché, le tout encadré par la navigation et les mentions du site qui l'a servie. Ce n'est jamais un document émis par l'entreprise elle-même : c'est la capture d'une page web tierce.

### Marqueurs

Le signal ne vient pas d'un habillage unique, chacun des dix sites source a le sien, mais de la **combinaison d'un habillage de capture web et du vocabulaire du recrutement**.

| Marqueur | Force |
|---|---|
| horodatage et titre de page en en-tête, URL de la page répétée en pied de chaque page | fort, et c'est le marqueur le plus universel : c'est la signature d'une impression navigateur, présente sur les vingt-quatre exemples |
| bouton d'appel à candidature, « Postuler », « Envoyer ma candidature », « Apply » | fort |
| bloc de métadonnées du poste : type de contrat, lieu, dates, rémunération, télétravail | fort |
| vocabulaire de recrutement : missions, profil recherché, compétences, tuteur, mention H/F ou F/H | fort |
| bloc entreprise : nom, secteur, taille en nombre de salariés, logo | moyen |
| bloc « offres similaires », « recherches similaires » ou « plus d'offres d'emploi » en fin de document | moyen, quasi systématique mais de poids variable |
| navigation du site, fil d'ariane, bandeau de consentement aux cookies | moyen, absent sur les captures en image pure |
| mentions RGPD, diversité, situation de handicap | faible |

### Désambiguïsation

Aucun autre type du périmètre ne partage cette structure de capture web ; le risque n'est donc pas la confusion avec un voisin métier mais avec **n'importe quelle autre page web imprimée en PDF**, ou avec le **contrat signé** que l'offre précède.

| Confusion | Ce qui tranche |
|---|---|
| une page web quelconque imprimée en PDF, hors périmètre | la coexistence du vocabulaire de recrutement, missions et profil recherché, avec le bloc métadonnées de contrat. Une page web sans ces deux éléments n'est pas une offre |
| `CONT_APP_PRO`, `CONVENTION` et leurs spécialisations, priorité 2 | l'offre est un appel à candidature avant embauche : aucune signature, aucune partie nommée engagée, souvent un simple bouton « Postuler ». Le contrat ou la convention sont rédigés en articles et signés par des personnes identifiées |
| une fiche métier ou une fiche ROME générique, sans offre précise | l'offre porte toujours une entreprise, un lieu et une date de début identifiés. Une fiche métier générique n'engage personne |

**Règle opérationnelle :** en présence d'un horodatage et d'une URL de capture web, d'un intitulé de poste rattaché à une entreprise, et du vocabulaire missions / profil recherché / candidature, répondre offre de stage, alternance ou emploi, **quel que soit le type de contrat affiché** : stage, alternance, apprentissage, contrat de professionnalisation, CDD et intérim figurent tous dans les exemples réels.

### Ce que montrent les 27 exemples réels

Fournis par le métier, non versionnés, dans `tmp/samples/Offres`. **Trois fichiers sont des doublons octet pour octet** de trois autres, chacun présent une fois sous son nom et une fois sous « - Copie » (`Alternance_Airbus_IT`, `Alternance_Réceptionniste d'hôtel`, `Stage_Airbus_Operateur`), soit **24 offres distinctes**.

**Dix plateformes sources, aucune dominante.** France Travail et JobTeaser en fournissent quatre chacune, Welcome to the Jungle trois, 1jeune1solution.gouv.fr, LinkedIn, Indeed, HelloWork et le portail Workday d'Airbus deux chacun, une plateforme de fiches ROME non identifiée deux, Jobs that makesense une.

**Deux régimes d'acquisition, très inégaux.** Vingt des vingt-quatre offres sont du **texte natif réel**, la couche PDF standard d'une impression de navigateur. **Quatre, toutes issues de JobTeaser, sont une image intégrale sans la moindre couche texte** : `pdftotext` y extrait zéro mot alors que le texte y est parfaitement net à l'œil, sans bruit ni rotation. Ce régime n'est ni du texte natif ni un scan dégradé, c'est une **capture d'interface numérique propre**, opaque à l'extraction basique mais lisible sans effort par l'OCR, à la différence du régime scanné des autres types du corpus qui simule une usure physique.

**Multipage systématique, de deux à neuf pages, et la majorité du contenu n'est pas l'offre elle-même.** Sur les captures les plus longues, jobs.makesense et Welcome to the Jungle, plus de la moitié du texte est du bruit de site : bandeau de cookies répété jusqu'à sept fois sur un même document, articles de blog suggérés, postes « similaires » sans rapport avec l'offre. Le contenu utile, titre, entreprise, contrat, missions, profil, tient le plus souvent sur une à deux pages.

**Le type de contrat affiché ne suffit pas à repérer l'alternance ou le stage.** Sur les quatre offres de France Travail, trois sont administrativement typées « CDD » ou « Intérim » dans le bloc métadonnées alors que le corps du texte révèle un contrat d'apprentissage ou de professionnalisation : Centrakor, CDD 24 mois affiché pour un contrat d'apprentissage ; CPAM, CDD 12 mois affiché pour un contrat d'apprentissage ; Synergie, Intérim 10 mois affiché pour un contrat de professionnalisation CQPM. Une règle qui ne lirait que le champ contrat manquerait ces trois cas.

**Le bloc « offres similaires » introduit librement d'autres types de contrat et d'autres métiers.** Sur les quatre offres France Travail, cette liste de fin de document mélange CDI, CDD et intérim sur des métiers sans rapport avec l'offre consultée : c'est la principale source de vocabulaire parasite du type.

**Un contact nominatif apparaît sur plusieurs plateformes**, ce qui confirme ce que [[Etape 1 - Corpus et protocole]] laissait ouvert : un nom complet de gestionnaire RH sur l'offre CPAM et sur l'offre Kempchen, un nom complet accompagné d'une photo réelle sur l'offre LinkedIn Panzani, et sur Welcome to the Jungle des prénoms de collaborateurs accompagnés de leur photo, présentés en « Rencontrez Jean-Louis », « Rencontrez Anne », « Rencontrez Lucia ». L'assainissement du type doit donc couvrir ce cas, pas seulement le contenu de l'offre.

**Un cas de stage non rémunéré.** L'offre Matchbox affiche explicitement « Rémunération : Aucune », un cas réel conforme à la réglementation en dessous du seuil de gratification.

**Neuf habillages de site visuellement distincts sur les vingt-quatre offres, aucun tronc commun de mise en page :** bloc de fiche technique à droite (France Travail), formulaire de candidature intégré au document (HelloWork), deux colonnes avec compteur de vues et bandeau Premium (LinkedIn), fiche ROME avec compétences acquises et simulateur d'aides financières (1jeune1solution et la plateforme non identifiée), portail RH bilingue (Workday, Airbus), bandeau de cookies répété et suggestions éditoriales (Jobs that makesense), index d'égalité professionnelle et portraits de collaborateurs (Welcome to the Jungle).

### Spécification de génération

Ce que le générateur doit produire pour que le type soit représentatif. Chaque axe est tiré des observations ci-dessus.

1. **Plusieurs habillages de site distincts**, au moins quatre à cinq templates HTML et CSS couvrant les familles observées : sobre institutionnel, jobboard commercial avec formulaire intégré, réseau professionnel à deux colonnes, portail RH bilingue, site jeune et éditorialisé. Pas de tronc commun entre eux, c'est la variété qui porte le signal, comme pour la carte de visite.
2. **En-tête et pied de page d'impression navigateur obligatoires** : date et heure, titre de la page, URL complète répétée en pied de chaque page produite.
3. **Bloc offre** : intitulé de poste, entreprise avec secteur et taille, type de contrat, lieu, dates, rémunération ou son absence, bouton d'appel à candidature.
4. **Corps rédigé** : missions en liste à puces, profil recherché, présentation de l'entreprise en paragraphe.
5. **Type de contrat tiré librement** parmi stage, alternance, apprentissage, contrat de professionnalisation, CDD, intérim, y compris un désaccord entre le champ contrat affiché et la nature réelle du poste, pour reproduire le cas France Travail observé.
6. **Bruit de fin de document obligatoire et volumineux** : bloc « offres similaires » ou équivalent, mêlant librement d'autres types de contrat et d'autres métiers, sur une fraction significative des pages produites.
7. **Multipage de deux à neuf pages**, la majorité du volume portée par le bruit de site plutôt que par l'offre.
8. **Une minorité, environ un document sur six**, rendue comme capture d'interface intégrale sans couche texte, nette et sans dégradation physique, à la différence du régime scanné des autres types du corpus.
9. **Contact nominatif occasionnel**, recruteur ou collaborateur cité avec prénom ou nom complet, parfois accompagné d'une photo, pour couvrir le risque PII observé sur plusieurs plateformes.
10. **Bandeau de consentement aux cookies répété plusieurs fois** sur un même document pour au moins un des templates, et cas d'un stage sans rémunération affichée.

Le manifest enregistre pour chaque document le template de site imité, le régime d'acquisition, le nombre de pages, le type de contrat affiché face au type de contrat réel, et la présence d'un contact nominatif.

## `RIB`, relevé d'identité bancaire

### Définition

Document émis par un établissement bancaire qui identifie un compte : coordonnées de l'établissement, codes nationaux ou IBAN et BIC, et le plus souvent le titulaire. Il ne porte ni cartouche à signer, ni autorisation de prélèvement, ni montant : c'est une pièce d'identification, à la différence du mandat SEPA qui l'accompagne parfois.

### Marqueurs

Le signal tient à la coexistence d'un petit nombre de blocs très normés, quelle que soit la mise en page qui les porte.

| Marqueur | Force |
|---|---|
| IBAN, toujours présent, souvent imprimé par groupes de quatre caractères | fort, et c'est le seul marqueur commun aux deux exemples réels |
| BIC, toujours présent, à côté de l'IBAN | fort |
| pavé des quatre codes nationaux, établissement, guichet, compte, clé, en table | fort quand il est présent, mais absent d'un export récent qui ne montre que l'IBAN |
| en-tête ou bandeau de l'établissement bancaire, logo et nom | fort |
| bloc « Titulaire du compte », nom et adresse | moyen, absent sur un des deux exemples réels |
| mention d'usage en pied (« Remettez ce relevé à tout organisme... ») | moyen |
| aucun cartouche à signer, aucune clause d'autorisation, aucun montant | fort en négatif, c'est ce qui le distingue du mandat SEPA |

### Désambiguïsation

Le seul risque de confusion réel est avec ses deux voisins directs du même groupe, `SEPA` et `RIB_SEPA` : les trois partagent le même pavé de coordonnées bancaires.

| Confusion | Ce qui tranche |
|---|---|
| `SEPA` | le mandat ne montre jamais le pavé de codes établissement/guichet/compte/clé, seulement l'IBAN et le BIC, et il porte en revanche RUM, ICS, un cartouche de type de paiement et une signature. Le RIB isolé n'a rien de tout cela |
| `RIB_SEPA` | présence conjointe du pavé de codes bancaires **et** du cartouche de mandat, RUM et ICS, tranche pour `RIB_SEPA`. Le RIB isolé n'a jamais de RUM ni d'ICS |
| pavé RIB en pied d'une facture ou d'un devis | un RIB isolé occupe toute la page à lui seul. Le pavé qui figure au bas d'une pièce comptable est un fragment au milieu d'un tableau de lignes, de totaux et d'un pavé légal, jamais la totalité du document |

**Règle opérationnelle :** en présence d'un IBAN et d'un BIC, sans cartouche de mandat ni RUM ni ICS, et occupant la totalité d'une page par ailleurs peu chargée, répondre RIB.

### Ce que montrent les 2 exemples réels

Fournis par le métier, non versionnés, dans `tmp/samples/RIB`. Deux PDF d'une seule page chacun, tous deux d'origine bancaire.

**Le premier est un export bancaire sans couche texte.** `IPTIC-2020-IPTIC-6144-RIB 2.pdf` est un relevé CIC, PDF A4 sans aucun texte extractible malgré un rendu net et vectoriel : ni `pdftotext` ni un lecteur de texte PDF n'y trouvent le moindre mot, seule une inspection visuelle révèle le contenu. Il porte un bandeau CIC, un pavé de quatre codes (banque, guichet, compte, clé, plus la devise EUR), une section IBAN et BIC, un bloc « Titulaire du compte (Account Owner) » nominatif avec adresse, un bloc domiciliation avec le téléphone de l'agence, et une mention d'usage en pied de page.

**Le second est explicitement un spécimen pédagogique.** `RIB_AXA-BANQUE 1.pdf` porte un filigrane diagonal « DOCUMENT PÉDAGOGIQUE » et une couche de texte native. Sa mise en page est empilée en un seul bloc plutôt qu'en deux colonnes : titre « Relevé d'identité bancaire édité le JJ/MM/AAAA » avec la date d'édition intégrée au titre plutôt qu'affichée à part, puis trois blocs successifs, Domiciliation avec le pavé de codes, IBAN découpé en sept cases individuelles, Code BIC dans sa propre case. **Aucun bloc titulaire n'y figure du tout** : le document ne montre que l'identité bancaire, pas celle du client.

**L'invariant confirmé sur les deux exemples est réduit à l'essentiel.** Codes nationaux, IBAN, BIC. Tout le reste, présence du titulaire, disposition en une ou deux colonnes, position de la date d'édition, filigrane ou non, varie déjà sur un échantillon de deux. Ce constat valide le choix déjà pris par le générateur : produire une seule mise en page mais volontairement peu chargée, plutôt que plusieurs mises en page qui simuleraient une variété non observée.

**Le découpage de l'IBAN par groupes de quatre caractères**, déjà implémenté via `iban_formate`, est confirmé par l'exemple AXA : `FR76 4497 1647 9557 2177 4041 842`, exactement le format que produit `python-stdnum`.

### Spécification de génération

1. **Un seul template**, volontairement peu variable : dans la réalité, les RIB se ressemblent presque tous, leur forme étant quasi normalisée. Inventer plusieurs présentations rendrait le corpus moins représentatif, pas plus.
2. **Pavé des quatre codes nationaux, IBAN et BIC toujours présents**, seuls marqueurs communs aux deux exemples réels.
3. **Bloc titulaire tiré, pas systématique** : le second exemple réel n'en montre aucun, un export purement bancaire peut se limiter à l'identité du compte.
4. **Titulaire personne physique ou entreprise**, tiré indépendamment, une entreprise étant imposée quand le RIB est celui de l'organisme émetteur d'une facture ou d'un devis.
5. **Aucun cartouche à signer, aucune clause d'autorisation, aucun montant** : c'est ce qui distingue structurellement ce type de `SEPA` et `RIB_SEPA`.

Le manifest enregistre pour chaque document le titulaire, sa nature personne ou entreprise, et les identifiants bancaires injectés comme PII de vérité terrain.

## `SEPA`, mandat SEPA

### Définition

Formulaire par lequel un débiteur autorise un créancier, ici l'organisme de formation, à prélever son compte. Il repose sur un modèle largement normalisé au niveau européen (spécification EPC, reprise telle quelle par la quasi-totalité des créanciers français) : référence unique de mandat (RUM), identifiant créancier SEPA (ICS), type de paiement, clause d'autorisation, signature du débiteur.

**Aucun exemple réel n'a été fourni pour ce type** (D25 ne couvre que carte de visite, feuille d'émargement, offre et RIB). Cette fiche s'appuie sur le modèle normatif EPC/CFONB, la source recommandée par [[Etape 1 - Corpus et protocole]] pour les types normés de la priorité 2, plutôt que sur un dépouillement d'exemples.

### Marqueurs

| Marqueur | Force |
|---|---|
| référence unique de mandat (RUM) | fort, et propre au groupe `SEPA` / `RIB_SEPA` |
| identifiant créancier SEPA (ICS), forme `FR` + 2 chiffres + 3 caractères + identifiant national | fort, et propre au groupe `SEPA` / `RIB_SEPA` |
| clause d'autorisation standardisée (« vous autorisez (A)... et (B)... ») | fort, texte quasi identique d'un créancier à l'autre |
| cartouche de type de paiement, « récurrent / répétitif » ou « ponctuel » | fort |
| IBAN et BIC du débiteur, sans le pavé de codes établissement/guichet/compte/clé | moyen, présents mais sous une forme plus dépouillée que sur un RIB |
| identité du créancier et du débiteur, chacun avec son adresse | moyen |
| lieu, date et signature manuscrite du débiteur | moyen |

### Désambiguïsation

| Confusion | Ce qui tranche |
|---|---|
| `RIB` | le RIB ne porte jamais de RUM ni d'ICS, jamais de cartouche de type de paiement, jamais de signature |
| `RIB_SEPA` | présence conjointe du pavé de coordonnées bancaires **et** du cartouche de mandat tranche pour `RIB_SEPA`. Un mandat isolé n'a pas le pavé de codes établissement/guichet/compte/clé, seulement l'IBAN et le BIC |
| formulaire d'autorisation générique, hors périmètre | RUM et ICS sont propres au prélèvement SEPA, aucun autre formulaire du périmètre ne les porte |

**Règle opérationnelle :** en présence d'un RUM et d'un ICS, avec une clause d'autorisation de prélèvement et une signature, mais sans le pavé de codes établissement/guichet/compte/clé, répondre mandat SEPA.

### Spécification de génération

1. **Un seul template**, partagé avec `RIB_SEPA` (`mandat_sepa_01.html`) : seul le booléen qui affiche ou masque le pavé de codes bancaires distingue les deux types au rendu, sur le modèle de ce que `documents/piece.py` fait déjà pour la facture et le devis.
2. **Jamais le pavé de codes établissement/guichet/compte/clé**, seulement l'IBAN et le BIC du débiteur, cohérents avec ces mêmes codes en interne.
3. **RUM et ICS toujours présents et valides**, l'ICS portant ses deux chiffres de contrôle calculés par le même algorithme ISO 7064 que l'IBAN.
4. **Type de paiement tiré**, récurrent ou répétitif dans la majorité des cas, un paiement ponctuel restant minoritaire.
5. **Clause d'autorisation standardisée**, reprise du modèle EPC quasi universel en France, avec le nom du créancier interpolé.
6. **Signature manuscrite tracée**, jamais du texte en police cursive : même contrainte que sur la feuille d'émargement, un mandat déposé en gestion documentaire est signé, et un copier-coller ne doit jamais en révéler le nom.
7. **Débiteur et créancier tirés indépendamment** du débiteur et du créancier d'une facture ou d'un devis de même numéro, pour éviter les quasi-jumeaux textuels au sein du corpus.

Le manifest enregistre le RUM, le type de paiement et les identifiants bancaires injectés comme PII de vérité terrain.

## `RIB_SEPA`, relevé d'identité bancaire et mandat SEPA

### Définition

Formulaire combiné qui réunit dans une seule pièce le relevé d'identité bancaire du débiteur et le mandat de prélèvement SEPA qu'il signe. C'est le document que beaucoup de créanciers envoient à faire compléter et retourner en une fois, plutôt que deux pièces séparées.

**Aucun exemple réel n'a été fourni pour ce type**, comme pour `SEPA` : cette fiche combine le modèle normatif du mandat et la forme du RIB observée sur les deux exemples réels du groupe.

### Marqueurs

C'est l'union littérale des marqueurs de `RIB` et de `SEPA` : c'est ce qui en fait le cas le plus dur du corpus, décrit comme tel dans [[Etape 1 - Corpus et protocole]].

| Marqueur | Force |
|---|---|
| présence simultanée du pavé de codes établissement/guichet/compte/clé **et** du cartouche RUM/ICS | fort, et c'est le marqueur qui tranche à lui seul |
| IBAN, BIC | fort, partagé avec `RIB` et `SEPA` |
| clause d'autorisation, type de paiement, signature | fort, partagé avec `SEPA` |
| identité du créancier et du débiteur | moyen |

### Désambiguïsation

| Confusion | Ce qui tranche |
|---|---|
| `RIB` | absence du cartouche RUM/ICS sur un RIB isolé |
| `SEPA` | absence du pavé de codes établissement/guichet/compte/clé sur un mandat isolé |

**Règle opérationnelle :** un seul des deux blocs, pavé de codes bancaires ou cartouche de mandat, tranche pour l'autre type du groupe. Leur présence conjointe tranche pour `RIB_SEPA`, et c'est la seule règle qui sépare correctement les trois types.

### Spécification de génération

1. **Même template et mêmes données que `SEPA`**, avec le pavé de codes établissement/guichet/compte/clé affiché en plus, au-dessus du cartouche de mandat.
2. **Les mêmes identifiants bancaires** alimentent le pavé, l'IBAN et le BIC : ils sont générés ensemble, comme pour `RIB`, afin qu'un document ne se contredise jamais lui-même.
3. Pour le reste, cf. la spécification de `SEPA`, points 3 à 7, inchangée.

Le manifest enregistre les mêmes champs que `SEPA`.
