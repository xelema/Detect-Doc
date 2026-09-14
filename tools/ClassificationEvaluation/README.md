# ClassificationEvaluation

Client de mesure de la chaîne complète de classification. Il transmet les PDF du corpus à une API Detect-Doc déjà démarrée, puis produit les métriques de classification et de confiance à partir de sa réponse.

## Lancer un run dev

Depuis `tools/ClassificationEvaluation/ClassificationEvaluation` :

```powershell
dotnet run -- --corpus ..\..\..\corpus --manifest ..\..\..\corpus\manifest.jsonl
```

L'API doit être démarrée et configurée avec Flowise. Le runner utilise `http://localhost:5080`, le moteur `auto`, le split `dev` et les six types ciblés par défaut : `carte_visite`, `feuille_emar`, `offre_emploi`, `rib`, `rib_sepa`, `sepa`.

```powershell
dotnet run -- --corpus ..\..\..\corpus --manifest ..\..\..\corpus\manifest.jsonl --api http://localhost:5080 --engine ppocr-tiny --types carte_visite,feuille_emar,offre_emploi,rib,rib_sepa,sepa --split dev
dotnet run -- --self-check
```

`--descriptions <chemin>` soumet chaque type sous la forme objet `{id, libelle, description}` lue dans un fichier JSON `{code: {libelle, description}}`, par exemple `descriptions-types.json` à côté de ce README. Chaque type soumis doit y être décrit, et son sha256 est enregistré dans `run.json` pour la comparabilité. Sans l'option, le runner envoie les identifiants seuls comme avant.

Chaque run crée `run/<date>_classification/` avec les conditions d'exécution, les résultats individuels, le récapitulatif et la matrice de confusion. Les factures et devis restent dans le lot comme distracteurs et ne participent pas aux métriques de classification fermée lorsque la liste par défaut est utilisée.

Le runner affiche les étapes de préparation, d'envoi du lot, de réception de la réponse et d'écriture du rapport. L'API traite le lot avant de répondre, il n'existe donc pas de progression fiable par document.

Les tokens, le coût Mistral, la version du flow et l'empreinte du prompt ne sont pas publiés par l'API. Ils ne figurent donc pas dans le rapport généré.
