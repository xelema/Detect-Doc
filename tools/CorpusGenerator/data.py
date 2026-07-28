"""Generation des donnees factices d'un document du corpus.

Deux principes qui commandent tout ce fichier.

Le generateur connait les valeurs qu'il injecte : c'est la verite terrain qui permettra
de mesurer le masquage des donnees personnelles sans annotation manuelle.

Tout est pilote par une graine. Le meme numero de document redonne exactement le meme
contenu, ce qui autorise a ne pas versionner les binaires du corpus : ils se refabriquent.
"""

from dataclasses import dataclass, field
from datetime import date, timedelta
from decimal import Decimal

from faker import Faker

from cles import cle_rib, iban_francais, numero_tva_intracommunautaire

# Un organisme de formation doit afficher son numero de declaration d'activite et,
# depuis 2022, sa certification Qualiopi. Ce sont des marqueurs forts du type "facture
# de formation", et ils n'apparaissent sur aucun autre type de document.
CATALOGUE_FORMATIONS = [
    ("Habilitation electrique B1V B2V BR BC", 21),
    ("SST - Sauveteur secouriste du travail", 14),
    ("CACES R489 categorie 3 - Chariot elevateur", 21),
    ("Prevention des risques lies a l'activite physique", 14),
    ("Excel - Perfectionnement tableaux croises", 14),
    ("Management d'equipe - Niveau 1", 21),
    ("Anglais professionnel - Niveau B1", 30),
    ("AIPR - Operateur", 7),
    ("Le RGPD pour les managers", 7),
    ("Gestes et postures au poste de travail", 7),
]

FORMES_JURIDIQUES = ["SAS", "SARL", "SASU", "EURL"]

# Periode sur laquelle sont tirees les dates des documents.
# Les dates sont calculees en jours entiers a partir de cette borne, et non avec
# `Faker.date_between` : cette methode passe par des horodatages convertis en heure
# locale et ne redonne pas le meme resultat d'une machine a l'autre a graine egale.
# Le corpus doit se refabriquer a l'identique, la generation ne peut donc dependre
# que d'arithmetique entiere.
DEBUT_PERIODE = date(2025, 1, 1)
FIN_PERIODE = date(2026, 6, 30)

# La raison sociale est construite ici plutot que prise de Faker : `company()` y colle
# deja une forme juridique, ce qui donnerait "Maury Henry SARL (SAS)" une fois la forme
# affichee separement dans le pied de facture. Et un organisme de formation ne s'appelle
# pas comme une entreprise generique, ces motifs sont ceux du secteur.
MOTIFS_ORGANISME = [
    "Institut {nom}",
    "{nom} Formation",
    "{nom} Competences",
    "Campus {ville}",
    "{nom} Conseil et Formation",
    "Academie {ville}",
    "Centre de formation {nom}",
]

# Noms de banques inventes. Aucune banque reelle n'est nommee : le corpus doit etre
# realiste dans sa forme, pas usurper une marque existante.
BANQUES = [
    ("Banque du Cerisier", "BCERFRPP"),
    ("Credit Meridien", "CMERFRP1"),
    ("Banque Lafayette et Associes", "BLAAFRPX"),
    ("Caisse de l'Ouvrage", "COUVFRP2"),
    ("Banque Atlantique du Ponant", "BAPOFRPP"),
]


@dataclass
class Coordonnees:
    """Bloc adresse commun a toutes les parties d'un document."""

    ligne1: str
    code_postal: str
    ville: str


@dataclass
class Organisme:
    """L'organisme de formation qui emet la facture."""

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
    # Un organisme de formation peut etre exonere de TVA au titre de l'article 261-4-4 a
    # du CGI. Les deux regimes existent en production, la facture n'a pas la meme tete.
    exonere_tva: bool


@dataclass
class Client:
    """Le destinataire. Professionnel dans la majorite des cas, particulier parfois."""

    est_professionnel: bool
    designation: str
    adresse: Coordonnees
    siren: str | None = None
    tva: str | None = None
    contact: str | None = None


@dataclass
class LigneFacture:
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
class Facture:
    numero: str
    date_emission: date
    date_execution: date
    date_echeance: date
    organisme: Organisme
    client: Client
    lignes: list[LigneFacture]
    reference_convention: str
    intitule_action: str
    stagiaires: list[str]
    # Les quatre mentions ajoutees par la reforme de la facturation electronique entrent
    # en vigueur par paliers, septembre 2026 puis septembre 2027. La production contient
    # donc un melange des deux formes, le corpus doit le refleter.
    nouvelles_mentions: bool
    adresse_livraison: Coordonnees | None
    categorie_operation: str
    coordonnees_bancaires: "Rib"
    acompte_verse: Decimal = Decimal("0.00")

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
    def reste_a_payer(self) -> Decimal:
        return self.total_ttc - self.acompte_verse


@dataclass
class Rib:
    """Coordonnees bancaires, utilisees dans le pave de bas de facture et comme
    document a part entiere."""

    titulaire: str
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

    @property
    def iban_formate(self) -> str:
        """L'IBAN s'imprime par groupes de 4 caracteres, comme sur un vrai releve."""
        return " ".join(self.iban[i:i + 4] for i in range(0, len(self.iban), 4))


def _faker(graine: int) -> Faker:
    fake = Faker("fr_FR")
    fake.seed_instance(graine)
    return fake


def _raison_sociale_organisme(fake: Faker) -> str:
    motif = fake.random_element(MOTIFS_ORGANISME)
    return motif.format(nom=fake.last_name(), ville=fake.city())


def _coordonnees(fake: Faker) -> Coordonnees:
    return Coordonnees(
        ligne1=fake.street_address().replace("\n", " "),
        code_postal=fake.postcode(),
        ville=fake.city(),
    )


def generer_rib(graine: int, titulaire: str | None = None,
                adresse: Coordonnees | None = None) -> Rib:
    fake = _faker(graine)

    code_banque = fake.numerify("#####")
    code_guichet = fake.numerify("#####")
    numero_compte = fake.numerify("###########")
    cle = cle_rib(code_banque, code_guichet, numero_compte)

    banque, bic = fake.random_element(BANQUES)
    adresse_agence = _coordonnees(fake)

    return Rib(
        titulaire=titulaire or fake.company(),
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
    )


def generer_organisme(graine: int) -> Organisme:
    fake = _faker(graine)
    # Le SIRET porte sa propre cle de Luhn sur ses 14 chiffres : le fabriquer en
    # concatenant un SIREN valide et 5 chiffres au hasard produirait un SIRET invalide.
    # On part donc du SIRET et on en deduit le SIREN, qui en est le prefixe.
    siret = fake.siret().replace(" ", "")
    siren = siret[:9]
    adresse = _coordonnees(fake)

    return Organisme(
        raison_sociale=_raison_sociale_organisme(fake),
        forme_juridique=fake.random_element(FORMES_JURIDIQUES),
        capital=fake.random_element([10000, 25000, 50000, 100000]),
        adresse=adresse,
        siren=siren,
        siret=siret,
        tva=numero_tva_intracommunautaire(siren),
        rcs_ville=adresse.ville,
        numero_declaration_activite=fake.numerify("## ## ##### ##"),
        telephone=fake.phone_number(),
        email=fake.company_email(),
        site_web=fake.domain_name(),
        exonere_tva=fake.boolean(chance_of_getting_true=30),
    )


def generer_client(graine: int, professionnel: bool) -> Client:
    fake = _faker(graine)
    adresse = _coordonnees(fake)

    if not professionnel:
        return Client(
            est_professionnel=False,
            designation=f"{fake.first_name()} {fake.last_name()}",
            adresse=adresse,
        )

    siren = fake.siren().replace(" ", "")
    return Client(
        est_professionnel=True,
        designation=fake.company(),
        adresse=adresse,
        siren=siren,
        tva=numero_tva_intracommunautaire(siren),
        contact=f"{fake.first_name()} {fake.last_name()}",
    )


def generer_facture(graine: int, professionnel: bool = True,
                    nouvelles_mentions: bool | None = None) -> Facture:
    fake = _faker(graine)

    organisme = generer_organisme(graine)
    client = generer_client(graine + 1, professionnel)
    rib = generer_rib(graine + 2, organisme.raison_sociale, organisme.adresse)

    intitule, duree = fake.random_element(CATALOGUE_FORMATIONS)
    nb_stagiaires = fake.random_int(1, 8) if professionnel else 1
    stagiaires = [f"{fake.first_name()} {fake.last_name()}" for _ in range(nb_stagiaires)]

    # Un organisme exonere facture sans TVA, sinon le taux normal s'applique.
    taux = Decimal("0") if organisme.exonere_tva else Decimal("20")
    prix_heure = Decimal(fake.random_element([28, 35, 42, 55, 70]))

    lignes = [
        LigneFacture(
            designation=intitule,
            detail=f"Action de formation, {duree} heures, {nb_stagiaires} stagiaire(s)",
            quantite=Decimal(duree * nb_stagiaires),
            unite="heure",
            prix_unitaire_ht=prix_heure,
            taux_tva=taux,
        )
    ]
    if fake.boolean(chance_of_getting_true=40):
        lignes.append(
            LigneFacture(
                designation="Supports pedagogiques",
                detail=f"{nb_stagiaires} classeur(s) remis en debut de session",
                quantite=Decimal(nb_stagiaires),
                unite="unite",
                prix_unitaire_ht=Decimal("45"),
                taux_tva=taux,
            )
        )

    jours = fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)
    date_execution = DEBUT_PERIODE + timedelta(days=jours)
    date_emission = date_execution + timedelta(days=fake.random_int(1, 10))

    if nouvelles_mentions is None:
        nouvelles_mentions = fake.boolean(chance_of_getting_true=50)

    return Facture(
        numero=f"FA{date_emission.year}-{fake.numerify('####')}",
        date_emission=date_emission,
        date_execution=date_execution,
        date_echeance=date_emission + timedelta(days=30),
        organisme=organisme,
        client=client,
        lignes=lignes,
        reference_convention=fake.numerify("CONV-####-###"),
        intitule_action=intitule,
        stagiaires=stagiaires,
        nouvelles_mentions=nouvelles_mentions,
        adresse_livraison=None,
        categorie_operation="Prestations de services",
        coordonnees_bancaires=rib,
        acompte_verse=Decimal("0.00"),
    )
