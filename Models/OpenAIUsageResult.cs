namespace Jellyfin.Plugin.SmartSpotifyPlaylists.Models;

/// <summary>
/// Represents token usage and cost information from an OpenAI API call.
/// </summary>
public class OpenAIUsageResult
{
    /// <summary>
    /// Gets or sets the number of input tokens used.
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// Gets or sets the number of output tokens used.
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// Gets the total number of tokens used.
    /// </summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// Gets or sets the estimated cost in USD.
    /// </summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>
    /// Gets or sets the model that was used.
    /// </summary>
    public string ModelUsed { get; set; } = string.Empty;

    /// <summary>
    /// Adds another usage result to this one, combining token counts and costs.
    /// </summary>
    /// <param name="other">The other usage result to add.</param>
    public void Add(OpenAIUsageResult other)
    {
        InputTokens += other.InputTokens;
        OutputTokens += other.OutputTokens;
        EstimatedCostUsd += other.EstimatedCostUsd;
    }
}
