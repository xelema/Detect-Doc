"""Choix de la présentation d'un document, piloté par la seed.

Deux axes, qui ne se testent pas pareil et qu'il faut donc faire varier séparément.

La **structure** décide de ce que le classifieur voit : quels blocs existent, où ils sont
posés, comment les intitulés sont formulés. Si tous les documents d'un type partagent une
seule mise en page, un modèle apprend la mise en page et pas le type.

La **typographie** décide de ce que l'OCR voit : famille, corps, contraste, filets du
tableau. Une facture rouge et une facture bleue sont identiques pour un OCR ; du 7 points
condensé gris clair ne l'est pas du tout.

Chaque style est écrit tel quel dans le manifest. C'est ce qui permettra de dire "ce
moteur perd N points sur telle famille de police" au lieu d'une moyenne inexploitable.
"""

import random
from dataclasses import asdict, dataclass

# Familles réellement présentes sur des documents de gestion français. Carlito et Arimo
# ont les métriques de Calibri et d'Arial, Tinos celles de Times New Roman, Cousine
# celles de Courier New : ce sont les polices que produisent les suites bureautiques.
POLICES_SANS = ["Carlito", "Arimo", "Liberation Sans", "Noto Sans", "Nimbus Sans"]
POLICES_SERIF = ["Tinos", "Caladea", "C059", "Nimbus Roman", "URW Bookman"]
POLICES_MONO = ["Cousine", "Nimbus Mono PS", "DejaVu Sans Mono"]
POLICES_CONDENSEES = ["Liberation Sans Narrow", "Nimbus Sans Narrow"]

COULEURS = ["#1f4e79", "#256d5b", "#7a2f2f", "#4a3b6b", "#8a5a1f", "#2f5f7a", "#333333"]

# Une carte de visite ne se compose pas avec les polices d'une suite bureautique : elle
# sort d'un imprimeur en ligne, avec les familles à la mode du moment. C'est ce qui rend
# le type si varié typographiquement, et donc si inégal pour un OCR.
#
# Toute famille listée ici doit exister dans l'image Docker autant que sous WSL, sinon
# les deux environnements produisent deux corpus différents. Montserrat en a été retirée
# pour cette raison : elle est packagée sous Ubuntu mais pas sous Debian bookworm.
POLICES_CARTE = ["Lato", "Open Sans", "Inter", "Cabin",
                 "League Spartan", "Carlito", "Arimo"]
# Rondes et géométriques. Leurs chiffres se confondent volontiers, un 0 rond et un O
# étant presque identiques : cas utile pour éprouver la lecture des numéros de téléphone.
POLICES_CARTE_RONDES = ["Quicksand", "Comfortaa", "Dosis"]
POLICES_CARTE_SERIF = ["Roboto Slab", "Tinos", "Caladea", "C059", "URW Bookman"]

# Polices manuscrites, pour les cartes complétées à la main. Elles sont hostiles à l'OCR,
# ce qui est précisément le but : c'est le cas où le texte imprimé ne porte plus rien de
# l'identité.
#
# Toute police ajoutée ici doit couvrir les capitales accentuées. La plupart des polices
# manuscrites libres ne les dessinent pas, et un nom qui emprunte son É à une police de
# secours sort avec un glyphe d'un autre corps et d'une autre graisse, sans qu'aucune
# erreur ne soit levée. `verifier.py` contrôle cette couverture.
POLICES_MANUSCRITES = ["Kristi", "femkeklaver", "Breip", "Purisa"]

# Encres de stylo. Une annotation portée à la main sur un carton est bleue ou noire.
ENCRES_STYLO = ["#243a8c", "#1b2a6b", "#222222", "#2f2a55"]

# Toutes les familles que le générateur peut demander, quel que soit le type de document.
# Le vérificateur s'en sert pour contrôler qu'elles sont installées et qu'elles couvrent
# le français : la liste doit donc rester exhaustive.
POLICES_UTILISEES = sorted(set(
    POLICES_SANS + POLICES_SERIF + POLICES_MONO + POLICES_CONDENSEES
    + POLICES_CARTE + POLICES_CARTE_RONDES + POLICES_CARTE_SERIF + POLICES_MANUSCRITES
))

# Marques portées par une pièce réelle. Le filigrane en diagonale et la mosaïque sont
# imprimés par le logiciel émetteur ; le tampon est apposé après coup, à l'encre, ce qui
# lui donne une couleur qui tranche et une opacité bien plus forte.
FILIGRANES_IMPRIMES = ["DUPLICATA", "COPIE", "ORIGINAL"]
ENCRES_TAMPON = ["#b02020", "#1c3f8f", "#1f6b3a"]

# Les deux types ne reçoivent pas les mêmes cachets, et c'est un signal à part entière :
# une facture se tamponne une fois payée, un devis une fois accepté. Mélanger les deux
# listes apprendrait au classifieur une association fausse.
TAMPONS_FACTURE = ["PAYÉE", "ACQUITTÉE", "RÉGLÉE"]
TAMPONS_DEVIS = ["BON POUR ACCORD", "ACCEPTÉ", "SIGNÉ"]


def _taille_filigrane(texte: str) -> int:
    """Corps à donner à un filigrane en diagonale pour qu'il traverse la page sans être
    coupé par les bords.

    Un mot court doit être écrit bien plus gros qu'un mot long pour couvrir la même
    largeur, d'où le calcul plutôt qu'une valeur fixe.

    La largeur visée tient compte de l'inclinaison : un texte incliné de 32 degrés occupe
    en largeur cos(32) fois sa longueur, soit environ 85 %. Viser la diagonale de la page
    ferait donc déborder le mot des deux côtés. Ce n'est pas qu'une question d'aspect :
    un filigrane tronqué rend faux ce que le manifest déclare sur le document.
    """
    largeur_visee_pt = 620
    largeur_moyenne_caractere = 0.62
    return min(int(largeur_visee_pt / (len(texte) * largeur_moyenne_caractere)), 200)


def _marque(tirage: random.Random, probabilite: float, types: list[str],
            devis: bool = False) -> dict:
    """Tire une marque et retourne les champs correspondants du style.

    Retourner un fragment de style plutôt qu'un tuple évite de recopier les quatre champs
    dans chacune des fabriques : un cinquième champ de marque n'imposerait alors aucune
    édition ailleurs.
    """
    aucune = {"filigrane": "", "filigrane_type": "", "filigrane_taille_pt": 0,
              "filigrane_encre": ""}
    if tirage.random() >= probabilite:
        return aucune

    type_marque = tirage.choice(types)
    if type_marque == "tampon":
        return {**aucune,
                "filigrane": tirage.choice(TAMPONS_DEVIS if devis else TAMPONS_FACTURE),
                "filigrane_type": type_marque,
                "filigrane_encre": tirage.choice(ENCRES_TAMPON)}

    texte = tirage.choice(FILIGRANES_IMPRIMES)
    # Seule la diagonale écrit le mot en un seul bloc dont la taille doit être calculée.
    # La mosaïque répète le mot à taille fixe : lui annoncer un corps qu'elle n'applique
    # pas ferait mentir le manifest.
    taille = _taille_filigrane(texte) if type_marque == "diagonal" else 0
    return {**aucune, "filigrane": texte, "filigrane_type": type_marque,
            "filigrane_taille_pt": taille}


@dataclass
class Style:
    """Présentation d'un document. Sérialisé tel quel dans le manifest."""

    template: str
    police: str
    police_mono: str
    corps_pt: float
    couleur: str
    tableau: str
    avec_logo: bool
    avec_rib: bool
    avec_stagiaires: bool
    avec_reference: bool
    # Cartouche « Bon pour accord », date et signature du client. Réservé au devis :
    # c'est son marqueur visuel le plus net, et le seul bloc qu'une facture n'a jamais.
    # Les devis qui en sont dépourvus forment volontairement le cas le plus difficile.
    avec_acceptation: bool
    filigrane: str
    filigrane_type: str
    filigrane_taille_pt: int
    filigrane_encre: str

    def as_dict(self) -> dict:
        return asdict(self)


def _piece_moderne(tirage: random.Random, devis: bool) -> Style:
    """Mise en page des logiciels de facturation récents : logo, couleur, tableau dense."""
    return Style(
        template="piece_01.html",
        police=tirage.choice(POLICES_SANS),
        police_mono="DejaVu Sans Mono",
        corps_pt=tirage.choice([8.5, 9.0, 9.5]),
        couleur=tirage.choice(COULEURS),
        tableau=tirage.choice(["filets", "horizontaux", "bandes"]),
        avec_logo=tirage.random() < 0.85,
        avec_rib=tirage.random() < 0.8,
        avec_stagiaires=tirage.random() < 0.7,
        avec_reference=tirage.random() < 0.8,
        avec_acceptation=devis and tirage.random() < 0.8,
        **_marque(tirage, 0.25, ["diagonal", "tampon"], devis),
    )


def _piece_classique(tirage: random.Random, devis: bool) -> Style:
    """Pièce de cabinet : serif, aucun logo, aucune couleur, montant en lettres.

    C'est la forme la plus difficile pour un classifieur, parce qu'elle ressemble à une
    lettre commerciale plus qu'à une sortie de logiciel.
    """
    return Style(
        template="piece_02.html",
        police=tirage.choice(POLICES_SERIF),
        police_mono="Nimbus Mono PS",
        corps_pt=tirage.choice([9.5, 10.0, 10.5]),
        couleur="",
        tableau="horizontaux",
        avec_logo=False,
        avec_rib=tirage.random() < 0.6,
        avec_stagiaires=tirage.random() < 0.4,
        avec_reference=tirage.random() < 0.5,
        # Un devis de cabinet part par courrier et revient signé : le cartouche y est
        # la règle plutôt que l'exception.
        avec_acceptation=devis and tirage.random() < 0.9,
        # Une pièce de cabinet est imprimée puis manipulée : elle porte un tampon à
        # l'encre plutôt qu'un filigrane, que seul un logiciel émetteur sait produire.
        **_marque(tirage, 0.25, ["tampon"], devis),
    )


def _piece_listing(tirage: random.Random, devis: bool) -> Style:
    """Sortie d'un logiciel de gestion ancien : chasse fixe, majuscules, filets en tirets.

    Beaucoup d'ERP éditent encore comme ça. Le rendu est pauvre mais très régulier, ce
    qui en fait un cas intéressant : facile pour l'OCR, trompeur pour le classifieur.
    """
    return Style(
        template="piece_03.html",
        police=tirage.choice(POLICES_MONO),
        police_mono=tirage.choice(POLICES_MONO),
        corps_pt=tirage.choice([7.5, 8.0, 8.5]),
        couleur="",
        tableau="aucun",
        avec_logo=False,
        avec_rib=tirage.random() < 0.5,
        avec_stagiaires=tirage.random() < 0.5,
        # Un progiciel de gestion imprime toujours sa référence de dossier.
        avec_reference=True,
        avec_acceptation=devis and tirage.random() < 0.7,
        **_marque(tirage, 0.2, ["tampon", "diagonal"], devis),
    )


def _piece_dense(tirage: random.Random, devis: bool) -> Style:
    """Pièce d'un service en ligne : bandeau coloré, condensé, petit corps, filigrane.

    Le cas le plus hostile à l'OCR du lot : corps réduit, police condensée, texte clair
    sur fond teinté, et parfois un filigrane par-dessus le contenu.
    """
    return Style(
        template="piece_04.html",
        police=tirage.choice(POLICES_CONDENSEES + ["Carlito"]),
        police_mono="Cousine",
        corps_pt=tirage.choice([7.0, 7.5, 8.0]),
        couleur=tirage.choice(COULEURS),
        tableau=tirage.choice(["aucun", "bandes"]),
        avec_logo=tirage.random() < 0.5,
        avec_rib=tirage.random() < 0.7,
        avec_stagiaires=tirage.random() < 0.5,
        avec_reference=tirage.random() < 0.6,
        # Un service en ligne fait accepter son devis d'un clic : le cartouche à signer
        # y est bien plus rare qu'ailleurs.
        avec_acceptation=devis and tirage.random() < 0.4,
        **_marque(tirage, 0.4, ["diagonal", "repete", "tampon"], devis),
    )


# Les quatre mises en page servent les deux types. Un organisme édite son devis et sa
# facture avec le même logiciel, donc sur le même papier à en-tête : leur donner des
# templates distincts offrirait au classifieur un raccourci visuel qui n'existe pas en
# production, et masquerait la seule difficulté qui compte, celle du texte.
FABRIQUES_PIECE = [
    _piece_moderne,
    _piece_classique,
    _piece_listing,
    _piece_dense,
]


def _style_piece(seed: int, tirage: random.Random, devis: bool) -> Style:
    """Style d'une facture ou d'un devis.

    Le template tourne sur la seed plutôt que d'être tiré au hasard : sur un corpus de
    taille modeste, un tirage aléatoire produirait des templates sous-représentés, et
    donc des mesures moins fiables là où on en a le plus besoin.
    """
    return FABRIQUES_PIECE[seed % len(FABRIQUES_PIECE)](tirage, devis)


def style_facture(seed: int) -> Style:
    return _style_piece(seed, random.Random(seed), devis=False)


def style_devis(seed: int) -> Style:
    """Style d'un devis.

    Le tirage passe par un flux nommé, sinon le devis n° 1 et la facture n° 1 sortiraient
    non seulement sur le même template, ce qui est voulu, mais aussi avec la même police,
    le même corps et la même couleur, ce qui réduirait la variété du corpus de moitié.
    """
    return _style_piece(seed, random.Random(f"devis-{seed}"), devis=True)


@dataclass
class StyleCarte:
    """Présentation d'une carte de visite. Sérialisée telle quelle dans le manifest.

    Style à part plutôt que le `Style` des pièces comptables : une carte n'a ni tableau,
    ni filigrane, ni bloc de stagiaires, et porte au contraire des axes qui n'existent
    nulle part ailleurs. Lui faire traîner une dizaine de champs sans objet obligerait à
    les lire pour comprendre qu'ils ne servent à rien.

    Les axes sont ceux relevés sur les cartes réelles : régime d'acquisition, orientation
    du carton, angle dans le cadre, blocs présents, verso, complétion manuscrite. Les
    écrire un par un permettra au rapport de dire sur lequel le classifieur décroche, au
    lieu de livrer une exactitude moyenne sur le type.
    """

    template: str
    police: str
    police_manuscrite: str
    corps_pt: float
    couleur: str
    # Le carton lui-même : sens de lecture, puis la façon dont il a été numérisé.
    orientation: str
    acquisition: str
    # Teinte du support : blanc pour une feuille scannée, plan de travail pour une photo.
    fond_scene: str
    angle_deg: int
    position: str
    avec_logo: bool
    avec_photo: bool
    avec_qr: bool
    avec_fonction: bool
    avec_accroche: bool
    avec_adresse: bool
    avec_fixe: bool
    avec_site: bool
    avec_reseau: bool
    avec_certification: bool
    # Identité écrite au stylo sur un carton qui ne la porte pas imprimée.
    manuscrite: bool
    encre: str
    verso: str

    def as_dict(self) -> dict:
        return asdict(self)


# Emplacement du carton sur la feuille. Les valeurs restent à plus de 20 mm des bords,
# de quoi absorber le quart de tour : une carte de 85 sur 54 pivotée autour de son centre
# déborde de 15,5 mm de sa boîte, et sortirait sinon de la page.
POSITIONS = ["haut-gauche", "haut-droite", "centre", "bas-gauche", "bas-droite"]

# Plans de travail sur lesquels le carton est photographié : bureau clair, sous-main,
# bois, plateau sombre. La teinte compte plus qu'il n'y paraît, c'est elle qui donne ou
# non un contraste au bord de la carte, et donc qui décide si un détourage automatique
# retrouve le carton dans l'image.
FONDS_SCENE = ["#dcdcdf", "#e6e4e0", "#c9c4bb", "#b9a98f", "#8f9296"]


def _acquisition(tirage: random.Random) -> dict:
    """Comment le carton a été repris, et comment il se présente dans le cadre.

    Une carte de visite arrive presque toujours en gestion documentaire comme la reprise
    d'un objet physique, scannée à plat sur une feuille restée vide à 90 %, ou
    photographiée posée sur un bureau. Les deux régimes donnent des images très
    différentes à un OCR, d'où le tirage explicite plutôt qu'un rendu unique qu'on
    dégraderait ensuite.

    Le troisième mode, `fichier`, est la minorité qui compte quand même : un export d'un
    outil de mise en page, déposé tel quel. La page fait alors la taille du carton, sans
    support ni angle, et le document reste à texte natif. Il est rare, mais un corpus qui
    n'en contiendrait aucun ne dirait rien de ce cas, et personne ne saurait si le service
    le traite correctement.

    Trois cartes sur dix reçoivent un quart de tour. C'est la proportion observée sur les
    exemples fournis : un carton paysage se photographie volontiers en portrait, et se
    pose en travers sur la vitre d'un scanner. Un OCR qui ne redresse pas la page n'en
    lit alors rien, ce qui est exactement ce que le corpus doit pouvoir révéler.
    """
    quart_de_tour = tirage.random() < 0.3
    manuscrite = tirage.random() < 0.15
    acquisition = tirage.choices(["photo", "scan", "fichier"], weights=[50, 35, 15])[0]
    physique = acquisition != "fichier"
    return {
        "orientation": "portrait" if tirage.random() < 0.15 else "paysage",
        "acquisition": acquisition,
        # Une feuille passée au scanner est blanche ; un carton photographié repose sur
        # quelque chose, et ce quelque chose est rarement blanc.
        "fond_scene": tirage.choice(FONDS_SCENE) if acquisition == "photo" else "#ffffff",
        # Un fichier exporté est droit et cadré sur la carte : il n'a ni support, ni
        # angle, ni place sur une feuille.
        "angle_deg": (tirage.choice([90, 270]) if quart_de_tour else tirage.randint(-6, 6))
                     if physique else 0,
        "position": tirage.choice(POSITIONS),
        "manuscrite": manuscrite,
        "encre": tirage.choice(ENCRES_STYLO) if manuscrite else "",
        # Le verso se scanne en seconde page. Quand il existe, il ne porte jamais le nom
        # de la personne : c'est une page de document dont le type se lit sur l'autre.
        "verso": tirage.choices(["aucun", "logo", "publicite"], weights=[72, 18, 10])[0],
        "police_manuscrite": tirage.choice(POLICES_MANUSCRITES),
    }


def _carte_moderne(tirage: random.Random) -> StyleCarte:
    """Carte d'imprimeur en ligne : logo en tête, couleur d'accent, coordonnées alignées.

    La forme la plus courante, et la plus riche en texte du lot.
    """
    return StyleCarte(
        template="carte_01.html",
        police=tirage.choice(POLICES_CARTE),
        corps_pt=tirage.choice([6.5, 7.0, 7.5]),
        couleur=tirage.choice(COULEURS),
        avec_logo=True,
        avec_photo=False,
        avec_qr=tirage.random() < 0.5,
        avec_fonction=True,
        avec_accroche=tirage.random() < 0.4,
        avec_adresse=tirage.random() < 0.6,
        avec_fixe=tirage.random() < 0.5,
        avec_site=tirage.random() < 0.75,
        avec_reseau=tirage.random() < 0.25,
        avec_certification=tirage.random() < 0.3,
        **_acquisition(tirage),
    )


def _carte_sobre(tirage: random.Random) -> StyleCarte:
    """Carte minimaliste : noir sur blanc, beaucoup de vide, quelques mots.

    Le cas limite du corpus. Une fois océrisée, elle peut ne rendre que huit mots, dont
    aucun n'appartient au vocabulaire d'un type documentaire. C'est là que le score de
    confiance doit s'effondrer, et c'est donc là que se mesure sa calibration.
    """
    return StyleCarte(
        template="carte_02.html",
        police=tirage.choice(POLICES_CARTE + POLICES_CARTE_SERIF),
        corps_pt=tirage.choice([7.0, 7.5, 8.0]),
        # Sans aplat ni accent : la couleur ne sert qu'au logo, quand il y en a un.
        couleur="#222222",
        avec_logo=tirage.random() < 0.55,
        avec_photo=False,
        avec_qr=tirage.random() < 0.15,
        avec_fonction=tirage.random() < 0.6,
        avec_accroche=False,
        avec_adresse=tirage.random() < 0.2,
        avec_fixe=tirage.random() < 0.15,
        avec_site=tirage.random() < 0.35,
        avec_reseau=False,
        avec_certification=False,
        **_acquisition(tirage),
    )


def _carte_aplat(tirage: random.Random) -> StyleCarte:
    """Carte imprimée en fond perdu : couleur pleine, texte en réserve blanche.

    Le contraste inversé est le cas hostile de ce type. Beaucoup de binarisations d'OCR
    supposent du texte sombre sur fond clair et rendent une page vide sur celui-ci.
    """
    return StyleCarte(
        template="carte_03.html",
        police=tirage.choice(POLICES_CARTE + POLICES_CARTE_RONDES),
        corps_pt=tirage.choice([6.5, 7.0, 7.5]),
        couleur=tirage.choice(COULEURS),
        avec_logo=tirage.random() < 0.7,
        avec_photo=tirage.random() < 0.5,
        avec_qr=tirage.random() < 0.55,
        avec_fonction=True,
        avec_accroche=tirage.random() < 0.5,
        avec_adresse=tirage.random() < 0.45,
        avec_fixe=tirage.random() < 0.3,
        avec_site=tirage.random() < 0.7,
        avec_reseau=tirage.random() < 0.35,
        avec_certification=tirage.random() < 0.25,
        **_acquisition(tirage),
    )


FABRIQUES_CARTE = [_carte_moderne, _carte_sobre, _carte_aplat]


def style_carte_visite(seed: int) -> StyleCarte:
    """Style d'une carte de visite.

    Le template tourne sur la seed, comme pour les pièces comptables : sur un corpus de
    taille modeste, un tirage aléatoire laisserait une des trois mises en page
    sous-représentée, et les mesures la concernant sans valeur.
    """
    return FABRIQUES_CARTE[seed % len(FABRIQUES_CARTE)](random.Random(f"carte-{seed}"))


@dataclass
class StyleEmargement:
    """Structure et présentation d'une feuille d'émargement."""

    template: str
    famille: str
    police: str
    police_manuscrite: str
    corps_pt: float
    couleur: str
    orientation: str
    acquisition: str
    rotation_page_deg: int
    nombre_pages: int
    lignes_par_page: int
    nombre_creneaux: int
    mensuel: bool
    titre: str | None
    avec_bloc_formateur: bool
    avec_entreprise: bool
    avec_fonction: bool
    avec_matricule: bool
    avec_identifiant_metier: bool
    avec_document_pedagogique: bool
    avec_suivi_heures: bool
    avec_clause_attestation: bool
    avec_cachet: bool
    avec_opco: bool
    avec_consignes_qualiopi: bool
    residu_publipostage: bool

    def as_dict(self) -> dict:
        return asdict(self)


TITRES_EMARGEMENT = [
    "Feuille d'émargement",
    "Feuille de présence",
    "Feuille collective d'émargement",
    "Émargements par demi-journée ou soirée",
]

# La liste tourne sur dix documents. Deux feuilles de la même famille ne reçoivent pas
# automatiquement le même mode d'acquisition, ce qui évite de confondre structure et
# régime dans les mesures.
ACQUISITIONS_EMARGEMENT = [
    "scan", "photo", "scan", "fichier", "scan",
    "fichier", "scan", "photo", "scan", "scan",
]

ROTATIONS_EMARGEMENT = [0, 0, 90, 0, 0, 0, 0, 270, 90, 0]

# Les cinq entrées décrivent cinq structures de grille, pas cinq habillages d'un même
# tableau. Le choix tourne sur la seed pour que chacune reste représentée.
FAMILLES_EMARGEMENT = [
    {
        "template": "emargement_01.html",
        "famille": "seance-unique",
        "orientation": "portrait",
        "lignes_par_page": 8,
        "nombre_creneaux": 2,
        "mensuel": False,
    },
    {
        "template": "emargement_02.html",
        "famille": "multi-jours",
        "orientation": "paysage",
        "lignes_par_page": 7,
        "nombre_creneaux": 6,
        "mensuel": False,
    },
    {
        "template": "emargement_03.html",
        "famille": "recapitulatif-mensuel",
        "orientation": "paysage",
        "lignes_par_page": 4,
        "nombre_creneaux": 10,
        "mensuel": True,
    },
    {
        "template": "emargement_04.html",
        "famille": "feuille-collective",
        "orientation": "paysage",
        "lignes_par_page": 8,
        "nombre_creneaux": 4,
        "mensuel": False,
    },
    {
        "template": "emargement_05.html",
        "famille": "officiel-sectoriel",
        "orientation": "portrait",
        "lignes_par_page": 6,
        "nombre_creneaux": 3,
        "mensuel": False,
    },
]


def style_feuille_emargement(seed: int) -> StyleEmargement:
    """Style d'une feuille d'émargement, sur les axes observés dans les exemples."""
    tirage = random.Random(f"feuille-emargement-{seed}")
    famille = FAMILLES_EMARGEMENT[(seed - 1) % len(FAMILLES_EMARGEMENT)]
    acquisition_index = (seed - 1) % len(ACQUISITIONS_EMARGEMENT)
    acquisition = ACQUISITIONS_EMARGEMENT[acquisition_index]

    if seed % 9 == 0:
        nombre_pages = 3
    elif seed % 4 == 0:
        nombre_pages = 2
    else:
        nombre_pages = 1

    titre = None if seed % 7 == 0 else tirage.choice(TITRES_EMARGEMENT)
    officiel = famille["famille"] == "officiel-sectoriel"
    mensuel = famille["mensuel"]

    return StyleEmargement(
        **famille,
        police=tirage.choice(POLICES_SANS + POLICES_CONDENSEES),
        police_manuscrite=tirage.choice(POLICES_MANUSCRITES),
        corps_pt=tirage.choice([7.0, 7.5, 8.0, 8.5]),
        couleur=tirage.choice(COULEURS),
        acquisition=acquisition,
        rotation_page_deg=(
            0 if acquisition == "fichier"
            else ROTATIONS_EMARGEMENT[acquisition_index]
        ),
        nombre_pages=nombre_pages,
        titre=titre,
        # Le formulaire sectoriel consacre une table entière à l'intervenant : sa
        # structure ne tient plus sans elle, et un tel formulaire ne circule jamais sans
        # le nom de celui qui a animé la session. Les autres familles se passent du bloc
        # de temps en temps, comme les feuilles où le formateur signe à part. Le pas est
        # premier avec celui des familles, sans quoi l'absence retomberait toujours sur la
        # même structure et l'axe ne serait jamais exercé.
        avec_bloc_formateur=officiel or seed % 6 != 0,
        avec_entreprise=officiel or tirage.random() < 0.55,
        avec_fonction=tirage.random() < 0.45,
        avec_matricule=mensuel or tirage.random() < 0.35,
        avec_identifiant_metier=officiel,
        avec_document_pedagogique=officiel or tirage.random() < 0.2,
        avec_suivi_heures=mensuel or tirage.random() < 0.2,
        avec_clause_attestation=officiel or tirage.random() < 0.25,
        avec_cachet=tirage.random() < 0.35,
        avec_opco=tirage.random() < 0.45,
        avec_consignes_qualiopi=tirage.random() < 0.6,
        residu_publipostage=seed % 13 == 0,
    )


def style_rib(seed: int) -> Style:
    """Style d'un RIB.

    Volontairement peu variable : dans la réalité, les RIB se ressemblent presque tous,
    leur forme étant quasi normalisée. Inventer six présentations rendrait le corpus
    moins représentatif, pas plus.
    """
    tirage = random.Random(seed)
    return Style(
        template="rib_01.html",
        police=tirage.choice(POLICES_SANS),
        police_mono="DejaVu Sans Mono",
        corps_pt=tirage.choice([9.0, 9.5, 10.0]),
        couleur=tirage.choice(COULEURS),
        tableau="filets",
        avec_logo=True,
        avec_rib=True,
        avec_stagiaires=False,
        avec_reference=False,
        avec_acceptation=False,
        # Un relevé d'identité bancaire n'est ni tamponné ni filigrané.
        **_marque(tirage, 0, []),
    )
