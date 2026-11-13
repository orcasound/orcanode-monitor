// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
namespace OrcanodeMonitor.Models
{
    /// <summary>
    /// Summary comparison statistics for a single detection model.
    /// </summary>
    public class ModelComparisonResult
    {
        public string Name { get; set; } = string.Empty;
        public long Evaluated { get; set; }
        public long Correct { get; set; }
        public double Accuracy => Evaluated == 0 ? 0 : (double)Correct / Evaluated;
        public double F1 { get; set; }
        public double ResidentFalsePositiveRate { get; set; }
        public double ResidentFalseNegativeRate { get; set; }
        public double TransientFalsePositiveRate { get; set; }
        public double TransientFalseNegativeRate { get; set; }
        public double HumpbackFalsePositiveRate { get; set; }
        public double HumpbackFalseNegativeRate { get; set; }
    }

    /// <summary>
    /// A single row in a confusion matrix: counts of predicted classes for a given actual class.
    /// </summary>
    public class ConfusionMatrixRow
    {
        public string ActualClass { get; set; } = string.Empty;
        public Dictionary<string, long> PredictedCounts { get; set; } = new();
        public long Total => PredictedCounts.Values.Sum();
    }

    /// <summary>
    /// Confusion matrix for a single model, including the ordered list of predicted
    /// class column headers and the per-actual-class rows.
    /// </summary>
    public class ConfusionMatrix
    {
        public string ModelName { get; set; } = string.Empty;
        public SortedSet<string> PredictedClasses { get; set; } = new();
        public SortedDictionary<string, ConfusionMatrixRow> Rows { get; set; } = new();

        private static readonly HashSet<string> WhaleClassNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "humpback",
            "resident",
            "transient",
            "whale", // Used by human detections.
        };

        public long TotalReviewed => Rows.Sum(r => r.Key == "unreviewed" ? 0 : r.Value.Total);

        /// <summary>
        /// Compute macro F1 across whale classes that appear in actual labels or predictions.
        /// Returns 0 if no whale labels are present.
        public double GetWhaleF1()
        {
            // Labels seen in rows (actual) and columns (predicted).
            var labelsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var actual in Rows.Keys)
            {
                labelsSeen.Add(actual);
            }
            foreach (var row in Rows.Values)
            {
                foreach (var predicted in row.PredictedCounts.Keys)
                {
                    labelsSeen.Add(predicted);
                }
            }

            var whaleLabels = labelsSeen
                .Where(label => WhaleClassNames.Contains(label))
                .OrderBy(label => label)
                .ToList();

            if (whaleLabels.Count == 0)
            {
                return 0;
            }

            var f1Scores = new List<double>();

            foreach (var label in whaleLabels)
            {
                // TP: actual=label and predicted=label
                long truePositives = 0;
                if (Rows.TryGetValue(label, out var labelRow) &&
                    labelRow.PredictedCounts.TryGetValue(label, out var tp))
                {
                    truePositives = tp;
                }

                // Actual count for label (all predictions in that row)
                long actualCount = Rows.TryGetValue(label, out var actualRow) ? actualRow.Total : 0;

                // FN: actual=label but predicted!=label
                long falseNegatives = actualCount - truePositives;

                // FP: actual!=label but predicted=label
                long falsePositives = Rows
                    .Where(r => r.Key != "unreviewed" &&
                                !string.Equals(r.Key, label, StringComparison.OrdinalIgnoreCase))
                    .Sum(r => r.Value.PredictedCounts.TryGetValue(label, out var count) ? count : 0);

                double precision = (truePositives + falsePositives) > 0
                    ? (double)truePositives / (truePositives + falsePositives)
                    : 0.0;

                double recall = (truePositives + falseNegatives) > 0
                    ? (double)truePositives / (truePositives + falseNegatives)
                    : 0.0;

                double f1 = (precision + recall) > 0
                    ? (2 * precision * recall) / (precision + recall)
                    : 0.0;

                f1Scores.Add(f1);
            }

            return f1Scores.Average();
        }
    }
}