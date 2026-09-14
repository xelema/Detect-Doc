using DetectDoc.Core.Pii;
using DetectDoc.Infrastructure.Pii;
using Xunit;

namespace DetectDoc.Tests;

public class RegexPiiDetectorTests
{
    [Fact]
    public async Task DetectAsync_FindsAnEmailAddress()
    {
        var text = "Courriel : jean.dupont@example.com.";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        var detection = Assert.Single(detections);
        Assert.Equal(PiiCategory.Email, detection.Category);
        Assert.Equal("Email", detection.Rule);
        Assert.Equal(PiiDetectionKind.Specification, detection.Kind);
        Assert.Equal(text.IndexOf("jean.dupont", StringComparison.Ordinal), detection.Start);
        Assert.Equal("jean.dupont@example.com".Length, detection.Length);
    }

    [Fact]
    public async Task DetectAsync_FindsAFrenchPhoneNumber()
    {
        var text = "Téléphone : 06 12 34 56 78.";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        var detection = Assert.Single(detections);
        Assert.Equal(PiiCategory.Phone, detection.Category);
        Assert.Equal("TelephoneFr", detection.Rule);
        Assert.Equal(PiiDetectionKind.Specification, detection.Kind);
        Assert.Equal(text.IndexOf("06 12 34 56 78", StringComparison.Ordinal), detection.Start);
        Assert.Equal("06 12 34 56 78".Length, detection.Length);
    }

    [Fact]
    public async Task DetectAsync_FindsAnIbanWithSpaces()
    {
        var text = "IBAN : FR76 3000 6000 0112 3456 7890 189.";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        var detection = Assert.Single(detections);
        Assert.Equal(PiiCategory.Iban, detection.Category);
        Assert.Equal("Iban", detection.Rule);
        Assert.Equal(PiiDetectionKind.Specification, detection.Kind);
        Assert.Equal(text.IndexOf("FR76", StringComparison.Ordinal), detection.Start);
        Assert.Equal("FR76 3000 6000 0112 3456 7890 189".Length, detection.Length);
    }

    [Fact]
    public async Task DetectAsync_FindsASiretAfterItsLabel()
    {
        var text = "SIRET 123 456 789 00012";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        var detection = Assert.Single(detections);
        Assert.Equal(PiiCategory.Siret, detection.Category);
        Assert.Equal("SiretApresLibelle", detection.Rule);
        Assert.Equal(PiiDetectionKind.Label, detection.Kind);
        Assert.Equal(text.IndexOf("123", StringComparison.Ordinal), detection.Start);
        Assert.Equal("123 456 789 00012".Length, detection.Length);
    }

    [Fact]
    public async Task DetectAsync_FindsANameAfterACivility()
    {
        var text = "M. Jean Dupont";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        var detection = Assert.Single(detections);
        Assert.Equal(PiiCategory.Name, detection.Category);
        Assert.Equal("NomApresCivilite", detection.Rule);
        Assert.Equal(PiiDetectionKind.Label, detection.Kind);
        Assert.Equal(text.IndexOf("Jean", StringComparison.Ordinal), detection.Start);
        Assert.Equal("Jean Dupont".Length, detection.Length);
    }

    [Fact]
    public async Task DetectAsync_FindsANameAfterTheTitulaireLabel()
    {
        var text = "Titulaire : Jean Dupont";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        var detection = Assert.Single(detections);
        Assert.Equal(PiiCategory.Name, detection.Category);
        Assert.Equal("NomApresLibelle", detection.Rule);
        Assert.Equal(PiiDetectionKind.Label, detection.Kind);
        Assert.Equal(text.IndexOf("Jean", StringComparison.Ordinal), detection.Start);
        Assert.Equal("Jean Dupont".Length, detection.Length);
    }

    [Fact]
    public async Task DetectAsync_FindsAPostalAddress()
    {
        var text = "Adresse : 12 rue de la Paix 75002 Paris.";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        var detection = Assert.Single(detections);
        Assert.Equal(PiiCategory.Address, detection.Category);
        Assert.Equal("AdressePostale", detection.Rule);
        Assert.Equal(PiiDetectionKind.Specification, detection.Kind);
        Assert.Equal(text.IndexOf("12 rue", StringComparison.Ordinal), detection.Start);
        Assert.Equal("12 rue de la Paix 75002 Paris".Length, detection.Length);
    }

    [Fact]
    public async Task DetectAsync_FindsALinkedInProfile()
    {
        var text = "Profil : in/jean-dupont.";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        var detection = Assert.Single(detections);
        Assert.Equal(PiiCategory.SocialNetwork, detection.Category);
        Assert.Equal("ProfilLinkedIn", detection.Rule);
        Assert.Equal(PiiDetectionKind.Specification, detection.Kind);
        Assert.Equal(text.IndexOf("in/jean-dupont", StringComparison.Ordinal), detection.Start);
        Assert.Equal("in/jean-dupont".Length, detection.Length);
    }

    [Fact]
    public async Task DetectAsync_ReturnsNoDetectionForACleanText()
    {
        var text = "Le document est un texte simple sans coordonnées.";

        var detections = await new RegexPiiDetector().DetectAsync(text);

        Assert.Empty(detections);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DetectAsync_ThrowsArgumentExceptionForNullOrWhiteSpaceText(string? text)
    {
        var detector = new RegexPiiDetector();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => detector.DetectAsync(text!));
    }
}
