namespace OcrComparison.Benchmark;

/// <summary>
/// Relève le plus haut working set atteint entre sa construction et son arrêt, par échantillonnage.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System.Diagnostics.Process.PeakWorkingSet64"/> ne répond pas à la question : c'est le
/// pic du processus depuis son démarrage, un compteur qui ne redescend jamais. Relevé par document,
/// il ferait hériter chaque document du pic de tous ceux qui l'ont précédé, et le maximum sur un run
/// vaudrait toujours la valeur du dernier. Ni Windows ni Linux ne tiennent ce pic à l'échelle d'un
/// intervalle, il n'existe donc aucun compteur système à lire.
/// </para>
/// <para>
/// Les compteurs du GC ne conviennent pas non plus : l'essentiel de la mémoire consommée ici est
/// allouée en non managé par Leptonica et Tesseract, invisible pour eux. Reste l'échantillonnage du
/// working set, qui est aussi ce que fait un profileur externe, et qui a l'avantage de compter la
/// mémoire réellement occupée quelle que soit sa provenance.
/// </para>
/// </remarks>
internal sealed class SondeMemoire : IDisposable
{
    // Période d'échantillonnage, coincée entre deux erreurs opposées.
    // Trop lente, un pic passerait entre deux relevés et n'existerait nulle part dans la mesure.
    // Trop rapide, la sonde consommerait du temps processeur, qui est justement l'autre métrique
    // relevée autour du même appel.
    private const int PeriodeMs = 25;

    private readonly Timer _minuterie;
    private long _pic;

    public SondeMemoire()
    {
        Depart = Environment.WorkingSet;
        _pic = Depart;
        _minuterie = new Timer(_ => Relever(), null, PeriodeMs, PeriodeMs);
    }

    /// <summary>
    /// Working set relevé avant le travail mesuré. Le coût propre de ce travail est l'écart entre
    /// <see cref="Arreter"/> et cette valeur, le pic absolu portant aussi tout ce que le processus
    /// occupait déjà (runtime, modèles de langue chargés, document précédent pas encore rendu à l'OS).
    /// </summary>
    public long Depart { get; }

    /// <summary>
    /// Arrête l'échantillonnage et rend le plus haut working set observé, celui de départ compris.
    /// </summary>
    public long Arreter()
    {
        // Attendre la fin d'un relevé déjà en cours, pour que le dernier relevé et la lecture qui
        // suivent soient seuls à toucher au compteur.
        using var relevesTermines = new ManualResetEvent(false);
        if (_minuterie.Dispose(relevesTermines))
        {
            relevesTermines.WaitOne();
        }

        // Le pic peut tomber dans le dernier intervalle, que plus aucun échantillon ne couvrirait.
        Relever();
        return _pic;
    }

    public void Dispose() => _minuterie.Dispose();

    private void Relever()
    {
        var courant = Environment.WorkingSet;
        if (courant > _pic)
        {
            _pic = courant;
        }
    }
}
