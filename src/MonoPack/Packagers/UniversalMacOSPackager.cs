using System.Diagnostics;
using System.IO.Compression;

namespace MonoPack.Packagers;

internal sealed class UniversalMacOSPackager : PlatformPackager
{
    private readonly string _infoPlistPath;
    private readonly string _icnsPath;
    private readonly string _x64BuildDir;
    private readonly string _arm64BuildDir;
    private readonly string _actualExecutableName;

    /// <summary>Initializes a new instance of the <see cref="UniversalMacOSPackager"/> class.</summary>
    /// <param name="infoPlistPath">Path to the Info.plist file.</param>
    /// <param name="icnsPath">Path to the .icns icon file.</param>
    /// <param name="x64BuildDir">Directory containing the osx-x64 build artifacts.</param>
    /// <param name="arm64BuildDir">Directory containing the osx-arm64 build artifacts.</param>
    /// <param name="actualExecutableName">The actual name of the executable file produced by the build.</param>
    public UniversalMacOSPackager(string infoPlistPath, string icnsPath, string x64BuildDir, string arm64BuildDir, string actualExecutableName)
    {
        _infoPlistPath = infoPlistPath;
        _icnsPath = icnsPath;
        _x64BuildDir = x64BuildDir;
        _arm64BuildDir = arm64BuildDir;
        _actualExecutableName = actualExecutableName;
    }

    /// <inheritdoc/>
    public override void Package(string sourceDir, string outputDir, string projectName, string? executableName, string rid, bool useZip)
    {
        string appName = executableName ?? projectName;
        string appDir = Path.Combine(outputDir, $"{appName}.app");
        string contentsDir = Path.Combine(appDir, "Contents");
        string macOSDir = Path.Combine(contentsDir, "MacOS");
        string resourcesDir = Path.Combine(contentsDir, "Resources");
        string resourcesContentDir = Path.Combine(resourcesDir, "Content");

        // Create app bundle directories
        Directory.CreateDirectory(contentsDir);
        Directory.CreateDirectory(macOSDir);
        Directory.CreateDirectory(resourcesDir);

        // Copy Info.plist
        string infoPlistDest = Path.Combine(contentsDir, "Info.plist");
        File.Copy(_infoPlistPath, infoPlistDest, overwrite: true);

        // Copy the icns file
        string icnsDest = Path.Combine(resourcesDir, Path.GetFileName(_icnsPath));
        File.Copy(_icnsPath, icnsDest, overwrite: true);

        // Handle universal binary creation based on platform
        if (OperatingSystem.IsMacOS())
        {
            CreateUniversalBinaryWithLipo(macOSDir, resourcesContentDir, appName);
        }
        else
        {
            CreateUniversalBinaryWithScript(macOSDir, resourcesContentDir, appName);
        }

        // Create the archive
        string archivePath = Path.Combine(outputDir, $"{appName}-universal.{(useZip ? "zip" : "tar.gz")}");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using FileStream fileStream = File.OpenWrite(archivePath);

        if (useZip)
        {
            using ZipArchive zip = new ZipArchive(fileStream, ZipArchiveMode.Create);
            ZipDirectory(appDir, true, zip, executableName, projectName, archivePath);
        }
        else
        {
            using GZipStream gzStream = new GZipStream(fileStream, CompressionMode.Compress);
            TarDirectory(appDir, true, gzStream, executableName, projectName, archivePath);
        }

        // Clean up build directories
        DeleteDirectory(_x64BuildDir, recursive: true);
        DeleteDirectory(_arm64BuildDir, recursive: true);

        Console.WriteLine($"Created universal macOS archive: {archivePath}");
    }

    private void CreateUniversalBinaryWithLipo(string macOSDir, string resourcesContentDir, string appName)
    {
        // Copy the x64 build to the MacOS directory
        CopyDirectory(_x64BuildDir, macOSDir);

        // Use lip to create universal executable
        // Use lipo to create universal executable
        string x64ExecutablePath = Path.Combine(_x64BuildDir, _actualExecutableName);
        string arm64ExecutablePath = Path.Combine(_arm64BuildDir, _actualExecutableName);
        string universalExecutablePath = Path.Combine(macOSDir, _actualExecutableName);

        ExecuteLipo(x64ExecutablePath, arm64ExecutablePath, universalExecutablePath);

        // If the app name differs from the actual executable name, rename it
        if(!_actualExecutableName.Equals(appName, StringComparison.Ordinal))
        {
            string renamedPath = Path.Combine(macOSDir, appName);
            File.Move(universalExecutablePath, renamedPath);
        }

        // Move Content directory to Resources
        string gameContentDir = Path.Combine(macOSDir, "Content");
        if (Directory.Exists(gameContentDir))
        {
            DeleteDirectory(resourcesContentDir, recursive: true);
            Directory.Move(gameContentDir, resourcesContentDir);
        }
    }

    private void CreateUniversalBinaryWithScript(string macOSDir, string resourcesContentDir, string appName)
    {
        // Create architecture-specific subdirectories
        string amd64Dir = Path.Combine(macOSDir, "amd64");
        string arm64Dir = Path.Combine(macOSDir, "arm64");

        Directory.CreateDirectory(amd64Dir);
        Directory.CreateDirectory(arm64Dir);

        // Copy x64 build to amd64 subdirectory
        CopyDirectory(_x64BuildDir, amd64Dir);
        string x64ContentDir = Path.Combine(amd64Dir, "Content");
        if (Directory.Exists(x64ContentDir))
        {
            DeleteDirectory(x64ContentDir, recursive: true);
        }

        // Copy ARM64 build to arm64 subdirectory
        CopyDirectory(_arm64BuildDir, arm64Dir);
        string arm64ContentDir = Path.Combine(arm64Dir, "Content");
        if (Directory.Exists(arm64ContentDir))
        {
            DeleteDirectory(arm64ContentDir, recursive: true);
        }

        // Move Content directory from ARM64 build to Resources
        // (both builds should have identical content)
        string sourceContentDir = Path.Combine(_arm64BuildDir, "Content");
        if (Directory.Exists(sourceContentDir))
        {
            DeleteDirectory(resourcesContentDir, recursive: true);
            Directory.Move(sourceContentDir, resourcesContentDir);
        }

        // Create launcher script that selects the correct architecture at runtime
        CreateLauncherScript(macOSDir, appName, _actualExecutableName);
    }

    private static void CreateLauncherScript(string macOSDir, string appName, string actualExecutableName)
    {
        string launchScriptPath = Path.Combine(macOSDir, appName);
        string scriptContent =
        $"""
        #!/bin/bash

        cd "$(dirname $BASH_SOURCE)/../Resources"
        if [[ $(uname -p) == 'arm' ]]; then
          ./../MacOS/arm64/{actualExecutableName}
        else
          ./../MacOS/amd64/{actualExecutableName}
        fi
        """;

        // Ensure Unix line endings (LF)
        scriptContent = scriptContent.ReplaceLineEndings("\n");
        File.WriteAllText(launchScriptPath, scriptContent);

        // Set executable permissions if on Unix platform
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(launchScriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static void ExecuteLipo(string x64Path, string arm64Path, string outputPath)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "lipo",
            Arguments = $"-create \"{x64Path}\" \"{arm64Path}\" -output \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, args) =>
        {
            if (args.Data is not null)
            {
                Console.WriteLine(args.Data);
            }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data is not null)
            {
                Console.Error.WriteLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"lipo command failed with exit code {process.ExitCode}");
        }

        Console.WriteLine($"Created universal binary using lipo: {outputPath}");
    }
}
