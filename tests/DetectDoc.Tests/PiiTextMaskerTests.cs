using DetectDoc.Core.Pii;
using Xunit;

namespace DetectDoc.Tests;

public class PiiTextMaskerTests
{
    [Fact]
    public async Task MaskAsync_MergesOverlappingDetectionsWithoutDuplicatingMarkers()
    {
        var text = "Contact : Jean Dupont, IBAN FR76 3000 6000 0112 3456 7890 189, téléphone : 06 12 34 56 78.";
        var masker = new PiiTextMasker(new FakePiiDetector(t =>
        {
            var nameStart = t.IndexOf("Jean Dupont", StringComparison.Ordinal);
            var ibanStart = t.IndexOf("FR76", StringComparison.Ordinal);
            var addressStart = t.IndexOf("IBAN FR76", StringComparison.Ordinal);
            var phoneStart = t.IndexOf("06 12 34 56 78", StringComparison.Ordinal);
            return
            [
                new PiiDetection(PiiCategory.Name, "NomApresLibelle", PiiDetectionKind.Label, nameStart, 11),
                new PiiDetection(PiiCategory.Name, "PERSON", PiiDetectionKind.Model, nameStart, 11),
                new PiiDetection(PiiCategory.Iban, "Iban", PiiDetectionKind.Specification, ibanStart, 33),
                new PiiDetection(PiiCategory.Iban, "IBAN_CODE", PiiDetectionKind.Model, ibanStart, 33),
                new PiiDetection(PiiCategory.Address, "LOCATION", PiiDetectionKind.Model, addressStart, 9),
                new PiiDetection(PiiCategory.Phone, "Telephone", PiiDetectionKind.Specification, phoneStart, 14)
            ];
        }));

        var masked = await masker.MaskAsync(text);

        Assert.Equal("Contact : [PERSON], [PII], téléphone : [PHONE].", masked);
    }

    [Fact]
    public async Task MaskAsync_MasksModelRemainderOverlappedByDifferentCategoryRule()
    {
        var masker = new PiiTextMasker(new FakePiiDetector(t =>
        {
            var nameStart = t.IndexOf("Jean Dupont", StringComparison.Ordinal);
            var addressStart = t.IndexOf("Dupont demeure", StringComparison.Ordinal);
            return
            [
                new PiiDetection(PiiCategory.Name, "PERSON", PiiDetectionKind.Model, nameStart, "Jean Dupont".Length),
                new PiiDetection(PiiCategory.Address, "VoieDemeure", PiiDetectionKind.Label, addressStart, "Dupont demeure".Length)
            ];
        }));

        var masked = await masker.MaskAsync("Jean Dupont demeure ici.");

        Assert.Equal("[PII] ici.", masked);
        Assert.DoesNotContain("Jean", masked);
    }

    [Fact]
    public async Task MaskAsync_ExtendsMaskWhenModelDetectionIsWiderThanSameCategoryRule()
    {
        var masker = new PiiTextMasker(new FakePiiDetector(t =>
        {
            var nameStart = t.IndexOf("Jean Dupont Martin", StringComparison.Ordinal);
            return
            [
                new PiiDetection(PiiCategory.Name, "NomApresLibelle", PiiDetectionKind.Label, nameStart, "Jean Dupont".Length),
                new PiiDetection(PiiCategory.Name, "PERSON", PiiDetectionKind.Model, nameStart, "Jean Dupont Martin".Length)
            ];
        }));

        var masked = await masker.MaskAsync("Contact : Jean Dupont Martin.");

        Assert.Equal("Contact : [PERSON].", masked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t\n ")]
    public async Task MaskAsync_ReturnsWhitespaceOrEmptyTextUnchanged(string text)
    {
        var masker = new PiiTextMasker(new FakePiiDetector(
            _ => throw new InvalidOperationException("Le détecteur ne doit pas être appelé.")));

        Assert.Equal(text, await masker.MaskAsync(text));
    }

    [Fact]
    public async Task MaskAsync_ThrowsArgumentNullExceptionForNullText()
    {
        var masker = new PiiTextMasker(new FakePiiDetector(_ => []));

        await Assert.ThrowsAsync<ArgumentNullException>(() => masker.MaskAsync(null!));
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, 0)]
    [InlineData(2, 20)]
    public async Task MaskAsync_ThrowsInvalidDataExceptionWhenDetectionIsOutOfRange(int start, int length)
    {
        var masker = new PiiTextMasker(new FakePiiDetector(
            _ => [new PiiDetection(PiiCategory.Name, "PERSON", PiiDetectionKind.Model, start, length)]));

        await Assert.ThrowsAsync<InvalidDataException>(() => masker.MaskAsync("Contact : Jean Dupont"));
    }

    private sealed class FakePiiDetector(Func<string, IReadOnlyList<PiiDetection>> detect) : IPiiDetector
    {
        public Task<IReadOnlyList<PiiDetection>> DetectAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(detect(text));
    }
}
