"""Mandat de prélèvement SEPA, isolé.

Ne montre jamais le pavé de codes établissement/guichet/compte/clé propre au RIB, mais
seulement l'IBAN et le BIC du débiteur, comme les mandats normalisés qui circulent en
France : le pavé de codes nationaux est une habitude du RIB, pas une exigence du mandat.
C'est justement l'absence de ce pavé, conjointe à la présence du cartouche RUM et ICS, qui
distingue ce type de `RIB_SEPA`. Voir `documents/mandat.py` pour le modèle de données et
le template, partagés avec cette variante fusionnée.
"""

from documents.base import DocumentType
from documents.mandat import produire_mandat


class SepaDocument(DocumentType):
    type_attendu = "sepa"

    def produire(self, numero: int) -> dict:
        return produire_mandat(numero, self.type_attendu, "sepa", avec_pave_rib=False)


DOCUMENT = SepaDocument()
