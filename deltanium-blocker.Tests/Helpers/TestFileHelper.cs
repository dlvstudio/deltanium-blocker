using System.IO;

namespace deltanium_blocker.Tests.Helpers;

public class TestFileHelper : IDisposable
{
    private readonly string _tempDirectory;

    public TestFileHelper()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"deltanium-blocker-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public string CreateTempFile(string fileName, string content = "")
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    public string GetTempFilePath(string fileName)
    {
        return Path.Combine(_tempDirectory, fileName);
    }

    public string TempDirectory => _tempDirectory;

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}







