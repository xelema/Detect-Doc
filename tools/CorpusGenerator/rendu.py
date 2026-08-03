"""Rendu d'un document en PDF à partir d'un template HTML.

Le formatage des montants et des dates est écrit à la main plutôt que confié à la locale
du système : le corpus doit sortir identique sur n'importe quelle machine.
"""

import os
from datetime import date
from decimal import Decimal
from functools import cache
from pathlib import Path

from configuration import flux

# Doit être posé avant l'import de weasyprint, qui charge fontTools.
#
# WeasyPrint embarque un sous-ensemble de chaque police utilisée, et fontTools horodate
# ce sous-ensemble dans sa table head. Sans cette variable, deux rendus successifs du
# même document diffèrent de trois octets et n'ont donc pas le même hash, ce qui
# ruine la promesse du manifest. SOURCE_DATE_EPOCH est la convention des compilations
# reproductibles, fontTools la respecte. Valeur choisie : 1er janvier 2025.
# Affectation directe et non setdefault : un shell ou un conteneur qui aurait déjà
# posé la variable imposerait sinon sa propre date, et le corpus produit différerait sans
# que rien ne l'explique.
os.environ["SOURCE_DATE_EPOCH"] = "1735689600"

import fitz  # noqa: E402
from jinja2 import Environment, FileSystemLoader, select_autoescape  # noqa: E402
from markupsafe import Markup  # noqa: E402
from weasyprint import HTML  # noqa: E402

DOSSIER_TEMPLATES = Path(__file__).parent / "templates"


# Le séparateur de milliers du format ',' est fixe, contrairement à 'n' qui suit la
# locale du système. Le corpus sort donc identique sur n'importe quelle machine.
def euros(montant: Decimal) -> str:
    """Formate un montant à la française : 12 345,67 EUR."""
    return f"{montant:,.2f}".replace(",", " ").replace(".", ",") + " €"


def nombre(valeur: int) -> str:
    """Sépare les milliers par une espace : 10 000."""
    return f"{valeur:,}".replace(",", " ")


def date_fr(jour: date) -> str:
    return jour.strftime("%d/%m/%Y")


UNITES = ["zero", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit",
          "neuf", "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize"]
DIZAINES = {20: "vingt", 30: "trente", 40: "quarante", 50: "cinquante", 60: "soixante"}


def _sous_cent(valeur: int) -> str:
    """Nombre de 0 à 99 en toutes lettres.

    Le français compte par vingtaines au-delà de soixante : 75 se dit "soixante-quinze"
    et 92 "quatre-vingt-douze". Ces deux plages se traitent donc en retranchant 60 ou 80
    puis en réécrivant le reste, plutôt qu'avec un nom de dizaine.
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
    """Montant écrit en toutes lettres, comme sur une facture de cabinet.

    Gère jusqu'à 999 999 euros, ce qui couvre très largement une facture de formation.
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


ENCRE_MANUSCRITE = "#243a8c"


def paraphe_svg(texte: str) -> Markup:
    """Signature manuscrite, dessinée en courbes et non écrite dans une police cursive.

    Un paraphe posé avec une police manuscrite reste du texte : sur un PDF à texte natif,
    un simple copier-coller rendrait le nom du signataire, ce qu'aucune feuille signée à la
    main ne fait. Le document mentirait alors sur ce qu'il est, et le service de détection
    apprendrait une régularité qui n'existe pas en production. Le tracé est donc une
    courbe, sans aucun caractère derrière.

    Le nom ne sert qu'à amorcer le tirage : un même signataire pose toujours le même
    paraphe, deux personnes différentes ne signent pas pareil, et le corpus reste
    reproductible d'une exécution à l'autre.
    """
    tirage = flux("paraphe", texte)
    largeur, hauteur = 100, 30
    ligne_base = tirage.uniform(19.0, 23.0)

    # Boucle d'attaque, l'initiale de la signature : plus haute et plus large que le corps
    # du nom, comme la majuscule qu'elle représente.
    fin_initiale = tirage.uniform(18, 30)
    commandes = [
        f"M{tirage.uniform(2, 6):.1f} {ligne_base + tirage.uniform(1, 5):.1f}",
        f"C{tirage.uniform(0, 6):.1f} {tirage.uniform(1, 6):.1f} "
        f"{fin_initiale + tirage.uniform(-4, 4):.1f} {tirage.uniform(0, 5):.1f} "
        f"{fin_initiale:.1f} {ligne_base:.1f}",
    ]

    # Corps du nom. L'amplitude est tirée boucle par boucle : une ondulation régulière se
    # verrait immédiatement comme un motif, là où une main fatigue et accélère.
    boucles = tirage.randint(3, 6)
    fin_corps = tirage.uniform(72, 90)
    pas = (fin_corps - fin_initiale) / boucles
    x = fin_initiale
    for _ in range(boucles):
        hauteur_boucle = tirage.uniform(5, 17)
        commandes.append(
            f"C{x + pas * 0.3:.1f} {ligne_base - hauteur_boucle:.1f} "
            f"{x + pas * 0.7:.1f} {ligne_base + tirage.uniform(1, 6):.1f} "
            f"{x + pas:.1f} {ligne_base + tirage.uniform(-3, 2):.1f}")
        x += pas

    # Trait de fin, jeté vers la droite au-dessus du reste du paraphe.
    commandes.append(
        f"C{x + 4:.1f} {tirage.uniform(6, 16):.1f} "
        f"{largeur - 14:.1f} {tirage.uniform(3, 12):.1f} "
        f"{largeur - tirage.uniform(1, 10):.1f} {tirage.uniform(8, 20):.1f}")

    traits = [f'<path d="{" ".join(commandes)}"/>']
    if tirage.random() < 0.35:
        souligne_y = ligne_base + tirage.uniform(4, 7)
        traits.append(
            f'<path d="M{tirage.uniform(4, 14):.1f} {souligne_y:.1f} '
            f'Q{largeur / 2:.1f} {souligne_y + tirage.uniform(-3, 3):.1f} '
            f'{largeur - tirage.uniform(6, 24):.1f} {souligne_y:.1f}"/>')

    # La largeur est posée sur l'élément lui-même : deux personnes ne signent pas sur la
    # même longueur, et une case dont tous les paraphes tiennent exactement la même place
    # ne ressemble à aucune feuille réelle.
    return Markup(
        f'<svg viewBox="0 0 {largeur} {hauteur}" xmlns="http://www.w3.org/2000/svg" '
        f'style="width: {tirage.uniform(58, 100):.0f}%" '
        f'fill="none" stroke="{ENCRE_MANUSCRITE}" '
        f'stroke-width="{tirage.uniform(1.3, 2.2):.1f}" stroke-linecap="round">'
        + "".join(traits) + "</svg>"
    )


def logo_svg(raison_sociale: str, couleur: str) -> Markup:
    """Logo inventé, construit à partir des initiales de la raison sociale.

    Un vrai logo d'entreprise ne peut pas être réutilisé, et un document sans logo ne
    ressemble à rien de réel. Un bloc de couleur avec des initiales est ce que produisent
    la plupart des petits organismes, et il est généré sans fichier externe.

    La couleur vient du style, pour que le logo et le document s'accordent.
    """
    initiales = "".join(mot[0] for mot in raison_sociale.split()[:2]).upper()
    # Markup, sinon l'échappement automatique de Jinja2 imprime le source SVG en clair
    # dans le document. L'auto-échappement reste actif pour tout le reste du template.
    return Markup(
        f'<svg width="54" height="54" viewBox="0 0 54 54" xmlns="http://www.w3.org/2000/svg">'
        f'<rect width="54" height="54" rx="8" fill="{couleur}"/>'
        f'<text x="27" y="35" font-family="Liberation Sans, sans-serif" font-size="22" '
        f'font-weight="bold" fill="#ffffff" text-anchor="middle">{initiales}</text>'
        f"</svg>"
    )


def qr_svg(seed: int, taille_mm: float = 15) -> Markup:
    """Motif de QR code, factice mais de forme correcte.

    Une carte de visite sur deux en porte un, et il occupe une surface que l'OCR verra
    comme un bloc de bruit. Le contenu n'a aucune importance ici, seule la forme compte :
    grille de 21 modules et trois cibles de repérage aux angles, comme un QR de version 1.
    Dessiner le motif évite d'ajouter une bibliothèque d'encodage pour un carré noir.

    Le QR d'une vraie carte encode les mêmes coordonnées que le texte imprimé. C'est une
    limite à retenir pour le masquage : traiter le texte et laisser le code n'aurait rien
    masqué du tout.
    """
    modules = 21
    tirage = flux("qr", seed)
    cibles = [(0, 0), (modules - 7, 0), (0, modules - 7)]

    def dans_une_cible(x: int, y: int) -> bool:
        return any(cx <= x < cx + 7 and cy <= y < cy + 7 for cx, cy in cibles)

    formes = [f'<rect x="{x}" y="{y}" width="1" height="1"/>'
              for y in range(modules) for x in range(modules)
              if not dans_une_cible(x, y) and tirage.random() < 0.45]

    for cx, cy in cibles:
        formes.append(f'<rect x="{cx}" y="{cy}" width="7" height="7"/>')
        formes.append(f'<rect x="{cx + 1}" y="{cy + 1}" width="5" height="5" fill="#fff"/>')
        formes.append(f'<rect x="{cx + 2}" y="{cy + 2}" width="3" height="3"/>')

    return Markup(
        f'<svg width="{taille_mm}mm" height="{taille_mm}mm" '
        f'viewBox="0 0 {modules} {modules}" xmlns="http://www.w3.org/2000/svg" '
        f'shape-rendering="crispEdges" fill="#000">' + "".join(formes) + "</svg>"
    )


def portrait_svg(couleur: str, taille_mm: float = 17) -> Markup:
    """Silhouette, pour les cartes qui portent la photo de la personne.

    Un visage n'a pas sa place dans un corpus de test, et un disque vide ne ressemblerait
    à rien. Une silhouette occupe la même surface et produit le même effet sur la mise en
    page, qui est tout ce qui compte ici : la photo ne porte aucun texte.
    """
    return Markup(
        f'<svg width="{taille_mm}mm" height="{taille_mm}mm" viewBox="0 0 40 40" '
        f'xmlns="http://www.w3.org/2000/svg">'
        f'<circle cx="20" cy="20" r="20" fill="#dcdcdc"/>'
        f'<circle cx="20" cy="15" r="7.5" fill="{couleur}"/>'
        f'<path d="M5 40a15 15 0 0 1 30 0z" fill="{couleur}"/>'
        f"</svg>"
    )


@cache
def _environnement() -> Environment:
    """Environnement Jinja2, construit une seule fois.

    L'environnement porte le cache des templates compilés. En reconstruire un à chaque
    document recompilerait les cinq templates à chaque fois, ce qui coûte seize
    millisecondes par document pour rien.
    """
    env = Environment(
        loader=FileSystemLoader(DOSSIER_TEMPLATES),
        autoescape=select_autoescape(["html"]),
    )
    env.filters["euros"] = euros
    env.filters["date_fr"] = date_fr
    env.filters["nombre"] = nombre
    env.filters["lettres"] = montant_en_lettres
    env.filters["paraphe"] = paraphe_svg
    return env


def rendre_pdf(template: str, destination: Path, **contexte) -> int:
    """Rend un template en PDF. Retourne le nombre de pages produites.

    Le nombre de pages est lu sur le document rendu plutôt que relu depuis le PDF :
    c'est gratuit ici, et ça évite une dépendance de plus pour une information que
    le manifest doit porter.
    """
    html = _environnement().get_template(template).render(**contexte)

    destination.parent.mkdir(parents=True, exist_ok=True)
    # ase_url permet au template de référencer des ressources locales si besoin.
    document = HTML(string=html, base_url=str(DOSSIER_TEMPLATES)).render()
    document.write_pdf(destination)
    return len(document.pages)


def texte_natif(chemin: Path) -> str:
    """Texte natif du PDF qui vient d'être rendu à `chemin`.

    Vérité terrain pour la comparaison des moteurs OCR : le générateur connaît
    par construction le contenu exact de chaque document, plutôt que de le redéduire d'un
    fichier séparé. À appeler juste après `rendre_pdf`, avant qu'une éventuelle
    numérisation n'écrase le fichier avec sa version dégradée : un paraphe manuscrit, par
    exemple, est une courbe SVG et ne ressort jamais dans ce texte, exactement comme il ne
    ressortirait pas d'un OCR sur le document scanné.
    """
    with fitz.open(chemin) as document:
        return "".join(page.get_text() for page in document)
