"""Génération des données factices d'un document du corpus.

Deux principes qui commandent tout ce fichier.

Le générateur connaît les valeurs qu'il injecte : c'est la vérité terrain qui permettra
de mesurer le masquage des données personnelles sans annotation manuelle.

Tout est piloté par une seed. Le même numéro de document redonne exactement le même
contenu, ce qui autorise à ne pas versionner les binaires du corpus : ils se refabriquent.
"""

import random
import unicodedata
from dataclasses import dataclass
from datetime import date, timedelta
from decimal import Decimal

from faker import Faker
from stdnum import iban as iban_stdnum

from cles import cle_rib, iban_francais, numero_tva_intracommunautaire

# Un organisme de formation doit afficher son numéro de déclaration d'activité et,
# depuis 2022, sa certification Qualiopi. Ce sont des marqueurs forts du type "facture
# de formation", et ils n'apparaissent sur aucun autre type de document.
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

# Intitulés sous lesquels un devis circule réellement. Le premier est de loin le plus
# fréquent, d'où sa répétition : le tirage suit la réalité plutôt qu'une équiprobabilité
# qui rendrait le corpus trompeur.
LIBELLES_DEVIS = ["Devis", "Devis", "Devis", "Proposition commerciale", "Offre de prix"]

# Préfixes de numérotation d'un devis, distincts de ceux d'une facture. C'est le premier
# indice que lit un humain, et il ne doit donc pas être unique dans le corpus.
PREFIXES_DEVIS = ["DEV", "D", "DV", "PRO"]

# Durées de validité rencontrées sur des devis de formation. Trois mois est courant chez
# les organismes, un mois chez les prestataires de service.
DUREES_VALIDITE_JOURS = [15, 30, 30, 45, 60, 90]

# Part demandée à la commande. Zéro est fréquent en formation professionnelle, où le
# financement passe souvent par un OPCO : l'acompte disparaît alors du document.
ACOMPTES_POURCENT = [0, 0, 30, 30, 50]

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

# Noms de banques inventés. Aucune banque réelle n'est nommée : le corpus doit être
# réaliste dans sa forme, pas usurper une marque existante.
BANQUES = [
    ("Banque du Cerisier", "BCERFRPP"),
    ("Crédit Méridien", "CMERFRP1"),
    ("Banque Lafayette et Associés", "BLAAFRPX"),
    ("Caisse de l'Ouvrage", "COUVFRP2"),
    ("Banque Atlantique du Ponant", "BAPOFRPP"),
]


@dataclass
class Coordonnees:
    """Bloc adresse commun à toutes les parties d'un document."""

    ligne1: str
    code_postal: str
    ville: str


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


@dataclass
class Ligne:
    designation: str
    detail: str
    quantite: Decimal
    unite: str
    prix_unitaire_ht: Decimal
    taux_tva: Decimal

    @property
    def total_ht(self) -> Decimal:
        return (self.quantite * self.prix_unitaire_ht).quantize(Decimal("0.01"))

    @property
    def montant_tva(self) -> Decimal:
        return (self.total_ht * self.taux_tva / 100).quantize(Decimal("0.01"))


@dataclass
class Piece:
    """Tronc commun de la facture et du devis.

    Un organisme édite ses devis et ses factures avec le même logiciel : même papier à
    en-tête, même tableau de lignes, mêmes totaux, même pavé légal. Ce qui les sépare
    tient à quelques mentions, aux dates et au cartouche d'acceptation. Partager le
    tronc ici garantit que la différence reste là où elle est dans la réalité, au lieu
    d'être offerte au classifieur sous forme de deux mises en page étrangères.
    """

    numero: str
    date_emission: date
    organisme: Organisme
    client: Client
    lignes: list[Ligne]
    # Convention de formation sur une facture, numéro d'affaire sur un devis : la
    # référence existe dans les deux cas, seul son intitulé imprimé change.
    reference_dossier: str
    intitule_action: str
    stagiaires: list[str]
    coordonnees_bancaires: "Rib"

    # Lus par les templates, qui servent les deux types. Des attributs de classe plutôt
    # que des champs : ces valeurs ne varient pas d'un document à l'autre.
    #
    # L'intitulé imprimé en tête, lui, est déclaré par chaque sous-classe et non ici :
    # une valeur posée sur Piece deviendrait la valeur par défaut du champ de même nom
    # du devis, qui doit rester obligatoire.
    est_devis = False
    libelle_reference = "Convention"

    @property
    def total_ht(self) -> Decimal:
        return sum((l.total_ht for l in self.lignes), Decimal("0.00"))

    @property
    def total_tva(self) -> Decimal:
        return sum((l.montant_tva for l in self.lignes), Decimal("0.00"))

    @property
    def total_ttc(self) -> Decimal:
        return self.total_ht + self.total_tva

    @property
    def taux_tva(self) -> Decimal:
        """Taux appliqué au document, pour que les templates ne l'écrivent pas en dur.

        Toutes les lignes portent le même taux : un organisme exonéré l'est pour tout
        le document. Le jour où un taux réduit apparaîtra, c'est ici qu'il faudra
        décider quoi afficher, pas dans les quatre templates.
        """
        return self.lignes[0].taux_tva


@dataclass
class Facture(Piece):
    date_execution: date
    date_echeance: date
    # Les quatre mentions ajoutées par la réforme de la facturation électronique entrent
    # en vigueur par paliers, septembre 2026 puis septembre 2027. La production contient
    # donc un mélange des deux formes, le corpus doit le refléter.
    nouvelles_mentions: bool
    categorie_operation: str

    libelle = "Facture"


@dataclass
class Devis(Piece):
    """Proposition chiffrée, émise avant la prestation.

    Voisin le plus confondable de la facture : mêmes parties, mêmes montants, même
    pavé légal. Ce qui l'en distingue tient dans son intitulé, sa durée de validité,
    ses dates au futur, son acompte et son cartouche d'acceptation, et rien d'autre.
    C'est précisément ce jeu de marqueurs que la classification devra savoir lire.
    """

    # « Devis » domine, mais « proposition commerciale » et « offre de prix » désignent
    # le même document et se rencontrent tels quels en gestion documentaire. Un
    # classifieur qui ne reconnaîtrait que le premier mot passerait à côté.
    libelle: str
    date_validite: date
    duree_validite_jours: int
    date_debut_prevue: date
    delai_realisation: str
    # 0 quand l'organisme ne demande pas d'acompte : la mention disparaît alors du
    # document, ce qui le rapproche encore d'une facture.
    acompte_pourcentage: int

    est_devis = True
    libelle_reference = "Référence"

    @property
    def montant_acompte(self) -> Decimal:
        return (self.total_ttc * self.acompte_pourcentage / 100).quantize(Decimal("0.01"))


@dataclass
class Rib:
    """Coordonnées bancaires, utilisées dans le pavé de bas de facture et comme
    document à part entière."""

    titulaire: str
    # Un RIB déposé en gestion documentaire appartient le plus souvent à une personne
    # physique. L'information est portée ici plutôt que devinée plus tard sur la forme du
    # nom : ajouter une civilité ou un motif de raison sociale ferait alors mentir le
    # manifest, sans que rien ne le signale.
    titulaire_est_personne: bool
    adresse_titulaire: Coordonnees
    banque: str
    bic: str
    agence: str
    adresse_agence: Coordonnees
    code_banque: str
    code_guichet: str
    numero_compte: str
    cle: str
    iban: str
    date_edition: date

    @property
    def iban_formate(self) -> str:
        """L'IBAN s'imprime par groupes de 4 caractères, comme sur un vrai relevé."""
        return iban_stdnum.format(self.iban)


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


def _faker(seed: int) -> Faker:
    fake = Faker("fr_FR")
    fake.seed_instance(seed)
    return fake


def _sans_accent(texte: str) -> str:
    decompose = unicodedata.normalize("NFKD", texte)
    return "".join(c for c in decompose if not unicodedata.combining(c))


def _nom_de_domaine(raison_sociale: str) -> str:
    """Domaine déduit de la raison sociale.

    Un organisme dont l'adresse de courriel n'a aucun rapport avec son nom se repère
    immédiatement comme généré.
    """
    # Les mots de liaison sont écartés, sinon "Grenier Conseil et Formation" donne
    # un domaine tronqué en "grenier-conseil-et".
    mots = [m for m in _sans_accent(raison_sociale).lower().split() if len(m) > 2]
    return "-".join(mots[:3]).replace("'", "") + ".fr"


def _raison_sociale_organisme(fake: Faker) -> str:
    motif = fake.random_element(MOTIFS_ORGANISME)
    _, ville = fake.random_element(VILLES)
    return motif.format(nom=fake.last_name(), ville=ville)


def _coordonnees(fake: Faker) -> Coordonnees:
    code_postal, ville = fake.random_element(VILLES)
    return Coordonnees(
        ligne1=f"{fake.building_number()} {fake.street_name()}",
        code_postal=code_postal,
        ville=ville,
    )


def _titulaire_par_defaut(fake: Faker) -> tuple[str, bool]:
    """Titulaire d'un RIB isolé, et s'il s'agit d'une personne physique.

    Un RIB déposé en gestion documentaire est majoritairement celui d'un stagiaire ou
    d'un salarié, plus rarement celui d'une entreprise.
    """
    if fake.boolean(chance_of_getting_true=70):
        # Le prénom est tiré selon la civilité, sinon on produit des "M. Audrey".
        if fake.boolean():
            return f"M. {fake.first_name_male()} {fake.last_name()}", True
        return f"Mme {fake.first_name_female()} {fake.last_name()}", True
    return fake.random_element(MOTIFS_CLIENT).format(nom=fake.last_name()), False


def generer_rib(seed: int, titulaire: str | None = None,
                adresse: Coordonnees | None = None) -> Rib:
    fake = _faker(seed)

    code_banque = fake.numerify("#####")
    code_guichet = fake.numerify("#####")
    numero_compte = fake.numerify("###########")
    cle = cle_rib(code_banque, code_guichet, numero_compte)

    banque, bic = fake.random_element(BANQUES)
    adresse_agence = _coordonnees(fake)

    # Un titulaire imposé vient toujours de l'organisme émetteur d'une facture, donc
    # d'une entreprise.
    nom_titulaire, est_personne = (
        (titulaire, False) if titulaire else _titulaire_par_defaut(fake))

    return Rib(
        titulaire=nom_titulaire,
        titulaire_est_personne=est_personne,
        adresse_titulaire=adresse or _coordonnees(fake),
        banque=banque,
        bic=bic,
        agence=f"Agence {adresse_agence.ville}",
        adresse_agence=adresse_agence,
        code_banque=code_banque,
        code_guichet=code_guichet,
        numero_compte=numero_compte,
        cle=cle,
        iban=iban_francais(code_banque, code_guichet, numero_compte, cle),
        date_edition=DEBUT_PERIODE + timedelta(
            days=fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)),
    )


def generer_organisme(seed: int) -> Organisme:
    fake = _faker(seed)
    # Le SIRET porte sa propre clé de Luhn sur ses 14 chiffres : le fabriquer en
    # concaténant un SIREN valide et 5 chiffres au hasard produirait un SIRET invalide.
    # On part donc du SIRET et on en déduit le SIREN, qui en est le préfixe.
    siret = fake.siret().replace(" ", "")
    siren = siret[:9]
    adresse = _coordonnees(fake)
    raison_sociale = _raison_sociale_organisme(fake)
    domaine = _nom_de_domaine(raison_sociale)

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
    fake = _faker(seed)
    adresse = _coordonnees(fake)

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


def _lignes_formation(fake: Faker, intitule: str, duree: int, nb_stagiaires: int,
                      taux: Decimal) -> list[Ligne]:
    """Lignes chiffrées d'une action de formation.

    Identiques sur un devis et sur une facture : le montant proposé est celui qui sera
    facturé. Un corpus où les deux types n'auraient pas la même forme de tableau
    donnerait au classifieur un raccourci qui n'existe pas en production.
    """
    prix_heure = Decimal(fake.random_element([28, 35, 42, 55, 70]))

    lignes = [
        Ligne(
            designation=intitule,
            detail=f"Action de formation de {duree} heures, "
                   f"{nb_stagiaires} stagiaire{'s' if nb_stagiaires > 1 else ''}",
            quantite=Decimal(duree * nb_stagiaires),
            unite="heure",
            prix_unitaire_ht=prix_heure,
            taux_tva=taux,
        )
    ]
    if fake.boolean(chance_of_getting_true=40):
        lignes.append(
            Ligne(
                designation="Supports pédagogiques",
                detail=f"{nb_stagiaires} classeur{'s' if nb_stagiaires > 1 else ''} "
                       f"remis en début de session",
                quantite=Decimal(nb_stagiaires),
                unite="unite",
                prix_unitaire_ht=Decimal("45"),
                taux_tva=taux,
            )
        )
    return lignes


def generer_facture(seed: int, professionnel: bool = True) -> Facture:
    fake = _faker(seed)

    organisme = generer_organisme(seed)
    client = generer_client(seed + 1, professionnel)
    rib = generer_rib(seed + 2, organisme.raison_sociale, organisme.adresse)

    intitule, duree = fake.random_element(CATALOGUE_FORMATIONS)
    nb_stagiaires = fake.random_int(1, 8) if professionnel else 1
    stagiaires = [f"{fake.first_name()} {fake.last_name()}" for _ in range(nb_stagiaires)]

    # Un organisme exonéré facture sans TVA, sinon le taux normal s'applique.
    taux = Decimal("0") if organisme.exonere_tva else Decimal("20")
    lignes = _lignes_formation(fake, intitule, duree, nb_stagiaires, taux)

    jours = fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)
    date_execution = DEBUT_PERIODE + timedelta(days=jours)
    date_emission = date_execution + timedelta(days=fake.random_int(1, 10))

    return Facture(
        numero=f"FA{date_emission.year}-{fake.numerify('####')}",
        date_emission=date_emission,
        organisme=organisme,
        client=client,
        lignes=lignes,
        reference_dossier=fake.numerify("CONV-####-###"),
        intitule_action=intitule,
        stagiaires=stagiaires,
        coordonnees_bancaires=rib,
        date_execution=date_execution,
        date_echeance=date_emission + timedelta(days=30),
        nouvelles_mentions=fake.boolean(chance_of_getting_true=50),
        categorie_operation="Prestations de services",
    )


# Le devis n'est pas tiré sur la même seed que la facture de même numéro : sans ce
# décalage, devis n° 1 et facture n° 1 sortiraient avec le même organisme, le même
# client, la même formation et les mêmes montants. Le corpus contiendrait alors des
# paires de quasi-jumeaux textuels au lieu de documents distincts, et la matrice de
# confusion facture / devis mesurerait un cas de figure qui n'existe pas en production.
DECALAGE_SEED_DEVIS = 500


def generer_devis(seed: int, professionnel: bool = True) -> Devis:
    graine = seed + DECALAGE_SEED_DEVIS
    fake = _faker(graine)

    organisme = generer_organisme(graine)
    client = generer_client(graine + 1, professionnel)
    rib = generer_rib(graine + 2, organisme.raison_sociale, organisme.adresse)

    intitule, duree = fake.random_element(CATALOGUE_FORMATIONS)
    nb_stagiaires = fake.random_int(1, 8) if professionnel else 1
    stagiaires = [f"{fake.first_name()} {fake.last_name()}" for _ in range(nb_stagiaires)]

    taux = Decimal("0") if organisme.exonere_tva else Decimal("20")
    lignes = _lignes_formation(fake, intitule, duree, nb_stagiaires, taux)

    # Un devis précède la prestation : sa date d'émission est antérieure au début prévu,
    # là où la facture suit une exécution déjà passée. C'est l'inversion la plus
    # discriminante entre les deux documents, et elle doit se lire dans les dates
    # elles-mêmes, pas seulement dans les intitulés.
    jours = fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)
    date_emission = DEBUT_PERIODE + timedelta(days=jours)
    duree_validite = fake.random_element(DUREES_VALIDITE_JOURS)
    date_debut_prevue = date_emission + timedelta(days=fake.random_int(15, 90))

    # Une journée de formation compte sept heures, décompte usuel du secteur.
    nb_journees = duree // 7
    delai = (f"{nb_journees} journée{'s' if nb_journees > 1 else ''} de 7 heures"
             if nb_journees else f"{duree} heures")

    return Devis(
        numero=f"{fake.random_element(PREFIXES_DEVIS)}{date_emission.year}"
               f"-{fake.numerify('####')}",
        date_emission=date_emission,
        organisme=organisme,
        client=client,
        lignes=lignes,
        reference_dossier=fake.numerify("AFF-####-###"),
        intitule_action=intitule,
        stagiaires=stagiaires,
        coordonnees_bancaires=rib,
        libelle=fake.random_element(LIBELLES_DEVIS),
        date_validite=date_emission + timedelta(days=duree_validite),
        duree_validite_jours=duree_validite,
        date_debut_prevue=date_debut_prevue,
        delai_realisation=delai,
        acompte_pourcentage=fake.random_element(ACOMPTES_POURCENT),
    )
