"""Interface commune à tout type de document du corpus.

Chaque type vit dans son propre fichier de ce dossier et y expose une instance `DOCUMENT`
d'une sous-classe de `DocumentType`. `generer.py` se contente de les enregistrer et de les
appeler : ajouter un type ne demande donc de modifier que ce nouveau fichier, plus
`manifest.TYPES_CONNUS`, qui reste volontairement indépendant pour que le validateur du
manifest n'ait pas à importer les bibliothèques de rendu.
"""

from abc import ABC, abstractmethod


class DocumentType(ABC):
    """Un type de document produit par le générateur.

    `type_attendu` est la valeur écrite dans le manifest, elle doit rester alignée avec
    une entrée de `manifest.TYPES_CONNUS`. `physique` signale un document dont le rendu
    propre n'est qu'une étape de fabrication, écrasée par sa numérisation avant d'entrer
    au manifest : une carte de visite ou une feuille d'émargement arrivent en gestion
    documentaire comme la reprise d'un objet physique, pas comme un fichier natif.
    """

    type_attendu: str
    physique: bool = False

    @abstractmethod
    def produire(self, numero: int) -> dict:
        """Génère un document et retourne son entrée de manifest."""
