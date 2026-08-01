using StockMate.Services;
using StockMate.Ui;
using StockMate.Models;
using System.Text.Json;
using StockMate.Platforms.Android;
using Microsoft.Extensions.DependencyInjection;

namespace StockMate.Pages;

public sealed class SettingsPage : ContentPage
{
    readonly AppDataService _data;
    readonly UniverseService _universe;
    readonly EvaluationExportService _evaluationExport;
    readonly Entry _cash=new(){Keyboard=Keyboard.Numeric}, _officialRealized=new(){Keyboard=Keyboard.Numeric}, _risk=new(){Keyboard=Keyboard.Numeric}, _monthly=new(){Keyboard=Keyboard.Numeric}, _buyFee=new(){Keyboard=Keyboard.Numeric}, _sellFee=new(){Keyboard=Keyboard.Numeric}, _delay=new(){Keyboard=Keyboard.Numeric};
    readonly Switch _speculative=new(), _autoScan=new(), _autoEvents=new();
    readonly Picker _language = new() { Title = "Bahasa / Language", ItemsSource = new[] { "Bahasa Indonesia", "English" } };
    readonly Label _universeInfo=UiKit.Sub("");
    bool _saving;
    public SettingsPage(AppDataService data, UniverseService universe)
    {
        _data=data; _universe=universe;
        _evaluationExport = App.Services.GetRequiredService<EvaluationExportService>();
        Title=Loc.T("Pengaturan", "Settings"); BackgroundColor=UiKit.Navy;
        var root=UiKit.PageStack(); root.Children.Add(UiKit.Heading(this, "Pengaturan", "Settings",
            "Pengaturan ini memengaruhi position sizing, estimasi fee, scanner, dan perhitungan portofolio. Perubahan bahasa diterapkan setelah menekan Simpan.",
            "These settings affect position sizing, fee estimates, scanner behavior, and portfolio calculations. Language changes apply after tapping Save."));
        root.Children.Add(Field("Bahasa / Language", _language));
        root.Children.Add(UiKit.SectionHeading(this, "Portofolio & risiko", "Portfolio & risk",
            "Isi angka resmi dari broker dan batas risiko pribadi. Bagian ini memengaruhi rekomendasi dan sizing.",
            "Enter official broker values and personal risk limits. This section affects recommendations and sizing."));
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Angka resmi Stockbit", "Official Stockbit figures"),
            Loc.T("Kas dan realized P/L", "Cash and realized P/L"),
            new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    Field("Kas tersedia", _cash),
                    Field("Realized P/L resmi Stockbit", _officialRealized)
                }
            }));
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Batas risiko", "Risk limits"),
            Loc.T("Per transaksi dan per bulan", "Per trade and monthly"),
            new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    Field("Risiko normal per transaksi", _risk),
                    Field("Batas rugi bulanan", _monthly)
                }
            }));
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Biaya transaksi", "Transaction fees"),
            Loc.T("Fee beli dan jual", "Buy and sell fees"),
            new VerticalStackLayout
            {
                Spacing = 10,
                Children = { Field("Fee beli (%)", _buyFee), Field("Fee jual (%)", _sellFee) }
            }));
        var row=new Grid{ColumnDefinitions=[new(GridLength.Star),new(GridLength.Auto)]}; row.Add(new VerticalStackLayout{Children={new Label{Text=Loc.T("Sertakan saham spekulatif"),TextColor=Colors.White},UiKit.Sub("Maksimal alokasi scanner Rp500 ribu")}},0); row.Add(_speculative,1); root.Children.Add(UiKit.Box(row));
        var autoRow = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        autoRow.Add(new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = Loc.T("Scan closing otomatis"), TextColor = Colors.White },
                UiKit.Sub("Tarik & analisis 07.00 • cek 12.15 & 16.30 • retry bila closing belum siap")
            }
        }, 0);
        autoRow.Add(_autoScan, 1);
        var eventRow = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        eventRow.Add(new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = Loc.T("Analisis isu opening & closing"), TextColor = Colors.White },
                UiKit.Sub("Gratis • sebelum analisis & sekitar 08.45 • portofolio + kandidat teratas")
            }
        }, 0);
        eventRow.Add(_autoEvents, 1);
        var exactAlarm = UiKit.Secondary("Izinkan jadwal presisi Android");
        exactAlarm.Clicked += (_, _) =>
            BackgroundScanScheduler.OpenExactAlarmSettings(Android.App.Application.Context);
        var batterySettings = UiKit.Secondary("Atur penggunaan baterai background");
        batterySettings.Clicked += (_, _) =>
            BackgroundScanScheduler.OpenBatteryOptimizationSettings(
                Android.App.Application.Context);
        var notificationSettings = UiKit.Secondary("Periksa notifikasi progres");
        notificationSettings.Clicked += (_, _) =>
            ScanServiceBridge.OpenNotificationSettings();
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Background scanner", "Background scanner"),
            Loc.T("Tetap berjalan saat aplikasi ditutup", "Continues when the app is closed"),
            new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    autoRow,
                    eventRow,
                    exactAlarm,
                    batterySettings,
                    notificationSettings,
                    UiKit.Sub(Loc.T(
                        "Android akan menarik data mulai sekitar 07.00, lalu mengecek lagi 12.15 dan 16.30 pada hari bursa. Progres tetap tampil di notification bar dan hasil akhir berbunyi. Matikan optimasi baterai untuk reliabilitas terbaik. Force Stop tetap menghentikan seluruh pekerjaan sampai aplikasi dibuka kembali.",
                        "Android starts the data pull around 07:00, then checks again at 12:15 and 16:30 on trading days. Progress stays visible in the notification bar and completion alerts with sound. Disable battery optimization for best reliability. Force Stop still blocks all work until the app is opened again."))
                }
            }));
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Pengaturan teknis scanner", "Scanner technical settings"),
            Loc.T("Jeda request dan stabilitas koneksi", "Request delay and connection stability"),
            Field("Jeda request data (milidetik)", _delay)));
        var save=UiKit.Primary("Simpan pengaturan"); save.Clicked += async(_,_)=>await SaveAsync(); root.Children.Add(save);
        var strategyInfo=UiKit.Sub("");
        var import=UiKit.Secondary(Loc.T("Impor strategi", "Import strategy"));
        var export=UiKit.Secondary(Loc.T("Bagikan strategi", "Share strategy"));
        import.Clicked += async(_,_)=>{await ImportStrategyAsync(); strategyInfo.Text=StrategyText();};
        export.Clicked += async(_,_)=>await ExportStrategyAsync();
        var strategyButtons=new Grid{ColumnDefinitions=[new(GridLength.Star),new(GridLength.Star)],ColumnSpacing=10}; strategyButtons.Add(import,0); strategyButtons.Add(export,1);
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Strategi analisis", "Analysis strategy"),
            Loc.T("Impor atau bagikan parameter hasil trainer", "Import or share trained parameters"),
            new VerticalStackLayout { Spacing=10, Children={strategyInfo,strategyButtons} }));
        var updateUniverse=UiKit.Primary(Loc.T("Perbarui master IDX", "Update IDX master"));
        var exportEvaluation=UiKit.Secondary(Loc.T(
            "Ekspor evaluasi CSV", "Export evaluation CSV"));
        updateUniverse.Clicked += async(_,_)=>await UpdateUniverseAsync();
        exportEvaluation.Clicked += async(_,_)=>await ExportEvaluationAsync();
        var universeButtons=new Grid{ColumnDefinitions=[new(GridLength.Star),new(GridLength.Star)],ColumnSpacing=10}; universeButtons.Add(updateUniverse,0); universeButtons.Add(exportEvaluation,1);
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Data & evaluasi", "Data & evaluation"),
            Loc.T("Universe online otomatis dan data audit", "Automatic online universe and audit data"),
            new VerticalStackLayout
            {
                Spacing=10,
                Children=
                {
                    _universeInfo,
                    UiKit.Sub(Loc.T(
                        "Daftar saham diperiksa otomatis setiap hari dari situs IDX. File universe manual tidak diperlukan.",
                        "The stock list is checked automatically each day from the IDX website. A manual universe file is not required.")),
                    universeButtons
                }
            }));
        var resetScan = UiKit.Secondary("Reset hasil & checkpoint scanner");
        var resetHistory = UiKit.Secondary("Reset transaction history");
        var resetAll = UiKit.Secondary("Reset seluruh aplikasi");
        resetHistory.TextColor = UiKit.Red;
        resetAll.BackgroundColor = UiKit.Red;
        resetScan.Clicked += async (_, _) => await ResetScanAsync();
        resetHistory.Clicked += async (_, _) => await ResetHistoryAsync();
        resetAll.Clicked += async (_, _) => await ResetAllAsync();
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Reset data", "Reset data"),
            Loc.T("Tindakan berisiko—buka hanya bila diperlukan", "Destructive actions—open only when needed"),
            new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                UiKit.Sub("Pilih hanya data yang memang ingin dihapus. Tindakan dikonfirmasi sebelum dijalankan."),
                resetScan, resetHistory, resetAll
            }
        }, Loc.T("HATI-HATI", "CAUTION"), UiKit.Red));
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Tentang StockMate", "About StockMate"),
            $"v{AppInfo.Current.VersionString} · build {AppInfo.Current.BuildString}",
            new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                UiKit.Sub(Loc.T(
                    $"Versi {AppInfo.Current.VersionString} • build {AppInfo.Current.BuildString}",
                    $"Version {AppInfo.Current.VersionString} • build {AppInfo.Current.BuildString}")),
                UiKit.Sub($"Package: {AppInfo.Current.PackageName}"),
                UiKit.Sub("Nomor versi berasal langsung dari APK yang sedang terpasang.")
            }
        }));
        Content=new ScrollView{Content=root}; Appearing +=(_,_)=>Load();
        strategyInfo.Text=StrategyText();
    }

    async Task ResetScanAsync()
    {
        if (!await AppDialog.ConfirmAsync(this, "Reset data scanner?",
                "Snapshot, rekomendasi, histori evaluasi, dan checkpoint scanner akan dihapus. Transaction history dan Sync Up tetap aman.",
                "Reset scanner", "Batal", true)) return;
        if (ScanServiceBridge.IsRunning) ScanServiceBridge.Stop();
        await _data.ResetScanDataAsync();
        await AppDialog.ShowAsync(this, "Scanner direset",
            "Data scanner telah dikosongkan. Scan berikutnya akan mengambil snapshot baru.");
    }

    async Task ResetHistoryAsync()
    {
        if (!await AppDialog.ConfirmAsync(this, "Reset transaction history?",
                "Semua transaksi, posisi, input IPO, realized, dan rekonsiliasi kas akan dihapus. Universe dan hasil scanner tetap ada.",
                "Hapus history", "Batal", true)) return;
        await _data.ResetTransactionHistoryAsync();
        await AppDialog.ShowAsync(this, "History direset",
            "Impor kembali e-Statement dan jalankan Sync Up untuk membangun portofolio.");
        if (Window is not null)
            Window.Page = new ImportRequiredPage(_data,
                App.Services.GetRequiredService<TransactionHistoryService>());
    }

    async Task ResetAllAsync()
    {
        if (!await AppDialog.ConfirmAsync(this, "Reset seluruh aplikasi?",
                "Semua transaksi, Sync Up, universe, snapshot, rekomendasi, dan pengaturan akan kembali ke kondisi awal.",
                "Reset semuanya", "Batal", true)) return;
        if (ScanServiceBridge.IsRunning) ScanServiceBridge.Stop();
        await _data.ResetAllAsync();
        await AppDialog.ShowAsync(this, "Aplikasi direset",
            "Semua data lokal telah dikosongkan. Onboarding awal akan dibuka kembali.");
        if (Window is not null)
            Window.Page = new ImportRequiredPage(_data,
                App.Services.GetRequiredService<TransactionHistoryService>());
    }
    static Border Field(string label,Entry entry)
    {
        entry.TextColor=Colors.White; entry.BackgroundColor=Colors.Transparent;
        return UiKit.Box(new VerticalStackLayout{Children={UiKit.Sub(label),entry}});
    }
    static Border Field(string label,Picker picker) =>
        UiKit.Box(new VerticalStackLayout{Children={UiKit.Sub(label),picker}});
    void Load()
    {
        _cash.Text=_data.State.Cash.ToString("0"); _risk.Text=_data.State.RiskPerTrade.ToString("0"); _monthly.Text=_data.State.MonthlyLimit.ToString("0");
        _officialRealized.Text=_data.State.OfficialRealizedProfit?.ToString("0") ?? "";
        _buyFee.Text=(_data.State.BuyFeeRate*100).ToString("0.###"); _sellFee.Text=(_data.State.SellFeeRate*100).ToString("0.###"); _speculative.IsToggled=_data.State.IncludeSpeculative;
        _autoScan.IsToggled = _data.State.AutoScanAfterClose;
        _autoEvents.IsToggled = _data.State.AutoEventIntelligence;
        _language.SelectedIndex = _data.State.LanguageCode == "en" ? 1 : 0;
        _delay.Text=_data.State.RequestDelayMilliseconds.ToString();
        var source = _universe.SourceLabel;
        var updated = _universe.UsingRecoverySnapshot
            ? UniverseService.RecoverySnapshotDate
            : _data.State.UniverseUpdatedAt?.ToString("dd MMM yyyy") ??
              Loc.T("belum pernah");
        _universeInfo.Text = Loc.T(
            $"Universe aktif: {_universe.Symbols.Count} saham • sumber: {source} • diperbarui {updated}.",
            $"Active universe: {_universe.Symbols.Count} stocks • source: {source} • updated {updated}.");
    }

    async Task UpdateUniverseAsync()
    {
        try
        {
            _universeInfo.Text = Loc.T("Mengambil master emiten dari IDX…");
            var result = await _universe.EnsureCurrentAsync(true, CancellationToken.None);
            Load();
            await AppDialog.ShowAsync(this,
                result.Updated
                    ? Loc.T("Master diperbarui", "Master list updated")
                    : Loc.T("Menggunakan data pemulihan/cache", "Using recovery/cache data"),
                Loc.T(
                    $"{result.Message}\nUniverse aktif: {result.Count} saham.",
                    $"{result.Message}\nActive universe: {result.Count} stocks."));
        }
        catch(Exception ex)
        {
            await AppDialog.ShowAsync(this, Loc.T("Gagal", "Failed"),
                ex.Message, danger:true);
            Load();
        }
    }
    async Task SaveAsync()
    {
        if (_saving) return;
        _saving = true;
        var oldLanguage = _data.State.LanguageCode;
        var newLanguage = _language.SelectedIndex == 1 ? "en" : "id";
        string? scheduleWarning = null;
        try
        {
            if (ScanServiceBridge.IsRunning)
            {
                await AppDialog.ShowAsync(this,
                    Loc.T("Scanner masih berjalan", "Scanner is still running"),
                    Loc.T(
                        "Tunggu proses selesai atau hentikan scanner sebelum menyimpan pengaturan agar snapshot dan state tidak ditulis bersamaan.",
                        "Wait for completion or stop the scanner before saving settings so the snapshot and app state are not written concurrently."));
                return;
            }
            if (!decimal.TryParse(_cash.Text, out var desiredCash) ||
                desiredCash < 0 ||
                !decimal.TryParse(_risk.Text, out var risk) ||
                risk <= 0 ||
                !decimal.TryParse(_monthly.Text, out var monthly) ||
                monthly <= 0 ||
                !decimal.TryParse(_buyFee.Text, out var buyFeePercent) ||
                buyFeePercent is < 0 or > 5 ||
                !decimal.TryParse(_sellFee.Text, out var sellFeePercent) ||
                sellFeePercent is < 0 or > 5 ||
                !int.TryParse(_delay.Text, out var requestDelay) ||
                requestDelay is < 0 or > 5000)
            {
                await AppDialog.ShowAsync(this,
                    Loc.T("Data tidak valid", "Invalid data"),
                    Loc.T(
                        "Kas harus 0 atau lebih, batas risiko harus di atas 0, fee harus 0–5%, dan jeda request harus 0–5000 milidetik.",
                        "Cash must be 0 or more, risk limits must be above 0, fees must be 0–5%, and the request delay must be 0–5000 milliseconds."),
                    danger: true);
                return;
            }
            decimal? officialRealized = null;
            if (!string.IsNullOrWhiteSpace(_officialRealized.Text))
            {
                if (!decimal.TryParse(
                        _officialRealized.Text, out var parsedRealized))
                {
                    await AppDialog.ShowAsync(this,
                        Loc.T("Data tidak valid", "Invalid data"),
                        Loc.T(
                            "Realized P/L resmi harus berupa angka atau dikosongkan.",
                            "Official realized P/L must be numeric or left blank."),
                        danger: true);
                    return;
                }
                officialRealized = parsedRealized;
            }

            await UiKit.RunBusyAsync(this,
                Loc.T("Menyimpan pengaturan…", "Saving settings…"), async () =>
                {
                    _data.State.RiskPerTrade = risk;
                    _data.State.MonthlyLimit = monthly;
                    _data.State.BuyFeeRate = buyFeePercent / 100;
                    _data.State.SellFeeRate = sellFeePercent / 100;
                    if (officialRealized.HasValue)
                    {
                        _data.State.OfficialRealizedProfit =
                            officialRealized.Value;
                        _data.State.RealizedReconciledAt = DateTime.Now;
                    }
                    foreach (var tx in _data.State.Transactions.Where(x =>
                                 x.IsActive && x.Source == "HISTORY" &&
                                 x.Note.Contains("e-Statement Stockbit",
                                     StringComparison.OrdinalIgnoreCase)))
                        tx.Fee = decimal.Round(tx.GrossValue *
                            (tx.Side == "BUY"
                                ? _data.State.BuyFeeRate
                                : _data.State.SellFeeRate), 0);
                    _data.RebuildPositions();
                    var flows = _data.State.Transactions
                        .Where(x => x.IsActive && x.AffectsCash)
                        .Sum(x => x.NetCashFlow);
                    _data.State.CashOpeningBalance =
                        desiredCash - flows;
                    _data.State.CashReconciled = true;
                    _data.State.CashReconciledAt = DateTime.Now;
                    _data.RecalculateCash();
                    _data.State.IncludeSpeculative = _speculative.IsToggled;
                    _data.State.AutoScanAfterClose = _autoScan.IsToggled;
                    _data.State.AutoEventIntelligence = _autoEvents.IsToggled;
                    _data.State.LanguageCode = newLanguage;
                    _data.State.RequestDelayMilliseconds =
                        requestDelay;

                    try
                    {
                        if (_data.State.AutoScanAfterClose)
                            BackgroundScanScheduler.ScheduleDaily(
                                Android.App.Application.Context);
                        else
                            BackgroundScanScheduler.CancelDaily(
                                Android.App.Application.Context);
                    }
                    catch (Exception ex)
                    {
                        // Saving user settings must not be blocked by an Android
                        // alarm permission or vendor-specific scheduler failure.
                        scheduleWarning = ex.Message;
                    }
                    await _data.SaveAsync();
                });

            // Persist the cold-start notification language only after the app
            // state itself has been saved successfully.
            Preferences.Default.Set("app.language", newLanguage);
            Loc.Use(newLanguage);
            var savedMessage = Loc.T(
                "Pengaturan berhasil disimpan.",
                "Settings saved.");
            if (!string.IsNullOrWhiteSpace(scheduleWarning))
                savedMessage += "\n\n" + Loc.T(
                    $"Jadwal background belum dapat diperbarui: {scheduleWarning}",
                    $"The background schedule could not be updated: {scheduleWarning}");
            await AppDialog.ShowAsync(this, Loc.T("Tersimpan", "Saved"),
                savedMessage);

            if (oldLanguage != newLanguage)
            {
                var window = Window;
                if (window is not null)
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        window.Page = new AppShell());
            }
        }
        catch (Exception ex)
        {
            await AppDialog.ShowAsync(this, Loc.T("Gagal", "Failed"),
                Loc.T(
                    $"Pengaturan tidak dapat disimpan: {ex.Message}",
                    $"Settings could not be saved: {ex.Message}"),
                danger: true);
        }
        finally
        {
            _saving = false;
        }
    }

    async Task ExportEvaluationAsync()
    {
        try
        {
            var result = await _evaluationExport.ExportAsync();
            if (result.Rows == 0)
            {
                await AppDialog.ShowAsync(this,
                    Loc.T("Belum ada data", "No data yet"),
                    Loc.T(
                        "Jalankan analisis closing untuk menghasilkan data evaluasi.",
                        "Run a closing analysis to generate evaluation data."));
                return;
            }
            await Share.Default.RequestAsync(new ShareFileRequest(
                Loc.T(
                    $"Data evaluasi StockMate • {result.Rows} baris",
                    $"StockMate evaluation data • {result.Rows} rows"),
                new ShareFile(result.Path)));
        }
        catch(Exception ex)
        {
            await AppDialog.ShowAsync(this, Loc.T("Gagal", "Failed"),
                Loc.T(
                    $"Data evaluasi tidak dapat diekspor: {ex.Message}",
                    $"Evaluation data could not be exported: {ex.Message}"),
                danger:true);
        }
    }

    string StrategyText()
    {
        var strategy = _data.State.Strategy;
        var trained = strategy.Training is not
            { QualityGatePassed: true, Status: "READY_FOR_FORWARD_TEST" }
            ? Loc.T(
                "Rule engine aktif dan diberi label belum tervalidasi. Bundle model yang ditolak tidak pernah dipakai aplikasi.",
                "The rule engine is active and labelled unvalidated. A rejected model bundle is never used by the app.")
            : Loc.T(
                $"Parameter rule-based memiliki metadata walk-forward OOS: {strategy.Training.OutOfSampleFolds} fold • " +
                $"{strategy.Training.OutOfSampleTrades} trade • win rate " +
                $"{strategy.Training.OutOfSampleWinRate:P1} • max DD " +
                $"{strategy.Training.OutOfSampleMaxDrawdown:P1}. Bobot ML runtime belum diaktifkan.",
                $"The rule-based parameters have walk-forward OOS metadata: {strategy.Training.OutOfSampleFolds} folds • " +
                $"{strategy.Training.OutOfSampleTrades} trades • win rate " +
                $"{strategy.Training.OutOfSampleWinRate:P1} • max DD " +
                $"{strategy.Training.OutOfSampleMaxDrawdown:P1}. Runtime ML weights are not active.");
        return Loc.T(
            $"Strategi aktif: v{strategy.Version}\nRR minimum {strategy.MinimumRiskReward:N1} • " +
            $"BUY ≥ {strategy.BuyScore} • WATCH ≥ {strategy.WatchScore}\n{trained}\n" +
            "File strategi bisa diimpor tanpa publish ulang APK.",
            $"Active strategy: v{strategy.Version}\nMinimum RR {strategy.MinimumRiskReward:N1} • " +
            $"BUY ≥ {strategy.BuyScore} • WATCH ≥ {strategy.WatchScore}\n{trained}\n" +
            "A strategy file can be imported without republishing the APK.");
    }

    async Task ImportStrategyAsync()
    {
        try
        {
            var file=await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle=Loc.T("Pilih strategy.json")
            });
            if(file is null)return;
            await using var stream=await file.OpenReadAsync();
            var strategy=await JsonSerializer.DeserializeAsync<StrategyConfig>(stream);
            if(strategy is null || strategy.MinimumRiskReward<1 || strategy.BuyScore is <50 or >100 || strategy.WatchScore>=strategy.BuyScore)
            {
                await AppDialog.ShowAsync(this, "Tidak valid","Konfigurasi strategi tidak lolos validasi.",danger:true); return;
            }
            if (strategy.Training is { } training &&
                (!training.QualityGatePassed ||
                 training.Status != "READY_FOR_FORWARD_TEST" ||
                 training.OutOfSampleFolds < 5 ||
                 training.OutOfSampleTrades < 100 ||
                 training.OutOfSampleProfitFactor < 1.20m ||
                 training.OutOfSampleAuc < .52m ||
                 string.IsNullOrWhiteSpace(training.DataFingerprint)))
            {
                await AppDialog.ShowAsync(this,
                    Loc.T("Validasi training gagal", "Training validation failed"),
                    Loc.T(
                        "Artefak harus berstatus READY_FOR_FORWARD_TEST, lolos quality gate, memiliki minimal 5 blok OOS lengkap, 100 trade, profit factor 1,20, AUC 0,52, dan fingerprint data.",
                        "The artifact must be READY_FOR_FORWARD_TEST, pass every quality gate, and contain at least 5 complete OOS blocks, 100 trades, a 1.20 profit factor, 0.52 AUC, and a data fingerprint."),
                    danger:true);
                return;
            }
            _data.State.Strategy=strategy; _data.State.StrategyVersion=strategy.Version; _data.State.MinRiskReward=strategy.MinimumRiskReward;
            await _data.SaveAsync();
            await AppDialog.ShowAsync(this, Loc.T("Berhasil", "Success"),
                Loc.T(
                    $"Strategi v{strategy.Version} aktif.",
                    $"Strategy v{strategy.Version} is active."));
        }
        catch(Exception ex)
        {
            await AppDialog.ShowAsync(this, Loc.T("Gagal", "Failed"),
                Loc.T(
                    $"File tidak dapat dibaca: {ex.Message}",
                    $"The file could not be read: {ex.Message}"),
                danger:true);
        }
    }

    async Task ExportStrategyAsync()
    {
        try
        {
            var path=Path.Combine(FileSystem.CacheDirectory,"stockmate_strategy.json");
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(_data.State.Strategy,new JsonSerializerOptions{WriteIndented=true}));
            await Share.Default.RequestAsync(new ShareFileRequest("Strategi StockMate",new ShareFile(path)));
        }
        catch(Exception ex)
        {
            await AppDialog.ShowAsync(this, Loc.T("Gagal", "Failed"),
                Loc.T(
                    $"Strategi tidak dapat dibagikan: {ex.Message}",
                    $"The strategy could not be shared: {ex.Message}"),
                danger:true);
        }
    }
}
