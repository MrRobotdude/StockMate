using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using System.Runtime.Versioning;

namespace StockMate.Platforms.Android;

[SupportedOSPlatform("android")]
public static class BackgroundScanScheduler
{
    const int MorningRequest = 700;
    const int LunchRequest = 1215;
    const int EveningRequest = 1630;
    const int OpeningEventsRequest = 845;
    const int OpeningEventsRetryRequest = 846;
    const int RetryRequest = 1712;

    public static void ScheduleDaily(Context context)
    {
        // Morning pull uses yesterday's completed candle so recommendations
        // can be ready before IDX pre-opening.
        TrySchedule(() => ScheduleSession(
            context, MorningRequest, 7, 0, false));
        TrySchedule(() => ScheduleSession(
            context, LunchRequest, 12, 15, true));
        TrySchedule(() => ScheduleSession(
            context, EveningRequest, 16, 30, false));
        TrySchedule(() => ScheduleSession(
            context, OpeningEventsRequest, 8, 45, false, eventOnly: true));
    }

    public static bool HasExactAlarmAccess(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return true;
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        return alarm?.CanScheduleExactAlarms() == true;
    }

    public static void OpenExactAlarmSettings(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return;
        try
        {
            var intent = new Intent(Settings.ActionRequestScheduleExactAlarm)
                .SetData(global::Android.Net.Uri.Parse($"package:{context.PackageName}"))
                .AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "StockMateAlarmSettings", ex.ToString());
            TryOpenGeneralSettings(context);
        }
    }

    public static void OpenBatteryOptimizationSettings(Context context)
    {
        try
        {
            context.StartActivity(new Intent(Settings.ActionIgnoreBatteryOptimizationSettings)
                .AddFlags(ActivityFlags.NewTask));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "StockMateBatterySettings", ex.ToString());
            TryOpenGeneralSettings(context);
        }
    }

    static void TryOpenGeneralSettings(Context context)
    {
        try
        {
            context.StartActivity(new Intent(Settings.ActionSettings)
                .AddFlags(ActivityFlags.NewTask));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "StockMateGeneralSettings", ex.ToString());
        }
    }

    public static void CancelDaily(Context context)
    {
        Cancel(context, MorningRequest);
        Cancel(context, LunchRequest);
        Cancel(context, EveningRequest);
        Cancel(context, RetryRequest);
        Cancel(context, OpeningEventsRequest);
        Cancel(context, OpeningEventsRetryRequest);
    }

    public static void ScheduleRetry(Context context, bool intraday, TimeSpan delay)
    {
        TrySchedule(() =>
        {
            var alarm = (AlarmManager?)context.GetSystemService(
                Context.AlarmService);
            if (alarm is null) return;
            var trigger = Java.Lang.JavaSystem.CurrentTimeMillis() +
                          (long)delay.TotalMilliseconds;
            var pending = CreatePendingIntent(
                context, RetryRequest, intraday, true);
            SetAlarm(alarm, trigger, pending);
        });
    }

    public static void ScheduleEventRetry(Context context, TimeSpan delay)
    {
        TrySchedule(() =>
        {
            var alarm = (AlarmManager?)context.GetSystemService(
                Context.AlarmService);
            if (alarm is null) return;
            var trigger = Java.Lang.JavaSystem.CurrentTimeMillis() +
                          (long)delay.TotalMilliseconds;
            var pending = CreatePendingIntent(
                context, OpeningEventsRetryRequest, false, true,
                eventOnly: true);
            SetAlarm(alarm, trigger, pending);
        });
    }

    static void ScheduleSession(
        Context context, int requestCode, int hour, int minute, bool intraday,
        bool eventOnly = false, bool downloadOnly = false)
    {
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarm is null) return;
        var next = DateTime.Today.AddHours(hour).AddMinutes(minute);
        if (next <= DateTime.Now) next = next.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            next = next.AddDays(1);
        var trigger = new DateTimeOffset(next).ToUnixTimeMilliseconds();
        var pending = CreatePendingIntent(
            context, requestCode, intraday, false, eventOnly, downloadOnly);
        SetAlarm(alarm, trigger, pending);
    }

    static PendingIntent CreatePendingIntent(
        Context context, int requestCode, bool intraday, bool retry,
        bool eventOnly = false, bool downloadOnly = false)
    {
        var intent = new Intent(context, typeof(BackgroundScanAlarmReceiver))
            .PutExtra("intraday", intraday)
            .PutExtra("retry", retry)
            .PutExtra("eventOnly", eventOnly)
            .PutExtra("downloadOnly", downloadOnly);
        return PendingIntent.GetBroadcast(context, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    static void Cancel(Context context, int requestCode)
    {
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarm is null) return;
        var intent = new Intent(context, typeof(BackgroundScanAlarmReceiver));
        var pending = PendingIntent.GetBroadcast(context, requestCode, intent,
            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pending is not null) alarm.Cancel(pending);
    }

    static void SetAlarm(AlarmManager alarm, long trigger, PendingIntent pending)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S && alarm.CanScheduleExactAlarms())
            alarm.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, trigger, pending);
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, trigger, pending);
        else
            alarm.Set(AlarmType.RtcWakeup, trigger, pending);
    }

    static void TrySchedule(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "StockMateScheduler", ex.ToString());
        }
    }
}

[SupportedOSPlatform("android")]
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class BackgroundScanAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        try
        {
            var intraday =
                intent?.GetBooleanExtra("intraday", false) ?? false;
            var eventOnly =
                intent?.GetBooleanExtra("eventOnly", false) ?? false;
            var downloadOnly =
                intent?.GetBooleanExtra("downloadOnly", false) ?? false;
            BackgroundScanScheduler.ScheduleDaily(context);
            var service = new Intent(
                    context, typeof(ScanForegroundService))
                .PutExtra("intraday", intraday)
                .PutExtra("force", false)
                .PutExtra("scheduled", true)
                .PutExtra("eventOnly", eventOnly)
                .PutExtra("downloadOnly", downloadOnly);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(service);
            else
                context.StartService(service);
        }
        catch (Exception ex)
        {
            // Android may reject a background foreground-service start on
            // vendor-specific battery policies. Log it instead of allowing the
            // BroadcastReceiver callback to escape as JavaProxyThrowable.
            global::Android.Util.Log.Error(
                "StockMateAlarmReceiver", ex.ToString());
        }
    }
}

[SupportedOSPlatform("android")]
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced
])]
public sealed class BackgroundScanBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null) return;
        try
        {
            BackgroundScanScheduler.ScheduleDaily(context);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error(
                "StockMateBootReceiver", ex.ToString());
        }
    }
}
