using StockMate.Models;
using StockMate.Services;
using StockMate.Ui;
using StockMate.Platforms.Android;

namespace StockMate.Pages;

public sealed class ScannerPage : ContentPage
{
    readonly AppDataService _data;
    readonly ScanEngine _engine;
    readonly PortfolioDecisionService _decisions;
    readonly EventIntelligenceService _events;
    readonly VerticalStackLayout _results = new() { Spacing=10 };
    readonly Label _status = UiKit.Sub(Loc.T("Pilih scan siang atau malam."));
    readonly Label _phase = new() { Text = Loc.T("Belum berjalan"), TextColor = Colors.White, FontAttributes = FontAttributes.Bold, FontSize = 16 };
    readonly Label _current = UiKit.Sub(Loc.T("Tahap dan saham yang diproses akan tampil di sini."));
    readonly Label _counts = UiKit.Sub(Loc.T(
        "0/0 saham • berhasil 0 • gagal 0",
        "0/0 stocks • 0 succeeded • 0 failed"));
    readonly Label _batch = UiKit.Sub(Loc.T("Batch belum dimulai."));
    readonly Label _percent = new() { Text = "0%", TextColor = UiKit.Blue, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.End };
    readonly ProgressBar _progress = new() { Progress = 0, IsVisible = true, ProgressColor = UiKit.Blue };
    readonly Button _stop = new()
    {
        Text = Loc.T("Hentikan scan"), BackgroundColor = UiKit.Red, TextColor = Colors.White,
        CornerRadius = 14, HeightRequest = 48, IsVisible = false
    };
    readonly VerticalStackLayout _technicalLog = new() { Spacing = 5, IsVisible = false };
    readonly VerticalStackLayout _technicalItems = new() { Spacing = 5 };
    readonly List<TechnicalEntry> _technicalEntries = [];
    readonly Picker _technicalFilter = new() { Title = Loc.T("Semua proses") };
    readonly Label _technicalPageInfo = UiKit.Sub("");
    readonly Button _technicalPrevious = UiKit.Secondary("← " + Loc.T("Sebelumnya", "Previous"));
    readonly Button _technicalNext = UiKit.Secondary(Loc.T("Berikutnya", "Next") + " →");
    readonly Grid _technicalPager;
    int _technicalPage = 1;
    const int TechnicalPageSize = 10;
    readonly SearchBar _search = new() { Placeholder = Loc.T("Cari kode saham…") };
    readonly Picker _verdictFilter = new() { Title = Loc.T("Semua rekomendasi") };
    readonly Picker _sort = new() { Title = Loc.T("Urutkan") };
    readonly Label _closingInfo = UiKit.Sub("");
    readonly Label _resultSummary = UiKit.Sub("");
    readonly Label _pageInfo = UiKit.Sub("");
    readonly Button _previous = UiKit.Secondary("← " + Loc.T("Sebelumnya", "Previous"));
    readonly Button _next = UiKit.Secondary(Loc.T("Berikutnya", "Next") + " →");
    readonly Button _download;
    readonly Button _analyze;
    const int PageSize = 10;
    int _page = 1;
    CancellationTokenSource? _cts;
    public ScannerPage(
        AppDataService data,
        ScanEngine engine,
        PortfolioDecisionService decisions,
        EventIntelligenceService events)
    {
        _data=data; _engine=engine; _decisions=decisions; _events=events;
        Title="Scanner"; BackgroundColor=UiKit.Navy;
        _technicalPager = UiKit.Pager(_technicalPrevious, _technicalPageInfo, _technicalNext);
        _verdictFilter.ItemsSource = new[]
        {
            Loc.T("Shortlist rekomendasi", "Recommendation shortlist"),
            Loc.T("Semua hasil analisis", "All analysis results"),
            Loc.T("BELI", "BUY"), Loc.T("PANTAU", "WATCH"),
            Loc.T("TUNGGU", "WAIT"), "0 LOT",
            Loc.T("Bisa dieksekusi", "Executable"),
            Loc.T("Spekulatif", "Speculative"),
            Loc.T("Non-spekulatif", "Non-speculative")
        };
        _verdictFilter.SelectedIndex = 0;
        _technicalFilter.ItemsSource = new[]
        {
            Loc.T("Semua proses", "All processes"),
            Loc.T("Berhasil", "Succeeded"),
            Loc.T("Gagal / retry", "Failed / retry"),
            "Universe", "Download", Loc.T("Analisis", "Analysis")
        };
        _technicalFilter.SelectedIndex = 0;
        _sort.ItemsSource = new[]
        {
            Loc.T("Prioritas rekomendasi", "Recommendation priority"),
            Loc.T("Risk/reward terbaik", "Best risk/reward"),
            Loc.T("Potensi kenaikan target", "Largest target upside"),
            Loc.T("Risiko harga terkecil", "Smallest price risk"),
            Loc.T("Lot rekomendasi terbesar", "Largest recommended lot count"),
            Loc.T("Harga termurah", "Lowest price"),
            Loc.T("Kode A–Z", "Symbol A–Z")
        };
        _sort.SelectedIndex = 0;
        _search.TextChanged += (_, _) => { _page = 1; Render(_data.State.LastScan); };
        _verdictFilter.SelectedIndexChanged += (_, _) => { _page = 1; Render(_data.State.LastScan); };
        _sort.SelectedIndexChanged += (_, _) => { _page = 1; Render(_data.State.LastScan); };
        _previous.Clicked += (_, _) => { if (_page > 1) { _page--; Render(_data.State.LastScan); } };
        _next.Clicked += (_, _) => { if (_next.IsEnabled) { _page++; Render(_data.State.LastScan); } };
        _download=UiKit.Primary(Loc.T("Ambil / perbarui data", "Fetch / update data"));
        _analyze=UiKit.Secondary(Loc.T("Analisis snapshot", "Analyze snapshot"));
        _download.Clicked += async (_,_)=>await DownloadAsync(_engine.UseLatestClosing(), false);
        _analyze.Clicked += async (_,_)=>await AnalyzeSnapshotAsync(_engine.UseLatestClosing());
        _stop.Clicked += (_, _) =>
        {
            _stop.IsEnabled = false;
            _status.Text = Loc.T("Menghentikan scanner dengan aman…");
            ScanServiceBridge.Stop();
        };
        var grid=new Grid
        {
            RowDefinitions=[new(GridLength.Auto),new(GridLength.Auto)],
            RowSpacing=10
        };
        grid.Add(_download,0,0);
        grid.Add(_analyze,0,1);
        var refresh=UiKit.Tertiary(Loc.T("Paksa ambil ulang data", "Force data refresh"));
        refresh.Clicked += async (_,_) =>
        {
            var session = _engine.UseLatestClosing();
            var yes = await AppDialog.ConfirmAsync(this, "Ambil ulang data?",
                "Gunakan hanya jika data snapshot gagal/tidak lengkap. Ini akan memakai request internet lagi.",
                "Ambil ulang", "Batal");
            if (yes) await DownloadAsync(session, true);
        };
        var root=UiKit.PageStack();
        root.Children.Add(UiKit.Heading(this, "Peluang terbaik", "Best opportunities",
            "Tahap 1 mengambil data pasar dan menyimpannya sebagai snapshot. Tahap 2 menjalankan rule engine transparan tanpa mengunduh ulang. Bundle trainer tetap terpisah sampai lolos READY_FOR_FORWARD_TEST dan implementasi runtime-nya lulus parity test.",
            "Step 1 downloads market data and stores a snapshot. Step 2 runs the transparent rule engine without downloading again. A trainer bundle remains separate until it is READY_FOR_FORWARD_TEST and its runtime implementation passes parity testing."));
        root.Children.Add(_closingInfo);
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing=10,
            Children=
            {
                UiKit.SectionTitle(Loc.T("Pemindaian pasar", "Market scan")),
                UiKit.Sub(Loc.T("Ambil data terbaru, lalu jalankan analisis strategi aktif.",
                    "Fetch the latest data, then run the active strategy analysis.")),
                grid,
                refresh
            }
        }));
        root.Children.Add(_stop);
        var progressHeader = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        progressHeader.Add(_phase, 0);
        progressHeader.Add(_percent, 1);
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                progressHeader,
                _current,
                _batch,
                _counts,
                _progress
            }
        }));
        var technicalToggle = UiKit.Tertiary(Loc.T("Buka proses teknis", "Open technical process"));
        technicalToggle.Clicked += (_, _) =>
        {
            _technicalLog.IsVisible = !_technicalLog.IsVisible;
            technicalToggle.Text = _technicalLog.IsVisible
                ? Loc.T("Sembunyikan proses teknis")
                : Loc.T("Lihat proses teknis");
            if (_technicalLog.IsVisible)
                RenderTechnicalLog();
        };
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                technicalToggle,
                _technicalFilter,
                _technicalLog
            }
        }));
        _technicalLog.Children.Add(_technicalItems);
        _technicalLog.Children.Add(_technicalPager);
        _technicalFilter.SelectedIndexChanged += (_, _) => { _technicalPage = 1; RenderTechnicalLog(); };
        _technicalPrevious.Clicked += (_, _) => { if (_technicalPage > 1) { _technicalPage--; RenderTechnicalLog(); } };
        _technicalNext.Clicked += (_, _) => { _technicalPage++; RenderTechnicalLog(); };
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Status & evaluasi terakhir", "Latest status & evaluation"),
            Loc.T("Ketuk untuk melihat status lengkap.", "Tap to view full status."),
            _status));
        var evaluation = UiKit.Secondary(Loc.T(
            "Buka halaman evaluasi prediksi",
            "Open prediction evaluation page"));
        evaluation.Clicked += async (_, _) =>
            await Navigation.PushAsync(
                new PredictionEvaluationPage(_data, _engine));
        root.Children.Add(evaluation);
        var filters = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            ColumnSpacing = 8
        };
        filters.Add(_verdictFilter, 0);
        filters.Add(_sort, 1);
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { _resultSummary, _search, filters }
        }));
        root.Children.Add(_results);
        root.Children.Add(UiKit.Pager(_previous, _pageInfo, _next));
        Content=new ScrollView { Content=root };
        Appearing += async (_,_) =>
        {
            try
            {
                ScanServiceBridge.ProgressChanged -= OnProgress;
                ScanServiceBridge.ProgressChanged += OnProgress;
                if (ScanServiceBridge.IsRunning ||
                    ScanServiceBridge.CurrentProgress.Stage is "COMPLETE" or "ERROR")
                    OnProgress(ScanServiceBridge.CurrentProgress);
                await _engine.UpdatePredictionHistoryAsync();
                ShowPerformance();
                UpdateClosingInfo();
                await _decisions.RebuildAsync();
                Render(_data.State.LastScan);
            }
            catch (Exception ex)
            {
                _status.Text = Loc.T(
                    $"Scanner tidak dapat menyiapkan halaman: {ex.Message}",
                    $"The scanner page could not be prepared: {ex.Message}");
            }
        };
        Disappearing += (_,_) => ScanServiceBridge.ProgressChanged -= OnProgress;
    }

    void OnProgress(ScanProgress progress)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => OnProgress(progress));
            return;
        }
        _phase.Text = StageName(progress.Stage);
        _current.Text = string.IsNullOrWhiteSpace(progress.CurrentSymbol)
            ? LocalizeProgress(progress)
            : Loc.T(
                $"{LocalizeProgress(progress)} • sekarang: {progress.CurrentSymbol}",
                $"{LocalizeProgress(progress)} • current: {progress.CurrentSymbol}");
        _counts.Text = progress.Total <= 0
            ? Loc.T(
                "Menyiapkan jumlah pekerjaan…",
                "Preparing the workload…")
            : Loc.T(
                $"{progress.Completed}/{progress.Total} saham • berhasil {progress.Succeeded} • gagal {progress.Failed}",
                $"{progress.Completed}/{progress.Total} stocks • {progress.Succeeded} succeeded • {progress.Failed} failed");
        _batch.Text = progress.TotalBatches <= 0
            ? Loc.T(
                "Batch akan dibentuk setelah universe siap.",
                "Batches will be created after the universe is ready.")
            : Loc.T(
                $"Batch {progress.CurrentBatch}/{progress.TotalBatches} • {progress.BatchCompleted}/{progress.BatchSize} saham" +
              (progress.LastCompletedBatch > 0
                  ? $" • batch {progress.LastCompletedBatch} sudah selesai"
                  : ""),
                $"Batch {progress.CurrentBatch}/{progress.TotalBatches} • {progress.BatchCompleted}/{progress.BatchSize} stocks" +
              (progress.LastCompletedBatch > 0
                  ? $" • batch {progress.LastCompletedBatch} completed"
                  : ""));
        _percent.Text = progress.Total <= 0 ? "…" : $"{progress.Percent}%";
        _progress.Progress = progress.Total <= 0 ? 0 : progress.Completed / (double)progress.Total;
        AddTechnicalLog(progress);
        if (progress.Stage is "COMPLETE" or "ERROR")
        {
            _status.Text = LocalizeProgress(progress);
            _stop.IsVisible = false;
            _stop.IsEnabled = true;
            Render(_data.State.LastScan);
            if (progress.Stage == "COMPLETE")
            {
                ShowPerformance();
                UpdateClosingInfo();
            }
        }
        else if (ScanServiceBridge.IsRunning)
        {
            _stop.IsVisible = true;
            _stop.IsEnabled = true;
        }
    }

    void AddTechnicalLog(ScanProgress progress)
    {
        var detail = Loc.English
            ? LocalizeProgress(progress)
            : string.IsNullOrWhiteSpace(progress.TechnicalDetail)
                ? progress.Message
                : progress.TechnicalDetail;
        var source = string.IsNullOrWhiteSpace(progress.Source)
            ? ""
            : Loc.T(
                $" • sumber {progress.Source}",
                $" • source {LocalizeSource(progress.Source)}");
        var attempt = progress.Attempt > 0 ? $" • attempt {progress.Attempt}" : "";
        var elapsed = progress.ElapsedMilliseconds > 0
            ? $" • {progress.ElapsedMilliseconds / 1000d:0.0}s"
            : "";
        var line = $"{progress.OccurredAt:HH:mm:ss} [{progress.Stage}] {detail}{source}{attempt}{elapsed}";
        if (_technicalEntries.LastOrDefault()?.Text == line) return;
        _technicalEntries.Add(new TechnicalEntry(progress.Stage, line));
        if (_technicalEntries.Count > 200) _technicalEntries.RemoveAt(0);
        // The log panel is collapsed during normal use. Rebuilding its child
        // controls on every price request was wasted layout work and a visible
        // source of lag on mid-range Android devices.
        if (_technicalLog.IsVisible)
            RenderTechnicalLog();
    }

    void RenderTechnicalLog()
    {
        IEnumerable<TechnicalEntry> query = _technicalEntries;
        query = _technicalFilter.SelectedIndex switch
        {
            1 => query.Where(x => x.Stage is "REQUEST_OK" or "BATCH_COMPLETE" or "COMPLETE"),
            2 => query.Where(x => x.Stage is "REQUEST_ERROR" or "RETRY" or "RATE_LIMIT" or "FORBIDDEN_RETRY" or "ERROR"),
            3 => query.Where(x => x.Stage.Contains("UNIVERSE", StringComparison.OrdinalIgnoreCase)),
            4 => query.Where(x => x.Stage is "DOWNLOAD" or "REQUEST_START" or "REQUEST_OK" or "BATCH_START" or "BATCH_COMPLETE"),
            5 => query.Where(x => x.Stage is "EVENTS" or "ANALYZE" or "SAVING"),
            _ => query
        };
        var filtered = query.Reverse().ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)TechnicalPageSize));
        _technicalPage = Math.Clamp(_technicalPage, 1, pages);
        _technicalItems.Children.Clear();
        foreach (var entry in filtered.Skip((_technicalPage - 1) * TechnicalPageSize).Take(TechnicalPageSize))
            _technicalItems.Children.Add(new Label
            {
                Text = entry.Text,
                TextColor = UiKit.Muted,
                FontSize = 12,
                LineBreakMode = LineBreakMode.WordWrap
            });
        _technicalPageInfo.Text = Loc.T(
            $"Halaman {_technicalPage}/{pages} • {filtered.Count} log",
            $"Page {_technicalPage}/{pages} • {filtered.Count} logs");
        _technicalPrevious.IsEnabled = _technicalPage > 1;
        _technicalNext.IsEnabled = _technicalPage < pages;
    }

    async Task RunAsync(bool intraday, bool forceRefresh)
    {
        if (_cts is not null) return;
        _cts=new(); _results.Children.Clear();
        try
        {
            OnProgress(new ScanProgress
            {
                Stage = "PREPARING",
                Message = Loc.T(
                    "Menyalakan layanan scanner. Layar boleh dimatikan.",
                    "Starting the scanner service. The screen may be turned off.")
            });
            _status.Text = Loc.T("Progres juga tampil pada notifikasi Android.");
            await ScanServiceBridge.StartAsync(intraday, forceRefresh);
            var results = _data.State.LastScan;
            var snapshot = _engine.GetSnapshot(intraday);
            _status.Text = snapshot is null
                ? Loc.T("Snapshot belum tersedia.", "Snapshot is unavailable.")
                : Loc.T(
                    $"Selesai • {snapshot.Status} • {snapshot.Symbols.Count}/{snapshot.RequestedCount} saham • {results.Count} kandidat",
                    $"Complete • {snapshot.Status} • {snapshot.Symbols.Count}/{snapshot.RequestedCount} stocks • {results.Count} candidates");
            ShowPerformance();
            Render(results);
        }
        catch(OperationCanceledException)
        {
            _status.Text = Loc.T("Scan dibatalkan.");
        }
        catch(Exception ex)
        {
            _status.Text = Loc.T(
                $"Scan gagal: {ex.Message}",
                $"Scan failed: {ex.Message}");
        }
        finally { _cts.Dispose(); _cts=null; }
    }

    async Task DownloadAsync(bool intraday, bool forceRefresh)
    {
        if (_cts is not null) return;
        _cts = new();
        SetScannerActionsEnabled(false);
        try
        {
            OnProgress(new ScanProgress
            {
                Stage = "PREPARING",
                Message = Loc.T(
                    "Menyalakan layanan download. Aplikasi boleh ditutup.",
                    "Starting the download service. The app may be closed.")
            });
            _status.Text = Loc.T(
                "Progres tetap tampil di notifikasi Android saat aplikasi ditutup.");
            var ok = await ScanServiceBridge.StartAsync(
                intraday, forceRefresh, downloadOnly: true);
            if (!ok)
                throw new InvalidOperationException(
                    ScanServiceBridge.CurrentProgress.Message);
            var snapshot = _engine.GetSnapshot(intraday)
                ?? throw new InvalidOperationException(Loc.T(
                    "Download selesai tanpa snapshot.",
                    "The download completed without a snapshot."));
            _status.Text = Loc.T(
                $"Data siap • {snapshot.Symbols.Count}/{snapshot.RequestedCount} saham. Tekan Analisis snapshot.",
                $"Data ready • {snapshot.Symbols.Count}/{snapshot.RequestedCount} stocks. Tap Analyze snapshot.");
        }
        catch (OperationCanceledException) { _status.Text = Loc.T("Pengambilan dibatalkan.", "Download cancelled."); }
        catch (Exception ex) { _status.Text = $"{Loc.T("Pengambilan gagal", "Download failed")}: {ex.Message}"; }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetScannerActionsEnabled(true);
        }
    }

    async Task AnalyzeSnapshotAsync(bool intraday)
    {
        if (_cts is not null) return;
        var snapshot = _engine.GetSnapshot(intraday);
        if (snapshot is null)
        {
            await AppDialog.ShowAsync(this, Loc.T("Data belum tersedia", "Data unavailable"),
                Loc.T("Jalankan tahap 1 terlebih dahulu.", "Run step 1 first."));
            return;
        }
        _cts = new();
        SetScannerActionsEnabled(false);
        try
        {
            var progress = new Progress<ScanProgress>(OnProgress);
            string? eventWarning = null;
            var results = await Task.Run(() => _engine.AnalyzeAsync(
                snapshot.Session == "LUNCH", snapshot, true, progress, _cts.Token));
            if (_data.State.AutoEventIntelligence)
            {
                OnProgress(new ScanProgress
                {
                    Stage = "EVENTS",
                    Message = Loc.T(
                        "Memperbarui isu untuk shortlist terbaru",
                        "Updating events for the latest shortlist")
                });
                try
                {
                    await _events.RefreshAsync(_cts.Token);
                    // The first pass discovers today's shortlist. Refreshing
                    // events after that pass lets new candidates receive the
                    // same corporate-action veto, then the second local pass
                    // freezes the final recommendation without redownloading
                    // market prices.
                    results = await Task.Run(() => _engine.AnalyzeAsync(
                        snapshot.Session == "LUNCH",
                        snapshot,
                        true,
                        progress,
                        _cts.Token));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    eventWarning = Loc.T(
                        $"Data harga berhasil dianalisis, tetapi pembaruan isu gagal: {ex.Message}",
                        $"Market data was analyzed, but event refresh failed: {ex.Message}");
                }
            }
            await _decisions.RebuildAsync();
            Render(results);
            _status.Text = eventWarning ?? Loc.T(
                $"Analisis strategi v{_data.State.Strategy.Version} selesai • {results.Count} hasil",
                $"Strategy v{_data.State.Strategy.Version} analysis complete • {results.Count} results");
        }
        catch (OperationCanceledException) { _status.Text = Loc.T("Analisis dibatalkan.", "Analysis cancelled."); }
        catch (Exception ex) { _status.Text = $"{Loc.T("Analisis gagal", "Analysis failed")}: {ex.Message}"; }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetScannerActionsEnabled(true);
        }
    }

    void SetScannerActionsEnabled(bool enabled)
    {
        _download.IsEnabled = enabled;
        _analyze.IsEnabled = enabled;
    }

    static string StageName(string stage) => stage switch
    {
        "PREPARING" => Loc.T("1/5 • Menyiapkan scanner", "1/5 • Preparing scanner"),
        "UNIVERSE" => Loc.T("2/5 • Memperbarui daftar saham IDX", "2/5 • Updating IDX universe"),
        "UNIVERSE_REQUEST" => Loc.T("2/5 • Menghubungi IDX", "2/5 • Contacting IDX"),
        "UNIVERSE_PARSE" => Loc.T("2/5 • Membaca respons IDX", "2/5 • Reading IDX response"),
        "UNIVERSE_READY" => Loc.T("2/5 • Universe siap", "2/5 • Universe ready"),
        "UNIVERSE_FALLBACK" => Loc.T("2/5 • Memakai cache universe", "2/5 • Using cached universe"),
        "TRADING_DATE" => Loc.T("2/5 • Menentukan closing acuan", "2/5 • Resolving reference close"),
        "BATCH_PLAN" => Loc.T("3/5 • Membentuk batch", "3/5 • Creating batches"),
        "REQUEST_START" => Loc.T("3/5 • Mengirim request harga", "3/5 • Requesting prices"),
        "REQUEST_OK" => Loc.T("3/5 • Data harga diterima", "3/5 • Price data received"),
        "REQUEST_ERROR" => Loc.T("3/5 • Request gagal, lanjut", "3/5 • Request failed, continuing"),
        "RETRY" => Loc.T("3/5 • Mencoba ulang request", "3/5 • Retrying request"),
        "CLOSING_FALLBACK" => Loc.T("3/5 • Melanjutkan dengan data tersedia", "3/5 • Continuing with available data"),
        "WAITING_CLOSE" => Loc.T("3/5 • Memastikan data closing tersedia", "3/5 • Verifying closing data"),
        "RATE_LIMIT" => Loc.T("3/5 • Menunggu batas request", "3/5 • Waiting for rate limit"),
        "FORBIDDEN_RETRY" => Loc.T("3/5 • Akses 403, mengganti endpoint", "3/5 • Access denied, switching endpoint"),
        "DOWNLOAD" => Loc.T("3/5 • Mengambil data harga", "3/5 • Fetching price data"),
        "BATCH_START" => Loc.T("3/5 • Memulai batch pengambilan", "3/5 • Starting download batch"),
        "BATCH_COMPLETE" => Loc.T("3/5 • Batch selesai, lanjut berikutnya", "3/5 • Batch complete, continuing"),
        "MARKET_REGIME" => Loc.T("4/5 • Membaca rezim IHSG", "4/5 • Reading the JCI regime"),
        "MARKET_REGIME_FALLBACK" => Loc.T("4/5 • Rezim IHSG belum tersedia", "4/5 • JCI regime unavailable"),
        "EVENTS" => Loc.T("4/5 • Memperbarui isu pasar", "4/5 • Updating market events"),
        "ANALYZE" => Loc.T("4/5 • Menganalisis seluruh saham", "4/5 • Analyzing all stocks"),
        "SAVING" => Loc.T("5/5 • Menyimpan hasil", "5/5 • Saving results"),
        "COMPLETE" => Loc.T("Selesai", "Complete"),
        "ERROR" => Loc.T("Proses gagal", "Process failed"),
        _ => stage
    };

    void Render(IEnumerable<ScanResult> results)
    {
        _results.Children.Clear();
        IEnumerable<ScanResult> query = results;
        if (!string.IsNullOrWhiteSpace(_search.Text))
            query = query.Where(x => x.Symbol.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (_verdictFilter.SelectedIndex != 1)
            query = _verdictFilter.SelectedIndex switch
            {
                0 => query.Where(x =>
                x.AllocationRank > 0 && x.SuggestedLots > 0 && x.Verdict == "BUY AREA"),
                2 => query.Where(x => x.Verdict == "BUY AREA"),
                3 => query.Where(x =>
                    x.Verdict.Contains("WATCH", StringComparison.OrdinalIgnoreCase) ||
                    x.Verdict.Contains("PANTAU", StringComparison.OrdinalIgnoreCase)),
                4 => query.Where(x => x.Verdict.Contains("WAIT", StringComparison.OrdinalIgnoreCase)),
                5 => query.Where(x => x.SuggestedLots <= 0),
                6 => query.Where(x => x.SuggestedLots > 0),
                7 => query.Where(x => x.IsSpeculative),
                8 => query.Where(x => !x.IsSpeculative),
                _ => query
            };
        query = _sort.SelectedIndex switch
        {
            1 => query.OrderByDescending(x => x.RiskReward)
                      .ThenByDescending(x => x.CombinedScore),
            2 => query.OrderByDescending(x => x.LastPrice <= 0 ? 0 : (x.Target1 - x.LastPrice) / x.LastPrice),
            3 => query.OrderBy(x => x.LastPrice <= 0 ? decimal.MaxValue : (x.LastPrice - x.StopLoss) / x.LastPrice),
            4 => query.OrderByDescending(x => x.SuggestedLots)
                      .ThenByDescending(x => x.CombinedScore),
            5 => query.OrderBy(x => x.LastPrice)
                      .ThenByDescending(x => x.CombinedScore),
            6 => query.OrderBy(x => x.Symbol),
            _ => query.OrderByDescending(x => VerdictPriority(x.Verdict))
                      .ThenByDescending(x => x.CombinedScore)
        };
        var filtered = query.ToList();
        var all = results.ToList();
        var universe = _data.State.ScanHistory.LastOrDefault()?.UniverseCount
            ?? _engine.GetLatestSnapshot()?.RequestedCount ?? 0;
        var shortlist = all.Count(x => x.AllocationRank > 0);
        var buySignals = all.Count(x => x.Verdict == "BUY AREA");
        var marketRegime = all.FirstOrDefault()?.MarketRegime ?? "UNKNOWN";
        var marketBreadth = all.FirstOrDefault()?.MarketBreadth20Percent ?? 0;
        _resultSummary.Text =
            Loc.T(
                $"{universe:N0} universe • {all.Count:N0} dianalisis • rezim {marketRegime} (breadth {marketBreadth:0}%) • {buySignals:N0} sinyal BUY • {shortlist:N0} masuk alokasi kas Rp{_data.State.Cash:N0}.",
                $"{universe:N0} universe • {all.Count:N0} analyzed • {marketRegime} regime ({marketBreadth:0}% breadth) • {buySignals:N0} BUY signals • {shortlist:N0} allocated from Rp{_data.State.Cash:N0} cash.");
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        _page = Math.Clamp(_page, 1, totalPages);
        var items = filtered.Skip((_page - 1) * PageSize).Take(PageSize).ToList();
        _pageInfo.Text = Loc.T(
            $"Halaman {_page}/{totalPages} • {filtered.Count} hasil",
            $"Page {_page}/{totalPages} • {filtered.Count} results");
        _previous.IsEnabled = _page > 1;
        _next.IsEnabled = _page < totalPages;
        if (items.Count == 0)
        {
            _results.Children.Add(UiKit.EmptyState("⌕", "Belum ada hasil",
                ScanServiceBridge.IsRunning
                    ? "Hasil kandidat akan muncul setelah data yang cukup berhasil dianalisis."
                    : "Mulai scan sesi terakhir untuk mencari peluang di seluruh IDX."));
            return;
        }
        foreach(var r in items)
        {
            var position = _data.State.Positions.FirstOrDefault(x =>
                x.Shares > 0 && x.Symbol.Equals(r.Symbol, StringComparison.OrdinalIgnoreCase));
            var portfolioDecision = position is null ? null :
                _data.State.PortfolioDecisions.FirstOrDefault(x =>
                    x.Symbol.Equals(r.Symbol, StringComparison.OrdinalIgnoreCase));
            var isOwned = position is not null;
            var stack=new VerticalStackLayout { Spacing=8, Children=
            {
                new Label
                {
                    Text = Loc.T(
                        $"SETUP UTAMA · {r.PrimarySetup}",
                        $"PRIMARY SETUP · {(!string.IsNullOrWhiteSpace(r.PrimarySetupEn) ? r.PrimarySetupEn : r.PrimarySetup)}"),
                    TextColor = r.Verdict == "BUY AREA"
                        ? UiKit.Green : UiKit.Purple,
                    FontAttributes = FontAttributes.Bold
                },
                UiKit.Caption(Loc.T(
                    $"Rezim {r.MarketRegime} • IHSG 20 hari {r.MarketReturn20Percent:+0.0;-0.0;0.0}% • breadth MA20 {r.MarketBreadth20Percent:0}% • {ResearchStatusText(r.ResearchStatus)}",
                    $"{r.MarketRegime} regime • 20-day JCI {r.MarketReturn20Percent:+0.0;-0.0;0.0}% • MA20 breadth {r.MarketBreadth20Percent:0}% • {ResearchStatusText(r.ResearchStatus)}")),
                new Label
                {
                    Text = Loc.T(
                        $"Limit {r.EntryHigh:N0} • batal jika opening > {r.EntryHigh:N0}",
                        $"Limit {r.EntryHigh:N0} • cancel if opening > {r.EntryHigh:N0}"),
                    TextColor=Colors.White
                },
                new Label { Text=$"Target {r.Target1:N0} / {r.Target2:N0}  •  Stop {r.StopLoss:N0}", TextColor=UiKit.Muted },
                new Label
                {
                    Text = isOwned && portfolioDecision is not null
                        ? Loc.T(
                            $"Tindakan posisi: {Loc.Action(portfolioDecision)} • keyakinan {portfolioDecision.ConfidenceScore}/100",
                            $"Position action: {Loc.Action(portfolioDecision)} • confidence {portfolioDecision.ConfidenceScore}/100")
                        : r.SuggestedLots > 0
                        ? Loc.T(
                            $"Rencana: {r.SuggestedLots} lot @ limit {r.EntryHigh:N0}",
                            $"Plan: {r.SuggestedLots} lots @ {r.EntryHigh:N0} limit")
                        : Loc.T(
                            "Tidak dapat dieksekusi: ukuran aman 0 lot",
                            "Not executable: safe size is 0 lots"),
                    TextColor = isOwned
                        ? ActionColor(portfolioDecision?.ActionCode)
                        : r.SuggestedLots > 0 ? UiKit.Green : UiKit.Red,
                    FontAttributes = FontAttributes.Bold
                },
                new Label
                {
                    Text = portfolioDecision is null ? "" :
                        $"{portfolioDecision.RiskAction}\n{portfolioDecision.TakeProfitAction}",
                    IsVisible = portfolioDecision is not null,
                    TextColor = UiKit.Muted
                },
                new Label
                {
                    Text = Loc.English &&
                           !string.IsNullOrWhiteSpace(r.ExecutionNoteEn)
                        ? r.ExecutionNoteEn : r.ExecutionNote,
                    IsVisible = !string.IsNullOrWhiteSpace(r.ExecutionNote),
                    TextColor = r.AllocationRank > 0 ? UiKit.Blue : UiKit.Muted,
                },
                new Label
                {
                    Text = Loc.T(
                        $"{r.DataSession} • tanggal data {r.DataTime:dd MMM yyyy} • bukan jam transaksi real-time",
                        $"{Loc.Session(r.DataSession)} • data date {r.DataTime:dd MMM yyyy} • not a real-time transaction timestamp"),
                    TextColor=UiKit.Muted
                }
            }};
            var detail=UiKit.Tertiary(Loc.T("Buka detail saham", "Open stock details"));
            detail.Clicked += async (_,_) => await Navigation.PushAsync(
                new PositionDetailPage(_data, _decisions, r.Symbol));
            stack.Children.Add(detail);
            _results.Children.Add(UiKit.ExpandableCard(
                isOwned && portfolioDecision is not null
                    ? $"{r.Symbol} · {Loc.Action(portfolioDecision)}"
                    : $"{r.Symbol} · {Loc.Verdict(r.Verdict)}",
                isOwned && portfolioDecision is not null
                    ? Loc.T(
                        $"Posisi {position!.Lots} lot · keyakinan {portfolioDecision.ConfidenceScore}/100",
                        $"Position {position!.Lots} lots · confidence {portfolioDecision.ConfidenceScore}/100")
                    : r.SuggestedLots > 0
                    ? Loc.T(
                        $"Limit Rp {r.EntryHigh:N0} · {r.SuggestedLots} lot",
                        $"Limit Rp {r.EntryHigh:N0} · {r.SuggestedLots} lots")
                    : Loc.T(
                        $"Pantau harga Rp {r.EntryHigh:N0}",
                        $"Watch Rp {r.EntryHigh:N0}"),
                stack,
                isOwned && portfolioDecision is not null
                    ? $"{portfolioDecision.ConfidenceScore}/100"
                    : r.AllocationRank > 0 ? $"#{r.AllocationRank}" : Loc.T("PANTAU", "WATCH"),
                isOwned && portfolioDecision is not null
                    ? ActionColor(portfolioDecision.ActionCode)
                    : r.AllocationRank > 0 ? UiKit.Green : UiKit.Blue));
        }
    }

    static Color ActionColor(string? actionCode) =>
        Loc.IsSellAction(actionCode)
            ? UiKit.Red
            : Loc.IsBuyAction(actionCode)
                ? UiKit.Green
                : UiKit.Blue;

    static int VerdictPriority(string verdict) => verdict.Contains("BUY") ? 3 :
        verdict.Contains("WATCH") || verdict.Contains("PANTAU") ? 2 :
        verdict.Contains("WAIT") ? 1 : 0;

    void ShowPerformance()
    {
        var predictions = _data.State.ScanHistory
            .SelectMany(x => x.Predictions)
            .ToList();
        var observed = predictions
            .Where(x => x.NextTradingDate.HasValue)
            .ToList();
        var filledObserved = observed
            .Where(x => x.EntryFilledAt.HasValue &&
                        x.NextDayReturnPercent.HasValue)
            .ToList();
        var waiting = predictions.Count(x =>
            !x.NextTradingDate.HasValue &&
            x.Outcome != "CANCELLED");
        var notFilled = predictions.Count(x =>
            x.Outcome == "NOT_FILLED");
        var cancelled = predictions.Count(x =>
            x.Outcome == "CANCELLED");
        if (predictions.Count == 0)
        {
            _status.Text = Loc.T(
                "Belum ada histori. Rekomendasi BUY pertama akan menjadi baseline evaluasi.",
                "There is no history yet. The first BUY recommendation will become the evaluation baseline.");
            return;
        }
        if (observed.Count == 0)
        {
            var latestSignal = predictions
                .Select(x => x.SignalDate == default
                    ? x.PredictedAt.Date
                    : x.SignalDate.Date)
                .Max();
            _status.Text = Loc.T(
                $"{predictions.Count} rekomendasi tersimpan • menunggu candle hari bursa setelah {latestSignal:dd MMM yyyy}, bukan menunggu target swing.",
                $"{predictions.Count} recommendations saved • waiting for the trading-day candle after {latestSignal:dd MMM yyyy}, not for the swing target.");
            return;
        }
        var positive = filledObserved.Count(x =>
            x.NextDayReturnPercent > 0);
        var hitRate = filledObserved.Count == 0
            ? 0
            : positive * 100m / filledObserved.Count;
        var average = filledObserved.Count == 0
            ? 0
            : filledObserved.Average(x =>
                x.NextDayReturnPercent!.Value);
        _status.Text = Loc.T(
            $"T+1 tersedia {observed.Count}/{predictions.Count} • order terisi {filledObserved.Count} • positif {hitRate:0.0}% • rata-rata {average:+0.00;-0.00;0.00}% net • menunggu {waiting} • tidak terisi {notFilled} • dibatalkan {cancelled}",
            $"T+1 available {observed.Count}/{predictions.Count} • filled {filledObserved.Count} • positive {hitRate:0.0}% • average {average:+0.00;-0.00;0.00}% net • waiting {waiting} • not filled {notFilled} • cancelled {cancelled}");
    }

    void UpdateClosingInfo()
    {
        var intraday = _engine.UseLatestClosing();
        var snapshot = _engine.GetSnapshot(intraday);
        var label = intraday ? "Closing Sesi 1" : "Closing Sesi 2 / hari bursa terakhir";
        _closingInfo.Text = snapshot is null
            ? Loc.T(
                $"Otomatis memakai {label} yang terakhir tersedia.",
                $"Automatically uses the latest available {(intraday ? "Session 1 close" : "Session 2 / last trading day close")}.")
            : Loc.T(
                $"Closing otomatis: {(snapshot.Session == "LUNCH" ? "Sesi 1" : "Sesi 2")} • {snapshot.TradingDate:dd MMM yyyy} • data dipilih berdasarkan waktu scan.",
                $"Automatic close: {(snapshot.Session == "LUNCH" ? "Session 1" : "Session 2")} • {snapshot.TradingDate:dd MMM yyyy} • data selected according to scan time.");
    }

    static string LocalizeProgress(ScanProgress progress)
    {
        if (!Loc.English) return progress.Message;
        return progress.Stage switch
        {
            "PREPARING" => "Preparing the scanner",
            "UNIVERSE" => "Updating the IDX universe",
            "UNIVERSE_REQUEST" => "Requesting the IDX issuer master",
            "UNIVERSE_PARSE" => "Reading the IDX response",
            "UNIVERSE_READY" => "IDX universe is ready",
            "UNIVERSE_FALLBACK" => "Using the cached IDX universe",
            "TRADING_DATE" => "Resolving the reference trading date",
            "BATCH_PLAN" => "Preparing download batches",
            "REQUEST_START" => string.IsNullOrWhiteSpace(progress.CurrentSymbol)
                ? "Requesting price data"
                : $"Requesting {progress.CurrentSymbol}",
            "REQUEST_OK" => string.IsNullOrWhiteSpace(progress.CurrentSymbol)
                ? "Price data received"
                : $"{progress.CurrentSymbol} received",
            "REQUEST_ERROR" => string.IsNullOrWhiteSpace(progress.CurrentSymbol)
                ? "A request failed; continuing"
                : $"{progress.CurrentSymbol} failed; continuing",
            "RETRY" => string.IsNullOrWhiteSpace(progress.CurrentSymbol)
                ? "Retrying a request"
                : $"Retrying {progress.CurrentSymbol}",
            "WAITING_CLOSE" => "Verifying closing data",
            "CLOSING_FALLBACK" => "Continuing with available closing data",
            "RATE_LIMIT" => "Waiting for the data-source rate limit",
            "FORBIDDEN_RETRY" => "Access denied; switching endpoint",
            "DOWNLOAD" => "Downloading market data",
            "BATCH_START" => "Starting a download batch",
            "BATCH_COMPLETE" => "Download batch completed",
            "MARKET_REGIME" => "Fetching the JCI market regime",
            "MARKET_REGIME_FALLBACK" => "JCI unavailable; using UNKNOWN regime",
            "EVENTS" => "Updating market events",
            "ANALYZE" => "Analyzing candidates",
            "SAVING" => "Saving scan results and portfolio decisions",
            "COMPLETE" => "Process completed",
            "ERROR" => progress.Message.StartsWith("Scan gagal:",
                StringComparison.OrdinalIgnoreCase)
                ? "Scan failed: " + progress.Message["Scan gagal:".Length..].Trim()
                : "The process failed",
            _ => Loc.T(progress.Message)
        };
    }

    static string LocalizeSource(string source) =>
        source.ToLowerInvariant() switch
        {
            "cache lokal" => "local cache",
            "kalender sesi" => "session calendar",
            "scheduler lokal" => "local scheduler",
            "fallback waktu sesi" => "session-time fallback",
            _ => source
        };

    static string ResearchStatusText(string status) => status switch
    {
        "RULE_BASED_TUNED_PARAMETERS" => Loc.T(
            "rule-based • parameter hasil riset",
            "rule-based • research-tuned parameters"),
        "READY_FOR_FORWARD_TEST" => Loc.T(
            "siap shadow forward test",
            "ready for shadow forward test"),
        _ => Loc.T(
            "rule-based • belum tervalidasi",
            "rule-based • unvalidated")
    };

    sealed record TechnicalEntry(string Stage, string Text);
}
