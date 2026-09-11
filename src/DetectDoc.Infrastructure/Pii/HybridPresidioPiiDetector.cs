using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DetectDoc.Core.Pii;

namespace DetectDoc.Infrastructure.Pii;

public sealed class HybridPresidioPiiDetector : IPiiDetector, IDisposable
{
    private readonly PresidioPiiDetector _presidio = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<PiiDetection>> DetectAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        // Le scan regex est un calcul CPU sans état partagé : il n'a besoin d'aucune synchronisation,
        // contrairement à l'appel Presidio qui reste sérialisé derrière le sémaphore ci-dessous.
        var detectionsRegex = RegexPiiDetector.Detect(text);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return [.. detectionsRegex, .. await _presidio.DetectAsync(text, cancellationToken)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _presidio.Dispose();
        _gate.Dispose();
    }

    private static class RegexPiiDetector
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
        private const string SameLine = @"[^\S\n]+";
        private const string Word = @"\p{Lu}[\p{L}'’-]+";
        private const string Suffix = @"(?:BTP|Industrie|Compétences|Formations?|Conseil)\b";
        private const string Name = $@"(?:M\.|Mme|Mlle|Dr\.?)?[^\S\n]*{Word}(?:{SameLine}{Word}){{1,2}}";

        private static readonly (PiiCategory Category, string Rule, PiiDetectionKind Kind, Regex Pattern)[] Rules =
        [
            (PiiCategory.Email, "Email", PiiDetectionKind.Specification,
                Pattern(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+(?:\r?\n[\p{L}\p{N}.-]+)?\.\p{L}{2,}")),
            (PiiCategory.Iban, "Iban", PiiDetectionKind.Specification,
                Pattern(@"\b[A-Z]{2}[0-9]{2}(?: ?[A-Z0-9]{4}){3,7} ?[A-Z0-9]{0,4}\b")),
            (PiiCategory.Phone, "TelephoneFr", PiiDetectionKind.Specification,
                Pattern(@"\b0[1-9](?:[ .-]?[0-9]{2}){4}\b")),
            (PiiCategory.Phone, "TelephoneInternational", PiiDetectionKind.Specification,
                Pattern(@"\+[0-9]{1,3}(?:[ .-]?[0-9]){8,11}\b")),
            (PiiCategory.Bic, "BicApresLibelle", PiiDetectionKind.Label,
                Pattern(@"\bBIC\b[^\p{L}\p{N}]{0,10}([A-Z]{6}[A-Z0-9]{2}(?:[A-Z0-9]{3})?)\b")),
            (PiiCategory.Siret, "SiretApresLibelle", PiiDetectionKind.Label,
                Pattern(@"\bSIRET\b[^\p{N}]{0,5}([0-9]{3} ?[0-9]{3} ?[0-9]{3} ?[0-9]{5})\b")),
            (PiiCategory.ProfessionalIdentifier, "RppsApresLibelle", PiiDetectionKind.Label,
                Pattern(@"\bRPPS\b[^\p{N}]{0,5}([0-9]{11})\b")),
            (PiiCategory.ProfessionalIdentifier, "AdeliApresLibelle", PiiDetectionKind.Label,
                Pattern(@"\bADELI\b[^\p{N}]{0,5}([0-9]{9})\b")),
            (PiiCategory.DocumentNumber, "RumApresLibelle", PiiDetectionKind.Label,
                Pattern(@"\bRUM\b[^\p{L}\p{N}]{0,5}([A-Z]{2,6}[0-9]{6,12})\b")),
            (PiiCategory.Address, "AdressePostale", PiiDetectionKind.Specification,
                Pattern(@"\b[0-9]{1,4}(?: ?(?i:bis|ter))?\s+(?i:rue|avenue|boulevard|chemin|impasse|place|all[ée]e|route|quai|cours|voie)\b.{0,60}?\b[0-9]{5}\b[ \t]*\p{Lu}[\p{L}'’-]*(?:[- ]\p{L}[\p{L}'’-]+){0,3}", RegexOptions.Singleline)),
            (PiiCategory.Name, "NomApresLibelle", PiiDetectionKind.Label,
                Pattern($@"(?i:titulaire du compte|titulaire|formateur|formatrice|stagiaire|contact|auteur de l'offre d'emploi|rencontrez)\s*:?\s*({Name})")),
            (PiiCategory.Name, "NomApresAttentionDe", PiiDetectionKind.Label,
                Pattern($@"(?i:à l['’]attention de)\s+({Name})")),
            (PiiCategory.Name, "NomApresCivilite", PiiDetectionKind.Label,
                Pattern($@"(?:M\.|Mme|Mlle|Dr\.?){SameLine}({Word}(?:{SameLine}{Word}){{0,2}})")),
            (PiiCategory.Name, "NomDansListeParticipants", PiiDetectionKind.Label,
                Pattern(@"(?<=(?i:participants?)\s*:[^\n]{0,300})\p{Lu}[\p{L}'’-]+\s+\p{Lu}[\p{L}'’-]+")),
            (PiiCategory.Organization, "OrganismeAvecPrefixe", PiiDetectionKind.Vocabulary,
                Pattern($"(?i:centre de formation|institut|académie|campus|groupe|cabinet){SameLine}{Word}(?:{SameLine}{Word})?")),
            (PiiCategory.Organization, "OrganismeAvecSuffixe", PiiDetectionKind.Vocabulary,
                Pattern($"{Word}{SameLine}{Suffix}(?:{SameLine}et{SameLine}{Suffix})?")),
            (PiiCategory.SocialNetwork, "ProfilLinkedIn", PiiDetectionKind.Specification,
                Pattern(@"\bin/[\p{L}\p{N}-]{3,60}\b"))
        ];

        public static IReadOnlyList<PiiDetection> Detect(string text)
        {
            var detections = new List<PiiDetection>();
            foreach (var (category, rule, kind, pattern) in Rules)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    var range = match.Groups.Count > 1 ? match.Groups[1] : match.Groups[0];
                    detections.Add(new PiiDetection(category, rule, kind, range.Index, range.Length));
                }
            }

            return detections;
        }

        private static Regex Pattern(string pattern, RegexOptions options = RegexOptions.None) =>
            new(pattern, options | RegexOptions.CultureInvariant, Timeout);
    }

    private sealed class PresidioPiiDetector : IDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _standardError;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Response>> _pending = new();
        private readonly Lock _sendLock = new();
        private int _nextId;

        public PresidioPiiDetector()
        {
            var python = Environment.GetEnvironmentVariable("PII_PYTHON") ?? "python3";
            var script = Path.Combine(AppContext.BaseDirectory, "Pii", "detecteur.py");
            _process = Process.Start(new ProcessStartInfo(python, ["-u", script])
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardInputEncoding = new UTF8Encoding(false)
            }) ?? throw new InvalidOperationException("Le processus Presidio n'a pas démarré.");
            _standardError = _process.StandardError.ReadToEndAsync();

            var startupLine = _process.StandardOutput.ReadLine();
            var startup = startupLine is null ? null : JsonSerializer.Deserialize<Startup>(startupLine);
            if (startup?.Error is not null || startupLine is null)
            {
                throw Fail($"Chargement de Presidio impossible : {startup?.Error ?? "réponse absente"}.");
            }

            _ = Task.Run(DistributeAsync);
        }

        public async Task<IReadOnlyList<PiiDetection>> DetectAsync(
            string text,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;

            Response response;
            try
            {
                lock (_sendLock)
                {
                    _process.StandardInput.WriteLine(JsonSerializer.Serialize(new { id, texte = text }));
                    _process.StandardInput.Flush();
                }

                response = await completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                // Retire l'entrée dans tous les cas : réponse reçue, échec d'écriture ou annulation
                // par l'appelant, pour ne jamais laisser une requête abandonnée dans la table.
                _pending.TryRemove(id, out _);
            }

            if (response.Error is not null)
            {
                throw new InvalidOperationException($"Presidio : {response.Error}");
            }

            if (response.Detections.Any(d => d.Start < 0 || d.Length <= 0 || d.Start > text.Length - d.Length))
            {
                throw new InvalidDataException("Presidio a retourné une plage invalide.");
            }

            return [.. response.Detections
                .Select(d => new PiiDetection(
                    Enum.Parse<PiiCategory>(d.Category),
                    d.Rule,
                    PiiDetectionKind.Model,
                    d.Start,
                    d.Length))];
        }

        private async Task DistributeAsync()
        {
            try
            {
                while (await _process.StandardOutput.ReadLineAsync() is { } line)
                {
                    Response? response;
                    try
                    {
                        response = JsonSerializer.Deserialize<Response>(line);
                    }
                    catch (JsonException)
                    {
                        // Une ligne stdout illisible ne doit pas arrêter la distribution des réponses suivantes.
                        continue;
                    }

                    if (response is not null && _pending.TryRemove(response.Id, out var completion))
                    {
                        completion.SetResult(response);
                    }
                }
            }
            finally
            {
                foreach (var completion in _pending.Values)
                {
                    completion.TrySetException(new InvalidOperationException("Le processus Presidio s'est interrompu."));
                }
            }
        }

        private InvalidOperationException Fail(string message)
        {
            _process.StandardInput.Close();
            _process.WaitForExit(5000);
            var error = _standardError.IsCompletedSuccessfully ? _standardError.Result.TrimEnd() : string.Empty;
            return new InvalidOperationException($"{message} {error}".TrimEnd());
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                _process.WaitForExit(5000);
            }

            _process.Dispose();
        }

        private sealed record Startup([property: JsonPropertyName("erreur")] string? Error);

        private sealed record Response(
            [property: JsonPropertyName("id")] int Id,
            [property: JsonPropertyName("detections")] IReadOnlyList<PythonDetection> Detections,
            [property: JsonPropertyName("erreur")] string? Error);

        private sealed record PythonDetection(
            [property: JsonPropertyName("categorie")] string Category,
            [property: JsonPropertyName("start")] int Start,
            [property: JsonPropertyName("length")] int Length,
            [property: JsonPropertyName("regle")] string Rule);
    }
}
