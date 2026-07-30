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

    `autonome` signale un type dont `produire` a déjà tranché lui-même son régime final,
    texte natif ou capture nette, sans détour par la reprise d'un objet. `generer.py`
    n'y ajoute alors ni reprise forcée ni jumeau dégradé probabiliste : contrairement à
    la carte de visite, ce n'est pas la reprise d'un objet physique, et contrairement à
    la facture, il n'existe jamais deux versions du même document. Une offre d'emploi
    imprimée depuis un navigateur est soit un fichier natif, soit une capture d'écran,
    jamais les deux, et jamais un objet qu'on aurait scanné.
    """

    type_attendu: str
    physique: bool = False
    autonome: bool = False

    @abstractmethod
    def produire(self, numero: int) -> dict:
        """Génère un document et retourne son entrée de manifest."""
