"""Expose Presidio par un protocole JSON ligne à ligne pour l'API .NET."""

import contextlib
import json
import os
import sys

LANGUAGE = "fr"
OUTPUT = sys.stdout
CATEGORIES = {
    "PERSON": "Name",
    "ORGANIZATION": "Organization",
    "LOCATION": "Address",
    "EMAIL_ADDRESS": "Email",
    "PHONE_NUMBER": "Phone",
    "IBAN_CODE": "Iban",
}


def load_detector():
    import spacy
    from presidio_analyzer import AnalyzerEngine, RecognizerRegistry
    from presidio_analyzer.nlp_engine import SpacyNlpEngine
    from presidio_analyzer.predefined_recognizers import PhoneRecognizer

    model = os.environ.get("PII_PRESIDIO_MODELE", "fr_core_news_md")
    requested = os.environ.get("PII_PRESIDIO_ENTITES")
    entities = ([item.strip() for item in requested.split(",") if item.strip() in CATEGORIES]
                if requested else list(CATEGORIES))
    excluded = [item.strip() for item in os.environ.get(
        "PII_PRESIDIO_EXCLURE",
        "morphologizer,parser,attribute_ruler,lemmatizer,senter,tagger").split(",") if item.strip()]
    pipeline = spacy.load(model, exclude=excluded)

    class FrenchSpacyEngine(SpacyNlpEngine):
        def __init__(self):
            super().__init__(models=[{"lang_code": LANGUAGE, "model_name": model}])
            self.nlp = {LANGUAGE: pipeline}

    registry = RecognizerRegistry(supported_languages=[LANGUAGE])
    registry.load_predefined_recognizers(languages=[LANGUAGE])
    registry.add_recognizer(PhoneRecognizer(
        supported_language=LANGUAGE,
        supported_regions=["FR"]))
    analyzer = AnalyzerEngine(
        nlp_engine=FrenchSpacyEngine(),
        registry=registry,
        supported_languages=[LANGUAGE])

    def detect(text):
        for result in analyzer.analyze(text=text, language=LANGUAGE, entities=entities):
            yield CATEGORIES[result.entity_type], result.entity_type, result.start, result.end

    return detect, {"modele": model, "entites": ", ".join(entities)}


def utf16_range(text, start, end):
    position = len(text[:start].encode("utf-16-le")) // 2
    length = len(text[start:end].encode("utf-16-le")) // 2
    return position, length


def main():
    try:
        with contextlib.redirect_stdout(sys.stderr):
            detect, description = load_detector()
    except (Exception, SystemExit) as error:
        print(json.dumps({"erreur": f"{type(error).__name__}: {error}"}), flush=True)
        raise SystemExit(1)

    print(json.dumps(description), file=OUTPUT, flush=True)
    for line in sys.stdin:
        if not line.strip():
            continue

        request = json.loads(line)
        try:
            detections = []
            for category, rule, start, end in detect(request["texte"]):
                position, length = utf16_range(request["texte"], start, end)
                detections.append({
                    "categorie": category,
                    "start": position,
                    "length": length,
                    "regle": rule,
                })
            response = {"id": request["id"], "detections": detections}
        except Exception as error:
            response = {"id": request.get("id"),
                        "erreur": f"{type(error).__name__}: {error}"}

        print(json.dumps(response), file=OUTPUT, flush=True)


if __name__ == "__main__":
    main()
