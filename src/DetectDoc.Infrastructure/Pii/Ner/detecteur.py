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


def utf16_offsets(text):
    offsets = [0]
    for ch in text:
        offsets.append(offsets[-1] + (2 if ord(ch) > 0xFFFF else 1))
    return offsets


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

        request = None
        try:
            request = json.loads(line)
            text = request["texte"]
            offsets = utf16_offsets(text)
            detections = []
            for category, rule, start, end in detect(text):
                detections.append({
                    "categorie": category,
                    "start": offsets[start],
                    "length": offsets[end] - offsets[start],
                    "regle": rule,
                })
            response = {"id": request["id"], "detections": detections}
        except Exception as error:
            request_id = request.get("id") if isinstance(request, dict) else None
            response = {"id": request_id,
                        "erreur": f"{type(error).__name__}: {error}"}

        print(json.dumps(response), file=OUTPUT, flush=True)


if __name__ == "__main__":
    main()
