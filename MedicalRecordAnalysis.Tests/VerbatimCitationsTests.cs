using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public class VerbatimCitationsTests
{
    private const string EdText = "HISTORY:\nPatient was stopped in traffic. She denies loss of\nconsciousness. Reports a headache.\n" +
        "The patient's injuries are consistent with the reported mechanism of a rear-end motor vehicle collision.";
    private const string PtText = "Neck pain 6/10. Reports a headache.\nShe can lift her son briefly but needs to sit down to do it.";

    private static readonly SourceDocument[] Sources =
    [
        new SourceDocument() { FilePath = "/records/ed.pdf", Passages = [new Passage() { StartPosition = 100, Text = EdText }] },
        new SourceDocument() { FilePath = "/records/pt.txt", Passages = [new Passage() { StartPosition = 0, Text = PtText }] },
    ];

    private static DemandDraft Draft(params DraftSegment[] segments) => new DemandDraft() { Segments = segments };

    [Fact]
    public void CitesASentenceCopiedVerbatimWithTheExactSourceSpan()
    {
        DemandDraft cited = VerbatimCitations.Apply(Draft(new DraftSegment() { Text = "At the hospital. She denies loss of consciousness. Then more." }), Sources);

        DraftSegment segment = Assert.Single(cited.Segments, segment => segment.Citations.Length > 0);
        Citation citation = Assert.Single(segment.Citations);
        Assert.Equal("She denies loss of consciousness.", segment.Text);
        Assert.Equal("/records/ed.pdf", citation.FilePath);
        // The source wraps the line; the citation keeps the source's own text and position.
        Assert.Equal("She denies loss of\nconsciousness.", citation.CitedText);
        Assert.Equal(100 + EdText.IndexOf("She denies", StringComparison.Ordinal), citation.StartPosition);
        Assert.Equal("She denies loss of consciousness.", citation.Claim);
    }

    [Fact]
    public void IgnoresLetterCase()
    {
        DemandDraft cited = VerbatimCitations.Apply(Draft(new DraftSegment() { Text = "she can lift her son briefly but needs to sit down to do it." }), Sources);

        Assert.Equal("/records/pt.txt", Assert.Single(cited.Segments).Citations.Single().FilePath);
    }

    [Fact]
    public void CitesRecordWordingAfterAShortLeadIn()
    {
        DemandDraft cited = VerbatimCitations.Apply(
            Draft(new DraftSegment() { Text = "The ED physician wrote that the patient's injuries are consistent with the reported mechanism of a rear-end motor vehicle collision." }),
            Sources);

        Citation citation = Assert.Single(Assert.Single(cited.Segments).Citations);
        Assert.StartsWith("The patient's injuries are consistent", citation.CitedText);
    }

    [Fact]
    public void LeavesTextFoundInSeveralPlacesUncited()
    {
        DemandDraft cited = VerbatimCitations.Apply(Draft(new DraftSegment() { Text = "Reports a headache and more." }, new DraftSegment() { Text = " Reports a headache." }), Sources);

        Assert.All(cited.Segments, segment => Assert.Empty(segment.Citations));
    }

    [Fact]
    public void LeavesParaphrasesShortTextAndHeadingsUncitedAndReportsThem()
    {
        DemandDraft cited = VerbatimCitations.Apply(
            Draft(new DraftSegment() { Text = "## Facts of the Incident\n\nShe was rear-ended while waiting at a light. Pain 6/10." }),
            Sources);

        Assert.All(cited.Segments, segment => Assert.Empty(segment.Citations));
        Assert.Equal(["She was rear-ended while waiting at a light.", "Pain 6/10."], cited.UnsupportedStatements);
    }

    [Fact]
    public void KeepsExistingCitationsAndTheDraftText()
    {
        Citation existing = new Citation() { FilePath = "/records/pt.txt", CitedText = "Neck pain 6/10.", StartPosition = 0 };
        DemandDraft draft = Draft(
            new DraftSegment() { Text = "Neck pain was 6/10", Citations = [existing] },
            new DraftSegment() { Text = ". She can lift her son briefly but needs to sit down to do it. She seemed tired." });

        DemandDraft cited = VerbatimCitations.Apply(draft, Sources);

        Assert.Equal(draft.Narrative, cited.Narrative);
        Assert.Same(existing, cited.Segments[0].Citations.Single());
        Assert.Equal(2, cited.Segments.Count(segment => segment.Citations.Length > 0));
        Assert.Equal(["She seemed tired."], cited.UnsupportedStatements);
    }

    [Fact]
    public void LeavesDraftsWithoutSourcesUnchanged()
    {
        DemandDraft draft = Draft(new DraftSegment() { Text = "She denies loss of consciousness." });

        Assert.Same(draft, VerbatimCitations.Apply(draft, []));
    }
}
