namespace GsiHost.Diagnostics;

/// <summary>
/// Configures the host-local file used for MVP+ go/no-go counters.
/// </summary>
/// <remarks>
/// There is no network transport and no machine or user identifier.
/// Tests set <see cref="Directory"/> to a temporary path. When
/// <see cref="Directory"/> is empty, the store writes under the host content root.
/// </remarks>
public sealed class LocalGoNoGoCounterOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Diagnostics:GoNoGo";

    /// <summary>
    /// The default file name inside <see cref="Directory"/>.
    /// </summary>
    public const string DefaultFileName = "go-no-go-counters.json";

    /// <summary>
    /// Gets or sets the directory that contains the state file.
    /// </summary>
    /// <value>
    /// An absolute or content-root-relative path. Empty or whitespace uses the host content root.
    /// </value>
    public string? Directory { get; set; }

    /// <summary>
    /// Gets or sets the file name written inside <see cref="Directory"/>.
    /// </summary>
    /// <value>The default is <see cref="DefaultFileName"/>.</value>
    public string FileName { get; set; } = DefaultFileName;
}
