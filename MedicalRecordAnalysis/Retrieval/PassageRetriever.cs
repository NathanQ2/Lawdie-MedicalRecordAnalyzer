using System.Text.RegularExpressions;

namespace MedicalRecordAnalysis.Retrieval;

/**
 * A part of the demand letter and the terms that signal a passage is relevant to it.
 */
public record DraftTopic(string Name, IReadOnlyList<string> Keywords);

/**
 * Selects the passages most relevant to each part of a demand letter, so drafting sees in-depth source text
 * without the whole case. Scoring is keyword based: each topic keyword found in a passage adds its inverse
 * document frequency, and passages containing quotes extracted during ingest get a boost. Topics take turns
 * adding their best remaining passage until the character budget is full; if the whole case fits the budget,
 * every passage is kept.
 */
public static class PassageRetriever
{
    public const int DefaultCharacterBudget = 40_000;

    private const double ExtractedQuoteBoost = 1.0;

    public static readonly IReadOnlyList<DraftTopic> DemandLetterTopics =
    [
        new DraftTopic("Incident", ["collision", "accident", "mvc", "mva", "rear-end", "struck", "vehicle", "driver",
            "passenger", "restrained", "seatbelt", "airbag", "ambulance", "ems", "scene", "impact", "police"]),
        new DraftTopic("Injuries and diagnoses", ["diagnosis", "diagnosed", "assessment", "impression", "strain", "sprain",
            "fracture", "herniation", "disc", "radiculopathy", "tear", "contusion", "concussion", "whiplash", "mri", "ct",
            "x-ray", "findings", "icd"]),
        new DraftTopic("Treatment", ["plan", "prescribed", "mg", "therapy", "physical therapy", "injection", "surgery",
            "procedure", "referred", "follow up", "follow-up", "visit", "sessions", "medication", "discharged"]),
        new DraftTopic("Pain and daily-life impact", ["pain", "/10", "unable", "difficulty", "sleep", "work", "lift",
            "daily", "limited", "limitation", "disability", "stiffness", "headache", "function", "activities", "driving",
            "missed", "cannot"]),
        new DraftTopic("Causation", ["secondary to", "due to", "caused", "result of", "following", "related to",
            "consistent with", "causally", "prior", "history of", "pre-existing", "preexisting"]),
        new DraftTopic("Damages", ["wage", "wages", "hourly", "overtime", "salary", "employed", "leave", "off work",
            "return to work", "modified duty", "charges", "billing", "bill", "cost", "out-of-pocket", "$"]),
    ];

    public static RetrievedPassage[] Select(PICase piCase, int characterBudget = DefaultCharacterBudget)
    {
        return Select(piCase, DemandLetterTopics, characterBudget);
    }

    public static RetrievedPassage[] Select(PICase piCase, IReadOnlyList<DraftTopic> topics, int characterBudget)
    {
        List<Candidate> candidates = piCase.Sources
            .SelectMany(source => source.Passages.Select(passage => new Candidate(source.FilePath, passage)))
            .ToList();

        if (candidates.Sum(candidate => candidate.Passage.Text.Length) <= characterBudget)
        {
            return candidates.Select(ToRetrieved).ToArray();
        }

        HashSet<Candidate> withQuotes = candidates.Where(candidate => ContainsExtractedQuote(piCase, candidate)).ToHashSet();
        List<double[]> topicScores = topics.Select(topic => Score(candidates, CountKeywords(candidates, topic), withQuotes)).ToList();

        // Topics take turns adding their best remaining passage that fits, so every part of the letter gets
        // coverage. A passage's score is divided by one more than the number of passages already taken from its
        // file, so one long record (like months of daily therapy notes) cannot crowd out the rest of the case.
        bool[] selected = new bool[candidates.Count];
        Dictionary<string, int> takenFromFile = [];
        int used = 0;
        bool added = true;
        while (added)
        {
            added = false;
            foreach (double[] scores in topicScores)
            {
                int best = -1;
                double bestScore = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (selected[i] || scores[i] <= 0 || used + candidates[i].Passage.Text.Length > characterBudget)
                    {
                        continue;
                    }

                    double adjusted = scores[i] / (1 + takenFromFile.GetValueOrDefault(candidates[i].FilePath));
                    if (adjusted > bestScore)
                    {
                        best = i;
                        bestScore = adjusted;
                    }
                }

                if (best >= 0)
                {
                    selected[best] = true;
                    used += candidates[best].Passage.Text.Length;
                    takenFromFile[candidates[best].FilePath] = takenFromFile.GetValueOrDefault(candidates[best].FilePath) + 1;
                    added = true;
                }
            }
        }

        return candidates.Where((_, index) => selected[index]).Select(ToRetrieved).ToArray();
    }

    /**
     * Counts each keyword's whole-word, case-insensitive occurrences in every candidate. A keyword starting or
     * ending in punctuation (like "/10") only needs a word boundary on its alphanumeric side.
     */
    private static Dictionary<string, int[]> CountKeywords(List<Candidate> candidates, DraftTopic topic)
    {
        return topic.Keywords.Distinct().ToDictionary(keyword => keyword, keyword =>
        {
            Regex pattern = new Regex((char.IsLetterOrDigit(keyword[0]) ? @"\b" : "") + Regex.Escape(keyword) +
                (char.IsLetterOrDigit(keyword[^1]) ? @"\b" : ""), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return candidates.Select(candidate => pattern.Count(candidate.Passage.Text)).ToArray();
        });
    }

    private static double[] Score(List<Candidate> candidates, Dictionary<string, int[]> counts, HashSet<Candidate> withQuotes)
    {
        Dictionary<string, double> idf = counts.ToDictionary(entry => entry.Key,
            entry => Math.Log(1.0 + (double)candidates.Count / (1 + entry.Value.Count(count => count > 0))));

        return candidates.Select((candidate, index) =>
        {
            double score = counts.Sum(entry => idf[entry.Key] * Math.Log(1 + entry.Value[index]));
            return score > 0 && withQuotes.Contains(candidate) ? score + ExtractedQuoteBoost : score;
        }).ToArray();
    }

    private static bool ContainsExtractedQuote(PICase piCase, Candidate candidate)
    {
        int start = candidate.Passage.StartPosition;
        int end = start + candidate.Passage.Text.Length;
        return piCase.DocumentSummaries
            .SelectMany(summary => summary.Citations)
            .Any(citation => citation.FilePath == candidate.FilePath && citation.StartPosition < end && citation.StartPosition + citation.CitationLength > start);
    }

    private static RetrievedPassage ToRetrieved(Candidate candidate)
    {
        return new RetrievedPassage(candidate.FilePath, candidate.Passage.StartPosition, candidate.Passage.Text);
    }

    private sealed record Candidate(string FilePath, Passage Passage);
}
