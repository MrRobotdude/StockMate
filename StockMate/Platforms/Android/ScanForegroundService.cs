using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using StockMate.Services;
using StockMate.Models;
using System.Runtime.Versioning;

namespace StockMate.Platforms.Android;

public static class ScanServiceBridge
{
    static TaskCompletionSource<bool>? _completion;
    public static event Action<ScanProgress>? ProgressChanged;
    public static bool IsRunning { get; internal set; }
    public static ScanProgress CurrentProgress { get; private set; } = new();
    static long _lastUiReportTicks;

    public static Task StartAsync(bool intraday, bool forceRefresh)
    {
        if (IsRunning) return _completion?.Task ?? Task.CompletedTask;
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(ScanForegroundService));
        intent.PutExtra("intraday", intraday);
        intent.PutExtra("force", forceRefresh);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) context.StartForegroundService(intent);
        else context.StartService(intent);
        return _completion.Task;
    }

    public static void Stop()
    {
        if (!IsRunning) return;
        var context = global::Android.App.Application.Context;
        context.StartService(new Intent(context, typeof(ScanForegroundService))
            .SetAction(ScanForegroundService.StopAction));
    }

    internal static void Report(ScanProgress progress)
    {
        if (progress.Total <= 0 && CurrentProgress.Total > 0)
        {
            progress.Total = CurrentProgress.Total;
            progress.Completed = CurrentProgress.Completed;
            progress.Succeeded = CurrentProgress.Succeeded;
            progress.Failed = CurrentProgress.Failed;
            progress.CurrentBatch = CurrentProgress.CurrentBatch;
            progress.TotalBatches = CurrentProgress.TotalBatches;
            progress.BatchCompleted = CurrentProgress.BatchCompleted;
            progress.BatchSize = CurrentProgress.BatchSize;
            progress.LastCompletedBatch = CurrentProgress.LastCompletedBatch;
            if (string.IsNullOrWhiteSpace(progress.Source))
                progress.Source = CurrentProgress.Source;
            if (string.IsNullOrWhiteSpace(progress.TechnicalDetail))
                progress.TechnicalDetail = CurrentProgress.TechnicalDetail;
        }
        CurrentProgress = progress;
        var nowTicks = System.Environment.TickCount64;
        var important = progress.Stage is "BATCH_START" or "BATCH_COMPLETE" or
            "REQUEST_ERROR" or "RATE_LIMIT" or "FORBIDDEN_RETRY" or
            "COMPLETE" or "ERROR";
        if (important || nowTicks - _lastUiReportTicks >= 150)
        {
            _lastUiReportTicks = nowTicks;
            MainThread.BeginInvokeOnMainThread(() => ProgressChanged?.Invoke(progress));
        }
    }

    internal static void Complete(bool ok, string message)
    {
        IsRunning = false;
        Report(new()
        {
            Stage = ok ? "COMPLETE" : "ERROR",
            Message = message,
            Completed = CurrentProgress.Completed,
            Total = CurrentProgress.Total,
            Succeeded = CurrentProgress.Succeeded,
            Failed = CurrentProgress.Failed,
            CurrentBatch = CurrentProgress.CurrentBatch,
            TotalBatches = CurrentProgress.TotalBatches,
            BatchCompleted = CurrentProgress.BatchCompleted,
            BatchSize = CurrentProgress.BatchSize,
            LastCompletedBatch = CurrentProgress.LastCompletedBatch,
            Source = CurrentProgress.Source,
            TechnicalDetail = CurrentProgress.TechnicalDetail,
            ElapsedMilliseconds = CurrentProgress.ElapsedMilliseconds
        });
        _completion?.TrySetResult(ok);
    }
}

[SupportedOSPlatform("android")]
[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class ScanForegroundService : Service
{
    internal const string StopAction = "stockmate.action.STOP_SCAN";
    const string ChannelId = "stockmate_scanner";
    const int NotificationId = 1401;
    CancellationTokenSource? _cts;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction)
        {
            _cts?.Cancel();
            return StartCommandResult.NotSticky;
        }
        if (ScanServiceBridge.IsRunning) return StartCommandResult.NotSticky;
        ScanServiceBridge.IsRunning = true;
        _cts = new();
        CreateChannel();
        StartForeground(NotificationId, BuildNotification("Menyiapkan scanner…"));
        var intraday = intent?.GetBooleanExtra("intraday", false) ?? false;
        var force = intent?.GetBooleanExtra("force", false) ?? false;
        var scheduled = intent?.GetBooleanExtra("scheduled", false) ?? false;
        // A Service starts on Android's main looper. Run the complete network
        // pipeline on a worker so no handler can perform network I/O on it.
        _ = Task.Run(() => RunAsync(intraday, force, scheduled, _cts.Token), _cts.Token);
        return StartCommandResult.NotSticky;
    }

    async Task RunAsync(bool intraday, bool force, bool scheduled, CancellationToken ct)
    {
        try
        {
            var services = IPlatformApplication.Current?.Services
                ?? throw new InvalidOperationException("Service aplikasi tidak tersedia.");
            var engine = services.GetService<ScanEngine>()
                ?? throw new InvalidOperationException("ScanEngine tidak tersedia.");
            var data = services.GetService<AppDataService>()
                ?? throw new InvalidOperationException("Penyimpanan aplikasi tidak tersedia.");
            await data.LoadAsync();
            if (scheduled && !data.State.AutoScanAfterClose)
            {
                ScanServiceBridge.Complete(true, "Scan otomatis nonaktif.");
                return;
            }
            var progress = new DirectProgress<ScanProgress>(UpdateNotification);
            var result = await engine.RunAsync(
                intraday, force, progress, ct, requireVerifiedClosing: scheduled);
            var snapshot = result.Snapshot
                ?? throw new InvalidOperationException("Scanner selesai tanpa snapshot.");
            var decisions = services.GetService<PortfolioDecisionService>();
            if (decisions is not null) await decisions.RebuildAsync();
            ScanServiceBridge.Complete(true,
                $"Scan selesai • {snapshot.Symbols.Count}/{snapshot.RequestedCount} saham");
        }
        catch (ClosingDataNotReadyException ex)
        {
            BackgroundScanScheduler.ScheduleRetry(this, intraday, TimeSpan.FromMinutes(10));
            ScanServiceBridge.Complete(true, $"{ex.Message} Cek ulang otomatis 10 menit lagi.");
        }
        catch (System.OperationCanceledException)
        {
            ScanServiceBridge.Complete(false, "Scan dihentikan. Progres tersimpan dan dapat dilanjutkan.");
        }
        catch (Exception ex)
        {
            ScanServiceBridge.Complete(false, $"Scan gagal: {ex.Message}");
        }
        finally
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                StopForeground(StopForegroundFlags.Remove);
            else
#pragma warning disable CS0618
                StopForeground(true);
#pragma warning restore CS0618
            StopSelf();
        }
    }

    sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    void UpdateNotification(ScanProgress progress)
    {
        ScanServiceBridge.Report(progress);
        NotificationManagerCompat.From(this).Notify(NotificationId, BuildNotification(progress));
    }

    Notification BuildNotification(string text) =>
        BuildNotification(new ScanProgress { Message = text });

    Notification BuildNotification(ScanProgress progress)
    {
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("StockMate sedang mengambil data")
            .SetContentText(progress.DisplayText)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetOnlyAlertOnce(true).SetOngoing(true);
        builder.SetProgress(progress.Total, progress.Completed, progress.IsIndeterminate);
        return builder.Build() ?? throw new InvalidOperationException("Notifikasi foreground gagal dibuat.");
    }

    void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(
            ChannelId, "StockMate Scanner", NotificationImportance.Low));
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnDestroy();
    }
}
