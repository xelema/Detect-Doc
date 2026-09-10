"""Carte de visite : identité professionnelle d'une personne, sur un carton de 85 par
54 millimètres.

Le type le plus pauvre en signal du corpus : quelques dizaines de mots, aucune phrase
verbale, aucune date, aucun montant, aucun destinataire. Ce qui le désigne n'est pas un
vocabulaire mais la coexistence d'un nom, d'une fonction et de coordonnées, sans rien
qui les relie.

Une carte de visite arrive presque toujours en gestion documentaire comme la reprise
d'un objet physique, photographiée ou scannée : `physique = True`. Le rendu propre n'est
qu'une étape de fabrication, écrasée par sa numérisation avant d'entrer au manifest.
"""

import random
from dataclasses import asdict, dataclass
from datetime import date, timedelta

import degradation
import manifest
from configuration import flux
from documents.base import DocumentType
from documents.commun import (COULEURS, DEBUT_PERIODE, FIN_PERIODE, POLICES_MANUSCRITES,
                              Coordonnees, coordonnees, faker, nom_de_domaine,
                              raison_sociale_organisme, sans_accent, telephone)
from rendu import logo_svg, portrait_svg, qr_svg, rendre_pdf, texte_natif

TYPE_ATTENDU = "carte_visite"

# Fonctions portées par les cartes de visite qui atterrissent dans un dossier de
# formation : dirigeants de petites structures, commerciaux, référents pédagogiques.
FONCTIONS = [
    "Gérant", "Gérante", "Président", "Directeur", "Directrice", "Co-dirigeant",
    "Responsable commercial", "Responsable commerciale", "Ingénieur commercial",
    "Chargé de développement", "Chargée de relation entreprises",
    "Conseiller en formation", "Conseillère en formation", "Référent pédagogique",
    "Référente handicap", "Coordinateur pédagogique", "Assistante de formation",
]

# Accroches imprimées sous le nom de l'entreprise. Le vocabulaire de la formation y
# apparaît franchement, y compris en anglais : c'est précisément pour cela qu'il ne peut
# pas servir à reconnaître les autres types documentaires du corpus.
ACCROCHES = [
    "La formation qui vous ressemble",
    "Pour une montée en compétences durable",
    "Organisme de formation certifié",
    "Formations sur mesure pour les pros",
    "Votre partenaire compétences",
    "Apprendre autrement",
    "Learning made simple",
]

# Labels affichés par un organisme de formation. Qualiopi conditionne l'accès aux
# financements publics depuis 2022, il figure donc sur beaucoup de cartes du secteur.
CERTIFICATIONS = [
    "Qualiopi", "Certifié Qualiopi", "NF Service Formation",
    "Qualiopi · Actions de formation",
]

# Indicatifs de téléphonie fixe. L'indicatif exact n'a pas à correspondre à la ville :
# il suffit qu'il ne soit pas toujours le même, sans quoi une expression régulière de
# masquage se réglerait sur un seul motif.
INDICATIFS_FIXES = ["01", "02", "03", "04", "05"]

# Familles à la mode chez les imprimeurs de cartes en ligne, pas dans une suite
# bureautique : c'est ce qui rend le type si varié typographiquement, et donc si inégal
# pour un OCR.
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

# Encres de stylo. Une annotation portée à la main sur un carton est bleue ou noire.
ENCRES_STYLO = ["#243a8c", "#1b2a6b", "#222222", "#2f2a55"]

# Emplacement du carton sur la feuille. Les valeurs restent à plus de 20 mm des bords,
# de quoi absorber le quart de tour : une carte de 85 sur 54 pivotée autour de son centre
# déborde de 15,5 mm de sa boîte, et sortirait sinon de la page.
POSITIONS = ["haut-gauche", "haut-droite", "centre", "bas-gauche", "bas-droite"]

# Plans de travail sur lesquels le carton est photographié : bureau clair, sous-main,
# bois, plateau sombre. La teinte compte plus qu'il n'y paraît, c'est elle qui donne ou
# non un contraste au bord de la carte, et donc qui décide si un détourage automatique
# retrouve le carton dans l'image.
FONDS_SCENE = ["#dcdcdf", "#e6e4e0", "#c9c4bb", "#b9a98f", "#8f9296"]


@dataclass
class CarteVisite:
    """Identité professionnelle d'une personne, sur un carton de 85 par 54 millimètres.

    Toutes les coordonnées sont portées ici et c'est le style qui décide lesquelles sont
    imprimées, comme pour les autres types. C'est ce qui reproduit l'écart de richesse
    observé sur les cartes réelles, d'un facteur cinq entre la plus dépouillée et la plus
    chargée.
    """

    civilite: str
    prenom: str
    nom: str
    fonction: str
    organisme: str
    accroche: str
    certification: str
    email: str
    mobile: str
    fixe: str
    site: str
    adresse: Coordonnees
    reseau: str
    # Horodatage incrusté par l'appareil photo, et non contenu du carton. Il n'apparaît
    # que sur les cartes photographiées, et c'est le seul endroit où ce type porte une
    # date : de quoi vérifier qu'un classifieur ne s'appuie pas sur la présence d'une
    # date pour écarter la carte de visite.
    date_prise: date
    heure_prise: str

    @property
    def nom_complet(self) -> str:
        return f"{self.prenom} {self.nom}"

    @property
    def adresse_complete(self) -> str:
        return (f"{self.adresse.ligne1}, {self.adresse.code_postal} "
                f"{self.adresse.ville}")


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


def generer_carte_visite(seed: int) -> CarteVisite:
    fake = faker(seed)

    # Le prénom suit la civilité, sinon on produit des "Mme Nicolas".
    if fake.boolean():
        civilite, prenom = "M.", fake.first_name_male()
    else:
        civilite, prenom = "Mme", fake.first_name_female()
    nom = fake.last_name()

    organisme = raison_sociale_organisme(fake)
    domaine = nom_de_domaine(organisme)
    # Le couple prénom.nom est la forme d'adresse professionnelle la plus répandue, et
    # c'est aussi ce qui rend l'e-mail lui-même porteur du nom de la personne : le
    # masquer ne suffira pas à masquer l'identité.
    identifiant = f"{sans_accent(prenom)}.{sans_accent(nom)}".lower().replace("'", "")

    return CarteVisite(
        civilite=civilite,
        prenom=prenom,
        nom=nom,
        fonction=fake.random_element(FONCTIONS),
        organisme=organisme,
        accroche=fake.random_element(ACCROCHES),
        certification=fake.random_element(CERTIFICATIONS),
        email=f"{identifiant}@{domaine}",
        mobile=telephone(fake, fake.random_element(["06", "07"])),
        fixe=telephone(fake, fake.random_element(INDICATIFS_FIXES)),
        site=f"www.{domaine}",
        adresse=coordonnees(fake),
        reseau=f"in/{identifiant.replace('.', '-')}",
        date_prise=DEBUT_PERIODE + timedelta(
            days=fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)),
        heure_prise=f"{fake.random_int(7, 19):02d}:{fake.random_int(0, 59):02d}",
    )


def _reprise(seed: int, tirage: random.Random) -> dict:
    """Comment le carton a été repris, et comment il se présente dans le cadre.

    Une carte de visite arrive presque toujours en gestion documentaire comme la reprise
    d'un objet physique, scannée à plat sur une feuille restée vide à 90 %, ou
    photographiée posée sur un bureau. Les deux régimes donnent des images très
    différentes à un OCR, d'où la question posée explicitement plutôt qu'un rendu unique
    qu'on dégraderait ensuite.

    Le troisième mode, `fichier`, est la minorité qui compte quand même : un export d'un
    outil de mise en page, déposé tel quel. La page fait alors la taille du carton, sans
    support ni angle, et le document reste à texte natif. Il est rare, mais un corpus qui
    n'en contiendrait aucun ne dirait rien de ce cas, et personne ne saurait si le service
    le traite correctement.

    Le mode, la part de cartons restés fichiers et la part de quarts de tour se règlent
    dans le TOML : un carton paysage se photographie volontiers en portrait, et se pose en
    travers sur la vitre d'un scanner, mais la proportion exacte est une décision de
    corpus, pas une propriété du type.
    """
    acquisition, _ = degradation.acquisition(TYPE_ATTENDU, seed)
    physique = acquisition != "fichier"
    quart_de_tour = physique and degradation.quart_de_tour(TYPE_ATTENDU, seed)
    manuscrite = tirage.random() < 0.15
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


def _carte_moderne(tirage: random.Random, reprise: dict) -> StyleCarte:
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
        **reprise,
    )


def _carte_sobre(tirage: random.Random, reprise: dict) -> StyleCarte:
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
        **reprise,
    )


def _carte_aplat(tirage: random.Random, reprise: dict) -> StyleCarte:
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
        **reprise,
    )


FABRIQUES_CARTE = [_carte_moderne, _carte_sobre, _carte_aplat]


def style_carte_visite(seed: int) -> StyleCarte:
    """Style d'une carte de visite.

    Le template tourne sur la seed, comme pour les pièces comptables : sur un corpus de
    taille modeste, un tirage aléatoire laisserait une des trois mises en page
    sous-représentée, et les mesures la concernant sans valeur.

    La reprise est calculée à part et passée aux trois fabriques : elle ne dépend pas de
    la mise en page du carton, et deux cartes du même numéro doivent arriver de la même
    façon quelle que soit la fabrique qui les dessine.
    """
    tirage = flux("carte", seed)
    return FABRIQUES_CARTE[seed % len(FABRIQUES_CARTE)](tirage, _reprise(seed, tirage))


class CarteVisiteDocument(DocumentType):
    type_attendu = TYPE_ATTENDU
    physique = True

    def produire(self, numero: int) -> dict:
        carte = generer_carte_visite(numero)
        style = style_carte_visite(numero)
        identifiant = f"cvi-{numero:04d}"
        chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

        pages = rendre_pdf(
            style.template, chemin,
            c=carte,
            s=style,
            logo=logo_svg(carte.organisme, style.couleur),
            qr=qr_svg(numero),
            portrait=portrait_svg(style.couleur),
        )

        tags = [style.acquisition, style.orientation]
        if style.angle_deg in (90, 270):
            tags.append("quart-de-tour")
        if style.manuscrite:
            tags.append("manuscrite")
        if style.verso != "aucun":
            tags.append(f"verso-{style.verso}")
        if not style.avec_logo:
            tags.append("sans-logo")

        # Seules les coordonnées réellement imprimées sont déclarées. Le manifest sert de
        # vérité terrain au masquage : y annoncer une donnée absente du document la ferait
        # compter plus tard comme une détection manquée, et le rappel mesuré serait faux.
        pii = {
            "nom_complet": carte.nom_complet,
            "email": carte.email,
            "telephone_mobile": carte.mobile,
            "organisme": carte.organisme,
        }
        if style.avec_fixe:
            pii["telephone_fixe"] = carte.fixe
        if style.avec_adresse:
            pii["adresse"] = carte.adresse_complete
        if style.avec_reseau:
            pii["reseau_social"] = carte.reseau

        return manifest.construire_entree(
            identifiant, chemin, self.type_attendu, numero, pages,
            variante=style.as_dict(),
            pii=pii,
            tags=tags,
            texte_reference=texte_natif(chemin),
        )


DOCUMENT = CarteVisiteDocument()
