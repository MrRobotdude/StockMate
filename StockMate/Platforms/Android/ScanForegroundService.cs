using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using StockMate.Services;
using StockMate.Models;
using StockMate.Ui;
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
        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(
                    global::Android.Provider.Settings.ActionAppNotificationSettings)
                .PutExtra(
                    global::Android.Provider.Settings.ExtraAppPackage,
                    context.PackageName)
                .AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "StockMateNotifications", ex.ToString());
        }
    }

    public static Task<bool> StartAsync(bool intraday, bool forceRefresh, bool downloadOnly = false)
    {
        if (IsRunning) return _completion?.Task ?? WaitForCurrentRunAsync();
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(context, typeof(ScanForegroundService));
            intent.PutExtra("intraday", intraday);
            intent.PutExtra("force", forceRefresh);
            intent.PutExtra("downloadOnly", downloadOnly);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Exception ex)
        {
            Complete(false, Loc.T(
                $"Layanan background tidak dapat dimulai: {ex.Message}",
                $"The background service could not start: {ex.Message}"));
        }
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
        try
        {
            var context = global::Android.App.Application.Context;
            context.StartService(
                new Intent(context, typeof(ScanForegroundService))
                    .SetAction(ScanForegroundService.StopAction));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "StockMateScannerStop", ex.ToString());
        }
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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var handlers = ProgressChanged?.GetInvocationList();
                if (handlers is null) return;
                foreach (var handler in handlers)
                    try
                    {
                        ((Action<ScanProgress>)handler)(progress);
                    }
                    catch
                    {
                        // A page can disappear while a progress callback is
                        // queued. One stale listener must not crash the service.
                    }
            });
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
    long _lastNotificationTicks;
    int _lastNotificationCompleted = -1;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        try
        {
            if (intent?.Action == StopAction)
            {
                _cts?.Cancel();
                return StartCommandResult.NotSticky;
            }
            var intraday =
                intent?.GetBooleanExtra("intraday", false) ?? false;
            var force =
                intent?.GetBooleanExtra("force", false) ?? false;
            var scheduled =
                intent?.GetBooleanExtra("scheduled", false) ?? false;
            var downloadOnly =
                intent?.GetBooleanExtra("downloadOnly", false) ?? false;
            var eventOnly =
                intent?.GetBooleanExtra("eventOnly", false) ?? false;
            if (ScanServiceBridge.IsRunning)
            {
                // Android can deliver the 08:45 or midday alarm while the
                // morning universe download is still running. Do not silently
                // drop that scheduled work.
                if (scheduled)
                {
                    if (eventOnly)
                        BackgroundScanScheduler.ScheduleEventRetry(
                            this, TimeSpan.FromMinutes(10));
                    else
                        BackgroundScanScheduler.ScheduleRetry(
                            this, intraday, TimeSpan.FromMinutes(10));
                }
                return StartCommandResult.Sticky;
            }
            ScanServiceBridge.IsRunning = true;
            _cts = new();
            // A cold background launch happens before MAUI creates a page.
            // Restore the selected language before showing the mandatory
            // foreground notification.
            Loc.Use(Preferences.Default.Get("app.language", "id"));
            CreateChannel();
            AcquireWakeLock();
            StartForeground(NotificationId,
                BuildNotification(Loc.T(
                    "Menyiapkan scanner…",
                    "Preparing scanner…")));
            // A Service starts on Android's main looper. Run the complete
            // network pipeline on a worker. Do not pass the token to Task.Run:
            // a pre-cancelled token would skip RunAsync and its cleanup.
            _ = Task.Run(() => RunAsync(
                intraday, force, scheduled, downloadOnly,
                eventOnly, _cts.Token));
            return StartCommandResult.RedeliverIntent;
        }
        catch (Exception ex)
        {
            ScanServiceBridge.Complete(false, Loc.T(
                $"Scanner gagal dimulai: {ex.Message}",
                $"Scanner failed to start: {ex.Message}"));
            ReleaseWakeLock();
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }
    }

    async Task RunAsync(
        bool intraday, bool force, bool scheduled, bool downloadOnly,
        bool eventOnly, CancellationToken ct)
    {
        var finalTitle = Loc.T(
            "StockMate scanner selesai",
            "StockMate scanner completed");
        var finalMessage = Loc.T(
            "Proses selesai.",
            "Process completed.");
        try
        {
            var services = IPlatformApplication.Current?.Services
                ?? throw new InvalidOperationException(Loc.T(
                    "Service aplikasi tidak tersedia.",
                    "Application services are unavailable."));
            var engine = services.GetService<ScanEngine>()
                ?? throw new InvalidOperationException(Loc.T(
                    "ScanEngine tidak tersedia.",
                    "ScanEngine is unavailable."));
            var data = services.GetService<AppDataService>()
                ?? throw new InvalidOperationException(Loc.T(
                    "Penyimpanan aplikasi tidak tersedia.",
                    "Application storage is unavailable."));
            await data.LoadAsync();
            Loc.Use(data.State.LanguageCode);
            finalTitle = Loc.T(
                "StockMate scanner selesai",
                "StockMate scanner completed");
            finalMessage = Loc.T("Proses selesai.", "Process completed.");
            var eventIntel = services.GetService<EventIntelligenceService>();
            if (eventOnly)
            {
                if (!data.State.AutoEventIntelligence)
                {
                    finalMessage = Loc.T(
                        "Analisis isu otomatis nonaktif.",
                        "Automatic event analysis is disabled.");
                    ScanServiceBridge.Complete(true, finalMessage);
                    return;
                }
                UpdateNotification(new()
                {
                    Stage = "EVENTS",
                    Message = Loc.T(
                        "Memeriksa isu pasar, portofolio, dan kandidat teratas",
                        "Checking market events, portfolio, and top candidates")
                });
                var count = eventIntel is null ? 0 : await eventIntel.RefreshAsync(ct);
                var eventSnapshot = engine.GetLatestSnapshot();
                var recommendationMessage = Loc.T(
                    "Belum ada snapshot lengkap untuk memeriksa ulang rekomendasi.",
                    "There is no complete snapshot available to recheck recommendations.");
                if (eventSnapshot is { IsComplete: true })
                {
                    UpdateNotification(new()
                    {
                        Stage = "ANALYZE",
                        Message = Loc.T(
                            "Memeriksa ulang dan membatalkan rekomendasi yang sudah tidak valid",
                            "Rechecking and cancelling recommendations that are no longer valid")
                    });
                    var eventProgress =
                        new DirectProgress<ScanProgress>(UpdateNotification);
                    await engine.AnalyzeAsync(
                        eventSnapshot.Session == "LUNCH",
                        eventSnapshot,
                        true,
                        eventProgress,
                        ct);
                    var cancelled = data.State.ScanHistory
                        .LastOrDefault(x =>
                            x.SessionKey == eventSnapshot.SessionKey &&
                            x.StrategyVersion == data.State.Strategy.Version)?
                        .Predictions.Count(x => x.Outcome == "CANCELLED") ?? 0;
                    recommendationMessage = Loc.T(
                        $"Rekomendasi diperiksa ulang; {cancelled} order dibatalkan.",
                        $"Recommendations were rechecked; {cancelled} orders were cancelled.");
                }
                var eventDecisions = services.GetService<PortfolioDecisionService>();
                if (eventDecisions is not null) await eventDecisions.RebuildAsync();
                finalTitle = Loc.T(
                    "Analisis isu StockMate selesai",
                    "StockMate event analysis completed");
                finalMessage = Loc.T(
                    $"{count} berita relevan diperbarui. {recommendationMessage}",
                    $"{count} relevant news items were updated. {recommendationMessage}");
                ScanServiceBridge.Complete(true, finalMessage);
                return;
            }
            if (scheduled && !data.State.AutoScanAfterClose)
            {
                finalTitle = Loc.T(
                    "StockMate scan otomatis nonaktif",
                    "StockMate automatic scan is disabled");
                finalMessage = Loc.T(
                    "Jadwal dilewati karena scan closing otomatis dimatikan.",
                    "The schedule was skipped because automatic closing scans are disabled.");
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
                    Loc.T(
                        $"Data siap • {snapshot.Symbols.Count}/{snapshot.RequestedCount} saham. Buka Scanner untuk analisis.",
                        $"Data ready • {snapshot.Symbols.Count}/{snapshot.RequestedCount} stocks. Open Scanner to analyze.");
            }
            else
            {
                if (data.State.AutoEventIntelligence &&
                    eventIntel is not null)
                {
                    UpdateNotification(new()
                    {
                        Stage = "EVENTS",
                        Message = Loc.T(
                            "Memperbarui isu pasar sebelum analisis",
                            "Updating market events before analysis")
                    });
                    try
                    {
                        await eventIntel.RefreshAsync(ct);
                    }
                    catch (System.OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // News is a defensive veto layer. A feed outage must
                        // not destroy an otherwise valid technical snapshot.
                        global::Android.Util.Log.Warn(
                            "StockMateEvents", ex.ToString());
                    }
                }
                var result = await engine.RunAsync(
                    intraday, force, progress, ct, requireVerifiedClosing: scheduled);
                snapshot = result.Snapshot
                    ?? throw new InvalidOperationException(Loc.T(
                        "Scanner selesai tanpa snapshot.",
                        "The scanner completed without a snapshot."));
                var decisions = services.GetService<PortfolioDecisionService>();
                if (decisions is not null) await decisions.RebuildAsync();
                finalMessage =
                    Loc.T(
                        $"Scan selesai • {snapshot.Symbols.Count}/{snapshot.RequestedCount} saham",
                        $"Scan completed • {snapshot.Symbols.Count}/{snapshot.RequestedCount} stocks");
            }
            ScanServiceBridge.Complete(true, finalMessage);
        }
        catch (ClosingDataNotReadyException ex)
        {
            BackgroundScanScheduler.ScheduleRetry(this, intraday, TimeSpan.FromMinutes(10));
            finalTitle = Loc.T(
                "StockMate menunggu data closing",
                "StockMate is waiting for closing data");
            finalMessage = Loc.T(
                $"{ex.Message} Cek ulang otomatis 10 menit lagi.",
                $"{ex.Message} An automatic retry will run in 10 minutes.");
            ScanServiceBridge.Complete(true, finalMessage);
        }
        catch (System.OperationCanceledException)
        {
            finalTitle = Loc.T(
                "StockMate scanner dihentikan",
                "StockMate scanner stopped");
            finalMessage = Loc.T(
                "Scan dihentikan. Progres terakhir tetap tersimpan.",
                "The scan was stopped. The latest progress remains saved.");
            ScanServiceBridge.Complete(false, finalMessage);
        }
        catch (Exception ex)
        {
            finalTitle = Loc.T(
                "StockMate scanner gagal",
                "StockMate scanner failed");
            finalMessage = Loc.T(
                $"Scan gagal: {ex.Message}",
                $"Scan failed: {ex.Message}");
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
        // Building and publishing an Android notification for every request
        // produced thousands of Binder/layout operations during a full IDX
        // scan. Keep progress visible, but coalesce routine ticker events.
        var now = System.Environment.TickCount64;
        var important = progress.Stage is
            "PREPARING" or "UNIVERSE_READY" or "BATCH_START" or
            "BATCH_COMPLETE" or "REQUEST_ERROR" or "RETRY" or
            "RATE_LIMIT" or "FORBIDDEN_RETRY" or "MARKET_REGIME" or
            "EVENTS" or "SAVING" or "COMPLETE" or "ERROR";
        var advanced = progress.Completed >= _lastNotificationCompleted + 5;
        if (!important && !advanced && now - _lastNotificationTicks < 750)
            return;
        _lastNotificationTicks = now;
        _lastNotificationCompleted = progress.Completed;
        NotificationManagerCompat.From(this).Notify(NotificationId, BuildNotification(progress));
    }

    Notification BuildNotification(string text) =>
        BuildNotification(new ScanProgress { Message = text });

    Notification BuildNotification(ScanProgress progress)
    {
        var text = NotificationText(progress);
        var builder = new NotificationCompat.Builder(this, ProgressChannelId)
            .SetContentTitle(Loc.T(
                "StockMate sedang mengambil data",
                "StockMate is fetching data"))
            .SetContentText(text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(text))
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(BuildContentIntent())
            .AddAction(0, Loc.T("Hentikan", "Stop"), BuildStopIntent())
            .SetCategory(NotificationCompat.CategoryProgress)
            .SetPriority(NotificationCompat.PriorityDefault)
            .SetOnlyAlertOnce(true)
            .SetOngoing(true);
        builder.SetProgress(progress.Total, progress.Completed, progress.IsIndeterminate);
        return builder.Build() ?? throw new InvalidOperationException(Loc.T(
            "Notifikasi foreground gagal dibuat.",
            "The foreground notification could not be created."));
    }

    static string NotificationText(ScanProgress progress)
    {
        var message = Loc.English
            ? progress.Stage switch
            {
                "PREPARING" => "Preparing scanner",
                "UNIVERSE" or "UNIVERSE_REQUEST" or "UNIVERSE_PARSE" =>
                    "Updating IDX universe",
                "UNIVERSE_READY" => "IDX universe ready",
                "UNIVERSE_FALLBACK" => "Using the cached IDX universe",
                "TRADING_DATE" => "Resolving the reference trading date",
                "BATCH_PLAN" => "Preparing download batches",
                "BATCH_START" => "Starting a download batch",
                "BATCH_COMPLETE" => "Download batch completed",
                "DOWNLOAD" or "REQUEST_START" => "Fetching price data",
                "REQUEST_OK" => "Price data received",
                "REQUEST_ERROR" => "Price request failed; continuing",
                "RETRY" => "Retrying price request",
                "RATE_LIMIT" => "Waiting for data-source rate limit",
                "FORBIDDEN_RETRY" => "Access denied; switching endpoint",
                "WAITING_CLOSE" => "Verifying closing data",
                "CLOSING_FALLBACK" => "Continuing with available closing data",
                "MARKET_REGIME" => "Fetching the JCI market regime",
                "MARKET_REGIME_FALLBACK" =>
                    "JCI unavailable; using UNKNOWN regime",
                "EVENTS" => "Updating market events",
                "ANALYZE" => "Analyzing candidates",
                "SAVING" => "Saving results",
                "COMPLETE" => "Process completed",
                "ERROR" => "Process failed",
                _ => Loc.T(progress.Message)
            }
            : progress.Message;
        return progress.Total <= 0
            ? message
            : Loc.T(
                $"{message} • {progress.Completed}/{progress.Total} ({progress.Percent}%) • berhasil {progress.Succeeded} • gagal {progress.Failed}",
                $"{message} • {progress.Completed}/{progress.Total} ({progress.Percent}%) • {progress.Succeeded} succeeded • {progress.Failed} failed");
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
            ProgressChannelId,
            Loc.T("Progres pengambilan data", "Data-fetch progress"),
            NotificationImportance.Default)
        {
            Description = Loc.T(
                "Progres aktif saat StockMate mengambil dan menganalisis data pasar",
                "Active progress while StockMate fetches and analyzes market data")
        };
        progress.EnableVibration(true);
        var result = new NotificationChannel(
            ResultChannelId,
            Loc.T("Hasil proses StockMate", "StockMate process results"),
            NotificationImportance.High)
        {
            Description = Loc.T(
                "Pemberitahuan saat pengambilan data selesai atau gagal",
                "Alerts when data fetching completes or fails")
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
