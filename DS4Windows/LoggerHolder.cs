/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.IO;
using System.Threading;
using DS4Windows;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace DS4WinWPF
{
    public class LoggerHolder
    {
        private Logger logger;// = LogManager.GetCurrentClassLogger();
        public Logger Logger { get => logger; }
        internal string LogDirectory { get; }
        private FileTarget configuredFileTarget;
        private ReaderWriterLockSlim logLock = new ReaderWriterLockSlim();

        public LoggerHolder(DS4Windows.ControlService service)
        {
            string logDirectory = ResolveLogDirectory();
            LogDirectory = logDirectory;
            Directory.CreateDirectory(logDirectory);

            LoggingConfiguration configuration =
                LogManager.Configuration ?? new LoggingConfiguration();
            FileTarget fileTarget = FindFileTarget(configuration);
            if (fileTarget == null)
            {
                fileTarget = new FileTarget("logfile")
                {
                    Layout = "${longdate}|${level:uppercase=true}|${message}",
                };
                configuration.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);
            }
            configuredFileTarget = fileTarget;
            ConfigureArchiveLimits(fileTarget);
            fileTarget.FileName = Path.Combine(logDirectory, "ds4windows_log.txt");
            fileTarget.ArchiveFileName = Path.Combine(logDirectory, "ds4windows_log_{#}.txt");
            LogManager.Configuration = configuration;
            LogManager.ReconfigExistingLoggers();
            logger = LogManager.GetCurrentClassLogger();
            if (service != null) service.Debug += WriteToLog;
            DS4Windows.AppLogger.GuiLog += WriteToLog;
        }

        internal static string ResolveLogDirectory()
        {
            string dataPath = PortableLabContext.Current?.DataPath ?? DS4Windows.Global.appdatapath;
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                dataPath = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                    "DS4Windows");
            }

            return Path.Combine(dataPath, "Logs");
        }

        internal static void ConfigureArchiveLimits(FileTarget target)
        {
            // Defer age cleanup until the saved retention preference is loaded.
            target.MaxArchiveDays = 0;
            target.MaxArchiveFiles = StoredLogManager.MaximumArchiveFiles;
            target.ArchiveAboveSize = StoredLogManager.ArchiveBytes;
            target.ArchiveEvery = FileArchivePeriod.Day;
            target.ArchiveOldFileOnStartup = true;
            target.ArchiveNumbering = ArchiveNumberingMode.DateAndSequence;
            target.ArchiveDateFormat = "yyyyMMdd";
            target.Encoding = new System.Text.UTF8Encoding(false);
        }

        internal StoredLogCleanupResult ApplyRetention() => ApplyRetention(
            configuredFileTarget, LogManager.LogFactory, LogDirectory,
            Global.LogRetentionDays, DateTime.UtcNow);

        internal static StoredLogCleanupResult ApplyRetention(FileTarget target,
            LogFactory factory, string directory, int days, DateTime utcNow)
        {
            // Drain the async startup header before changing archive policy.
            // Updating MaxArchiveDays resets NLog's archive state; startup
            // rollover must only happen once, not again on the next log write.
            // ConfigureArchiveLimits re-enables it for the next process launch.
            factory.Flush(TimeSpan.FromSeconds(1));
            target.ArchiveOldFileOnStartup = false;
            target.MaxArchiveDays = StoredLogManager.NormalizeRetentionDays(days);
            return StoredLogManager.Cleanup(directory, days, utcNow);
        }

        internal static FileTarget FindFileTarget(
            LoggingConfiguration configuration)
        {
            Target target = configuration?.FindTargetByName("logfile");
            while (target is WrapperTargetBase wrapper)
            {
                target = wrapper.WrappedTarget;
            }

            return target as FileTarget;
        }

        private void WriteToLog(object sender, DS4Windows.DebugEventArgs e)
        {
            if (e.Temporary)
            {
                return;
            }

            using WriteLocker locker = new WriteLocker(logLock);
            if (!e.Warning)
            {
                logger.Info(e.Data);
            }
            else
            {
                logger.Warn(e.Data);
            }
        }
    }
}
