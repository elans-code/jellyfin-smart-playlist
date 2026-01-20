using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Helpers;

/// <summary>
/// Static helper for calculating OpenAI API costs.
/// </summary>
public static class OpenAICostCalculator
{
    /// <summary>
    /// Pricing per 1 million tokens for each model (as of Jan 2025).
    /// </summary>
    private static readonly Dictionary<string, (decimal Input, decimal Output)> ModelPricing = new()
    {
        { "gpt-4o-mini", (0.15m, 0.60m) },
        { "gpt-4o", (2.50m, 10.00m) },
        { "gpt-4-turbo", (10.00m, 30.00m) },
        { "gpt-3.5-turbo", (0.50m, 1.50m) }
    };

    /// <summary>
    /// Gets the list of available models.
    /// </summary>
    public static IReadOnlyList<string> AvailableModels { get; } = new[]
    {
        "gpt-4o-mini",
        "gpt-4o",
        "gpt-4-turbo",
        "gpt-3.5-turbo"
    };

    /// <summary>
    /// Gets the pricing for a specific model.
    /// </summary>
    /// <param name="model">The model name.</param>
    /// <returns>Tuple of (input cost per 1M tokens, output cost per 1M tokens).</returns>
    public static (decimal InputCostPer1M, decimal OutputCostPer1M) GetModelPricing(string model)
    {
        if (ModelPricing.TryGetValue(model, out var pricing))
        {
            return pricing;
        }

        // Default to gpt-4o-mini pricing if model not found
        return ModelPricing["gpt-4o-mini"];
    }

    /// <summary>
    /// Estimates the cost for a sync operation before it runs.
    /// </summary>
    /// <param name="trackCount">Number of tracks to analyze.</param>
    /// <param name="clusters">Number of clusters to generate.</param>
    /// <param name="model">The model to use.</param>
    /// <returns>Estimated cost in USD.</returns>
    public static decimal EstimateCost(int trackCount, int clusters, string model)
    {
        // Token estimation formula:
        // - Tokens per track ≈ 8 (artist + title)
        // - Cluster prompt overhead ≈ 200 tokens
        // - Assignment prompt overhead ≈ 150 tokens
        // - Input tokens ≈ (tracks × 8) + 200 + (tracks × 8) + 150
        // - Output tokens ≈ 100 + (tracks × 2)

        const int TokensPerTrack = 8;
        const int ClusterPromptOverhead = 200;
        const int AssignmentPromptOverhead = 150;
        const int BaseOutputTokens = 100;
        const int OutputTokensPerTrack = 2;

        int inputTokens = (trackCount * TokensPerTrack) + ClusterPromptOverhead +
                          (trackCount * TokensPerTrack) + AssignmentPromptOverhead;
        int outputTokens = BaseOutputTokens + (trackCount * OutputTokensPerTrack);

        return CalculateActualCost(inputTokens, outputTokens, model);
    }

    /// <summary>
    /// Calculates the actual cost based on token usage.
    /// </summary>
    /// <param name="inputTokens">Number of input tokens used.</param>
    /// <param name="outputTokens">Number of output tokens used.</param>
    /// <param name="model">The model that was used.</param>
    /// <returns>Cost in USD.</returns>
    public static decimal CalculateActualCost(int inputTokens, int outputTokens, string model)
    {
        var (inputCostPer1M, outputCostPer1M) = GetModelPricing(model);

        // Convert from cost per 1M tokens to cost per token
        decimal inputCost = (inputTokens / 1_000_000m) * inputCostPer1M;
        decimal outputCost = (outputTokens / 1_000_000m) * outputCostPer1M;

        return inputCost + outputCost;
    }

    /// <summary>
    /// Formats a cost value for display.
    /// </summary>
    /// <param name="cost">The cost in USD.</param>
    /// <returns>Formatted string with appropriate decimal places.</returns>
    public static string FormatCost(decimal cost)
    {
        if (cost < 0.01m)
        {
            return cost.ToString("$0.0000");
        }

        return cost.ToString("$0.00");
    }
}
