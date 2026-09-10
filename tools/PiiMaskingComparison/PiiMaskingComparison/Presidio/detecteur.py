"""Pont Presidio pour le comparatif : un document par ligne sur stdin, ses plages sur stdout.

Le modèle spaCy met plusieurs secondes à charger, d'où un processus unique tenu ouvert pour tout
le run plutôt qu'un appel par document. La poignée de main envoyée au démarrage sort ce
chargement de la fenêtre chronométrée par le benchmark.
"""

import json
import sys
import time

from presidio_analyzer import AnalyzerEngine, RecognizerRegistry
from presidio_analyzer.nlp_engine import NlpEngineProvider
from presidio_analyzer.predefined_recognizers import PhoneRecognizer

MODELE = "fr_core_news_sm"
LANGUE = "fr"

# Entités Presidio retenues et leur catégorie du comparatif. Ce qui n'est pas ici est ignoré :
# DATE_TIME ou URL sortiraient en faux positifs sans correspondre à une catégorie mesurée.
CATEGORIES = {
    "PERSON": "Nom",
    "ORGANIZATION": "Organisme",
    "LOCATION": "Adresse",
    "EMAIL_ADDRESS": "Email",
    "PHONE_NUMBER": "Telephone",
    "IBAN_CODE": "Iban",
}


def creer_analyzer():
    provider = NlpEngineProvider(nlp_configuration={
        "nlp_engine_name": "spacy",
        "models": [{"lang_code": LANGUE, "model_name": MODELE}],
    })

    registre = RecognizerRegistry(supported_languages=[LANGUE])
    registre.load_predefined_recognizers(languages=[LANGUE])
    # Les régions par défaut du reconnaisseur de téléphones n'incluent pas la France.
    registre.add_recognizer(PhoneRecognizer(supported_language=LANGUE, supported_regions=["FR"]))

    return AnalyzerEngine(
        nlp_engine=provider.create_engine(),
        registry=registre,
        supported_languages=[LANGUE],
    )


def en_indices_utf16(texte, debut, fin):
    """Python indexe en points de code, .NET en unités UTF-16. La conversion évite un décalage
    des plages dès qu'un caractère hors du plan de base apparaît avant une PII."""
    depart = len(texte[:debut].encode("utf-16-le")) // 2
    longueur = len(texte[debut:fin].encode("utf-16-le")) // 2
    return depart, longueur


def main():
    depart = time.perf_counter()
    analyzer = creer_analyzer()
    print(json.dumps({"chargement_ms": (time.perf_counter() - depart) * 1000}), flush=True)

    for ligne in sys.stdin:
        ligne = ligne.strip()
        if not ligne:
            continue

        try:
            texte = json.loads(ligne)["texte"]
            resultats = analyzer.analyze(text=texte, language=LANGUE, entities=list(CATEGORIES))
            detections = []
            for resultat in resultats:
                debut, longueur = en_indices_utf16(texte, resultat.start, resultat.end)
                detections.append({
                    "categorie": CATEGORIES[resultat.entity_type],
                    "start": debut,
                    "length": longueur,
                    "regle": resultat.entity_type,
                })
            reponse = {"detections": detections}
        except Exception as erreur:  # remonte au benchmark, qui classe le document en erreur
            reponse = {"erreur": f"{type(erreur).__name__}: {erreur}"}

        print(json.dumps(reponse), flush=True)


if __name__ == "__main__":
    main()
