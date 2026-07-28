"""Rendu d'un document en PDF a partir d'un gabarit HTML.

Le formatage des montants et des dates est ecrit a la main plutot que confie a la locale
du systeme : le corpus doit sortir identique sur n'importe quelle machine.
"""

from datetime import date
from decimal import Decimal
from pathlib import Path

from jinja2 import Environment, FileSystemLoader, select_autoescape
from markupsafe import Markup
from weasyprint import HTML

DOSSIER_GABARITS = Path(__file__).parent / "templates"

# Couleurs de logo plausibles pour un organisme de formation. Le choix est pilote par
# la graine, donc stable d'une generation a l'autre.
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


def logo_svg(raison_sociale: str, graine: int) -> Markup:
    """Logo invente, construit a partir des initiales de la raison sociale.

    Un vrai logo d'entreprise ne peut pas etre reutilise, et un document sans logo ne
    ressemble a rien de reel. Un bloc de couleur avec des initiales est ce que produisent
    la plupart des petits organismes, et il est genere sans fichier externe.
    """
    initiales = "".join(mot[0] for mot in raison_sociale.split()[:2]).upper()
    couleur = COULEURS_LOGO[graine % len(COULEURS_LOGO)]
    # Markup, sinon l'echappement automatique de Jinja2 imprime le source SVG en clair
    # dans le document. L'autoechappement reste actif pour tout le reste du gabarit.
    return Markup(
        f'<svg width="54" height="54" viewBox="0 0 54 54" xmlns="http://www.w3.org/2000/svg">'
        f'<rect width="54" height="54" rx="8" fill="{couleur}"/>'
        f'<text x="27" y="35" font-family="Liberation Sans, sans-serif" font-size="22" '
        f'font-weight="bold" fill="#ffffff" text-anchor="middle">{initiales}</text>'
        f"</svg>"
    )


def _environnement() -> Environment:
    env = Environment(
        loader=FileSystemLoader(DOSSIER_GABARITS),
        autoescape=select_autoescape(["html"]),
    )
    env.filters["euros"] = euros
    env.filters["date_fr"] = date_fr
    env.filters["nombre"] = nombre
    return env


def rendre_pdf(gabarit: str, destination: Path, **contexte) -> Path:
    """Rend un gabarit en PDF. Retourne le chemin ecrit."""
    html = _environnement().get_template(gabarit).render(**contexte)

    destination.parent.mkdir(parents=True, exist_ok=True)
    # `base_url` permet au gabarit de referencer des ressources locales si besoin.
    HTML(string=html, base_url=str(DOSSIER_GABARITS)).write_pdf(destination)
    return destination
