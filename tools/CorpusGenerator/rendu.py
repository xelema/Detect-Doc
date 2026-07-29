"""Rendu d'un document en PDF a partir d'un template HTML.

Le formatage des montants et des dates est ecrit a la main plutot que confie a la locale
du systeme : le corpus doit sortir identique sur n'importe quelle machine.
"""

import os
from datetime import date
from decimal import Decimal
from pathlib import Path

# Doit etre pose avant l'import de weasyprint, qui charge fontTools.
#
# WeasyPrint embarque un sous-ensemble de chaque police utilisee, et fontTools horodate
# ce sous-ensemble dans sa table `head`. Sans cette variable, deux rendus successifs du
# meme document different de trois octets et n'ont donc pas le meme hash, ce qui
# ruine la promesse du manifest. `SOURCE_DATE_EPOCH` est la convention des compilations
# reproductibles, fontTools la respecte. Valeur choisie : 1er janvier 2025.
os.environ.setdefault("SOURCE_DATE_EPOCH", "1735689600")

from jinja2 import Environment, FileSystemLoader, select_autoescape  # noqa: E402
from markupsafe import Markup  # noqa: E402
from weasyprint import HTML  # noqa: E402

DOSSIER_TEMPLATES = Path(__file__).parent / "templates"

# Couleurs de logo plausibles pour un organisme de formation. Le choix est pilote par
# la seed, donc stable d'une generation a l'autre.
COULEURS_LOGO = ["#1f4e79", "#256d5b", "#7a2f2f", "#4a3b6b", "#8a5a1f", "#2f5f7a"]


def euros(montant: Decimal) -> str:
    """Formate un montant a la francaise : 12 345,67 EUR."""
    entier, _, decimales = f"{montant:.2f}".partition(".")
    groupes = []
    while len(entier) > 3:
        groupes.insert(0, entier[-3:])
        entier = entier[:-3]
    groupes.insert(0, entier)
    return f"{' '.join(groupes)},{decimales} €"


def date_fr(jour: date) -> str:
    return jour.strftime("%d/%m/%Y")


def nombre(valeur: int) -> str:
    """Separe les milliers par une espace : 10 000."""
    texte = str(valeur)
    groupes = []
    while len(texte) > 3:
        groupes.insert(0, texte[-3:])
        texte = texte[:-3]
    groupes.insert(0, texte)
    return " ".join(groupes)


UNITES = ["zero", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit",
          "neuf", "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize"]
DIZAINES = {20: "vingt", 30: "trente", 40: "quarante", 50: "cinquante", 60: "soixante"}


def _sous_cent(valeur: int) -> str:
    """Nombre de 0 a 99 en toutes lettres.

    Le francais compte par vingtaines au-dela de soixante : 75 se dit "soixante-quinze"
    et 92 "quatre-vingt-douze". Ces deux plages se traitent donc en retranchant 60 ou 80
    puis en reecrivant le reste, plutot qu'avec un nom de dizaine.
    """
    if valeur < 17:
        return UNITES[valeur]
    if valeur < 20:
        return f"dix-{UNITES[valeur - 10]}"

    if valeur < 70:
        dizaine, reste = (valeur // 10) * 10, valeur % 10
        if reste == 0:
            return DIZAINES[dizaine]
        # "vingt et un", mais "vingt-deux" : seul le un prend la conjonction.
        if reste == 1:
            return f"{DIZAINES[dizaine]} et un"
        return f"{DIZAINES[dizaine]}-{UNITES[reste]}"

    if valeur < 80:
        reste = valeur - 60
        if reste == 11:
            return "soixante et onze"
        return f"soixante-{_sous_cent(reste)}"

    # Quatre-vingts ne prend son s que lorsqu'il n'est suivi d'aucun autre nombre,
    # et ne prend jamais la conjonction : on dit "quatre-vingt-un".
    reste = valeur - 80
    if reste == 0:
        return "quatre-vingts"
    return f"quatre-vingt-{_sous_cent(reste)}"


def _sous_mille(valeur: int) -> str:
    centaines, reste = divmod(valeur, 100)
    if centaines == 0:
        return _sous_cent(reste)

    tete = "cent" if centaines == 1 else f"{UNITES[centaines]} cent"
    if reste == 0:
        return tete + ("s" if centaines > 1 else "")
    return f"{tete} {_sous_cent(reste)}"


def montant_en_lettres(montant: Decimal) -> str:
    """Montant ecrit en toutes lettres, comme sur une facture de cabinet.

    Gere jusqu'a 999 999 euros, ce qui couvre tres largement une facture de formation.
    """
    entier = int(montant)
    centimes = int((montant - entier) * 100)

    milliers, reste = divmod(entier, 1000)
    if milliers == 0:
        texte = _sous_mille(reste)
    elif milliers == 1:
        texte = "mille" if reste == 0 else f"mille {_sous_mille(reste)}"
    else:
        texte = f"{_sous_mille(milliers)} mille"
        if reste:
            texte += f" {_sous_mille(reste)}"

    texte += " euro" + ("s" if entier > 1 else "")
    if centimes:
        texte += f" et {_sous_cent(centimes)} centime" + ("s" if centimes > 1 else "")
    return texte


def couleur_logo(seed: int) -> str:
    """Couleur d'accent du document, partagee entre le logo et le template."""
    return COULEURS_LOGO[seed % len(COULEURS_LOGO)]


def logo_svg(raison_sociale: str, seed: int, couleur: str = "") -> Markup:
    """Logo invente, construit a partir des initiales de la raison sociale.

    Un vrai logo d'entreprise ne peut pas etre reutilise, et un document sans logo ne
    ressemble a rien de reel. Un bloc de couleur avec des initiales est ce que produisent
    la plupart des petits organismes, et il est genere sans fichier externe.

    La couleur vient du style quand il en impose une, pour que le logo et le document
    s'accordent. A defaut, elle est tiree de la seed.
    """
    initiales = "".join(mot[0] for mot in raison_sociale.split()[:2]).upper()
    couleur = couleur or couleur_logo(seed)
    # Markup, sinon l'echappement automatique de Jinja2 imprime le source SVG en clair
    # dans le document. L'autoechappement reste actif pour tout le reste du template.
    return Markup(
        f'<svg width="54" height="54" viewBox="0 0 54 54" xmlns="http://www.w3.org/2000/svg">'
        f'<rect width="54" height="54" rx="8" fill="{couleur}"/>'
        f'<text x="27" y="35" font-family="Liberation Sans, sans-serif" font-size="22" '
        f'font-weight="bold" fill="#ffffff" text-anchor="middle">{initiales}</text>'
        f"</svg>"
    )


def _environnement() -> Environment:
    env = Environment(
        loader=FileSystemLoader(DOSSIER_TEMPLATES),
        autoescape=select_autoescape(["html"]),
    )
    env.filters["euros"] = euros
    env.filters["date_fr"] = date_fr
    env.filters["nombre"] = nombre
    env.filters["lettres"] = montant_en_lettres
    return env


def rendre_pdf(template: str, destination: Path, **contexte) -> int:
    """Rend un template en PDF. Retourne le nombre de pages produites.

    Le nombre de pages est lu sur le document rendu plutot que relu depuis le PDF :
    c'est gratuit ici, et ca evite une dependance de plus pour une information que
    le manifest doit porter.
    """
    html = _environnement().get_template(template).render(**contexte)

    destination.parent.mkdir(parents=True, exist_ok=True)
    # `base_url` permet au template de referencer des ressources locales si besoin.
    document = HTML(string=html, base_url=str(DOSSIER_TEMPLATES)).render()
    document.write_pdf(destination)
    return len(document.pages)
