"""Pont NER pour le comparatif : un document par ligne sur stdin, ses plages sur stdout.

Les modèles mettent plusieurs secondes à charger, d'où un processus unique tenu ouvert pour tout
le run plutôt qu'un appel par document. La poignée de main envoyée au démarrage sort ce
chargement de la fenêtre chronométrée par le benchmark.
"""

import contextlib
import json
import os
import sys
import time

LANGUE = "fr"

CATEGORIES_PRESIDIO = {
    "PERSON": "Nom",
    "ORGANIZATION": "Organisme",
    "LOCATION": "Adresse",
    "EMAIL_ADDRESS": "Email",
    "PHONE_NUMBER": "Telephone",
    "IBAN_CODE": "Iban",
}

CATEGORIES_GLINER = {
    "person": "Nom",
    "organization": "Organisme",
    "address": "Adresse",
    "email": "Email",
    "phone number": "Telephone",
    "iban": "Iban",
}


def charger_presidio():
    from presidio_analyzer import AnalyzerEngine, RecognizerRegistry
    from presidio_analyzer.nlp_engine import NlpEngineProvider
    from presidio_analyzer.predefined_recognizers import PhoneRecognizer

    modele = os.environ.get("PII_PRESIDIO_MODELE", "fr_core_news_sm")
    demandees = os.environ.get("PII_PRESIDIO_ENTITES")
    entites = ([e.strip() for e in demandees.split(",") if e.strip() in CATEGORIES_PRESIDIO]
               if demandees else list(CATEGORIES_PRESIDIO))

    provider = NlpEngineProvider(nlp_configuration={
        "nlp_engine_name": "spacy",
        "models": [{"lang_code": LANGUE, "model_name": modele}],
    })

    registre = RecognizerRegistry(supported_languages=[LANGUE])
    registre.load_predefined_recognizers(languages=[LANGUE])
    # Les régions par défaut du reconnaisseur de téléphones n'incluent pas la France.
    registre.add_recognizer(PhoneRecognizer(supported_language=LANGUE, supported_regions=["FR"]))

    analyzer = AnalyzerEngine(
        nlp_engine=provider.create_engine(),
        registry=registre,
        supported_languages=[LANGUE],
    )

    def detecter(texte):
        for resultat in analyzer.analyze(text=texte, language=LANGUE, entities=entites):
            yield CATEGORIES_PRESIDIO[resultat.entity_type], resultat.entity_type, resultat.start, resultat.end

    return detecter, {"modele": modele, "entites": ", ".join(entites)}


def charger_gliner():
    from gliner import GLiNER

    modele = os.environ.get("PII_GLINER_MODELE", "urchade/gliner_multi_pii-v1")
    demandes = os.environ.get("PII_GLINER_LABELS")
    labels = ([l.strip() for l in demandes.split(",") if l.strip() in CATEGORIES_GLINER]
              if demandes else list(CATEGORIES_GLINER))
    seuil = float(os.environ.get("PII_GLINER_SEUIL", "0.5"))
    budget = int(os.environ.get("PII_GLINER_CARACTERES", "1200"))

    model = GLiNER.from_pretrained(modele)

    def detecter(texte):
        for debut_bloc, bloc in decouper(texte, budget):
            for entite in model.predict_entities(bloc, labels, threshold=seuil):
                categorie = CATEGORIES_GLINER.get(entite["label"])
                if categorie:
                    yield categorie, entite["label"], debut_bloc + entite["start"], debut_bloc + entite["end"]

    return detecter, {
        "modele": modele,
        "entites": ", ".join(labels),
        "seuil": seuil,
        "caracteres_par_bloc": budget,
    }


def charger_gliner2():
    from gliner2 import AutoExtractor

    modele = os.environ.get("PII_GLINER2_MODELE", "fastino/gliner2.5-multi-v1")
    demandes = os.environ.get("PII_GLINER2_LABELS")
    labels = ([l.strip() for l in demandes.split(",") if l.strip() in CATEGORIES_GLINER]
              if demandes else list(CATEGORIES_GLINER))
    seuil = float(os.environ.get("PII_GLINER2_SEUIL", "0.5"))
    budget = int(os.environ.get("PII_GLINER2_CARACTERES", "1200"))

    model = AutoExtractor.from_pretrained(modele)

    def detecter(texte):
        for debut_bloc, bloc in decouper(texte, budget):
            resultat = model.extract_entities(bloc, labels, threshold=seuil, include_spans=True)
            for label, entites in resultat["entities"].items():
                categorie = CATEGORIES_GLINER.get(label)
                if not categorie:
                    continue
                for entite in entites:
                    yield categorie, label, debut_bloc + entite["start"], debut_bloc + entite["end"]

    return detecter, {
        "modele": modele,
        "entites": ", ".join(labels),
        "seuil": seuil,
        "caracteres_par_bloc": budget,
    }


def decouper(texte, budget):
    """Blocs bornés par la limite de tokens du modèle, coupés sur des fins de ligne.

    Une entité à cheval sur deux blocs est perdue ; couper sur un retour à la ligne rend le cas
    rare sur ces documents, où chaque champ occupe sa ligne.
    """
    debut = 0
    courant = 0
    for ligne in texte.splitlines(keepends=True):
        if courant and courant + len(ligne) > budget:
            yield debut, texte[debut:debut + courant]
            debut += courant
            courant = 0
        courant += len(ligne)
    if courant:
        yield debut, texte[debut:debut + courant]


def en_indices_utf16(texte, debut, fin):
    """Python indexe en points de code, .NET en unités UTF-16. La conversion évite un décalage
    des plages dès qu'un caractère hors du plan de base apparaît avant une PII."""
    depart = len(texte[:debut].encode("utf-16-le")) // 2
    longueur = len(texte[debut:fin].encode("utf-16-le")) // 2
    return depart, longueur


BACKENDS = {"presidio": charger_presidio, "gliner": charger_gliner, "gliner2": charger_gliner2}


def main():
    backend = sys.argv[1] if len(sys.argv) > 1 else "presidio"
    depart = time.perf_counter()
    try:
        # Le chargement écrit sur stdout, spaCy et transformers y impriment leurs diagnostics :
        # le détourner vers stderr garde le canal de sortie réservé au protocole.
        with contextlib.redirect_stdout(sys.stderr):
            detecter, description = BACKENDS[backend]()
    except (Exception, SystemExit) as erreur:
        print(json.dumps({"erreur": f"{type(erreur).__name__}: {erreur}"}), flush=True)
        raise SystemExit(1)

    print(json.dumps({"chargement_ms": (time.perf_counter() - depart) * 1000, **description}), flush=True)

    for ligne in sys.stdin:
        ligne = ligne.strip()
        if not ligne:
            continue

        try:
            texte = json.loads(ligne)["texte"]
            detections = []
            with contextlib.redirect_stdout(sys.stderr):
                trouvees = list(detecter(texte))
            for categorie, regle, debut, fin in trouvees:
                position, longueur = en_indices_utf16(texte, debut, fin)
                detections.append({
                    "categorie": categorie,
                    "start": position,
                    "length": longueur,
                    "regle": regle,
                })
            reponse = {"detections": detections}
        except Exception as erreur:  # remonte au benchmark, qui classe le document en erreur
            reponse = {"erreur": f"{type(erreur).__name__}: {erreur}"}

        print(json.dumps(reponse), flush=True)


if __name__ == "__main__":
    main()
