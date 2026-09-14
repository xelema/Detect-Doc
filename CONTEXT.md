# Detect-Doc domain glossary

Domain language for this codebase. Module names in code follow these terms; French equivalents are the ones used in work items and the project vault.

| Term | Meaning | Code |
|---|---|---|
| Document type (type documentaire) | A category supplied by the caller per request: an `id`, optional `libelle` and `description`. The API holds no catalog. | `TypeDocumentaire` |
| Classification pipeline | The use case: extract text, mask PII, classify, measure each stage. The seam between transport and domain. | `DocumentClassification` |
| Extraction engine (moteur d'extraction) | Adapter turning a seekable PDF stream into per-page text plus optional OCR confidence. | `ITextExtractionEngine` (`PdfPigNativeTextEngine`, `PpOcrEngine`) |
| Engine selection | Resolving the `engine` request token (`auto`, `pdfpig`, `ppocr-tiny`) to an engine; `auto` is the native-then-OCR fallback policy. | `IExtractionSelector`, `AutomaticExtraction` |
| PII detection | Finding spans of personal data (names, IBAN, SIRET...) in extracted text. | `IPiiDetector` (`RegexPiiDetector`, `PresidioPiiDetector`, union `HybridPresidioPiiDetector`) |
| PII masking | Validating, merging and replacing detected spans with category markers before any external AI call. | `PiiTextMasker` |
| Classifier | Adapter returning the best matching type id and a confidence score for a masked text. | `IDocumentClassifier` (`FlowiseDocumentClassifier`) |
| Corpus splits | `dev`: tuning prompt, OCR and PII rules. `test`: sealed, reserved for report measurements. Never select an extraction strategy using ground truth unavailable at runtime. | `corpus/` manifest |
| Distractor | A corpus document whose true type is not in the submitted type list; used to measure score calibration. | corpus manifest |

Conventions: HTTP endpoints translate requests and `ProblemDetails`; domain logic lives in `DetectDoc.Core`; external systems (PdfPig, ONNX, Presidio subprocess, Flowise HTTP) sit behind interfaces in `DetectDoc.Infrastructure`.
