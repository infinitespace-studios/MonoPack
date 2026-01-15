namespace MonoPack.Builders;

/// <summary>Provides strategies for building applications for different platforms.</summary>
internal interface IPlatformBuilder
{
    /// <summary>Builds the application for a specific runtime identifier.</summary>
    /// <param name="projectPath">Path to the project file.</param>
    /// <param name="outputDir">Directory to place build artifacts.</param>
    /// <param name="rid">Runtime identifier for the target platform.</param>
    /// <param name="suggestedExecutableName">User-specified custom name for the exeutable (from -e flag).</param>
    /// <param name="defaultExecutableName">Default executable name from the project's MSBUILD configuration..</param>
    /// <param name="verbose">Indicates whether to display verbose output.</param>
    /// <param name="publishArgs">Custom arguments to pass to dotnet publish. When specified, default flags are not applied.</param>
    void Build(string projectPath, string outputDir, string rid, string? suggestedExecutableName, string defaultExecutableName, bool verbose, string? publishArgs);
}
