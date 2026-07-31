"""Feuille d'émargement : présence de plusieurs participants à une action de formation,
matérialisée créneau par créneau.

Cinq structures de grille sans tronc commun, contrairement à la facture et au devis :
séance unique, multi-jours, récapitulatif mensuel, feuille collective, format officiel
sectoriel. Le marqueur le plus stable n'est pas le titre, instable d'un organisme à
l'autre et parfois absent, mais la grille de signatures par créneau sous un en-tête de
session, avec un bloc de signature du formateur distinct de celui des stagiaires.

Comme la carte de visite, c'est un type `physique = True` : il arrive en gestion
documentaire comme la reprise d'un objet physique, pas comme un fichier natif.
"""

import random
from dataclasses import asdict, dataclass
from datetime import date, timedelta

import degradation
import manifest
from configuration import flux
from documents.base import DocumentType
from documents.commun import (CATALOGUE_FORMATIONS, COULEURS, DEBUT_PERIODE, FIN_PERIODE,
                              MOTIFS_CLIENT, POLICES_CONDENSEES, POLICES_MANUSCRITES,
                              POLICES_SANS, Organisme, faker, generer_organisme)
from rendu import logo_svg, rendre_pdf

TYPE_ATTENDU = "feuille_emar"

# Fonctions assez courtes pour tenir dans une grille d'émargement, tout en couvrant
# les secteurs où les exemples métier ont été produits.
FONCTIONS_STAGIAIRES = [
    "Assistante administrative",
    "Chargé de clientèle",
    "Cheffe de projet",
    "Conducteur de travaux",
    "Gestionnaire de ressources",
    "Ingénieure qualité",
    "Opérateur de production",
    "Responsable commercial",
    "Technicien de maintenance",
]

REFERENCES_OPCO = [
    "AKTO", "Atlas", "Constructys", "L'Opcommerce", "OPCO 2i", "OPCO EP",
]

ANNOTATIONS_EMARGEMENT = ["ABSENT", "RETARD", "excusé"]
TAUX_REMPLISSAGE_EMARGEMENT = [0.15, 0.35, 0.6, 0.85, 1.0]

# Natures de case qui valent présence, et donc heures facturables. Une case barrée est la
# marque d'un créneau non suivi, une case vide n'atteste rien, ABSENT et « excusé »
# constatent l'absence : aucune ne compte. Un retard, lui, laisse le stagiaire présent
# sur le créneau, l'organisme le compte.
NATURES_PRESENCE = {"signature", "retard"}

TITRES_EMARGEMENT = [
    "Feuille d'émargement",
    "Feuille de présence",
    "Feuille collective d'émargement",
    "Émargements par demi-journée ou soirée",
]

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


@dataclass
class CreneauEmargement:
    """Colonne temporelle d'une grille de signatures."""

    libelle: str
    date_session: date
    horaire: str


@dataclass
class CaseEmargement:
    """Contenu manuscrit d'une case de la grille."""

    nature: str
    texte: str


@dataclass
class ParticipantEmargement:
    """Participant et vérité terrain de ses émargements."""

    prenom: str
    nom: str
    entreprise: str
    fonction: str
    matricule: str
    rpps: str
    adeli: str
    document_pedagogique_recu: bool
    cases: list[CaseEmargement]
    heures_suivies: float
    heures_souscrites: float
    report_heures: float

    @property
    def nom_complet(self) -> str:
        return f"{self.prenom} {self.nom}"


@dataclass
class FormateurEmargement:
    """Formateur, séparé des participants dans presque toutes les mises en page."""

    prenom: str
    nom: str
    matricule: str
    cases: list[CaseEmargement]

    @property
    def nom_complet(self) -> str:
        return f"{self.prenom} {self.nom}"


@dataclass
class FeuilleEmargement:
    """Présence de plusieurs participants, matérialisée créneau par créneau."""

    organisme: Organisme
    numero_session: str
    intitule_action: str
    date_debut: date
    date_fin: date
    lieu: str
    creneaux: list[CreneauEmargement]
    participants: list[ParticipantEmargement]
    formateur: FormateurEmargement
    reference_opco: str

    @property
    def taux_remplissage(self) -> float:
        cases = [case for participant in self.participants for case in participant.cases]
        if not cases:
            return 0.0
        remplies = sum(case.nature != "vide" for case in cases)
        return round(remplies / len(cases), 2)


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


def _creneaux_emargement(date_debut: date, nombre: int,
                          mensuel: bool) -> list[CreneauEmargement]:
    """Créneaux d'une session, sans dépendre du fuseau horaire du système."""
    if nombre == 3 and not mensuel:
        return [
            CreneauEmargement("Matin", date_debut, "09:00 - 12:00"),
            CreneauEmargement("Après-midi", date_debut, "14:00 - 17:00"),
            CreneauEmargement("Soirée", date_debut, "18:00 - 20:00"),
        ]

    creneaux = []
    for index in range(nombre):
        if mensuel:
            jour = date_debut + timedelta(days=index * 2)
            libelle = jour.strftime("%d/%m")
            horaire = "09:00 - 12:30" if index % 2 == 0 else "13:30 - 17:00"
        else:
            jour = date_debut + timedelta(days=index // 2)
            libelle = "Matin" if index % 2 == 0 else "Après-midi"
            horaire = "09:00 - 12:30" if index % 2 == 0 else "13:30 - 17:00"
        creneaux.append(CreneauEmargement(libelle, jour, horaire))
    return creneaux


def _case_emargement(tirage: random.Random, prenom: str, nom: str,
                     taux_cible: float) -> CaseEmargement:
    if tirage.random() >= taux_cible:
        if tirage.random() < 0.2:
            return CaseEmargement("barre", "")
        return CaseEmargement("vide", "")

    if tirage.random() < 0.15:
        annotation = tirage.choice(ANNOTATIONS_EMARGEMENT)
        return CaseEmargement(annotation.lower(), annotation)

    signatures = [
        f"{prenom[0]}. {nom}",
        f"{prenom} {nom[0]}.",
        nom,
    ]
    return CaseEmargement("signature", tirage.choice(signatures))


def generer_feuille_emargement(seed: int, nombre_participants: int,
                               nombre_creneaux: int,
                               mensuel: bool) -> FeuilleEmargement:
    """Feuille d'émargement synthétique pour une structure décidée par le style."""
    seed_organisme = seed + 1000
    tirage = flux("emargement", seed)
    organisme = generer_organisme(seed_organisme)
    taux_cible = flux("remplissage", seed).choice(TAUX_REMPLISSAGE_EMARGEMENT)

    # Construit après l'appel ci-dessus : `faker()` reseede une instance partagée, la
    # prendre plus tôt la ferait dériver au passage de `generer_organisme` au lieu de
    # démarrer sur cette seed, cf. le docstring de `documents.commun.faker`.
    fake = faker(seed_organisme)
    intitule, _ = fake.random_element(CATALOGUE_FORMATIONS)
    marge_fin = 2 * max(nombre_creneaux, 2)
    jours = fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days - marge_fin)
    date_debut = DEBUT_PERIODE + timedelta(days=jours)
    creneaux = _creneaux_emargement(date_debut, nombre_creneaux, mensuel)

    participants = []
    for index in range(nombre_participants):
        prenom = fake.first_name()
        nom = fake.last_name()
        cases = [
            _case_emargement(tirage, prenom, nom, taux_cible)
            for _ in creneaux
        ]
        heures_suivies = sum(case.nature in NATURES_PRESENCE
                             for case in cases) * 3.5
        heures_souscrites = len(creneaux) * 3.5
        participants.append(
            ParticipantEmargement(
                prenom=prenom,
                nom=nom,
                entreprise=fake.random_element(MOTIFS_CLIENT).format(
                    nom=fake.last_name()),
                fonction=fake.random_element(FONCTIONS_STAGIAIRES),
                matricule=fake.bothify("STG-####"),
                rpps=fake.numerify("###########"),
                adeli=fake.numerify("#########"),
                document_pedagogique_recu=fake.boolean(
                    chance_of_getting_true=75),
                cases=cases,
                heures_suivies=heures_suivies,
                heures_souscrites=heures_souscrites,
                report_heures=float(fake.random_element([0, 0, 3.5, 7])),
            )
        )

    if fake.boolean():
        prenom_formateur = fake.first_name_male()
    else:
        prenom_formateur = fake.first_name_female()
    nom_formateur = fake.last_name()
    cases_formateur = [
        CaseEmargement(
            "signature",
            tirage.choice([
                f"{prenom_formateur[0]}. {nom_formateur}",
                nom_formateur,
            ]),
        )
        for _ in creneaux
    ]

    return FeuilleEmargement(
        organisme=organisme,
        numero_session=fake.bothify("FEM-####-??").upper(),
        intitule_action=intitule,
        date_debut=date_debut,
        date_fin=max(creneau.date_session for creneau in creneaux),
        lieu=(f"{organisme.adresse.ligne1}, {organisme.adresse.code_postal} "
              f"{organisme.adresse.ville}"),
        creneaux=creneaux,
        participants=participants,
        formateur=FormateurEmargement(
            prenom=prenom_formateur,
            nom=nom_formateur,
            matricule=fake.bothify("FOR-####"),
            cases=cases_formateur,
        ),
        reference_opco=f"{fake.random_element(REFERENCES_OPCO)} "
                       f"{fake.numerify('########')}",
    )


def style_feuille_emargement(seed: int) -> StyleEmargement:
    """Style d'une feuille d'émargement, sur les axes observés dans les exemples.

    La reprise, scanner du secrétariat, photo au téléphone ou export resté fichier, et le
    quart de tour se règlent dans le TOML. Ils sont demandés sur le seul numéro du
    document, jamais sur sa famille de grille : les faire dépendre l'un de l'autre
    empêcherait de distinguer, dans une mesure, ce qui tient à la structure de la feuille
    de ce qui tient à la façon dont elle est arrivée.
    """
    tirage = flux("feuille-emargement", seed)
    famille = FAMILLES_EMARGEMENT[(seed - 1) % len(FAMILLES_EMARGEMENT)]
    acquisition, _ = degradation.acquisition(TYPE_ATTENDU, seed)
    pivotee = acquisition != "fichier" and degradation.quart_de_tour(TYPE_ATTENDU, seed)

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
        rotation_page_deg=tirage.choice([90, 270]) if pivotee else 0,
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


class FeuilleEmargementDocument(DocumentType):
    type_attendu = TYPE_ATTENDU
    physique = True

    def produire(self, numero: int) -> dict:
        style = style_feuille_emargement(numero)
        nombre_participants = style.lignes_par_page * style.nombre_pages
        feuille = generer_feuille_emargement(
            numero,
            nombre_participants=nombre_participants,
            nombre_creneaux=style.nombre_creneaux,
            mensuel=style.mensuel,
        )
        groupes = [
            feuille.participants[index:index + style.lignes_par_page]
            for index in range(0, len(feuille.participants), style.lignes_par_page)
        ]

        identifiant = f"fem-{numero:04d}"
        chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"
        pages = rendre_pdf(
            style.template, chemin,
            f=feuille,
            s=style,
            groupes=groupes,
            logo=logo_svg(feuille.organisme.raison_sociale, style.couleur),
        )
        if pages != style.nombre_pages:
            raise RuntimeError(
                f"{identifiant} : {pages} page(s) rendue(s) au lieu de "
                f"{style.nombre_pages}")

        tags = [style.famille, style.acquisition]
        tags.append("sans-titre" if style.titre is None else "avec-titre")
        if style.rotation_page_deg in (90, 270):
            tags.append("quart-de-tour")
        if pages > 1:
            tags.append("multipage")
        if not style.avec_bloc_formateur:
            tags.append("sans-bloc-formateur")
        if feuille.taux_remplissage <= 0.35:
            tags.append("grille-peu-remplie")
        if style.residu_publipostage:
            tags.append("publipostage-inacheve")

        pii = {
            "noms_participants": [participant.nom_complet
                                  for participant in feuille.participants],
            "nom_formateur": feuille.formateur.nom_complet,
        }
        if style.avec_identifiant_metier:
            pii["rpps"] = [participant.rpps for participant in feuille.participants]
            pii["adeli"] = [participant.adeli for participant in feuille.participants]

        variante = {
            **style.as_dict(),
            "taux_remplissage": feuille.taux_remplissage,
        }
        return manifest.construire_entree(
            identifiant, chemin, self.type_attendu, numero, pages,
            variante=variante,
            pii=pii,
            tags=tags,
        )


DOCUMENT = FeuilleEmargementDocument()
