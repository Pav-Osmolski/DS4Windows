using System.Xml.Serialization;
using DS4Windows;
using DS4WinWPF;
using DS4WinWPF.DS4Control.DTOXml;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace DS4WindowsTests;

[TestClass]
public sealed class StoredLogManagerTests
{
    [TestMethod]
    public void RetentionRemovesOnlyExpiredOwnedFilesAndKeepsCurrentSessionAndUnrelatedContent()
    {
        using var fixture = new Files();
        fixture.Write("ds4windows_log_20260901.0.txt", 45);
        fixture.Write("ds4windows_log_20261015.0.txt", 3);
        fixture.Write("ds4windows_log.txt", 45);
        fixture.Write("startup_failure.log", 45);
        fixture.Write("profile.xml", 45);
        fixture.Write("ds4windows_log_my-notes.txt", 45);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "nested"));
        File.WriteAllText(Path.Combine(fixture.Root, "nested", "ds4windows_log_1.txt"), "keep");
        StoredLogCleanupResult result = StoredLogManager.Cleanup(fixture.Root, 30, fixture.Now);
        Assert.AreEqual(new StoredLogCleanupResult(2, 0), result);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "ds4windows_log.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "profile.xml")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "ds4windows_log_my-notes.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "ds4windows_log_20261015.0.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "nested", "ds4windows_log_1.txt")));
    }

    [TestMethod]
    public void ClearArchivesKeepsLoggingFileAndReportsLockedFileForRetry()
    {
        using var fixture = new Files();
        fixture.Write("ds4windows_log.txt", 0);
        fixture.Write("ds4windows_log_1.txt", 0);
        fixture.Write("ds4windows_log_2.txt", 0);
        using (File.Open(Path.Combine(fixture.Root, "ds4windows_log_2.txt"),
                   FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.AreEqual(new StoredLogCleanupResult(1, 1),
                StoredLogManager.Cleanup(fixture.Root, 30, fixture.Now, clearAll: true));
        }
        Assert.AreEqual(new StoredLogCleanupResult(1, 0),
            StoredLogManager.Cleanup(fixture.Root, 30, fixture.Now, clearAll: true));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "ds4windows_log.txt")));
    }

    [TestMethod]
    public void FileCountLimitKeepsNewestTwentyEvenWithinRetentionWindow()
    {
        using var fixture = new Files();
        for (int i = 0; i < 25; i++) fixture.Write($"ds4windows_log_{i}.txt", i);
        Assert.AreEqual(new StoredLogCleanupResult(5, 0),
            StoredLogManager.Cleanup(fixture.Root, 90, fixture.Now));
        Assert.AreEqual(20, Directory.GetFiles(fixture.Root).Length);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "ds4windows_log_0.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "ds4windows_log_24.txt")));
    }

    [TestMethod]
    public void MissingPreferenceDefaultsToThirtyDaysAndCustomPreferenceRoundTrips()
    {
        var serializer = new XmlSerializer(typeof(AppSettingsDTO));
        using var old = new StringReader("<Profile />");
        Assert.AreEqual(30, ((AppSettingsDTO)serializer.Deserialize(old)!).LogRetentionDays);
        Assert.AreEqual(30, new BackingStore().logRetentionDays);
        foreach (int days in StoredLogManager.RetentionChoices.Concat(new[] { -1, 0, 999 }))
        {
            var dto = new AppSettingsDTO();
            dto.MapFrom(new BackingStore { logRetentionDays = days });
            using var text = new StringWriter();
            serializer.Serialize(text, dto);
            using var reader = new StringReader(text.ToString());
            var restored = (AppSettingsDTO)serializer.Deserialize(reader)!;
            var destination = new BackingStore();
            restored.MapTo(destination);
            Assert.AreEqual(StoredLogManager.NormalizeRetentionDays(days), destination.logRetentionDays);
        }
    }

    [TestMethod]
    public void ProductionArchiveConfigurationRotatesAndContinuesWritingWithBoundedFiles()
    {
        using var fixture = new Files();
        using var factory = new LogFactory();
        var target = new FileTarget("logfile")
        {
            FileName = Path.Combine(fixture.Root, "ds4windows_log.txt"),
            ArchiveFileName = Path.Combine(fixture.Root, "ds4windows_log_{#}.txt"),
            Layout = "${message}"
        };
        LoggerHolder.ConfigureArchiveLimits(target);
        Assert.AreEqual(StoredLogManager.ArchiveBytes, target.ArchiveAboveSize);
        Assert.AreEqual(20, target.MaxArchiveFiles);
        Assert.AreEqual(FileArchivePeriod.Day, target.ArchiveEvery);
        // Exercise actual NLog rollover with small fixtures instead of 250 MB.
        target.ArchiveAboveSize = 256;
        var config = new LoggingConfiguration();
        config.AddRule(LogLevel.Info, LogLevel.Fatal, target);
        factory.Configuration = config;
        var logger = factory.GetLogger("stored-log-test");
        for (int i = 0; i < 25; i++) logger.Info(new string('x', 400));
        logger.Info("still writing");
        factory.Flush();
        Assert.IsTrue(Directory.GetFiles(fixture.Root).Length <= 21);
        factory.Shutdown();
        StringAssert.Contains(File.ReadAllText(Path.Combine(fixture.Root, "ds4windows_log.txt")), "still writing");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StartupAndRetentionChangesKeepHeaderAndSessionInOneLog(bool asynchronous)
    {
        using var fixture = new Files();
        string active = Path.Combine(fixture.Root, "ds4windows_log.txt");
        File.WriteAllText(active, "previous session");
        using var factory = new LogFactory();
        var target = new FileTarget("logfile")
        {
            FileName = active,
            ArchiveFileName = Path.Combine(fixture.Root, "ds4windows_log_{#}.txt"),
            Layout = "${message}"
        };
        LoggerHolder.ConfigureArchiveLimits(target);
        var config = new LoggingConfiguration();
        Target logTarget = asynchronous ? new AsyncTargetWrapper(target) { Name = "async-logfile" } : target;
        config.AddRule(LogLevel.Info, LogLevel.Fatal, logTarget);
        factory.Configuration = config;
        var logger = factory.GetLogger("session-continuity-test");
        logger.Info("startup header");
        factory.Flush();
        StoredLogManager.Cleanup(fixture.Root, 30, DateTime.UtcNow);
        logger.Info("controller connected");
        factory.Flush();
        StoredLogManager.Cleanup(fixture.Root, 7, DateTime.UtcNow);
        Assert.AreEqual(0, target.MaxArchiveDays, "Age retention is managed without reconfiguring the active target.");
        logger.Info("session continues after retention change");
        factory.Flush();
        Assert.AreEqual(1, Directory.GetFiles(fixture.Root, "ds4windows_log_*.txt").Length,
            "Only the previous session should be archived, not this run's startup header.");
        factory.Shutdown();
        string current = File.ReadAllText(active);
        StringAssert.Contains(current, "startup header");
        StringAssert.Contains(current, "controller connected");
        StringAssert.Contains(current, "session continues after retention change");
    }

    private sealed class Files : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "DS4WindowsTests", Guid.NewGuid().ToString("N"));
        internal DateTime Now { get; } = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        internal Files() => Directory.CreateDirectory(Root);
        internal void Write(string name, int ageDays)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, "test log");
            File.SetLastWriteTimeUtc(path, Now.AddDays(-ageDays));
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
