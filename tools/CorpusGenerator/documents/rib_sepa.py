"""Relevé d'identité bancaire et mandat SEPA réunis dans une seule pièce.

Le cas le plus dur des trois quasi-synonymes bancaires du corpus : `RIB_SEPA` est l'union
littérale de `RIB` et `SEPA`, le pavé de codes bancaires du premier et le cartouche de
mandat du second, RUM et ICS compris. C'est le formulaire que beaucoup d'organismes
envoient à faire compléter et signer en une seule pièce plutôt qu'en deux documents
séparés. Voir `documents/mandat.py` pour le modèle de données et le template, partagés
avec le mandat isolé.
"""

from documents.base import DocumentType
from documents.mandat import produire_mandat


class RibSepaDocument(DocumentType):
    type_attendu = "rib_sepa"

    def produire(self, numero: int) -> dict:
        return produire_mandat(numero, self.type_attendu, "ribsepa", avec_pave_rib=True)


DOCUMENT = RibSepaDocument()
