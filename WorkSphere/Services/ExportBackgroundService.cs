using System.Globalization;
using Cronos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkSphere.Models;

namespace WorkSphere.Services;

public class ExportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackupSchedulerState _schedulerState;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExportBackgroundService> _logger;

    public ExportBackgroundService(
        IServiceScopeFactory scopeFactory,
        BackupSchedulerState schedulerState,
        IConfiguration configuration,
        ILogger<ExportBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _schedulerState = schedulerState;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool?>("Export:ScheduledBackup:Enabled") ?? true;
        _schedulerState.IsEnabled = enabled;

        if (!enabled)
        {
            _logger.LogInformation("Automatic scheduled backups are disabled via configuration (Export:ScheduledBackup:Enabled = false).");
            return;
        }

        var cronStr = _configuration["Export:ScheduledBackup:CronExpression"];
        if (string.IsNullOrWhiteSpace(cronStr))
        {
            var legacyHour = _configuration.GetValue<int?>("Export:ScheduledBackup:DailyAtUtcHour");
            cronStr = legacyHour.HasValue ? $"0 {legacyHour.Value} * * *" : "0 2 * * *";
        }

        _schedulerState.CronExpression = cronStr;

        _logger.LogInformation(
            "ExportBackgroundService started. Schedule (Cron): {CronExpression}",
            cronStr);

        var referenceTime = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = CalculateNextRunUtc(_schedulerState.CronExpression, referenceTime);
            if (!nextRun.HasValue)
            {
                _logger.LogWarning("Unable to calculate next run for cron expression '{Cron}'. Checking again in 1 hour.", _schedulerState.CronExpression);
                try
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                referenceTime = DateTime.UtcNow;
                continue;
            }

            _schedulerState.NextRunUtc = nextRun.Value;

            var delay = nextRun.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "Next automatic backup scheduled for {NextRunUtc:yyyy-MM-dd HH:mm:ss} UTC (in {DelayHours:0.0} hours).",
                    nextRun.Value,
                    delay.TotalHours);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ExecuteScheduledBackupAsync();

            // Prevent sub-second early wakeup double-execution by scheduling strictly after the run that just executed
            referenceTime = nextRun.Value > DateTime.UtcNow ? nextRun.Value : DateTime.UtcNow;
        }

        _logger.LogInformation("ExportBackgroundService has stopped.");
    }

    public async Task ExecuteScheduledBackupAsync()
    {
        _schedulerState.IsRunningNow = true;
        _logger.LogInformation("ExportBackgroundService is triggering scheduled backup...");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var exportService = scope.ServiceProvider.GetRequiredService<IExportService>();
            var result = await exportService.RunBackupAsync();

            _schedulerState.LastRunUtc = DateTime.UtcNow;
            _schedulerState.LastRunSuccess = result.Success;
            _schedulerState.LastRunMessage = result.Success
                ? $"{result.FilesWritten} files written to {result.OutputDirectory}"
                : result.ErrorMessage;

            if (result.Success)
            {
                _logger.LogInformation("Scheduled backup succeeded: {Message}", _schedulerState.LastRunMessage);

                // Run retention cleanup if MaxBackupHistory is configured (> 0)
                var maxHistory = _configuration.GetValue<int?>("Export:ScheduledBackup:MaxBackupHistory");
                if (maxHistory.HasValue && maxHistory.Value > 0)
                {
                    var targetDir = exportService.GetDefaultExportDirectory();
                    var pruned = exportService.PruneOldSnapshots(targetDir, maxHistory.Value);
                    if (pruned > 0)
                    {
                        _logger.LogInformation("Pruned {Count} old backup snapshot(s) keeping last {Max}", pruned, maxHistory.Value);
                    }
                }
            }
            else
            {
                _logger.LogError("Scheduled backup completed with failure: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _schedulerState.LastRunUtc = DateTime.UtcNow;
            _schedulerState.LastRunSuccess = false;
            _schedulerState.LastRunMessage = ex.Message;
            _logger.LogError(ex, "Scheduled backup threw an unhandled exception.");
        }
        finally
        {
            _schedulerState.IsRunningNow = false;
        }
    }

    public static DateTime? CalculateNextRunUtc(string? cronExpression, DateTime? fromUtc = null)
    {
        var cron = !string.IsNullOrWhiteSpace(cronExpression) ? cronExpression.Trim() : "0 2 * * *";
        var now = fromUtc ?? DateTime.UtcNow;

        try
        {
            var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var format = parts.Length == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
            var parsed = CronExpression.Parse(cron, format);
            return parsed.GetNextOccurrence(now, TimeZoneInfo.Utc);
        }
        catch
        {
            var fallback = CronExpression.Parse("0 2 * * *", CronFormat.Standard);
            return fallback.GetNextOccurrence(now, TimeZoneInfo.Utc);
        }
    }
}
