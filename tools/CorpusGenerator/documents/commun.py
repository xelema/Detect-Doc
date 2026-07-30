"""Ce que plusieurs types de documents ont en commun.

Un élément n'atterrit ici que s'il sert à au moins deux types : l'identité de
l'organisme de formation et de son client (facture, devis, feuille d'émargement), le
vocabulaire du secteur, et la présentation d'une pièce comptable (facture, devis, RIB).
Un axe propre à un seul type reste dans son fichier, même s'il ressemble à ce qui est
listé ici : la duplication d'un futur type qui ne partage rien avec ceux-ci ne doit rien
coûter à ce module.

Comme pour la génération elle-même, tout est piloté par une seed. Le même numéro de
document redonne exactement le même contenu, ce qui autorise à ne pas versionner les
binaires du corpus : ils se refabriquent.
"""

import random
import unicodedata
from dataclasses import asdict, dataclass
from datetime import date, timedelta

from faker import Faker

from cles import numero_tva_intracommunautaire

# Intitulés et durées des actions de formation vendues par l'organisme. Un organisme de
# formation doit afficher son numéro de déclaration d'activité et, depuis 2022, sa
# certification Qualiopi. Ce sont des marqueurs forts, absents des autres types.
CATALOGUE_FORMATIONS = [
    ("Habilitation électrique B1V B2V BR BC", 21),
    ("SST - Sauveteur secouriste du travail", 14),
    ("CACES R489 catégorie 3 - Chariot élévateur", 21),
    ("Prévention des risques liés à l'activité physique", 14),
    ("Excel - Perfectionnement tableaux croisés", 14),
    ("Management d'équipe - Niveau 1", 21),
    ("Anglais professionnel - Niveau B1", 30),
    ("AIPR - Opérateur", 7),
    ("Le RGPD pour les managers", 7),
    ("Gestes et postures au poste de travail", 7),
]

FORMES_JURIDIQUES = ["SAS", "SARL", "SASU", "EURL"]

# Période sur laquelle sont tirées les dates des documents.
# Les dates sont calculées en jours entiers à partir de cette borne, et non avec
# Faker.date_between : cette méthode passe par des horodatages convertis en heure
# locale et ne redonne pas le même résultat d'une machine à l'autre à seed égale.
# Le corpus doit se refabriquer à l'identique, la génération ne peut donc dépendre
# que d'arithmétique entière.
DEBUT_PERIODE = date(2025, 1, 1)
FIN_PERIODE = date(2026, 6, 30)

# La raison sociale est construite ici plutôt que prise de Faker : company() y colle
# déjà une forme juridique, ce qui donnerait "Maury Henry SARL (SAS)" une fois la forme
# affichée séparément dans le pied de facture. Et un organisme de formation ne s'appelle
# pas comme une entreprise générique, ces motifs sont ceux du secteur.
MOTIFS_ORGANISME = [
    "Institut {nom}",
    "{nom} Formation",
    "{nom} Compétences",
    "Campus {ville}",
    "{nom} Conseil et Formation",
    "Académie {ville}",
    "Centre de formation {nom}",
]

# Les entreprises qui envoient des salariés en formation. Faker ne produit qu'un nom de
# famille pour company(), ce qui donne des clients trop maigres pour être crédibles.
# Sert aussi à nommer l'entreprise d'un participant de feuille d'émargement.
MOTIFS_CLIENT = [
    "Transports {nom}",
    "{nom} et Fils",
    "Établissements {nom}",
    "{nom} Industrie",
    "Logistique {nom}",
    "Menuiserie {nom}",
    "{nom} Maintenance",
    "Groupe {nom}",
    "{nom} BTP",
    "Propreté {nom}",
]

# Couples code postal / commune réels. Faker fr_FR fabrique des noms de communes à
# partir de patronymes et les associe à des codes postaux au hasard, ce qui donne des
# adresses du genre "77880 Sainte Rene" : crédible nulle part.
VILLES = [
    ("75011", "Paris"), ("69003", "Lyon"), ("13008", "Marseille"),
    ("31000", "Toulouse"), ("33000", "Bordeaux"), ("44000", "Nantes"),
    ("59000", "Lille"), ("67000", "Strasbourg"), ("34000", "Montpellier"),
    ("35000", "Rennes"), ("76000", "Rouen"), ("21000", "Dijon"),
    ("38000", "Grenoble"), ("49000", "Angers"), ("63000", "Clermont-Ferrand"),
    ("29200", "Brest"), ("87000", "Limoges"), ("57000", "Metz"),
    ("14000", "Caen"), ("45000", "Orléans"), ("80000", "Amiens"),
    ("51100", "Reims"), ("83000", "Toulon"), ("42000", "Saint-Étienne"),
]

# Familles réellement présentes sur des documents de gestion français. Carlito et Arimo
# ont les métriques de Calibri et d'Arial, Tinos celles de Times New Roman, Cousine
# celles de Courier New : ce sont les polices que produisent les suites bureautiques.
POLICES_SANS = ["Carlito", "Arimo", "Liberation Sans", "Noto Sans", "Nimbus Sans"]
POLICES_SERIF = ["Tinos", "Caladea", "C059", "Nimbus Roman", "URW Bookman"]
POLICES_MONO = ["Cousine", "Nimbus Mono PS", "DejaVu Sans Mono"]
POLICES_CONDENSEES = ["Liberation Sans Narrow", "Nimbus Sans Narrow"]

# Polices manuscrites, pour l'identité complétée au stylo sur une carte de visite et les
# annotations d'une feuille d'émargement. Elles sont hostiles à l'OCR, ce qui est
# précisément le but : c'est le cas où le texte imprimé ne porte plus rien de l'identité.
#
# Toute police ajoutée ici doit couvrir les capitales accentuées. La plupart des polices
# manuscrites libres ne les dessinent pas, et un nom qui emprunte son É à une police de
# secours sort avec un glyphe d'un autre corps et d'une autre graisse, sans qu'aucune
# erreur ne soit levée. `verifier.py` contrôle cette couverture.
POLICES_MANUSCRITES = ["Kristi", "femkeklaver", "Breip", "Purisa"]

COULEURS = ["#1f4e79", "#256d5b", "#7a2f2f", "#4a3b6b", "#8a5a1f", "#2f5f7a", "#333333"]

# Marques portées par une pièce comptable réelle (facture, devis, RIB). Le filigrane en
# diagonale et la mosaïque sont imprimés par le logiciel émetteur ; le tampon est apposé
# après coup, à l'encre, ce qui lui donne une couleur qui tranche et une opacité bien
# plus forte.
FILIGRANES_IMPRIMES = ["DUPLICATA", "COPIE", "ORIGINAL"]
ENCRES_TAMPON = ["#b02020", "#1c3f8f", "#1f6b3a"]

# Les deux types ne reçoivent pas les mêmes cachets, et c'est un signal à part entière :
# une facture se tamponne une fois payée, un devis une fois accepté. Mélanger les deux
# listes apprendrait au classifieur une association fausse.
TAMPONS_FACTURE = ["PAYÉE", "ACQUITTÉE", "RÉGLÉE"]
TAMPONS_DEVIS = ["BON POUR ACCORD", "ACCEPTÉ", "SIGNÉ"]


@dataclass
class Coordonnees:
    """Bloc adresse commun à toutes les parties d'un document."""

    ligne1: str
    code_postal: str
    ville: str

    @property
    def formatee(self) -> str:
        """Adresse sur une ligne, telle qu'elle s'écrit dans un pavé PII du manifest."""
        return f"{self.ligne1}, {self.code_postal} {self.ville}"


@dataclass
class Style:
    """Présentation d'une facture, d'un devis ou d'un RIB. Sérialisée telle quelle dans
    le manifest.

    Partagée entre les trois types plutôt que dupliquée : ils sortent du même genre de
    logiciel de gestion, avec le même jeu d'axes (police, couleur, tableau, blocs
    présents, marque). Un RIB isolé n'a simplement pas de tableau de lignes ni de
    cartouche d'acceptation, et pose ces champs à leur valeur neutre.
    """

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
    avec_acceptation: bool
    filigrane: str
    filigrane_type: str
    filigrane_taille_pt: int
    filigrane_encre: str

    def as_dict(self) -> dict:
        return asdict(self)


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


def marque(tirage: random.Random, probabilite: float, types: list[str],
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
class Organisme:
    """L'organisme de formation qui émet la facture."""

    raison_sociale: str
    forme_juridique: str
    capital: int
    adresse: Coordonnees
    siren: str
    siret: str
    tva: str
    rcs_ville: str
    numero_declaration_activite: str
    telephone: str
    email: str
    site_web: str
    # Un organisme de formation peut être exonéré de TVA au titre de l'article 261-4-4
    # du CGI. Les deux régimes existent en production, la facture n'a pas la même tête.
    exonere_tva: bool


@dataclass
class Client:
    """Le destinataire. Professionnel dans la majorité des cas, particulier parfois."""

    est_professionnel: bool
    designation: str
    adresse: Coordonnees
    siren: str | None = None
    tva: str | None = None
    contact: str | None = None


def client_professionnel(seed: int) -> bool:
    """Le destinataire est-il une entreprise ?

    Le B2B domine largement en gestion documentaire, mais le B2C existe et ses mentions
    obligatoires ne sont pas les mêmes.

    Comme la répartition entre jeu de réglage et jeu de mesure, le tirage passe par un
    flux nommé : plusieurs facettes du document dérivent de la même seed, et un modulo
    partagé les ferait s'aligner. Le jeu de réglage n'aurait alors jamais vu de facture
    adressée à un particulier.
    """
    return random.Random(f"professionnel-{seed}").random() < 0.75


_FAKER = Faker("fr_FR")


def faker(seed: int) -> Faker:
    """Faker seedé pour un document donné.

    Reseede l'instance partagée plutôt que de reconstruire un `Faker("fr_FR")` à chaque
    appel : la construction recharge les fournisseurs de données de la locale et coûte
    plusieurs ordres de grandeur de plus que `seed_instance` sur une instance existante,
    ce qu'un corpus entier, avec plusieurs appels par document, finit par payer plein
    tarif.

    Attention à l'ordre d'appel : l'instance étant partagée, un appelant qui la récupère
    puis appelle une fonction imbriquée qui reseede à nouveau via `faker()` (par exemple
    `generer_organisme`, `generer_client` ou `generer_rib` sur une seed différente) verra
    son propre flux aléatoire décalé sur celui du dernier appel imbriqué. Toujours
    demander l'instance juste avant de la consommer, une fois les sous-générateurs
    exécutés, jamais avant.
    """
    _FAKER.seed_instance(seed)
    return _FAKER


def sans_accent(texte: str) -> str:
    decompose = unicodedata.normalize("NFKD", texte)
    return "".join(c for c in decompose if not unicodedata.combining(c))


def nom_de_domaine(raison_sociale: str) -> str:
    """Domaine déduit de la raison sociale.

    Un organisme dont l'adresse de courriel n'a aucun rapport avec son nom se repère
    immédiatement comme généré.
    """
    # Les mots de liaison sont écartés, sinon "Grenier Conseil et Formation" donne
    # un domaine tronqué en "grenier-conseil-et".
    mots = [m for m in sans_accent(raison_sociale).lower().split() if len(m) > 2]
    return "-".join(mots[:3]).replace("'", "") + ".fr"


def coordonnees(fake: Faker) -> Coordonnees:
    code_postal, ville = fake.random_element(VILLES)
    return Coordonnees(
        ligne1=f"{fake.building_number()} {fake.street_name()}",
        code_postal=code_postal,
        ville=ville,
    )


def telephone(fake: Faker, indicatif: str) -> str:
    """Numéro français écrit par groupes de deux chiffres, comme sur un carton imprimé."""
    return indicatif + " " + " ".join(fake.numerify("##") for _ in range(4))


def raison_sociale_organisme(fake: Faker) -> str:
    """Raison sociale plausible d'un organisme de formation.

    Réutilisée telle quelle par la carte de visite : une personne qui y figure travaille
    pour un organisme du même genre que celui qui émet les pièces comptables du corpus.
    """
    motif = fake.random_element(MOTIFS_ORGANISME)
    _, ville = fake.random_element(VILLES)
    return motif.format(nom=fake.last_name(), ville=ville)


def generer_organisme(seed: int) -> Organisme:
    fake = faker(seed)
    # Le SIRET porte sa propre clé de Luhn sur ses 14 chiffres : le fabriquer en
    # concaténant un SIREN valide et 5 chiffres au hasard produirait un SIRET invalide.
    # On part donc du SIRET et on en déduit le SIREN, qui en est le préfixe.
    siret = fake.siret().replace(" ", "")
    siren = siret[:9]
    adresse = coordonnees(fake)
    raison_sociale = raison_sociale_organisme(fake)
    domaine = nom_de_domaine(raison_sociale)

    return Organisme(
        raison_sociale=raison_sociale,
        forme_juridique=fake.random_element(FORMES_JURIDIQUES),
        capital=fake.random_element([10000, 25000, 50000, 100000]),
        adresse=adresse,
        siren=siren,
        siret=siret,
        tva=numero_tva_intracommunautaire(siren),
        rcs_ville=adresse.ville,
        numero_declaration_activite=fake.numerify("## ## ##### ##"),
        telephone=fake.phone_number(),
        email=f"contact@{domaine}",
        site_web=f"www.{domaine}",
        exonere_tva=fake.boolean(chance_of_getting_true=30),
    )


def generer_client(seed: int, professionnel: bool) -> Client:
    fake = faker(seed)
    adresse = coordonnees(fake)

    if not professionnel:
        return Client(
            est_professionnel=False,
            designation=f"{fake.first_name()} {fake.last_name()}",
            adresse=adresse,
        )

    siren = fake.siren().replace(" ", "")
    motif = fake.random_element(MOTIFS_CLIENT)
    return Client(
        est_professionnel=True,
        designation=motif.format(nom=fake.last_name()),
        adresse=adresse,
        siren=siren,
        tva=numero_tva_intracommunautaire(siren),
        contact=f"{fake.first_name()} {fake.last_name()}",
    )
