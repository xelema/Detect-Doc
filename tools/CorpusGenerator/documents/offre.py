"""Offre de stage, alternance ou emploi : capture d'une page web de site d'emploi,
imprimée en PDF depuis un navigateur, jamais un document émis par l'entreprise
elle-même.

Dix plateformes sources sur les vingt-quatre exemples réels, aucune dominante et aucun
tronc commun de mise en page : c'est cette variété qui porte le signal, comme pour la
carte de visite. Cinq templates de site couvrent les familles observées, institutionnel,
jobboard commercial, réseau professionnel, portail RH bilingue et éditorial jeune.

`autonome = True` : ce type n'est ni la reprise d'un objet physique, ni une pièce
comptable qui reçoit parfois un jumeau dégradé. Chaque instance est, dès sa
génération, soit un fichier à texte natif, soit une capture d'écran nette sans couche
texte, jamais les deux. `produire` tranche donc lui-même son régime final, voir
`documents/base.py`.
"""

import random
from dataclasses import asdict, dataclass
from datetime import date, timedelta

import manifest
from configuration import flux
from degradation import capturer_ecran, numerise
from documents.base import DocumentType
from documents.commun import (COULEURS, DEBUT_PERIODE, FIN_PERIODE, POLICES_SANS,
                              VILLES, faker, sans_accent)
from rendu import portrait_svg, rendre_pdf

TYPE_ATTENDU = "offre_emploi"

# (intitulé du poste, nature réelle du contrat, secteur). Calqué sur les métiers des
# vingt-quatre exemples réels : commerce, IT, hôtellerie, RH, marketing, industrie
# aéronautique, distribution, contrôle de gestion, statistiques, espaces verts.
POSTES = [
    ("Assistant commercial", "alternance", "Commerce, vente"),
    ("Chargé de développement commercial", "alternance", "Commerce, vente"),
    ("Contrôleur budgétaire", "apprentissage", "Formation professionnelle"),
    ("Assistant administratif", "alternance", "Activités des sièges sociaux"),
    ("Gestionnaire de projet IT applicatif", "apprentissage", "Informatique"),
    ("Réceptionniste d'hôtel", "apprentissage", "Hôtellerie et restauration"),
    ("Pâtissier-chocolatier", "apprentissage", "Boulangerie et pâtisserie"),
    ("Pilote de contrat", "alternance", "Distribution d'énergie"),
    ("Responsable de rayon magasin", "alternance", "Distribution, commerce de détail"),
    ("Technico-commercial sédentaire", "alternance", "Industrie manufacturière"),
    ("Contrôleur de gestion et comptable", "apprentissage", "Activités des sièges sociaux"),
    ("Assistant statisticien", "apprentissage", "Activités générales de sécurité sociale"),
    ("Mécanicien aéronautique", "professionnalisation", "Industrie aéronautique"),
    ("Ouvrier du paysage", "professionnalisation", "Travaux paysagers"),
    ("Animateur radio", "professionnalisation", "Audiovisuel, médias"),
    ("Assistant chef de produit", "stage", "Fabrication de produits alimentaires"),
    ("Chargé d'études marketing", "stage", "Agroalimentaire, boissons"),
    ("Consultant en transformation digitale", "stage", "Conseil, design"),
    ("Chargé de communication et marketing", "stage", "Logiciels, SaaS"),
    ("Assistant chef de projet éditorial", "stage", "Agence digitale"),
    ("Business developer", "stage", "Plateforme audiovisuelle"),
    ("Opérateur de fabrication composite", "stage", "Industrie aéronautique"),
]

NATURES_CONTRAT = {
    "stage": "Stage",
    "alternance": "Alternance",
    "apprentissage": "Apprentissage",
    "professionnalisation": "Contrat de professionnalisation",
}

# Libellés affichés à la place du vrai quand le champ contrat ment sur sa nature. Observé
# sur trois offres France Travail réelles : Centrakor et la CPAM en « CDD » pour un
# apprentissage, Synergie en « Intérim » pour un contrat de professionnalisation. Une
# règle qui ne lirait que ce champ manquerait ces trois cas.
LIBELLES_TROMPEURS = ["CDD", "Intérim"]

MOTIFS_EMPLOYEUR = [
    "Groupe {nom}", "{nom} Solutions", "{nom} Distribution", "Établissements {nom}",
    "{nom} Industrie", "{nom} Consulting", "Studio {nom}", "{nom} et Associés",
    "{nom} Technologies",
]

TAILLES_ENTREPRISE = [
    "moins de 10 salariés", "10 à 49 salariés", "50 à 99 salariés",
    "100 à 249 salariés", "250 à 999 salariés", "1 000 à 4 999 salariés",
    "plus de 5 000 salariés",
]

DUREES_CONTRAT = ["4 à 6 mois", "6 mois", "12 mois", "12 à 24 mois", "24 mois",
                  "10 mois", "14 mois"]

TELETRAVAILS = ["Non autorisé", "Ponctuel", "Partiel", "Fréquent"]

# Volontairement génériques plutôt que couplées au métier, comme les fonctions d'une
# carte de visite : le signal du type tient à la structure, liste à puces sous
# « missions » et « profil recherché », pas au contenu métier précis de chaque ligne.
MISSIONS = [
    "Participer au développement de l'activité auprès des clients et prospects",
    "Assurer le suivi administratif et la mise à jour des tableaux de bord",
    "Contribuer à la préparation et à l'analyse des dossiers en cours",
    "Rédiger des comptes rendus et des supports de présentation",
    "Accompagner l'équipe dans l'organisation des réunions et des événements",
    "Réaliser une veille sur les pratiques du secteur",
    "Participer à la relation avec les fournisseurs et les prestataires externes",
    "Assurer la coordination entre les différents services concernés",
    "Contribuer à l'amélioration continue des processus internes",
    "Effectuer le reporting régulier auprès du responsable de service",
    "Participer aux réunions d'équipe et contribuer aux décisions collectives",
    "Assister le tuteur dans le suivi quotidien des dossiers",
    "Contribuer à la mise en place de nouveaux outils de suivi",
    "Participer à des projets transverses avec d'autres services",
    "Effectuer des analyses de données pour éclairer les décisions",
]

PROFIL = [
    "Vous préparez actuellement un diplôme dans le domaine concerné",
    "Vous êtes reconnu pour votre rigueur et votre sens de l'organisation",
    "Vous maîtrisez les outils bureautiques courants",
    "Vous avez un bon relationnel et le goût du travail en équipe",
    "Vous êtes autonome, curieux et force de proposition",
    "Vous faites preuve de réactivité et d'adaptabilité",
    "Une première expérience dans le secteur serait appréciée",
    "Vous avez un niveau d'anglais correct, à l'écrit comme à l'oral",
    "Vous savez faire preuve d'initiative et de créativité",
    "Vous êtes à l'aise avec l'analyse de données et la synthèse",
]

PRESENTATIONS_ENTREPRISE = [
    "{entreprise} est un acteur reconnu du secteur {secteur}, présent sur l'ensemble "
    "du territoire.",
    "{entreprise} accompagne ses clients avec une équipe engagée dans le secteur "
    "{secteur}.",
    "Fondée depuis plusieurs années, {entreprise} s'est imposée comme une référence "
    "dans le secteur {secteur}.",
    "{entreprise} réunit des équipes passionnées, animées par les enjeux du secteur "
    "{secteur}.",
]

ROLES_CONTACT = [
    "Chargé de recrutement", "Chargée de recrutement", "Chargé de mission RH",
    "Chargée de mission RH", "Responsable Talent Acquisition",
]
ROLES_TEMOIGNAGE = [
    "Ingénieur du son", "Gestionnaire de paie", "Consultant", "Head of Marketing",
    "User Researcher", "Lead Product Designer",
]

# Bruit de bas de document : métiers et types de contrat sans rapport avec l'offre
# consultée. Mélange volontaire de CDI, CDD, intérim et alternance, comme observé sur
# le bloc « d'autres offres » des exemples France Travail réels.
METIERS_SIMILAIRES = [
    ("Contrôleur financier", "CDI"), ("Technicien de maintenance", "Intérim"),
    ("Chargé d'études VRD", "CDI"), ("Gestionnaire caution", "Intérim"),
    ("Data analyst", "CDI"), ("Ouvrier polyvalent", "CDI"),
    ("Conseiller clientèle", "CDI"), ("Chef d'équipe espaces verts", "CDI"),
    ("Technicien qualité", "CDI"), ("Attaché de recherche clinique", "Intérim"),
    ("Assistant comptable", "CDD"), ("Product Owner", "CDI"),
    ("Chef de produit", "CDI"), ("Ingénieur commercial", "CDI"),
    ("Vendeur comptoir", "Alternance"), ("Téléprospecteur", "CDI"),
]

ARTICLES_SUGGERES = [
    "Les formations qui recrutent le plus cette année",
    "Comment réussir son entretien d'embauche",
    "Le guide de la reconversion professionnelle",
    "Les métiers qui montent dans la tech",
    "Négocier sa première rémunération : nos conseils",
    "Top 10 des métiers de la transition écologique",
    "Rédiger une lettre de motivation qui se démarque",
    "Les compétences les plus recherchées cette année",
    "Comment préparer son premier jour en entreprise",
    "Le télétravail vu par les jeunes actifs",
    "Cinq conseils pour un CV percutant",
    "Reconversion : par où commencer ?",
]

MOIS_FR = ["Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août",
          "Septembre", "Octobre", "Novembre", "Décembre"]

# Cinq templates de site sans tronc commun de mise en page, un par famille observée.
# Chacun a sa propre plage de pages de bruit visées, calquée sur les exemples réels : un
# portail RH imprime peu de bruit, un site éditorial en imprime beaucoup. Le nombre de
# pages réellement obtenu peut différer de cette plage : voir `OffreDocument.produire`.
TEMPLATES = [
    {"fichier_html": "offre_01.html", "template": "institutionnel", "pages_bruit": (3, 5)},
    {"fichier_html": "offre_02.html", "template": "jobboard", "pages_bruit": (2, 6)},
    {"fichier_html": "offre_03.html", "template": "reseau", "pages_bruit": (4, 6)},
    {"fichier_html": "offre_04.html", "template": "portail-rh", "pages_bruit": (2, 4)},
    {"fichier_html": "offre_05.html", "template": "editorial", "pages_bruit": (5, 9)},
]

# Probabilité d'un contact nominatif par template. Le portail RH n'en affiche jamais,
# conformément aux exemples Workday/Airbus reçus ; le réseau professionnel et le site
# éditorial en affichent le plus souvent, avec une photo.
PROBABILITE_CONTACT = {
    "institutionnel": 0.25, "jobboard": 0.25, "reseau": 0.6,
    "portail-rh": 0.0, "editorial": 0.7,
}


@dataclass
class OffreSimilaire:
    """Une ligne du bruit de bas de document : une offre sans rapport avec celle
    consultée, qui peut porter n'importe quel type de contrat."""

    intitule: str
    entreprise: str
    ville: str
    contrat: str


@dataclass
class Contact:
    """Contact nominatif, recruteur ou collaborateur, parfois avec sa photo.

    `nom` est None sur le template éditorial : les portraits de collaborateurs n'y sont
    légendés que d'un prénom, jamais d'un nom complet.
    """

    prenom: str
    nom: str | None
    role: str
    avec_photo: bool

    @property
    def nom_complet(self) -> str:
        return f"{self.prenom} {self.nom}" if self.nom else self.prenom


@dataclass
class OffreEmploi:
    """Contenu d'une offre de stage, alternance ou emploi."""

    intitule_poste: str
    nature_contrat: str
    entreprise: str
    secteur: str
    taille_entreprise: str
    ville: str
    code_postal: str
    type_contrat_affiche: str
    duree: str
    date_debut_libelle: str
    remuneration: str
    teletravail: str
    missions: list[str]
    profil: list[str]
    presentation_entreprise: str
    date_publication: date
    horodatage_impression: str
    jours_avant_capture: int
    nombre_clics: int
    url_capture: str
    contact: Contact | None
    offres_similaires: list[OffreSimilaire]

    @property
    def libelle_nature(self) -> str:
        return NATURES_CONTRAT[self.nature_contrat]


@dataclass
class StyleOffre:
    """Présentation d'une offre : template de site, régime, contrat trompeur.

    `fichier_html` est le fichier Jinja2 à rendre ; `template` est la famille de site
    imitée, institutionnel, jobboard, réseau, portail RH ou éditorial. Les deux
    coexistent comme `template` et `famille` sur `StyleEmargement` : l'un désigne le
    fichier, l'autre ce qu'il représente.
    """

    fichier_html: str
    template: str
    police: str
    couleur: str
    # Nombre de pages effectivement rendues. Fixé après coup par `produire`, sur le
    # compte réel de WeasyPrint plutôt que sur l'objectif qui a dimensionné le bruit de
    # bas de document : un contenu principal plus long qu'une page, notamment sur le
    # template jobboard avec son formulaire de candidature, ne doit jamais faire
    # mentir le manifest sur le nombre de pages du document.
    nombre_pages: int
    # Capture d'écran nette, sans couche texte. Ni bruit ni rotation : ce n'est pas la
    # reprise d'un objet physique, seule la couche texte disparaît. Sa part se règle dans
    # le TOML, un sixième des exemples réels étant dans ce cas.
    capture_ecran: bool
    contrat_trompeur: bool
    avec_cookies_repetes: bool

    def as_dict(self) -> dict:
        return asdict(self)


def _libelle_mois(jour: date) -> str:
    return f"{MOIS_FR[jour.month - 1]} {jour.year}"


def _horodatage(date_publication: date, tirage: random.Random) -> str:
    """Date et heure d'impression, toujours postérieures à la publication.

    `%d/%m/%Y` ne dépend que des champs numériques de la date, jamais d'un nom de mois
    ou de jour : contrairement à un format littéral, il reste identique quelle que soit
    la locale de la machine qui régénère le corpus.
    """
    capture = date_publication + timedelta(days=tirage.randint(0, 20))
    return (f"{capture.strftime('%d/%m/%Y')} "
           f"{tirage.randint(8, 18):02d}:{tirage.randint(0, 59):02d}")


def _slug(texte: str) -> str:
    return sans_accent(texte).lower().replace(" ", "-").replace("'", "")


def _url_capture(template: str, entreprise: str, intitule: str,
                 tirage: random.Random) -> str:
    """URL de la page capturée, dans la forme propre à chaque plateforme source."""
    slug_entreprise = _slug(entreprise)
    slug_poste = _slug(intitule)
    if template == "institutionnel":
        return ("https://candidat.francetravail.fr/offres/recherche/detail/"
               f"{tirage.getrandbits(24):06x}")
    if template == "jobboard":
        return f"https://www.hellowork.com/fr-fr/emplois/{tirage.randint(10000000, 99999999)}.html"
    if template == "reseau":
        return f"https://www.linkedin.com/jobs/view/{tirage.randint(1000000000, 9999999999)}/"
    if template == "portail-rh":
        return (f"https://{slug_entreprise}.wd3.myworkdayjobs.com/en-US/"
               f"{slug_entreprise}/job/{slug_poste}")
    return f"https://www.welcometothejungle.com/fr/companies/{slug_entreprise}/jobs/{slug_poste}"


def style_offre(seed: int) -> StyleOffre:
    """Style d'une offre. Le template tourne sur la seed, comme les templates de carte
    de visite ou les familles de feuille d'émargement, pour qu'aucun ne reste
    sous-représenté sur un corpus de taille modeste."""
    tirage = flux("offre", seed)
    modele = TEMPLATES[(seed - 1) % len(TEMPLATES)]
    minimum, maximum = modele["pages_bruit"]

    return StyleOffre(
        fichier_html=modele["fichier_html"],
        template=modele["template"],
        police=tirage.choice(POLICES_SANS),
        couleur=tirage.choice(COULEURS),
        nombre_pages=tirage.randint(minimum, maximum),
        capture_ecran=numerise(TYPE_ATTENDU, seed),
        contrat_trompeur=(modele["template"] == "institutionnel"
                          and tirage.random() < 0.4),
        avec_cookies_repetes=(modele["template"] == "editorial"),
    )


def generer_offre(seed: int, style: StyleOffre) -> OffreEmploi:
    fake = faker(seed)
    tirage = flux("offre-contenu", seed)

    intitule, nature, secteur = fake.random_element(POSTES)
    code_postal, ville = fake.random_element(VILLES)
    entreprise = fake.random_element(MOTIFS_EMPLOYEUR).format(nom=fake.last_name())

    libelle_reel = NATURES_CONTRAT[nature]
    trompeur = (style.contrat_trompeur
               and nature in {"apprentissage", "professionnalisation"})
    type_contrat_affiche = tirage.choice(LIBELLES_TROMPEURS) if trompeur else libelle_reel

    jours = fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)
    date_publication = DEBUT_PERIODE + timedelta(days=jours)
    date_debut = date_publication + timedelta(days=tirage.randint(30, 150))

    if nature == "stage" and tirage.random() < 0.15:
        remuneration = "Aucune"
    elif tirage.random() < 0.3:
        remuneration = "Information non renseignée"
    else:
        remuneration = f"{tirage.randint(600, 1900)} EUR / mois"

    missions = tirage.sample(MISSIONS, tirage.randint(4, 7))
    profil = tirage.sample(PROFIL, tirage.randint(3, 5))

    contact = None
    if tirage.random() < PROBABILITE_CONTACT[style.template]:
        if style.template == "editorial":
            prenom = fake.first_name()
            contact = Contact(prenom=prenom, nom=None,
                              role=tirage.choice(ROLES_TEMOIGNAGE), avec_photo=True)
        else:
            prenom = fake.first_name_male() if fake.boolean() else fake.first_name_female()
            contact = Contact(prenom=prenom, nom=fake.last_name(),
                              role=tirage.choice(ROLES_CONTACT),
                              avec_photo=(style.template == "reseau"))

    nombre_bruit = max(style.nombre_pages - 1, 0)
    offres_similaires = []
    for _ in range(nombre_bruit * 4):
        metier, contrat = fake.random_element(METIERS_SIMILAIRES)
        _, ville_similaire = fake.random_element(VILLES)
        offres_similaires.append(OffreSimilaire(
            intitule=metier,
            entreprise=fake.random_element(MOTIFS_EMPLOYEUR).format(nom=fake.last_name()),
            ville=ville_similaire,
            contrat=contrat,
        ))

    modele_presentation = fake.random_element(PRESENTATIONS_ENTREPRISE)

    return OffreEmploi(
        intitule_poste=intitule,
        nature_contrat=nature,
        entreprise=entreprise,
        secteur=secteur,
        taille_entreprise=tirage.choice(TAILLES_ENTREPRISE),
        ville=ville,
        code_postal=code_postal,
        type_contrat_affiche=type_contrat_affiche,
        duree=tirage.choice(DUREES_CONTRAT),
        date_debut_libelle=_libelle_mois(date_debut),
        remuneration=remuneration,
        teletravail=tirage.choice(TELETRAVAILS),
        missions=missions,
        profil=profil,
        presentation_entreprise=modele_presentation.format(
            entreprise=entreprise, secteur=secteur.lower()),
        date_publication=date_publication,
        horodatage_impression=_horodatage(date_publication, tirage),
        jours_avant_capture=tirage.randint(0, 20),
        nombre_clics=tirage.randint(5, 150),
        url_capture=_url_capture(style.template, entreprise, intitule, tirage),
        contact=contact,
        offres_similaires=offres_similaires,
    )


class OffreDocument(DocumentType):
    type_attendu = TYPE_ATTENDU
    autonome = True

    def produire(self, numero: int) -> dict:
        style = style_offre(numero)
        offre = generer_offre(numero, style)
        identifiant = f"off-{numero:04d}"
        chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

        contact_portrait = (portrait_svg(style.couleur)
                            if offre.contact and offre.contact.avec_photo else None)
        pages = rendre_pdf(
            style.fichier_html, chemin,
            o=offre,
            s=style,
            contact_portrait=contact_portrait,
            articles=ARTICLES_SUGGERES,
        )
        # Le contenu principal déborde parfois sur une deuxième page, notamment sur le
        # template jobboard avec son formulaire de candidature intégré : le nombre de
        # pages réellement rendu fait foi, jamais l'objectif qui a dimensionné le bruit
        # de bas de document. Un manifest qui annoncerait un autre nombre de pages
        # mentirait sur ce qu'il décrit.
        style.nombre_pages = pages

        tags = [style.template]
        tags.append("contrat-trompeur"
                    if offre.type_contrat_affiche != offre.libelle_nature
                    else "contrat-conforme")
        if offre.contact is not None:
            tags.append("contact-photo" if offre.contact.avec_photo
                        else "contact-nominatif")
        if offre.remuneration == "Aucune":
            tags.append("non-remunere")
        if style.capture_ecran:
            tags.append("capture-ecran")

        pii = {}
        if offre.contact is not None:
            if offre.contact.nom is None:
                pii["contact_prenom"] = offre.contact.prenom
            else:
                pii["contact_nom_complet"] = offre.contact.nom_complet

        entree = manifest.construire_entree(
            identifiant, chemin, self.type_attendu, numero, pages,
            variante=style.as_dict(),
            pii=pii,
            tags=tags,
        )

        if style.capture_ecran:
            capturer_ecran(chemin, chemin, numero)
            entree["sha256"] = manifest.hash_fichier(chemin)
            entree["regime"] = "scanne"

        return entree


DOCUMENT = OffreDocument()
