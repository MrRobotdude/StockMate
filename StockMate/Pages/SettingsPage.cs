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
    readonly Entry _cash=new(){Keyboard=Keyboard.Numeric}, _officialRealized=new(){Keyboard=Keyboard.Numeric}, _risk=new(){Keyboard=Keyboard.Numeric}, _monthly=new(){Keyboard=Keyboard.Numeric}, _buyFee=new(){Keyboard=Keyboard.Numeric}, _sellFee=new(){Keyboard=Keyboard.Numeric}, _delay=new(){Keyboard=Keyboard.Numeric};
    readonly Switch _speculative=new(), _autoScan=new();
    readonly Picker _language = new() { Title = "Bahasa / Language", ItemsSource = new[] { "Bahasa Indonesia", "English" } };
    readonly Label _universeInfo=UiKit.Sub("");
    public SettingsPage(AppDataService data, UniverseService universe)
    {
        _data=data; _universe=universe; Title=Loc.T("Pengaturan", "Settings"); BackgroundColor=UiKit.Navy;
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
        var row=new Grid{ColumnDefinitions=[new(GridLength.Star),new(GridLength.Auto)]}; row.Add(new VerticalStackLayout{Children={new Label{Text="Sertakan saham spekulatif",TextColor=Colors.White},UiKit.Sub("Maksimal alokasi scanner Rp500 ribu")}},0); row.Add(_speculative,1); root.Children.Add(UiKit.Box(row));
        var autoRow = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        autoRow.Add(new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = "Scan closing otomatis", TextColor = Colors.White },
                UiKit.Sub("Cek 12.15 & 16.30 • retry 10 menit bila data belum tersedia")
            }
        }, 0);
        autoRow.Add(_autoScan, 1);
        var exactAlarm = UiKit.Secondary("Izinkan jadwal presisi Android");
        exactAlarm.Clicked += (_, _) =>
            BackgroundScanScheduler.OpenExactAlarmSettings(Android.App.Application.Context);
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Background scanner", "Background scanner"),
            Loc.T("Tetap berjalan saat aplikasi ditutup", "Continues when the app is closed"),
            new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    autoRow,
                    exactAlarm,
                    UiKit.Sub(Loc.T(
                        "Android akan membangunkan StockMate sekitar 12.15 dan 16.30 pada hari bursa. Jika closing belum siap, aplikasi menjadwalkan pemeriksaan ulang. Download panjang memakai notifikasi foreground. Force Stop tetap menghentikan seluruh pekerjaan sampai aplikasi dibuka kembali.",
                        "Android wakes StockMate around 12:15 and 16:30 on trading days. If closing is not ready, another check is scheduled. Long downloads use a foreground notification. Force Stop still blocks all work until the app is opened again."))
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
        var importUniverse=UiKit.Secondary(Loc.T("Impor CSV/TXT", "Import CSV/TXT"));
        var updateUniverse=UiKit.Primary(Loc.T("Perbarui master IDX", "Update IDX master"));
        var exportUniverse=UiKit.Secondary(Loc.T("Bagikan universe", "Share universe"));
        updateUniverse.Clicked += async(_,_)=>await UpdateUniverseAsync();
        importUniverse.Clicked += async(_,_)=>await ImportUniverseAsync();
        exportUniverse.Clicked += async(_,_)=>await ExportUniverseAsync();
        var universeButtons=new Grid{ColumnDefinitions=[new(GridLength.Star),new(GridLength.Star)],ColumnSpacing=10}; universeButtons.Add(importUniverse,0); universeButtons.Add(exportUniverse,1);
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Universe IDX", "IDX universe"),
            Loc.T("Kelola daftar saham aktif", "Manage the active stock list"),
            new VerticalStackLayout { Spacing=10, Children={_universeInfo,updateUniverse,universeButtons} }));
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
                UiKit.Sub($"Versi {AppInfo.Current.VersionString} • build {AppInfo.Current.BuildString}"),
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
        _language.SelectedIndex = _data.State.LanguageCode == "en" ? 1 : 0;
        _delay.Text=_data.State.RequestDelayMilliseconds.ToString();
        _universeInfo.Text=$"Universe aktif: {_universe.Symbols.Count} saham • sumber: {(_data.State.UniverseSource.Length==0?"fallback lokal":_data.State.UniverseSource)} • diperbarui {(_data.State.UniverseUpdatedAt?.ToString("dd MMM yyyy")??"belum pernah")}.";
    }

    async Task UpdateUniverseAsync()
    {
        try
        {
            _universeInfo.Text = "Mengambil master emiten dari IDX…";
            var result = await _universe.EnsureCurrentAsync(true, CancellationToken.None);
            Load();
            await AppDialog.ShowAsync(this,
                result.Updated ? "Master diperbarui" : "Menggunakan cache",
                $"{result.Message}\nUniverse aktif: {result.Count} saham.");
        }
        catch(Exception ex) { await AppDialog.ShowAsync(this, "Gagal", ex.Message, danger:true); Load(); }
    }
    async Task SaveAsync()
    {
        await UiKit.RunBusyAsync(this, Loc.T("Menyimpan pengaturan…", "Saving settings…"), async () =>
        {
        decimal? desiredCash=decimal.TryParse(_cash.Text,out var cash)?cash:null;
        if(decimal.TryParse(_risk.Text,out var risk))_data.State.RiskPerTrade=risk;
        if(decimal.TryParse(_monthly.Text,out var monthly))_data.State.MonthlyLimit=monthly;
        if(decimal.TryParse(_buyFee.Text,out var buy))_data.State.BuyFeeRate=buy/100;
        if(decimal.TryParse(_sellFee.Text,out var sell))_data.State.SellFeeRate=sell/100;
        if(decimal.TryParse(_officialRealized.Text,out var officialRealized))
        {
            _data.State.OfficialRealizedProfit=officialRealized;
            _data.State.RealizedReconciledAt=DateTime.Now;
        }
        foreach(var tx in _data.State.Transactions.Where(x =>
                    x.IsActive && x.Source=="HISTORY" &&
                    x.Note.Contains("e-Statement Stockbit",StringComparison.OrdinalIgnoreCase)))
            tx.Fee=decimal.Round(tx.GrossValue*(tx.Side=="BUY"
                ?_data.State.BuyFeeRate:_data.State.SellFeeRate),0);
        _data.RebuildPositions();
        if(desiredCash.HasValue)
        {
            var flows=_data.State.Transactions.Where(x=>x.IsActive&&x.AffectsCash).Sum(x=>x.NetCashFlow);
            _data.State.CashOpeningBalance=desiredCash.Value-flows;
            _data.State.CashReconciled=true;
            _data.State.CashReconciledAt=DateTime.Now;
        }
        _data.RecalculateCash();
        _data.State.IncludeSpeculative=_speculative.IsToggled;
        _data.State.AutoScanAfterClose = _autoScan.IsToggled;
        if (_data.State.AutoScanAfterClose)
            BackgroundScanScheduler.ScheduleDaily(Android.App.Application.Context);
        else
            BackgroundScanScheduler.CancelDaily(Android.App.Application.Context);
        var oldLanguage = _data.State.LanguageCode;
        _data.State.LanguageCode = _language.SelectedIndex == 1 ? "en" : "id";
        if(int.TryParse(_delay.Text,out var delay))_data.State.RequestDelayMilliseconds=Math.Clamp(delay,0,5000);
        await _data.SaveAsync(); await AppDialog.ShowAsync(this, Loc.T("Tersimpan","Saved"),Loc.T("Pengaturan berhasil disimpan.","Settings saved."));
        if (oldLanguage != _data.State.LanguageCode && Window is not null)
        {
            Loc.Use(_data.State.LanguageCode);
            Window.Page = new AppShell();
        }
        });
    }

    async Task ImportUniverseAsync()
    {
        try
        {
            var file=await FilePicker.Default.PickAsync(new PickOptions{PickerTitle="Pilih daftar kode saham IDX"});
            if(file is null)return;
            await using var stream=await file.OpenReadAsync();
            var count=await _universe.ImportAsync(stream);
            _universeInfo.Text=$"Universe aktif: {count} saham.";
            await AppDialog.ShowAsync(this, "Universe diperbarui",$"{count} kode saham tersimpan. Format boleh satu kode per baris atau CSV.");
        }
        catch(Exception ex){await AppDialog.ShowAsync(this, "Gagal",$"Universe tidak dapat dibaca: {ex.Message}",danger:true);}
    }

    async Task ExportUniverseAsync()
    {
        try
        {
            var path=Path.Combine(FileSystem.CacheDirectory,"stockmate-idx-universe.txt");
            await _universe.ExportAsync(path);
            await Share.Default.RequestAsync(new ShareFileRequest("Universe IDX StockMate",new ShareFile(path)));
        }
        catch(Exception ex){await AppDialog.ShowAsync(this, "Gagal",$"Universe tidak dapat dibagikan: {ex.Message}",danger:true);}
    }

    string StrategyText()
    {
        var strategy = _data.State.Strategy;
        var trained = strategy.Training is null
            ? "Strategi manual/bawaan"
            : $"Walk-forward OOS: {strategy.Training.OutOfSampleFolds} fold • " +
              $"{strategy.Training.OutOfSampleTrades} trade • win rate " +
              $"{strategy.Training.OutOfSampleWinRate:P1} • max DD " +
              $"{strategy.Training.OutOfSampleMaxDrawdown:P1}";
        return $"Strategi aktif: v{strategy.Version}\nRR minimum {strategy.MinimumRiskReward:N1} • " +
               $"BUY ≥ {strategy.BuyScore} • WATCH ≥ {strategy.WatchScore}\n{trained}\n" +
               "File strategi bisa diimpor tanpa publish ulang APK.";
    }

    async Task ImportStrategyAsync()
    {
        try
        {
            var file=await FilePicker.Default.PickAsync(new PickOptions{PickerTitle="Pilih strategy.json"});
            if(file is null)return;
            await using var stream=await file.OpenReadAsync();
            var strategy=await JsonSerializer.DeserializeAsync<StrategyConfig>(stream);
            if(strategy is null || strategy.MinimumRiskReward<1 || strategy.BuyScore is <50 or >100 || strategy.WatchScore>=strategy.BuyScore)
            {
                await AppDialog.ShowAsync(this, "Tidak valid","Konfigurasi strategi tidak lolos validasi.",danger:true); return;
            }
            if (strategy.Training is { } training &&
                (training.Method != "walk-forward-v1" ||
                 training.OutOfSampleFolds < 3 ||
                 training.OutOfSampleTrades < 30 ||
                 string.IsNullOrWhiteSpace(training.DataFingerprint)))
            {
                await AppDialog.ShowAsync(this, "Validasi training gagal",
                    "Artefak training harus memiliki minimal 3 fold out-of-sample, 30 trade, dan fingerprint data.",
                    danger:true);
                return;
            }
            _data.State.Strategy=strategy; _data.State.StrategyVersion=strategy.Version; _data.State.MinRiskReward=strategy.MinimumRiskReward;
            await _data.SaveAsync(); await AppDialog.ShowAsync(this, "Berhasil",$"Strategi v{strategy.Version} aktif.");
        }
        catch(Exception ex){await AppDialog.ShowAsync(this, "Gagal",$"File tidak dapat dibaca: {ex.Message}",danger:true);}
    }

    async Task ExportStrategyAsync()
    {
        try
        {
            var path=Path.Combine(FileSystem.CacheDirectory,"stockmate_strategy.json");
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(_data.State.Strategy,new JsonSerializerOptions{WriteIndented=true}));
            await Share.Default.RequestAsync(new ShareFileRequest("Strategi StockMate",new ShareFile(path)));
        }
        catch(Exception ex){await AppDialog.ShowAsync(this, "Gagal",$"Strategi tidak dapat dibagikan: {ex.Message}",danger:true);}
    }
}
