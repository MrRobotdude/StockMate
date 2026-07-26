using Android.App;
using Android.Content.PM;
using Android.OS;
using Android;
using StockMate.Platforms.Android;

namespace StockMate
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                    CheckSelfPermission(
                        Manifest.Permission.PostNotifications) !=
                    Permission.Granted)
                    RequestPermissions(
                        [Manifest.Permission.PostNotifications], 1401);
                BackgroundScanScheduler.ScheduleDaily(this);
            }
            catch (Exception ex)
            {
                // Activity callbacks must never leak a managed exception into
                // Android's Java callback boundary.
                global::Android.Util.Log.Error(
                    "StockMateMainActivity", ex.ToString());
            }
        }
    }
}
