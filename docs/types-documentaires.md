# Types documentaires

Fiches des types que le service doit distinguer, sur les 14 types des priorités 1 et 2 de la liste métier. Chaque fiche donne une définition, les marqueurs qui portent le signal, ce que montrent les exemples réels quand il en existe, et ce que le générateur doit produire. Les règles de désambiguïsation sont portées par le groupe de types voisins, là où se joue réellement la classification.

Les fiches se recopient dans le pré-prompt du classifieur : elles sont écrites pour être lues par un modèle autant que par un humain.

## Les 14 types

| Code | Libellé | Groupe | Fiche |
|---|---|---|---|
| `RIB` | Relevé d'identité bancaire | bancaire | [écrite](#rib-relevé-didentité-bancaire) |
| `SEPA` | Mandat SEPA | bancaire | [écrite](#sepa-mandat-sepa) |
| `RIB_SEPA` | Relevé d'identité bancaire et mandat SEPA | bancaire | [écrite](#rib_sepa-relevé-didentité-bancaire-et-mandat-sepa) |
| `CARTE_VISITE` | Carte de visite | isolé | [écrite](#carte_visite-carte-de-visite) |
| `FEUILLE_EMAR` | Feuille d'émargement | isolé | [écrite](#feuille_emar-feuille-démargement) |
| `OFF_STAG_EMP` | Offre de stage, alternance, emploi | isolé | [écrite](#off_stag_emp-offre-de-stage-alternance-emploi) |
| `EVAL_FORM` | Évaluation de la formation | isolé | à écrire |
| `CONVENTION` | Convention | conventions | à écrire |
| `CONV_PROF` | Alternance, convention de formation en contrat de professionnalisation | conventions | à écrire |
| `APP_CONV_SFP` | Apprentissage, convention stagiaire de la formation professionnelle | conventions | à écrire |
| `CERTIF_FORM` | Certificat de formation | certificats | à écrire |
| `CERTIF_REAL` | Certificat de réalisation | certificats | à écrire |
| `CONT_APP_PRO` | Contrat d'apprentissage ou de professionnalisation | contrat | à écrire |
| `APP_CRFA_P2S` | Apprentissage, CERFA P2S | contrat | à écrire |

## Où se joue la classification

La difficulté ne se répartit pas également sur les 14 types. Elle se concentre sur quatre groupes de quasi-synonymes administratifs, plus deux types dont le problème n'est pas la confusion mais la nature du signal.

| Groupe | Types | Ce qui tranche |
|---|---|---|
| Bancaire | `RIB`, `SEPA`, `RIB_SEPA` | présence du pavé de codes nationaux, présence du cartouche de mandat, ou les deux. Le cas le plus dur du corpus, `RIB_SEPA` étant l'union littérale des deux autres |
| Conventions | `CONVENTION`, `CONV_PROF`, `APP_CONV_SFP` | la mention explicite du dispositif, professionnalisation ou apprentissage avec statut de stagiaire de la formation professionnelle. `CONVENTION` est le repli quand aucune n'apparaît |
| Certificats | `CERTIF_FORM`, `CERTIF_REAL` | le certificat de réalisation est le document réglementaire destiné au financeur, il porte dates et heures réalisées. Le certificat de formation s'adresse au stagiaire |
| Contrat | `CONT_APP_PRO`, `APP_CRFA_P2S` | le CERFA porte son numéro imprimé et une grille de cases, le contrat est rédigé en articles |
| Faible signal | `CARTE_VISITE` | quelques dizaines de mots après OCR, aucun voisin proche. Le risque est de projeter n'importe quel type sur du bruit, avec une confiance élevée |
| Capture web | `OFF_STAG_EMP` | le risque n'est pas un voisin métier mais toute autre page web imprimée en PDF |

## Groupe bancaire

Les trois types partagent le même pavé de coordonnées bancaires. Deux blocs, et eux seuls, les séparent.

| | Pavé des 4 codes nationaux | IBAN et BIC | RUM, ICS, clause d'autorisation, signature |
|---|---|---|---|
| `RIB` | oui | oui | non |
| `SEPA` | non | oui | oui |
| `RIB_SEPA` | oui | oui | oui |

**Règle opérationnelle :** la présence conjointe du pavé de codes et du cartouche de mandat tranche pour `RIB_SEPA`. Un seul des deux blocs tranche pour l'autre type du groupe. C'est la seule règle qui sépare correctement les trois.

Deux nuances tirées des exemples réels : un export bancaire récent peut ne montrer que l'IBAN, sans le pavé de codes, et un pavé de coordonnées bancaires figurant au bas d'une facture ou d'un devis n'est qu'un fragment au milieu d'un tableau de lignes et de totaux, jamais la totalité du document.

### `RIB`, relevé d'identité bancaire

Document émis par un établissement bancaire qui identifie un compte : coordonnées de l'établissement, codes nationaux ou IBAN et BIC, et le plus souvent le titulaire. Ni cartouche à signer, ni autorisation de prélèvement, ni montant.

| Marqueur | Force |
|---|---|
| IBAN, souvent imprimé par groupes de quatre caractères | fort, seul marqueur commun aux deux exemples réels avec le BIC |
| BIC, à côté de l'IBAN | fort |
| pavé des quatre codes nationaux, établissement, guichet, compte, clé | fort quand présent, absent d'un export récent limité à l'IBAN |
| en-tête ou bandeau de l'établissement bancaire, logo et nom | fort |
| aucun cartouche à signer, aucune clause d'autorisation, aucun montant | fort en négatif |
| bloc « Titulaire du compte », nom et adresse | moyen, absent d'un des deux exemples réels |
| mention d'usage en pied, « Remettez ce relevé à tout organisme... » | moyen |

**Ce que montrent les 2 exemples réels**, dans `tmp/samples/RIB`, deux PDF d'une page.

- **Un export CIC sans aucune couche texte**, malgré un rendu net et vectoriel : `pdftotext` n'y trouve pas un mot. Deux colonnes, pavé de quatre codes avec la devise, IBAN et BIC, bloc titulaire nominatif avec adresse, domiciliation avec téléphone d'agence, mention d'usage en pied.
- **Un spécimen AXA filigrané « DOCUMENT PÉDAGOGIQUE »**, à texte natif, empilé en un seul bloc : date d'édition intégrée au titre, domiciliation avec le pavé de codes, IBAN en sept cases, BIC dans sa propre case. **Aucun bloc titulaire.**
- **L'invariant tient à trois éléments** : codes nationaux, IBAN, BIC. Présence du titulaire, disposition, position de la date d'édition varient déjà sur un échantillon de deux.
- Le découpage de l'IBAN par groupes de quatre, `FR76 4497 1647 9557 2177 4041 842`, est exactement le format produit par `python-stdnum`.

**À générer.** Un seul template, volontairement peu variable : les RIB réels se ressemblent presque tous, leur forme étant quasi normalisée, et inventer des présentations rendrait le corpus moins représentatif. Pavé des quatre codes, IBAN et BIC toujours présents. Bloc titulaire tiré et non systématique, personne physique ou entreprise, une entreprise étant imposée quand le RIB est celui de l'émetteur d'une facture ou d'un devis. Jamais de cartouche à signer, de clause d'autorisation ni de montant.

Le manifest enregistre le titulaire, sa nature, et les identifiants bancaires injectés comme PII de vérité terrain.

### `SEPA`, mandat SEPA

Formulaire par lequel un débiteur autorise un créancier, ici l'organisme de formation, à prélever son compte. Il suit un modèle largement normalisé au niveau européen, spécification EPC reprise telle quelle par la quasi-totalité des créanciers français.

**Aucun exemple réel n'a été fourni pour ce type**, D25 ne couvrant que la carte de visite, la feuille d'émargement, l'offre et le RIB. Cette fiche s'appuie sur le modèle normatif EPC/CFONB plutôt que sur un dépouillement.

| Marqueur | Force |
|---|---|
| référence unique de mandat, RUM | fort, propre au groupe `SEPA` et `RIB_SEPA` |
| identifiant créancier SEPA, ICS, forme `FR` + 2 chiffres + 3 caractères + identifiant national | fort, propre au groupe `SEPA` et `RIB_SEPA` |
| clause d'autorisation standardisée, « vous autorisez (A)... et (B)... » | fort, texte quasi identique d'un créancier à l'autre |
| cartouche de type de paiement, récurrent ou ponctuel | fort |
| IBAN et BIC du débiteur, sans le pavé de codes nationaux | moyen |
| identité du créancier et du débiteur, chacun avec son adresse | moyen |
| lieu, date et signature manuscrite du débiteur | moyen |

**À générer.** Un seul template, `mandat_sepa_01.html`, partagé avec `RIB_SEPA` : seul le booléen qui affiche ou masque le pavé de codes bancaires distingue les deux types au rendu, sur le modèle de ce que `documents/piece.py` fait déjà pour la facture et le devis. Jamais le pavé de codes nationaux, seulement l'IBAN et le BIC du débiteur, cohérents avec ces codes en interne. RUM et ICS toujours valides, l'ICS portant ses deux chiffres de contrôle calculés par le même algorithme ISO 7064 que l'IBAN. Type de paiement tiré, récurrent en majorité. Clause d'autorisation reprise du modèle EPC, avec le nom du créancier interpolé. Signature manuscrite tracée, jamais du texte en police cursive : un copier-coller ne doit jamais révéler le nom. Débiteur et créancier tirés indépendamment de ceux d'une facture ou d'un devis de même numéro, pour éviter les quasi-jumeaux textuels.

Le manifest enregistre le RUM, le type de paiement et les identifiants bancaires injectés.

### `RIB_SEPA`, relevé d'identité bancaire et mandat SEPA

Formulaire combiné qui réunit dans une seule pièce le relevé d'identité bancaire du débiteur et le mandat de prélèvement qu'il signe. C'est le document que beaucoup de créanciers envoient à faire compléter et retourner en une fois, plutôt que deux pièces séparées.

**Aucun exemple réel n'a été fourni pour ce type**, comme pour `SEPA` : cette fiche combine le modèle normatif du mandat et la forme du RIB observée sur les deux exemples du groupe.

Ses marqueurs sont l'union littérale de ceux de `RIB` et de `SEPA`, ce qui en fait le cas le plus dur du corpus. Le seul qui tranche à lui seul est la **présence simultanée du pavé de codes nationaux et du cartouche RUM et ICS**.

**À générer.** Même template et mêmes données que `SEPA`, avec le pavé de codes affiché en plus, au-dessus du cartouche de mandat. Les mêmes identifiants bancaires alimentent le pavé, l'IBAN et le BIC, générés ensemble comme pour `RIB`, afin qu'un document ne se contredise jamais lui-même. Le reste est identique à `SEPA`.

## `CARTE_VISITE`, carte de visite

Carton de format carte de crédit portant l'identité professionnelle d'une personne : nom, fonction, employeur, coordonnées directes. Ni destinataire, ni date, ni objet, ni montant, ni signature.

Le signal ne vient pas du vocabulaire, il n'y en a presque pas, mais de la **combinaison d'entités sans texte liant**.

| Marqueur | Force |
|---|---|
| moins de 40 mots au total, aucune phrase verbale | fort, et c'est le marqueur principal |
| coexistence nom de personne, intitulé de fonction, téléphone, e-mail | fort |
| aucune date, aucun montant, aucun numéro de dossier | fort en négatif |
| numéro de mobile en `06` ou `07`, souvent avec un fixe en second | moyen |
| QR code, sur 6 des 14 exemples | moyen, invisible pour l'OCR |
| logo et nom d'entreprise dominants visuellement | moyen |

**Ce qui tranche.** Aucun autre type du périmètre ne ressemble à une carte de visite : le risque n'est pas la confusion mais **l'absence de signal**. Face à une page presque vide, la carte porte des coordonnées structurées. Face à `PHOTO` de la priorité 3, même régime d'acquisition, mais du texte imprimé exploitable. Face à l'en-tête d'un autre document, qui porte aussi logo et coordonnées, l'en-tête est suivi d'un corps : destinataire, objet, dates, montants. La carte n'a pas de corps.

**Règle opérationnelle :** en dessous de 40 mots extraits, avec des coordonnées et sans corps de document, répondre carte de visite. C'est le type sur lequel un score de confiance bas est légitime, et un bon révélateur de la calibration : un modèle qui répond 0,95 sur huit mots océrisés est mal calibré.

**Ce que montrent les 14 exemples réels**, dans `tmp/samples/Cartes de visites`, cités par leur entreprise et jamais par la personne.

- **Aucun n'est bureautique** : le document déposé est toujours la reprise photographique d'un objet physique. 8 photos au smartphone (JPEG 2288x4080, carte posée sur un bureau clair occupant 15 % à 60 % du cadre, éclairage inégal, modèle d'appareil et horodatage incrustés dans deux coins) et 6 scans à plat (PDF A4 sans couche texte, carte dans un coin d'une page vide à 85 % ou plus, bord de couvercle en bande noire). Rien n'interdit pourtant qu'un export d'outil de mise en page soit déposé tel quel, et le corpus doit couvrir ce cas.
- **Aucune n'est droite.** Quatre sont à 90° dans leur cadre, les autres à quelques degrés.
- **Deux des six PDF ont deux pages**, recto et verso scannés séparément. Sur l'un, la page nominative est la seconde. Sur l'autre, **le verso n'est pas une carte de visite** mais une publicité de l'entreprise.
- **Manuscrit sur 2 des 14.** Un scan porte un nom et un numéro ajoutés au stylo. Sur une photo, la carte ne porte **que le logo**, et tout ce qui identifie la personne est manuscrit, donc hors de portée d'un OCR de texte imprimé.
- **Amplitude de contenu d'un facteur cinq**, de huit mots (logo, prénom et nom, e-mail, mobile) à deux établissements avec adresses, deux fixes, deux URL, une accroche et deux logos de certification.
- **Quatorze compositions sur quatorze cartes**, portrait comme paysage, texte noir sur blanc, texte blanc sur aplat pleine page, bandeau vertical, logo en filigrane géant, une ou deux colonnes. La variété de templates est la caractéristique première du type.
- **Le vocabulaire de la formation apparaît sur des cartes de visite**, `Qualiopi`, `NF Service Formation continue`, accroches sur l'apprentissage : impossible de s'appuyer dessus pour classer.

**À générer.**

1. **Presque jamais de carte cadrée seule** : page A4 très majoritairement vide avec la carte dans un coin, ou photo où elle occupe une fraction du cadre. Une minorité, environ un sur sept, est l'export d'un outil de mise en page, à la taille du carton et à texte natif : le seul cas où l'OCR n'a rien à faire sur ce type.
2. **Trois chaînes de rendu**, scan à plat, photo au smartphone, fichier exporté, la photo restant majoritaire.
3. **Rotation obligatoire**, multiples de 90° compris.
4. **Incrustations de l'appareil photo** sur la variante photo, modèle et horodatage dans les coins.
5. **Templates nombreux et sans tronc commun**, portrait comme paysage, texte clair sur fond sombre inclus.
6. **Blocs optionnels tirés indépendamment** : photo de la personne, QR code, adresse postale, second numéro, URL, réseau social, logo de certification, accroche. Leur combinatoire produit l'amplitude observée.
7. **Variante recto verso sur deux pages**, dont un cas où la page nominative est la seconde et un cas où le verso est une publicité sans nom.
8. **Variante manuscrite**, jusqu'à la carte vierge de tout sauf le logo.
9. **Longueur de texte tirée entre 8 et 60 mots**, et non centrée sur une moyenne.

Le manifest enregistre le template, le régime d'acquisition, l'angle, les blocs présents et le nombre de mots, afin que le rapport puisse dire sur quel axe le classifieur décroche.

## `FEUILLE_EMAR`, feuille d'émargement

Document qui atteste la présence effective de participants, et le plus souvent du formateur, à une session de formation professionnelle. La présence se matérialise par une grille de signatures manuscrites en regard des noms, découpée par créneau. Il porte toujours l'identification de la session, dates, horaires, lieu, formateur, et émane d'un organisme de formation.

Le signal ne vient pas d'un vocabulaire fixe, il est instable, mais de la **coexistence d'une liste nominative et d'une grille de signatures découpée par créneau**, sous des métadonnées de session.

| Marqueur | Force |
|---|---|
| grille de signatures en regard d'une liste de personnes, découpée en créneaux, matin et après-midi, jours ou dates | fort, et c'est le marqueur principal |
| métadonnées de session : intitulé, numéro ou code stage, dates, horaires, lieu | fort |
| bloc de signature du formateur, distinct de la grille des stagiaires | fort, sur presque tous les exemples |
| titre « feuille d'émargement » ou « feuille de présence » | moyen, alterne librement et manque sur 2 des 16 exemples |
| numéro d'agrément ou de déclaration d'activité, mention Qualiopi | moyen |
| annotations manuscrites `ABSENT`, `RETARD`, `excusé` | moyen |
| colonnes entreprise, établissement, fonction, matricule, RPPS, ADELI | faible, très variable d'un template à l'autre |

**Ce qui tranche.** Aucun type du périmètre ne partage la grille de signatures par créneau. Le risque est ailleurs : un titre trop instable pour servir de règle, et une frontière floue avec l'attestation individuelle, d'autant que plusieurs feuilles portent la clause « en signant, j'atteste avoir reçu l'attestation de formation ».

| Confusion | Ce qui tranche |
|---|---|
| `CERTIF_FORM`, `CERTIF_REAL` | l'attestation nomme **une seule** personne et atteste un résultat ou un nombre d'heures. La feuille liste **plusieurs** participants et matérialise leur présence, créneau par créneau |
| `CONVENTION` et ses spécialisations | la convention est un contrat rédigé en articles. La feuille n'a pas de corps rédigé, seulement un en-tête de session et une grille |
| tableau ou formulaire générique | la feuille porte simultanément des noms, des signatures manuscrites et le vocabulaire d'une session de formation |
| grille non remplie | une feuille peut arriver quasi vierge, lignes barrées en diagonale. L'en-tête et la structure de grille restent le signal, pas le remplissage |

**Règle opérationnelle :** liste nominative plus grille de signatures par créneau, sous un en-tête de session, répondre feuille d'émargement, **sans exiger le mot « émargement » ni « présence »**. Un bloc de signature du formateur distinct de la grille renforce fortement la décision.

**Ce que montrent les 16 exemples réels**, dans `tmp/samples/FEM`, quinze PDF et un JPEG.

- **Le titre est instable et parfois absent** : quatre libellés coexistent, « feuille d'émargement », « feuille de présence », « feuille collective d'émargement », « émargements par demi-journée ou soirée », et **deux documents n'en portent aucun**, l'un ouvrant directement sur la grille, l'autre étant un récapitulatif mensuel d'heures.
- **Cinq familles de mise en page, sans tronc commun :**

| Famille | Structure |
|---|---|
| Séance unique | lignes stagiaires, deux colonnes de signature matin et après-midi |
| Grille multi-jours | lignes stagiaires, colonnes par jour, scindées ou non en demi-journées |
| Récapitulatif mensuel | **stagiaires en colonnes, dates en lignes**, heures cours, cumul, restant |
| Feuille collective | un bloc, colonnes par journée, colonne nombre d'heures, cachet de l'organisme |
| Format officiel sectoriel | deux tables, intervenants et participants, créneaux matin, après-midi et **soirée**, clause d'attestation signée, mention CNIL, RPPS et ADELI |

- **Trois régimes d'acquisition.** Deux PDF à texte natif avec signatures incrustées comme images, le reste numérisé, dont un scan très bruité au fond gris, une photo au smartphone avec déformation de perspective, un JPEG. Quatre documents en paysage tourné à 90°. Plusieurs multipages, de deux à trois pages.
- **Le bloc du formateur est presque universel**, souvent dédoublé matin et après-midi, séparé de la grille. Marqueur plus stable que le titre.
- **Blocs facultatifs tirés indépendamment** : entreprise, établissement, fonction, matricule, RPPS ou ADELI dans le secteur médical, case « document pédagogique reçu », remise d'un support, heures souscrites et report du mois précédent, clause d'attestation avec signature du responsable, cachet, référence d'OPCO.
- **Vocabulaire de conformité récurrent**, consignes Qualiopi imprimées du type « le formateur écrit la mention ABSENT ou RETARD en regard du nom concerné ».
- **Numéro d'agrément ou de déclaration d'activité presque systématique.**
- **Un artefact de publipostage** : un champ de fusion non résolu laissé visible, `OrgFor_Nom` entre guillemets.

**À générer.**

1. **Cinq familles de mise en page distinctes**, celles du tableau ci-dessus. La diversité de structure est la caractéristique première du type.
2. **Titre tiré indépendamment de la structure**, les quatre libellés observés et **une fraction sans aucun titre**, pour que le classifieur ne s'appuie pas dessus.
3. **En-tête de session obligatoire** : intitulé, numéro ou code stage, dates, horaires, lieu, organisme et numéro d'agrément.
4. **Bloc de signature du formateur** distinct de la grille, sur la grande majorité des documents.
5. **Grille découpée par créneau**, avec les découpages observés, jusqu'au cas médical à trois créneaux dont la soirée.
6. **Signatures manuscrites tirées**, avec cases vides et lignes barrées, jusqu'à des feuilles quasi vierges. Une minorité à signatures incrustées comme images, pour le cas du texte natif.
7. **Blocs facultatifs tirés indépendamment**, ceux de la liste ci-dessus.
8. **Annotations manuscrites d'absence**, accompagnées le cas échéant de la consigne Qualiopi correspondante.
9. **Trois chaînes de rendu**, texte natif, scan plus ou moins dégradé, photo au smartphone, avec rotations à 90° et documents multipages où l'en-tête se répète.
10. **Résidu de publipostage occasionnel.**

Le manifest enregistre la famille de mise en page, le titre employé ou son absence, le régime d'acquisition, l'angle, le nombre de pages, les blocs présents et le taux de remplissage de la grille.

## `OFF_STAG_EMP`, offre de stage, alternance, emploi

Annonce de recrutement publiée sur un site d'offres tiers puis imprimée en PDF depuis le navigateur : intitulé de poste rattaché à une entreprise, conditions du poste, missions et profil recherché, le tout encadré par la navigation et les mentions du site. Ce n'est jamais un document émis par l'entreprise elle-même, c'est la capture d'une page web tierce.

Le signal ne vient pas d'un habillage unique, chacun des dix sites source a le sien, mais de la **combinaison d'un habillage de capture web et du vocabulaire du recrutement**.

| Marqueur | Force |
|---|---|
| horodatage et titre de page en en-tête, URL répétée en pied de chaque page | fort, le plus universel : présent sur les vingt-quatre exemples |
| bouton d'appel à candidature, « Postuler », « Envoyer ma candidature », « Apply » | fort |
| bloc de métadonnées du poste : contrat, lieu, dates, rémunération, télétravail | fort |
| vocabulaire de recrutement : missions, profil recherché, compétences, tuteur, H/F | fort |
| bloc entreprise : nom, secteur, taille, logo | moyen |
| bloc « offres similaires » en fin de document | moyen, quasi systématique |
| navigation du site, fil d'ariane, bandeau de cookies | moyen, absent des captures en image pure |
| mentions RGPD, diversité, situation de handicap | faible |

**Ce qui tranche.** Aucun autre type du périmètre ne partage cette structure de capture web. Face à une page web quelconque imprimée en PDF, hors périmètre, c'est la coexistence du vocabulaire de recrutement et du bloc de métadonnées de contrat. Face à `CONT_APP_PRO` ou `CONVENTION`, l'offre est un appel à candidature avant embauche : aucune signature, aucune partie engagée, un simple bouton « Postuler ». Face à une fiche métier ou une fiche ROME générique, l'offre porte toujours une entreprise, un lieu et une date de début identifiés.

**Règle opérationnelle :** horodatage et URL de capture web, intitulé de poste rattaché à une entreprise, vocabulaire missions et profil recherché, répondre offre, **quel que soit le type de contrat affiché** : stage, alternance, apprentissage, professionnalisation, CDD et intérim figurent tous dans les exemples réels.

**Ce que montrent les 27 exemples réels**, dans `tmp/samples/Offres`. **Trois fichiers sont des doublons octet pour octet** de trois autres, chacun présent une fois sous son nom et une fois sous « - Copie », soit **24 offres distinctes**.

- **Dix plateformes sources, aucune dominante.** France Travail et JobTeaser quatre chacune, Welcome to the Jungle trois, 1jeune1solution, LinkedIn, Indeed, HelloWork et le portail Workday d'Airbus deux chacun, une plateforme de fiches ROME non identifiée deux, Jobs that makesense une.
- **Deux régimes d'acquisition très inégaux.** Vingt offres sont du texte natif, la couche PDF standard d'une impression navigateur. **Quatre, toutes de JobTeaser, sont une image intégrale sans couche texte** : zéro mot extrait alors que le rendu est parfaitement net. Ce n'est ni du texte natif ni un scan dégradé, c'est une **capture d'interface numérique propre**, opaque à l'extraction basique mais lisible sans effort par l'OCR.
- **Multipage systématique, de deux à neuf pages, et la majorité du contenu n'est pas l'offre.** Sur les plus longues, plus de la moitié du texte est du bruit de site : bandeau de cookies répété jusqu'à sept fois, articles de blog suggérés, postes similaires sans rapport. Le contenu utile tient sur une à deux pages.
- **Le type de contrat affiché est parfois trompeur.** Sur les quatre offres France Travail, trois sont typées « CDD » ou « Intérim » dans le bloc métadonnées alors que le corps révèle un contrat d'apprentissage ou de professionnalisation. Une règle qui ne lirait que le champ contrat manquerait ces trois cas.
- **Le bloc « offres similaires » introduit d'autres contrats et d'autres métiers**, principale source de vocabulaire parasite du type.
- **Un contact nominatif apparaît sur plusieurs plateformes** : gestionnaire RH nommé sur les offres CPAM et Kempchen, nom complet avec photo réelle sur LinkedIn, prénoms de collaborateurs avec photo sur Welcome to the Jungle, « Rencontrez Jean-Louis ». L'assainissement doit couvrir ce cas, pas seulement le contenu de l'offre.
- **Un cas de stage non rémunéré**, « Rémunération : Aucune », conforme à la réglementation en dessous du seuil de gratification.
- **Neuf habillages visuellement distincts**, sans tronc commun : fiche technique à droite (France Travail), formulaire de candidature intégré (HelloWork), deux colonnes avec compteur de vues et bandeau Premium (LinkedIn), fiche ROME avec simulateur d'aides (1jeune1solution), portail RH bilingue (Workday), cookies et suggestions éditoriales (Jobs that makesense), index d'égalité et portraits de collaborateurs (Welcome to the Jungle).

**À générer.**

1. **Plusieurs habillages de site distincts**, quatre à cinq templates couvrant les familles observées : sobre institutionnel, jobboard commercial avec formulaire intégré, réseau professionnel à deux colonnes, portail RH bilingue, site jeune et éditorialisé. Pas de tronc commun, c'est la variété qui porte le signal.
2. **En-tête et pied de page d'impression navigateur obligatoires** : date et heure, titre de page, URL complète répétée en pied.
3. **Bloc offre** : intitulé, entreprise avec secteur et taille, contrat, lieu, dates, rémunération ou son absence, bouton de candidature.
4. **Corps rédigé** : missions en liste à puces, profil recherché, présentation de l'entreprise.
5. **Type de contrat tiré librement**, y compris un désaccord entre le champ affiché et la nature réelle du poste.
6. **Bruit de fin de document obligatoire et volumineux**, bloc « offres similaires » mêlant d'autres contrats et d'autres métiers.
7. **Multipage de deux à neuf pages**, la majorité du volume portée par le bruit de site.
8. **Une minorité, environ un sur six**, rendue comme capture d'interface intégrale sans couche texte, nette et sans dégradation physique.
9. **Contact nominatif occasionnel**, recruteur ou collaborateur, parfois avec photo, pour couvrir le risque PII observé.
10. **Bandeau de cookies répété** sur au moins un template, et cas d'un stage sans rémunération.

Le manifest enregistre le template de site imité, le régime d'acquisition, le nombre de pages, le type de contrat affiché face au type réel, et la présence d'un contact nominatif.
