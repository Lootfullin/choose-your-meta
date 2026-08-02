using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RussianMetadata.Tests;

public sealed class StalePluginVersionCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"plugin-cleanup-{Guid.NewGuid():N}");

    [Fact]
    public void Cleanup_DeletesMatchingOlderVersionAndPreservesEverythingElse()
    {
        var current = CreateVersion("Choose your Meta!_1.4.6.0", Plugin.PluginGuid, "1.4.6.0");
        var old = CreateVersion("Choose your Meta!_1.4.5.0", Plugin.PluginGuid, "1.4.5.0");
        var newer = CreateVersion("Choose your Meta!_1.4.7.0", Plugin.PluginGuid, "1.4.7.0");
        var unrelated = CreateVersion("Other_1.0.0.0", Guid.NewGuid().ToString(), "1.0.0.0");

        Assert.Equal(0, StalePluginVersionCleaner.Cleanup(
            current, Plugin.PluginGuid, "Choose your Meta!", new Version(1, 4, 6, 0), NullLogger.Instance));
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(current));
        Assert.True(Directory.Exists(newer));
        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void Cleanup_MarksFailedDeletionAndRemovesDirectoryOnRetry()
    {
        var current = CreateVersion("Choose your Meta!_1.4.6.0", Plugin.PluginGuid, "1.4.6.0");
        var old = CreateVersion("Choose your Meta!_1.4.5.0", Plugin.PluginGuid, "1.4.5.0");
        File.SetAttributes(Path.Combine(old, "meta.json"), FileAttributes.ReadOnly);

        Assert.Equal(1, StalePluginVersionCleaner.Cleanup(
            current, Plugin.PluginGuid, "Choose your Meta!", new Version(1, 4, 6, 0), NullLogger.Instance, _ => false));
        Assert.Contains("Deleted", File.ReadAllText(Path.Combine(old, "meta.json")));
        Assert.Equal(0, StalePluginVersionCleaner.Cleanup(
            current, Plugin.PluginGuid, "Choose your Meta!", new Version(1, 4, 6, 0), NullLogger.Instance));
        Assert.False(Directory.Exists(old));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateVersion(string folderName, string guid, string version)
    {
        var directory = Path.Combine(_root, folderName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "meta.json"),
            $$"""{"guid":"{{guid}}","version":"{{version}}","status":"Active","autoUpdate":true}""");
        return directory;
    }
}
