# Types documentaires

Fiches des types que le service doit distinguer. Périmètre : les 14 types des priorités 1 et 2 de la liste métier. Chaque fiche donne une définition, les marqueurs qui portent le signal de classification, les règles de désambiguïsation avec les voisins, ce que montrent les exemples réels, et la spécification de génération correspondante.

Les fiches se recopient dans le pré-prompt du classifieur : elles sont écrites pour être lues par un modèle autant que par un humain.

| Code | Libellé | Fiche |
|---|---|---|
| `CARTE_VISITE` | Carte de visite | [écrite](#carte_visite-carte-de-visite) |
| `EVAL_FORM` | Évaluation de la formation | à écrire |
| `FEUILLE_EMAR` | Feuille d'émargement | à écrire |
| `SEPA` | Mandat SEPA | à écrire |
| `OFF_STAG_EMP` | Offre de stage, alternance, emploi | à écrire |
| `RIB` | Relevé d'identité bancaire | à écrire |
| `RIB_SEPA` | Relevé d'identité bancaire et mandat SEPA | à écrire |
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

**Deux régimes d'acquisition, aucun n'est bureautique.** Il n'existe pas de carte de visite « native » : le document déposé en GED est toujours une reprise photographique d'un objet physique.

| Régime | Nombre | Caractéristiques |
|---|---|---|
| Photo au smartphone | 8, JPEG 2288x4080 ou 4080x2288 | carte posée sur un bureau clair, occupant de 15 % à 60 % du cadre, éclairage inégal avec reflets, modèle de l'appareil et horodatage incrustés par le téléphone dans deux coins |
| Scan à plat A4 | 6, PDF A4 portrait sans couche texte | carte dans un coin d'une page vide à 85 % ou plus, bord de couvercle visible en bande noire, légère rotation de scanner |

**Orientation.** Quatre des quatorze cartes sont **à 90°** dans leur cadre : une carte paysage photographiée en portrait, ou posée dans le sens de la largeur sur la vitre du scanner. Les autres présentent des rotations de quelques degrés. Aucune n'est parfaitement droite.

**Multipage.** Deux des six PDF ont **deux pages**, recto et verso scannés séparément. Sur l'un, la page qui porte le nom de la personne est la seconde ; la première ne contient que le logo, un QR code et une accroche. Sur l'autre, **le verso n'est pas une carte de visite** mais une publicité de l'entreprise, avec photos et slogan.

**Manuscrit, sur 2 exemplaires des 14.** Un scan porte un nom et un numéro ajoutés au stylo bleu par-dessus la carte imprimée. Sur une photo, la carte ne porte **que le logo de l'entreprise**, et le nom, l'e-mail et le téléphone sont entièrement manuscrits : tout ce qui identifie la personne échappe à un OCR de texte imprimé.

**Amplitude de contenu, d'un facteur cinq.** La plus pauvre porte un logo, un prénom et nom en minuscules, un e-mail et un mobile, soit huit mots. La plus riche porte deux établissements avec leurs adresses, deux fixes, deux URL, une accroche, deux logos de certification et la mention de l'organisme certificateur. Entre les deux : avec ou sans photo de la personne, avec ou sans adresse postale, avec ou sans identifiant de réseau social, avec ou sans logo.

**Aucune mise en page n'est partagée.** Sur quatorze cartes, quatorze compositions : texte noir sur fond blanc, texte blanc sur aplat de couleur pleine page, bandeau vertical coloré, logo centré en haut, logo en filigrane géant, colonne unique, deux colonnes séparées d'un filet. Deux cartes sont au format portrait au lieu du paysage habituel. La variété de gabarits n'est donc pas un raffinement du générateur, c'est la caractéristique première du type.

**Vocabulaire du secteur.** Plusieurs cartes viennent d'organismes de formation et portent `Qualiopi`, `NF Service Formation continue`, ou des accroches en anglais sur l'apprentissage. Le vocabulaire de la formation apparaît donc sur des cartes de visite, ce qui interdit de s'appuyer dessus pour classer.

### Spécification de génération

Ce que le générateur doit produire pour que le type soit représentatif. Chaque axe est tiré des observations ci-dessus.

1. **Ne jamais produire une carte cadrée seule.** Le document final est une page A4 très majoritairement vide avec la carte dans un coin, ou une photo où la carte occupe une fraction du cadre sur un fond de bureau.
2. **Deux chaînes de rendu distinctes**, scan à plat et photo au smartphone, dans une proportion voisine de celle observée, soit un peu plus de photos que de scans.
3. **Rotation obligatoire**, incluant les multiples de 90° et pas seulement de petits angles.
4. **Incrustations de l'appareil photo** sur la variante photo : modèle et horodatage dans les coins, en blanc.
5. **Gabarits nombreux et sans tronc commun**, portrait comme paysage, avec et sans aplat de couleur, texte clair sur fond sombre inclus.
6. **Blocs optionnels tirés indépendamment** : photo de la personne, QR code, adresse postale, second numéro, URL, identifiant de réseau social, logo de certification, accroche. C'est leur combinatoire qui produit l'amplitude observée.
7. **Variante recto verso sur deux pages**, dont un cas où la page nominative est la seconde et un cas où le verso est une publicité sans nom.
8. **Variante manuscrite**, jusqu'au cas extrême de la carte vierge de tout sauf le logo, complétée à la main.
9. **Longueur de texte tirée entre 8 et 60 mots**, et non centrée sur une valeur moyenne.

Le manifest enregistre pour chaque document le gabarit, le régime d'acquisition, l'angle, les blocs présents et le nombre de mots, afin que le rapport puisse dire sur quel axe le classifieur décroche.
