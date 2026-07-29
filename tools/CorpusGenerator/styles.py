"""Choix de la presentation d'un document, pilote par la seed.

Deux axes, qui ne se testent pas pareil et qu'il faut donc faire varier separement.

La **structure** decide de ce que le classifieur voit : quels blocs existent, ou ils sont
poses, comment les intitules sont formules. Si tous les documents d'un type partagent une
seule mise en page, un modele apprend la mise en page et pas le type.

La **typographie** decide de ce que l'OCR voit : famille, corps, contraste, filets du
tableau. Une facture rouge et une facture bleue sont identiques pour un OCR ; du 7 points
condense gris clair ne l'est pas du tout.

Chaque style est ecrit tel quel dans le manifest. C'est ce qui permettra de dire "ce
moteur perd N points sur telle famille de police" au lieu d'une moyenne inexploitable.
"""

import random
from dataclasses import asdict, dataclass

# Familles reellement presentes sur des documents de gestion francais. Carlito et Arimo
# ont les metriques de Calibri et d'Arial, Tinos celles de Times New Roman, Cousine
# celles de Courier New : ce sont les polices que produisent les suites bureautiques.
POLICES_SANS = ["Carlito", "Arimo", "Liberation Sans", "Noto Sans", "Nimbus Sans"]
POLICES_SERIF = ["Tinos", "Caladea", "C059", "Nimbus Roman", "URW Bookman"]
POLICES_MONO = ["Cousine", "Nimbus Mono PS", "DejaVu Sans Mono"]
POLICES_CONDENSEES = ["Liberation Sans Narrow", "Nimbus Sans Narrow"]

COULEURS = ["#1f4e79", "#256d5b", "#7a2f2f", "#4a3b6b", "#8a5a1f", "#2f5f7a", "#333333"]

STYLES_TABLEAU = ["filets", "horizontaux", "aucun", "bandes"]

# Marques portees par une facture reelle. Le filigrane en diagonale et la mosaique sont
# imprimes par le logiciel emetteur ; le tampon est appose apres coup, a l'encre, ce qui
# lui donne une couleur qui tranche et une opacite bien plus forte.
FILIGRANES_IMPRIMES = ["DUPLICATA", "COPIE", "ORIGINAL"]
TAMPONS = ["PAYÉE", "ACQUITTÉE", "RÉGLÉE", "BON POUR ACCORD"]
ENCRES_TAMPON = ["#b02020", "#1c3f8f", "#1f6b3a"]


def _taille_filigrane(texte: str) -> int:
    """Corps a donner a un filigrane en diagonale pour qu'il traverse la page sans etre
    coupe par les bords.

    Un mot court doit etre ecrit bien plus gros qu'un mot long pour couvrir la meme
    largeur, d'ou le calcul plutot qu'une valeur fixe.

    La largeur visee tient compte de l'inclinaison : un texte incline de 32 degres occupe
    en largeur cos(32) fois sa longueur, soit environ 85 %. Viser la diagonale de la page
    ferait donc deborder le mot des deux cotes. Ce n'est pas qu'une question d'aspect :
    un filigrane tronque rend faux ce que le manifest declare sur le document.
    """
    largeur_visee_pt = 620
    largeur_moyenne_caractere = 0.62
    return min(int(largeur_visee_pt / (len(texte) * largeur_moyenne_caractere)), 200)


def _marque(tirage: random.Random, probabilite: float,
            types: list[str]) -> tuple[str, str, int, str]:
    """Tire une marque : texte, type, corps et encre. Chaine vide si aucune marque."""
    if tirage.random() >= probabilite:
        return "", "", 0, ""

    type_marque = tirage.choice(types)
    if type_marque == "tampon":
        texte = tirage.choice(TAMPONS)
        return texte, type_marque, 0, tirage.choice(ENCRES_TAMPON)

    texte = tirage.choice(FILIGRANES_IMPRIMES)
    return texte, type_marque, _taille_filigrane(texte), ""


@dataclass
class Style:
    """Presentation d'un document. Serialise tel quel dans le manifest."""

    template: str
    police: str
    police_mono: str
    corps_pt: float
    couleur: str
    tableau: str
    avec_logo: bool
    avec_rib: bool
    avec_stagiaires: bool
    avec_convention: bool
    avec_montant_lettres: bool
    filigrane: str
    filigrane_type: str
    filigrane_taille_pt: int
    filigrane_encre: str

    def as_dict(self) -> dict:
        return asdict(self)


def _facture_moderne(tirage: random.Random) -> Style:
    """Mise en page des logiciels de facturation recents : logo, couleur, tableau dense."""
    filigrane, type_filigrane, taille, encre = _marque(
        tirage, 0.25, ["diagonal", "tampon"])
    return Style(
        template="facture_01.html",
        police=tirage.choice(POLICES_SANS),
        police_mono="DejaVu Sans Mono",
        corps_pt=tirage.choice([8.5, 9.0, 9.5]),
        couleur=tirage.choice(COULEURS),
        tableau=tirage.choice(["filets", "horizontaux", "bandes"]),
        avec_logo=tirage.random() < 0.85,
        avec_rib=tirage.random() < 0.8,
        avec_stagiaires=tirage.random() < 0.7,
        avec_convention=tirage.random() < 0.8,
        avec_montant_lettres=False,
        filigrane=filigrane,
        filigrane_type=type_filigrane,
        filigrane_taille_pt=taille,
        filigrane_encre=encre,
    )


def _facture_classique(tirage: random.Random) -> Style:
    """Facture de cabinet : serif, aucun logo, aucune couleur, montant en lettres.

    C'est la forme la plus difficile pour un classifieur, parce qu'elle ressemble a une
    lettre commerciale plus qu'a une facture de logiciel.
    """
    # Une facture de cabinet est imprimee puis manipulee : elle porte un tampon a l'encre
    # plutot qu'un filigrane, que seul un logiciel emetteur sait produire.
    filigrane, type_filigrane, taille, encre = _marque(tirage, 0.25, ["tampon"])
    return Style(
        template="facture_02.html",
        police=tirage.choice(POLICES_SERIF),
        police_mono="Nimbus Mono PS",
        corps_pt=tirage.choice([9.5, 10.0, 10.5]),
        couleur="",
        tableau="horizontaux",
        avec_logo=False,
        avec_rib=tirage.random() < 0.6,
        avec_stagiaires=tirage.random() < 0.4,
        avec_convention=tirage.random() < 0.5,
        avec_montant_lettres=True,
        filigrane=filigrane,
        filigrane_type=type_filigrane,
        filigrane_taille_pt=taille,
        filigrane_encre=encre,
    )


def _facture_listing(tirage: random.Random) -> Style:
    """Sortie d'un logiciel de gestion ancien : chasse fixe, majuscules, filets en tirets.

    Beaucoup d'ERP editent encore comme ca. Le rendu est pauvre mais tres regulier, ce
    qui en fait un cas interessant : facile pour l'OCR, trompeur pour le classifieur.
    """
    filigrane, type_filigrane, taille, encre = _marque(
        tirage, 0.2, ["tampon", "diagonal"])
    return Style(
        template="facture_03.html",
        police=tirage.choice(POLICES_MONO),
        police_mono=tirage.choice(POLICES_MONO),
        corps_pt=tirage.choice([7.5, 8.0, 8.5]),
        couleur="",
        tableau="aucun",
        avec_logo=False,
        avec_rib=tirage.random() < 0.5,
        avec_stagiaires=tirage.random() < 0.5,
        avec_convention=True,
        avec_montant_lettres=False,
        filigrane=filigrane,
        filigrane_type=type_filigrane,
        filigrane_taille_pt=taille,
        filigrane_encre=encre,
    )


def _facture_dense(tirage: random.Random) -> Style:
    """Facture d'un service en ligne : bandeau colore, condense, petit corps, filigrane.

    Le cas le plus hostile a l'OCR du lot : corps reduit, police condensee, texte clair
    sur fond teinte, et parfois un filigrane par-dessus le contenu.
    """
    filigrane, type_filigrane, taille, encre = _marque(
        tirage, 0.4, ["diagonal", "repete", "tampon"])
    return Style(
        template="facture_04.html",
        police=tirage.choice(POLICES_CONDENSEES + ["Carlito"]),
        police_mono="Cousine",
        corps_pt=tirage.choice([7.0, 7.5, 8.0]),
        couleur=tirage.choice(COULEURS),
        tableau=tirage.choice(["aucun", "bandes"]),
        avec_logo=tirage.random() < 0.5,
        avec_rib=tirage.random() < 0.7,
        avec_stagiaires=tirage.random() < 0.5,
        avec_convention=tirage.random() < 0.6,
        avec_montant_lettres=False,
        filigrane=filigrane,
        filigrane_type=type_filigrane,
        filigrane_taille_pt=taille,
        filigrane_encre=encre,
    )


FABRIQUES_FACTURE = [
    _facture_moderne,
    _facture_classique,
    _facture_listing,
    _facture_dense,
]


def style_facture(seed: int) -> Style:
    """Style d'une facture.

    Le template tourne sur la seed plutot que d'etre tire au hasard : sur un corpus de
    taille modeste, un tirage aleatoire produirait des templates sous-representes, et
    donc des mesures moins fiables la ou on en a le plus besoin.
    """
    tirage = random.Random(seed)
    return FABRIQUES_FACTURE[seed % len(FABRIQUES_FACTURE)](tirage)


def style_rib(seed: int) -> Style:
    """Style d'un RIB.

    Volontairement peu variable : dans la realite, les RIB se ressemblent presque tous,
    leur forme etant quasi normalisee. Inventer six presentations rendrait le corpus
    moins representatif, pas plus.
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
        avec_convention=False,
        avec_montant_lettres=False,
        filigrane="",
        filigrane_type="",
        filigrane_taille_pt=0,
        filigrane_encre="",
    )
