using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using StockMate.Services;
using StockMate.Models;
using System.Runtime.Versioning;
using System.Text.Json;

namespace StockMate.Platforms.Android;

public static class ScanServiceBridge
{
    static TaskCompletionSource<bool>? _completion;
    public static event Action<ScanProgress>? ProgressChanged;
    public static bool IsRunning { get; internal set; }
    const string ProgressPreferenceKey = "scanner.last_progress";
    public static ScanProgress CurrentProgress { get; private set; } = RestoreProgress();
    static long _lastUiReportTicks;
    static long _lastPersistTicks;

    public static bool NotificationsEnabled =>
        NotificationManagerCompat.From(global::Android.App.Application.Context)
            .AreNotificationsEnabled();

    public static void OpenNotificationSettings()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(global::Android.Provider.Settings.ActionAppNotificationSettings)
            .PutExtra(global::Android.Provider.Settings.ExtraAppPackage, context.PackageName)
            .AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    public static Task<bool> StartAsync(bool intraday, bool forceRefresh, bool downloadOnly = false)
    {
        if (IsRunning) return _completion?.Task ?? WaitForCurrentRunAsync();
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(ScanForegroundService));
        intent.PutExtra("intraday", intraday);
        intent.PutExtra("force", forceRefresh);
        intent.PutExtra("downloadOnly", downloadOnly);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) context.StartForegroundService(intent);
        else context.StartService(intent);
        return _completion.Task;
    }

    static async Task<bool> WaitForCurrentRunAsync()
    {
        while (IsRunning)
            await Task.Delay(500);
        return CurrentProgress.Stage == "COMPLETE";
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
        if (important || nowTicks - _lastPersistTicks >= 1000)
        {
            _lastPersistTicks = nowTicks;
            PersistProgress(progress);
        }
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

    static ScanProgress RestoreProgress()
    {
        try
        {
            var json = Preferences.Default.Get(ProgressPreferenceKey, "");
            return string.IsNullOrWhiteSpace(json)
                ? new ScanProgress()
                : JsonSerializer.Deserialize<ScanProgress>(json) ?? new ScanProgress();
        }
        catch { return new ScanProgress(); }
    }

    static void PersistProgress(ScanProgress progress)
    {
        try
        {
            Preferences.Default.Set(ProgressPreferenceKey,
                JsonSerializer.Serialize(progress));
        }
        catch { }
    }
}

[SupportedOSPlatform("android")]
[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class ScanForegroundService : Service
{
    internal const string StopAction = "stockmate.action.STOP_SCAN";
    const string ProgressChannelId = "stockmate_scanner_progress_v2";
    const string ResultChannelId = "stockmate_scanner_result_v2";
    const int NotificationId = 1401;
    const int ResultNotificationId = 1402;
    CancellationTokenSource? _cts;
    PowerManager.WakeLock? _wakeLock;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction)
        {
            _cts?.Cancel();
            return StartCommandResult.NotSticky;
        }
        if (ScanServiceBridge.IsRunning) return StartCommandResult.Sticky;
        ScanServiceBridge.IsRunning = true;
        _cts = new();
        CreateChannel();
        AcquireWakeLock();
        StartForeground(NotificationId, BuildNotification("Menyiapkan scanner…"));
        var intraday = intent?.GetBooleanExtra("intraday", false) ?? false;
        var force = intent?.GetBooleanExtra("force", false) ?? false;
        var scheduled = intent?.GetBooleanExtra("scheduled", false) ?? false;
        var downloadOnly = intent?.GetBooleanExtra("downloadOnly", false) ?? false;
        var eventOnly = intent?.GetBooleanExtra("eventOnly", false) ?? false;
        // A Service starts on Android's main looper. Run the complete network
        // pipeline on a worker so no handler can perform network I/O on it.
        _ = Task.Run(() => RunAsync(intraday, force, scheduled, downloadOnly, eventOnly, _cts.Token), _cts.Token);
        // If Android kills the process under memory pressure, redeliver the
        // original intent. The download pipeline is idempotent and reuses its
        // cache/checkpoint, so a restarted run continues safely.
        return StartCommandResult.RedeliverIntent;
    }

    async Task RunAsync(
        bool intraday, bool force, bool scheduled, bool downloadOnly,
        bool eventOnly, CancellationToken ct)
    {
        var finalTitle = "StockMate scanner selesai";
        var finalMessage = "Proses selesai.";
        try
        {
            var services = IPlatformApplication.Current?.Services
                ?? throw new InvalidOperationException("Service aplikasi tidak tersedia.");
            var engine = services.GetService<ScanEngine>()
                ?? throw new InvalidOperationException("ScanEngine tidak tersedia.");
            var data = services.GetService<AppDataService>()
                ?? throw new InvalidOperationException("Penyimpanan aplikasi tidak tersedia.");
            await data.LoadAsync();
            var eventIntel = services.GetService<EventIntelligenceService>();
            if (eventOnly)
            {
                if (!data.State.AutoEventIntelligence)
                {
                    finalMessage = "Analisis isu otomatis nonaktif.";
                    ScanServiceBridge.Complete(true, finalMessage);
                    return;
                }
                UpdateNotification(new()
                {
                    Stage = "EVENTS",
                    Message = "Memeriksa isu pasar, portofolio, dan kandidat teratas"
                });
                var count = eventIntel is null ? 0 : await eventIntel.RefreshAsync(ct);
                var eventDecisions = services.GetService<PortfolioDecisionService>();
                if (eventDecisions is not null) await eventDecisions.RebuildAsync();
                finalTitle = "Analisis isu StockMate selesai";
                finalMessage = $"{count} berita relevan diperbarui. Buka detail saham untuk dampaknya.";
                ScanServiceBridge.Complete(true, finalMessage);
                return;
            }
            if (scheduled && !data.State.AutoScanAfterClose)
            {
                finalTitle = "StockMate scan otomatis nonaktif";
                finalMessage = "Jadwal dilewati karena scan closing otomatis dimatikan.";
                ScanServiceBridge.Complete(true, finalMessage);
                return;
            }
            var progress = new DirectProgress<ScanProgress>(UpdateNotification);
            MarketSnapshot snapshot;
            if (downloadOnly)
            {
                snapshot = await engine.RefreshMarketDataAsync(
                    intraday, force, progress, ct, requireVerifiedClosing: scheduled);
                finalMessage =
                    $"Data siap • {snapshot.Symbols.Count}/{snapshot.RequestedCount} saham. Buka Scanner untuk analisis.";
            }
            else
            {
                var result = await engine.RunAsync(
                    intraday, force, progress, ct, requireVerifiedClosing: scheduled);
                snapshot = result.Snapshot
                    ?? throw new InvalidOperationException("Scanner selesai tanpa snapshot.");
                var decisions = services.GetService<PortfolioDecisionService>();
                if (decisions is not null) await decisions.RebuildAsync();
                if (data.State.AutoEventIntelligence && eventIntel is not null)
                {
                    UpdateNotification(new()
                    {
                        Stage = "EVENTS",
                        Message = "Memperbarui isu setelah closing"
                    });
                    await eventIntel.RefreshAsync(ct);
                    if (decisions is not null) await decisions.RebuildAsync();
                }
                finalMessage =
                    $"Scan selesai • {snapshot.Symbols.Count}/{snapshot.RequestedCount} saham";
            }
            ScanServiceBridge.Complete(true, finalMessage);
        }
        catch (ClosingDataNotReadyException ex)
        {
            BackgroundScanScheduler.ScheduleRetry(this, intraday, TimeSpan.FromMinutes(10));
            finalTitle = "StockMate menunggu data closing";
            finalMessage = $"{ex.Message} Cek ulang otomatis 10 menit lagi.";
            ScanServiceBridge.Complete(true, finalMessage);
        }
        catch (System.OperationCanceledException)
        {
            finalTitle = "StockMate scanner dihentikan";
            finalMessage = "Scan dihentikan. Progres terakhir tetap tersimpan.";
            ScanServiceBridge.Complete(false, finalMessage);
        }
        catch (Exception ex)
        {
            finalTitle = "StockMate scanner gagal";
            finalMessage = $"Scan gagal: {ex.Message}";
            ScanServiceBridge.Complete(false, finalMessage);
        }
        finally
        {
            PublishFinalNotification(finalTitle, finalMessage);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
                StopForeground(StopForegroundFlags.Detach);
            else
#pragma warning disable CS0618
                StopForeground(false);
#pragma warning restore CS0618
            ReleaseWakeLock();
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
        var builder = new NotificationCompat.Builder(this, ProgressChannelId)
            .SetContentTitle("StockMate sedang mengambil data")
            .SetContentText(progress.DisplayText)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(progress.DisplayText))
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(BuildContentIntent())
            .AddAction(0, "Hentikan", BuildStopIntent())
            .SetCategory(NotificationCompat.CategoryProgress)
            .SetPriority(NotificationCompat.PriorityDefault)
            .SetOnlyAlertOnce(true)
            .SetOngoing(true);
        builder.SetProgress(progress.Total, progress.Completed, progress.IsIndeterminate);
        return builder.Build() ?? throw new InvalidOperationException("Notifikasi foreground gagal dibuat.");
    }

    void PublishFinalNotification(string title, string text)
    {
        NotificationManagerCompat.From(this).Cancel(NotificationId);
        var notification = new NotificationCompat.Builder(this, ResultChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(text))
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(BuildContentIntent())
            .SetCategory(NotificationCompat.CategoryStatus)
            .SetPriority(NotificationCompat.PriorityHigh)
            .SetDefaults((int)(NotificationDefaults.Sound | NotificationDefaults.Vibrate))
            .SetOngoing(false)
            .SetAutoCancel(true)
            .Build();
        if (notification is not null)
            NotificationManagerCompat.From(this).Notify(ResultNotificationId, notification);
    }

    PendingIntent BuildContentIntent()
    {
        var intent = new Intent(this, typeof(MainActivity))
            .AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(this, 1401, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    PendingIntent BuildStopIntent()
    {
        var intent = new Intent(this, typeof(ScanForegroundService))
            .SetAction(StopAction);
        return PendingIntent.GetService(this, 1403, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        var progress = new NotificationChannel(
            ProgressChannelId, "Progres pengambilan data", NotificationImportance.Default)
        {
            Description = "Progres aktif saat StockMate mengambil dan menganalisis data pasar"
        };
        progress.EnableVibration(true);
        var result = new NotificationChannel(
            ResultChannelId, "Hasil proses StockMate", NotificationImportance.High)
        {
            Description = "Pemberitahuan saat pengambilan data selesai atau gagal"
        };
        result.EnableVibration(true);
        manager.CreateNotificationChannel(progress);
        manager.CreateNotificationChannel(result);
    }

    void AcquireWakeLock()
    {
        try
        {
            var manager = (PowerManager?)GetSystemService(PowerService);
            _wakeLock = manager?.NewWakeLock(
                WakeLockFlags.Partial, $"{PackageName}:StockMateDataSync");
            _wakeLock?.SetReferenceCounted(false);
            _wakeLock?.Acquire(2 * 60 * 60 * 1000L);
        }
        catch
        {
            _wakeLock = null;
        }
    }

    void ReleaseWakeLock()
    {
        try
        {
            if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        }
        catch { }
        finally
        {
            _wakeLock?.Dispose();
            _wakeLock = null;
        }
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        ReleaseWakeLock();
        base.OnDestroy();
    }
}
