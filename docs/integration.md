# Intégration de l'API Detect-Doc

Guide du consommateur HTTP : comment démarrer le service, l'appeler et interpréter ses réponses, depuis ValIAPI ou tout autre client.

## Ce que fait le service

L'API reçoit un document PDF et une liste de types documentaires propre à l'appelant, puis renvoie le type le plus probable et un niveau de confiance, y compris quand ce score est faible. La chaîne interne est : extraction du texte (PdfPig pour le texte natif, PP-OCR tiny pour les pages scannées), masquage des données personnelles, classification par Mistral via un flow Flowise. Le texte envoyé à Flowise est déjà masqué : les noms, IBAN, e-mails et numéros détectés sont remplacés par des marqueurs.

L'API ne possède aucun catalogue de types : la liste est fournie à chaque requête et injectée dans le prompt. Deux clients peuvent donc soumettre des catalogues différents au même service.

## Démarrer le service

### En local, pour un essai

Prérequis : SDK .NET 10 (`global.json` épingle la version) et les modèles OCR installés par `Get-Modeles.ps1`. Depuis la racine du dépôt :

```powershell
.\Get-Modeles.ps1
dotnet run --project src/DetectDoc.Api -- --environment Development --urls http://localhost:5080
```

La surface de diagnostic (Swagger UI sur `http://localhost:5080/swagger`, description OpenAPI sur `/openapi/v1.json`, routes `/api/dev/*`) est exposée en `Development` comme en `Recette`. Le démarrage fonctionne sans secret ni service externe : les appels de classification échoueront en 503 tant que Flowise n'est pas configuré, mais `/health` et l'extraction répondent.

### En conteneur

```powershell
cp .env.exemple .env   # renseigner Flowise__FlowId
docker build --tag detect-doc:local .
docker run --rm --publish 8080:8080 --env-file .env detect-doc:local
Invoke-RestMethod http://localhost:8080/health
```

L'image embarque les modèles PP-OCRv6 tiny (téléchargés et vérifiés au build) et l'environnement Python Presidio complet : aucune variable de modèle ni `PII_PYTHON` à renseigner. Le conteneur écoute sur le port 8080 et tourne sous un utilisateur non privilégié. Le déploiement en recette dans Valia-stack n'est pas encore réalisé (work item 115219) ; la section suivante décrit la configuration à prévoir.

### Recette et production

Même image, deux configurations. Ce qui change entre les environnements :

| Réglage | Recette | Production |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Recette` | `Production` |
| `Flowise__BaseUrl` | `https://valia-flowise.val.local` | `https://flowise.ammon.cloud` |
| `Flowise__FlowId` | le flow de recette | le flow de production |
| `Flowise__AllowInvalidTls` | `true` | absent ou `false` |

En recette, `ASPNETCORE_ENVIRONMENT=Recette` garde `/swagger` et les routes de diagnostic `/api/dev/*` accessibles, ce qui permet d'y tester l'extraction et le masquage PII isolément. Le Flowise de recette présente un certificat interne que le conteneur ne reconnaît pas : `Flowise__AllowInvalidTls=true` désactive la validation de la chaîne pour cet appel sortant. L'option n'est acceptée que sous `Development` et `Recette` ; sous tout autre nom d'environnement, l'application refuse de démarrer si elle est définie. Le certificat public est valide en `Production` et le bypass inutile.

Le dépôt fournit `compose.yaml` et `.env.exemple` à la racine. Copier la configuration, renseigner le `Flowise__FlowId`, puis lancer :

```bash
cp .env.exemple .env
docker compose --env-file .env up -d --build
```

Le même `.env` sert à `docker run --env-file .env` et, pour la recette, on y passe `ASPNETCORE_ENVIRONMENT=Recette` et `Flowise__AllowInvalidTls=true`.

Deux alternatives au bypass, toutes deux pratiquées dans Valia-Stack : monter le magasin d'autorités de l'hôte dans le conteneur (`/usr/local/share/ca-certificates/:/etc/ssl/certs/:ro`, comme le fait `valiapi` en recette) pour faire confiance au certificat sans désactiver la validation ; ou, si le conteneur rejoint le réseau `ia_stack_lan`, viser l'adresse interne `http://flowise:3000` et la question du certificat ne se pose plus.

### Configuration

| Variable | Obligatoire | Effet |
|---|---|---|
| `Flowise__BaseUrl` | pour classer | URL de base de l'instance Flowise |
| `Flowise__FlowId` | pour classer | identifiant du flow de classification |
| `Flowise__AllowInvalidTls` | non | `true` accepte un certificat interne non reconnu, admis seulement en `Development` et `Recette` |
| `PpOcr__ModelsDirectory` | non | dossier contenant `v6/` des modèles PP-OCR, défaut `models/` à côté du binaire |
| `PpOcr__ReconnaissancesParalleles` | non | lignes reconnues simultanément par l'OCR, défaut `1` |
| `PII_PYTHON` | hors conteneur | exécutable Python portant `presidio-analyzer` et `fr_core_news_md`, voir `docs/development.md` |

## Routes

| Route | Objet |
|---|---|
| `GET /health` | `200 {"statut":"ok"}`, `503 {"statut":"degraded"}` quand le sous-processus Presidio est tombé et son budget de relances est épuisé |
| `POST /api/documents/classify` | extraction, masquage PII puis classification d'un PDF |
| `POST /api/documents/classify-batch` | même chaîne sur un lot de PDF, erreur par document |

Les routes `/api/dev/*` de diagnostic ne sont exposées qu'en `Development` et `Recette` : voir `docs/development.md`.

### `POST /api/documents/classify`

Formulaire `multipart/form-data` :

| Champ | Requis | Contenu |
|---|---|---|
| `file` | oui | le PDF, non vide |
| `types` | oui | catalogue du client, trois formes admises (ci-dessous) |
| `engine` | non | `auto` (défaut), `pdfpig` ou `ppocr-tiny` |

`types` accepte un tableau JSON d'identifiants, `["RIB","FACTURE"]`, ou d'objets avec `libelle` et `description` facultatifs, `[{"id":"RIB","libelle":"Relevé d'identité bancaire","description":"Coordonnées bancaires du titulaire"}]`. Les libellés et descriptions sont montrés au modèle et améliorent la discrimination entre types proches. La liste séparée par des virgules (`RIB,FACTURE`) reste admise, c'est ce que poste Swagger. Chaque `id` doit être non vide et unique.

`engine` choisit l'extraction : `pdfpig` lit le texte natif, `ppocr-tiny` passe chaque page par l'OCR, `auto` rend le texte natif quand chaque page en porte, et reprend l'extraction du document entier par l'OCR dès qu'une page n'en livre pas. Laisser `auto` sauf essai reproductible.

Réponse `200` :

| Champ | Contenu |
|---|---|
| `type` | un `id` du catalogue soumis, recopié à l'identique |
| `confiance` | score déclaré par le modèle, entre 0 et 1, renvoyé même s'il est faible |
| `moteur` | moteur effectivement utilisé, `pdfpig` ou `ppocr-tiny`, y compris après `auto` |
| `pagesTraitees` | nombre de pages extraites |
| `extractionMs`, `piiMaskingMs`, `classificationMs`, `totalMs` | durées des étapes et totale |
| `executionId` | identifiant d'exécution Flowise, quand le flow le retourne |

### `POST /api/documents/classify-batch`

Même formulaire avec plusieurs champs `files` à la place de `file`. La réponse est `200` avec un tableau dans l'ordre reçu : `nomFichier`, puis `resultat` (l'objet ci-dessus) ou `erreur` (un `ProblemDetails`). L'échec d'un document n'interrompt pas le lot : un `200` global ne signifie pas que chaque document a réussi, il faut inspecter `erreur` ligne par ligne. Seuls un lot vide ou un `types` invalide font échouer la requête entière en `400`.

## Erreurs

Les erreurs suivent le format `ProblemDetails` (`status`, `title`, `detail`).

| Statut | `title` | Cause |
|---|---|---|
| 400 | `Requête invalide.` | fichier ou `types` absent, `id` vide ou en double |
| 400 | `Moteur inconnu.` | `engine` hors de `auto`, `pdfpig`, `ppocr-tiny` |
| 422 | `PDF invalide.` | le fichier n'est pas un PDF lisible |
| 422 | `Texte inexploitable.` | l'extraction ne retourne aucun texte |
| 502 | `Réponse Flowise invalide.` | le flow n'a pas rendu le JSON attendu |
| 503 | `Moteur indisponible.` | modèles OCR absents du déploiement |
| 503 | `Traitement PII indisponible.` | sous-processus Presidio absent ou interrompu |
| 503 | `Flowise indisponible.` / `Flowise non configuré.` | hôte injoignable ou variables absentes |
| 504 | `Flowise a dépassé le délai d'appel.` | dépassement du délai de 60 s côté client Flowise |

Une erreur `5xx` appelle une reprise : la requête peut être rejouée telle quelle.

## Exemples

```bash
curl http://localhost:8080/health
# {"statut":"ok"}

curl -X POST http://localhost:8080/api/documents/classify \
    -F "file=@rib.pdf" \
    -F 'types=[{"id":"RIB","description":"Coordonnées bancaires du titulaire"},{"id":"FACTURE"}]'

curl -X POST http://localhost:8080/api/documents/classify-batch \
    -F "files=@rib.pdf" -F "files=@attestation.pdf" \
    -F 'types=["RIB","FACTURE","CONTRAT"]'
```

```csharp
using var client = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
using var form = new MultipartFormDataContent();
await using var pdf = File.OpenRead("rib.pdf");
form.Add(new StreamContent(pdf), "file", "rib.pdf");
form.Add(new StringContent("""["RIB","FACTURE","CONTRAT"]"""), "types");

var response = await client.PostAsync("/api/documents/classify", form);
response.EnsureSuccessStatusCode();
var result = await response.Content.ReadFromJsonAsync<DocumentClassificationResult>();

public sealed record DocumentClassificationResult(
    string Type,
    double Confiance,
    string Moteur,
    int PagesTraitees,
    long ExtractionMs,
    long PiiMaskingMs,
    long ClassificationMs,
    long TotalMs,
    string? ExecutionId);
```

## Points de vigilance

- **La confiance n'est pas un seuil.** Le service renvoie le meilleur type même quand le score est faible : c'est l'appelant qui décide à partir de quelle valeur un humain relit. Un document hors catalogue reçoit un type de repli avec un score bas, pas un rejet.
- **PDF uniquement.** Les images et documents Word ne sont pas acceptés à ce stade.
- **Pas d'authentification dans le POC**, ni sur l'API ni vers Flowise.
- **Le champ `moteur` est rejouable** : sa valeur peut être renvoyée dans `engine` pour reproduire une extraction.
- **La classification dépend du flow Flowise configuré** : modèle, prompt et version du flow font partie du résultat autant que le code de l'API. La configuration de référence est décrite dans `docs/flowise-classification.md`.
