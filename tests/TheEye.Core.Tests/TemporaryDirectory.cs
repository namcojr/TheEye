namespace TheEye.Core.Tests;

/// <summary>
/// Creates an isolated folder for tests that touch the file system and removes
/// it afterwards. The folder name is unique per test so parallel test classes
/// never share settings files.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "TheEye.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string FilePath(string fileName) => Path.Combine(DirectoryPath, fileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // A failed cleanup must not turn a passing test into a failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
