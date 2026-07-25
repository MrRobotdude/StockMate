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
    readonly VerticalStackLayout _results = new() { Spacing=10 };
    readonly Label _status = UiKit.Sub("Pilih scan siang atau malam.");
    readonly Label _phase = new() { Text = "Belum berjalan", TextColor = Colors.White, FontAttributes = FontAttributes.Bold, FontSize = 16 };
    readonly Label _current = UiKit.Sub("Tahap dan saham yang diproses akan tampil di sini.");
    readonly Label _counts = UiKit.Sub("0/0 saham • berhasil 0 • gagal 0");
    readonly Label _batch = UiKit.Sub("Batch belum dimulai.");
    readonly Label _percent = new() { Text = "0%", TextColor = UiKit.Blue, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.End };
    readonly ProgressBar _progress = new() { Progress = 0, IsVisible = true, ProgressColor = UiKit.Blue };
    readonly Button _stop = new()
    {
        Text = "Hentikan scan", BackgroundColor = UiKit.Red, TextColor = Colors.White,
        CornerRadius = 14, HeightRequest = 48, IsVisible = false
    };
    readonly VerticalStackLayout _technicalLog = new() { Spacing = 5, IsVisible = false };
    readonly VerticalStackLayout _technicalItems = new() { Spacing = 5 };
    readonly List<TechnicalEntry> _technicalEntries = [];
    readonly Picker _technicalFilter = new() { Title = "Semua proses" };
    readonly Label _technicalPageInfo = UiKit.Sub("");
    readonly Button _technicalPrevious = UiKit.Secondary("← " + Loc.T("Sebelumnya", "Previous"));
    readonly Button _technicalNext = UiKit.Secondary(Loc.T("Berikutnya", "Next") + " →");
    readonly Grid _technicalPager;
    int _technicalPage = 1;
    const int TechnicalPageSize = 10;
    readonly SearchBar _search = new() { Placeholder = "Cari kode saham…" };
    readonly Picker _verdictFilter = new() { Title = "Semua rekomendasi" };
    readonly Picker _sort = new() { Title = "Urutkan" };
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
        PortfolioDecisionService decisions)
    {
        _data=data; _engine=engine; _decisions=decisions;
        Title="Scanner"; BackgroundColor=UiKit.Navy;
        _technicalPager = UiKit.Pager(_technicalPrevious, _technicalPageInfo, _technicalNext);
        _verdictFilter.ItemsSource = new[] { "Shortlist rekomendasi", "Semua hasil analisis", "BUY AREA", "WATCH", "WAIT", "LOT 0", "Bisa dieksekusi", "Spekulatif", "Non-spekulatif" };
        _verdictFilter.SelectedIndex = 0;
        _technicalFilter.ItemsSource = new[] { "Semua proses", "Berhasil", "Gagal / retry", "Universe", "Download", "Analisis" };
        _technicalFilter.SelectedIndex = 0;
        _sort.ItemsSource = new[] { "Prioritas rekomendasi", "Risk/reward terbaik", "Potensi kenaikan target", "Risiko harga terkecil", "Lot rekomendasi terbesar", "Harga termurah", "Kode A–Z" };
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
            _status.Text = "Menghentikan scanner dengan aman…";
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
            "Tahap 1 mengambil data pasar dari internet dan menyimpannya sebagai snapshot. Tahap 2 menjalankan strategi aktif—termasuk hasil training terbaru—tanpa mengunduh ulang data.",
            "Step 1 downloads market data and stores a snapshot. Step 2 runs the active strategy—including the latest trained strategy—without downloading the data again."));
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
                ? "Sembunyikan proses teknis"
                : "Lihat proses teknis";
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
        var evaluation = UiKit.Secondary(Loc.T("Evaluasi prediksi vs realisasi", "Prediction vs actual"));
        evaluation.Clicked += async (_, _) => await ShowEvaluationAsync();
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
            ScanServiceBridge.ProgressChanged -= OnProgress;
            ScanServiceBridge.ProgressChanged += OnProgress;
            if (ScanServiceBridge.IsRunning ||
                ScanServiceBridge.CurrentProgress.Stage is "COMPLETE" or "ERROR")
                OnProgress(ScanServiceBridge.CurrentProgress);
            ShowPerformance();
            UpdateClosingInfo();
            await _decisions.RebuildAsync();
            Render(_data.State.LastScan);
        };
        Disappearing += (_,_) => ScanServiceBridge.ProgressChanged -= OnProgress;
    }

    void OnProgress(ScanProgress progress)
    {
        _phase.Text = StageName(progress.Stage);
        _current.Text = string.IsNullOrWhiteSpace(progress.CurrentSymbol)
            ? progress.Message
            : $"{progress.Message} • sekarang: {progress.CurrentSymbol}";
        _counts.Text = progress.Total <= 0
            ? "Menyiapkan jumlah pekerjaan…"
            : $"{progress.Completed}/{progress.Total} saham • berhasil {progress.Succeeded} • gagal {progress.Failed}";
        _batch.Text = progress.TotalBatches <= 0
            ? "Batch akan dibentuk setelah universe siap."
            : $"Batch {progress.CurrentBatch}/{progress.TotalBatches} • {progress.BatchCompleted}/{progress.BatchSize} saham" +
              (progress.LastCompletedBatch > 0
                  ? $" • batch {progress.LastCompletedBatch} sudah selesai"
                  : "");
        _percent.Text = progress.Total <= 0 ? "…" : $"{progress.Percent}%";
        _progress.Progress = progress.Total <= 0 ? 0 : progress.Completed / (double)progress.Total;
        AddTechnicalLog(progress);
        if (progress.Stage is "COMPLETE" or "ERROR")
        {
            _status.Text = progress.Message;
            _stop.IsVisible = false;
            _stop.IsEnabled = true;
            Render(_data.State.LastScan);
        }
        else if (ScanServiceBridge.IsRunning)
        {
            _stop.IsVisible = true;
            _stop.IsEnabled = true;
        }
    }

    void AddTechnicalLog(ScanProgress progress)
    {
        var detail = string.IsNullOrWhiteSpace(progress.TechnicalDetail)
            ? progress.Message
            : progress.TechnicalDetail;
        var source = string.IsNullOrWhiteSpace(progress.Source)
            ? ""
            : $" • sumber {progress.Source}";
        var attempt = progress.Attempt > 0 ? $" • attempt {progress.Attempt}" : "";
        var elapsed = progress.ElapsedMilliseconds > 0
            ? $" • {progress.ElapsedMilliseconds / 1000d:0.0}s"
            : "";
        var line = $"{progress.OccurredAt:HH:mm:ss} [{progress.Stage}] {detail}{source}{attempt}{elapsed}";
        if (_technicalEntries.LastOrDefault()?.Text == line) return;
        _technicalEntries.Add(new TechnicalEntry(progress.Stage, line));
        if (_technicalEntries.Count > 200) _technicalEntries.RemoveAt(0);
        RenderTechnicalLog();
    }

    void RenderTechnicalLog()
    {
        IEnumerable<TechnicalEntry> query = _technicalEntries;
        query = (_technicalFilter.SelectedItem?.ToString() ?? "Semua proses") switch
        {
            "Berhasil" => query.Where(x => x.Stage is "REQUEST_OK" or "BATCH_COMPLETE" or "COMPLETE"),
            "Gagal / retry" => query.Where(x => x.Stage is "REQUEST_ERROR" or "RETRY" or "RATE_LIMIT" or "FORBIDDEN_RETRY" or "ERROR"),
            "Universe" => query.Where(x => x.Stage.Contains("UNIVERSE", StringComparison.OrdinalIgnoreCase)),
            "Download" => query.Where(x => x.Stage is "DOWNLOAD" or "REQUEST_START" or "REQUEST_OK" or "BATCH_START" or "BATCH_COMPLETE"),
            "Analisis" => query.Where(x => x.Stage is "ANALYZE" or "SAVING"),
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
        _technicalPageInfo.Text = $"Halaman {_technicalPage}/{pages} • {filtered.Count} log";
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
                Message = "Menyalakan layanan scanner. Layar boleh dimatikan."
            });
            _status.Text = "Progres juga tampil pada notifikasi Android.";
            await ScanServiceBridge.StartAsync(intraday, forceRefresh);
            var results = _data.State.LastScan;
            var snapshot = _engine.GetSnapshot(intraday);
            _status.Text = snapshot is null ? "Snapshot belum tersedia."
                : $"Selesai • {snapshot.Status} • {snapshot.Symbols.Count}/{snapshot.RequestedCount} saham • {results.Count} kandidat";
            ShowPerformance();
            Render(results);
        }
        catch(OperationCanceledException) { _status.Text="Scan dibatalkan."; }
        catch(Exception ex) { _status.Text=$"Scan gagal: {ex.Message}"; }
        finally { _cts.Dispose(); _cts=null; }
    }

    async Task DownloadAsync(bool intraday, bool forceRefresh)
    {
        if (_cts is not null) return;
        _cts = new();
        SetScannerActionsEnabled(false);
        try
        {
            _status.Text = Loc.T("Mengambil data pasar…", "Fetching market data…");
            var progress = new Progress<ScanProgress>(OnProgress);
            var snapshot = await Task.Run(() => _engine.RefreshMarketDataAsync(
                intraday, forceRefresh, progress, _cts.Token));
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
            var results = await Task.Run(() => _engine.AnalyzeAsync(
                snapshot.Session == "LUNCH", snapshot, true, progress, _cts.Token));
            await _decisions.RebuildAsync();
            Render(results);
            _status.Text = Loc.T(
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
        "PREPARING" => "1/5 • Menyiapkan scanner",
        "UNIVERSE" => "2/5 • Memperbarui daftar saham IDX",
        "UNIVERSE_REQUEST" => "2/5 • Menghubungi IDX",
        "UNIVERSE_PARSE" => "2/5 • Membaca respons IDX",
        "UNIVERSE_READY" => "2/5 • Universe siap",
        "UNIVERSE_FALLBACK" => "2/5 • Memakai cache universe",
        "TRADING_DATE" => "2/5 • Menentukan closing acuan",
        "BATCH_PLAN" => "3/5 • Membentuk batch",
        "REQUEST_START" => "3/5 • Mengirim request harga",
        "REQUEST_OK" => "3/5 • Data harga diterima",
        "REQUEST_ERROR" => "3/5 • Request gagal, lanjut",
        "RETRY" => "3/5 • Mencoba ulang request",
        "CLOSING_FALLBACK" => "3/5 • Melanjutkan dengan data tersedia",
        "WAITING_CLOSE" => "3/5 • Memastikan data closing tersedia",
        "RATE_LIMIT" => "3/5 • Menunggu batas request",
        "FORBIDDEN_RETRY" => "3/5 • Akses 403, mengganti endpoint",
        "DOWNLOAD" => "3/5 • Mengambil data harga",
        "BATCH_START" => "3/5 • Memulai batch pengambilan",
        "BATCH_COMPLETE" => "3/5 • Batch selesai, lanjut berikutnya",
        "ANALYZE" => "4/5 • Menganalisis seluruh saham",
        "SAVING" => "5/5 • Menyimpan hasil",
        "COMPLETE" => "Selesai",
        "ERROR" => "Proses gagal",
        _ => stage
    };

    void Render(IEnumerable<ScanResult> results)
    {
        _results.Children.Clear();
        IEnumerable<ScanResult> query = results;
        if (!string.IsNullOrWhiteSpace(_search.Text))
            query = query.Where(x => x.Symbol.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        var filter = _verdictFilter.SelectedItem?.ToString() ?? "Shortlist rekomendasi";
        if (filter != "Semua hasil analisis")
            query = filter switch
            {
            "Shortlist rekomendasi" => query.Where(x =>
                x.AllocationRank > 0 && x.SuggestedLots > 0 && x.Verdict == "BUY AREA"),
                "LOT 0" => query.Where(x => x.SuggestedLots <= 0),
                "Bisa dieksekusi" => query.Where(x => x.SuggestedLots > 0),
                "Spekulatif" => query.Where(x => x.IsSpeculative),
                "Non-spekulatif" => query.Where(x => !x.IsSpeculative),
                _ => query.Where(x => x.Verdict.Contains(filter, StringComparison.OrdinalIgnoreCase))
            };
        query = _sort.SelectedIndex switch
        {
            1 => query.OrderByDescending(x => x.RiskReward).ThenByDescending(x => x.Score),
            2 => query.OrderByDescending(x => x.LastPrice <= 0 ? 0 : (x.Target1 - x.LastPrice) / x.LastPrice),
            3 => query.OrderBy(x => x.LastPrice <= 0 ? decimal.MaxValue : (x.LastPrice - x.StopLoss) / x.LastPrice),
            4 => query.OrderByDescending(x => x.SuggestedLots).ThenByDescending(x => x.Score),
            5 => query.OrderBy(x => x.LastPrice).ThenByDescending(x => x.Score),
            6 => query.OrderBy(x => x.Symbol),
            _ => query.OrderByDescending(x => VerdictPriority(x.Verdict))
                      .ThenByDescending(x => x.Score)
        };
        var filtered = query.ToList();
        var all = results.ToList();
        var universe = _data.State.ScanHistory.LastOrDefault()?.UniverseCount
            ?? _engine.GetLatestSnapshot()?.RequestedCount ?? 0;
        var shortlist = all.Count(x => x.AllocationRank > 0);
        var buySignals = all.Count(x => x.Verdict == "BUY AREA");
        _resultSummary.Text =
            $"{universe:N0} universe • {all.Count:N0} berhasil dianalisis • {buySignals:N0} sinyal BUY • {shortlist:N0} masuk alokasi kas Rp{_data.State.Cash:N0}.";
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        _page = Math.Clamp(_page, 1, totalPages);
        var items = filtered.Skip((_page - 1) * PageSize).Take(PageSize).ToList();
        _pageInfo.Text = $"Halaman {_page}/{totalPages} • {filtered.Count} hasil";
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
                new Label { Text=$"Entry {r.EntryLow:N0}–{r.EntryHigh:N0}  •  maksimal {r.MaxBuyPrice:N0}", TextColor=Colors.White },
                new Label { Text=$"Target {r.Target1:N0} / {r.Target2:N0}  •  Stop {r.StopLoss:N0}", TextColor=UiKit.Muted },
                new Label
                {
                    Text = isOwned && portfolioDecision is not null
                        ? $"Tindakan posisi: {portfolioDecision.Action} • keyakinan {portfolioDecision.Confidence}"
                        : r.SuggestedLots > 0
                        ? $"Rencana: {r.SuggestedLots} lot @ {r.EntryLow:N0}–{r.EntryHigh:N0}"
                        : "Tidak dapat dieksekusi: ukuran aman 0 lot",
                    TextColor = isOwned
                        ? ActionColor(portfolioDecision?.Action)
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
                    Text = r.ExecutionNote,
                    IsVisible = !string.IsNullOrWhiteSpace(r.ExecutionNote),
                    TextColor = r.AllocationRank > 0 ? UiKit.Blue : UiKit.Muted,
                },
                new Label
                {
                    Text=$"{r.DataSession} • tanggal data {r.DataTime:dd MMM yyyy} • bukan jam transaksi real-time",
                    TextColor=UiKit.Muted
                }
            }};
            var detail=UiKit.Tertiary(Loc.T("Buka detail saham", "Open stock details"));
            detail.Clicked += async (_,_) => await Navigation.PushAsync(
                new PositionDetailPage(_data, _decisions, r.Symbol));
            stack.Children.Add(detail);
            _results.Children.Add(UiKit.ExpandableCard(
                isOwned && portfolioDecision is not null
                    ? $"{r.Symbol} · {portfolioDecision.Action}"
                    : $"{r.Symbol} · {r.Verdict}",
                isOwned && portfolioDecision is not null
                    ? Loc.T(
                        $"Posisi {position!.Lots} lot · keyakinan {portfolioDecision.Confidence}",
                        $"Position {position!.Lots} lots · {portfolioDecision.Confidence} confidence")
                    : r.SuggestedLots > 0
                    ? Loc.T(
                        $"Beli ideal Rp {r.EntryLow:N0}–{r.EntryHigh:N0} · {r.SuggestedLots} lot",
                        $"Ideal buy Rp {r.EntryLow:N0}–{r.EntryHigh:N0} · {r.SuggestedLots} lots")
                    : Loc.T(
                        $"Pantau area Rp {r.EntryLow:N0}–{r.EntryHigh:N0}",
                        $"Watch Rp {r.EntryLow:N0}–{r.EntryHigh:N0}"),
                stack,
                isOwned && portfolioDecision is not null
                    ? portfolioDecision.Confidence
                    : r.AllocationRank > 0 ? $"#{r.AllocationRank}" : Loc.T("PANTAU", "WATCH"),
                isOwned && portfolioDecision is not null
                    ? ActionColor(portfolioDecision.Action)
                    : r.AllocationRank > 0 ? UiKit.Green : UiKit.Blue));
        }
    }

    static Color ActionColor(string? action) =>
        action?.Contains("SELL", StringComparison.OrdinalIgnoreCase) == true ||
        action?.Contains("REDUCE", StringComparison.OrdinalIgnoreCase) == true
            ? UiKit.Red
            : action?.Contains("ADD", StringComparison.OrdinalIgnoreCase) == true ||
              action?.Contains("AVERAGE", StringComparison.OrdinalIgnoreCase) == true
                ? UiKit.Green
                : UiKit.Blue;

    static int VerdictPriority(string verdict) => verdict.Contains("BUY") ? 3 :
        verdict.Contains("WATCH") ? 2 : verdict.Contains("WAIT") ? 1 : 0;

    void ShowPerformance()
    {
        var evaluated = _data.State.ScanHistory.SelectMany(x => x.Predictions)
            .Where(x => x.ReturnPercent.HasValue).ToList();
        if (evaluated.Count == 0)
        {
            _status.Text = _data.State.ScanHistory.Count == 0
                ? "Belum ada histori. Scan pertama akan disimpan sebagai baseline evaluasi."
                : "Prediksi tersimpan dan menunggu scan berikutnya untuk dievaluasi.";
            return;
        }
        var positive = evaluated.Count(x => x.ReturnPercent > 0);
        var avg = evaluated.Average(x => x.ReturnPercent!.Value);
        _status.Text = $"Evaluasi {evaluated.Count} prediksi • positif {(decimal)positive / evaluated.Count:P0} • rata-rata {avg:+0.00;-0.00;0.00}%";
    }

    async Task ShowEvaluationAsync()
    {
        var rows = _data.State.ScanHistory
            .SelectMany(run => run.Predictions.Select(p => new { Run = run, Prediction = p }))
            .OrderByDescending(x => x.Prediction.PredictedAt == default ? x.Run.RunTime : x.Prediction.PredictedAt)
            .ToList();
        if (rows.Count == 0)
        {
            await AppDialog.ShowAsync(this, "Belum ada prediksi",
                "Hanya shortlist rekomendasi nyata yang dicatat. Jalankan analisis untuk membuat baseline.");
            return;
        }
        var evaluated = rows.Where(x => x.Prediction.ReturnPercent.HasValue).ToList();
        var hitRate = evaluated.Count == 0 ? 0m :
            evaluated.Count(x => x.Prediction.ReturnPercent > 0) * 100m / evaluated.Count;
        var average = evaluated.Count == 0 ? 0m :
            evaluated.Average(x => x.Prediction.ReturnPercent!.Value);
        var details = string.Join("\n\n", rows.Take(15).Select(x =>
        {
            var p = x.Prediction;
            var actual = p.EvaluationPrice.HasValue
                ? $"aktual Rp{p.EvaluationPrice:N0} • {p.ReturnPercent:+0.00;-0.00;0.00}% • {p.Outcome}"
                : "aktual: menunggu snapshot berikutnya";
            return $"{p.Symbol} • {p.DataSession} • {p.PredictedAt:dd MMM HH:mm}\n" +
                   $"prediksi Rp{p.StartPrice:N0} → target Rp{p.Target1:N0} • stop Rp{p.StopLoss:N0}\n{actual}";
        }));
        var training = _data.State.Strategy.Training;
        var strategyText = training is null
            ? "Strategi aktif belum berasal dari trainer tervalidasi. Aplikasi mengevaluasi hasil, tetapi tidak mengubah bobot sendiri."
            : $"Strategi v{_data.State.Strategy.Version} • walk-forward {training.OutOfSampleFolds} fold / {training.OutOfSampleTrades} trade OOS.";
        await AppDialog.ShowAsync(this, "Evaluasi prediksi",
            $"{strategyText}\n\nDievaluasi {evaluated.Count}/{rows.Count} • positif {hitRate:0.0}% • return rata-rata {average:+0.00;-0.00;0.00}%\n\n15 prediksi terakhir:\n\n{details}");
    }

    void UpdateClosingInfo()
    {
        var intraday = _engine.UseLatestClosing();
        var snapshot = _engine.GetSnapshot(intraday);
        var label = intraday ? "Closing Sesi 1" : "Closing Sesi 2 / hari bursa terakhir";
        _closingInfo.Text = snapshot is null
            ? $"Otomatis memakai {label} yang terakhir tersedia."
            : $"Closing otomatis: {(snapshot.Session == "LUNCH" ? "Sesi 1" : "Sesi 2")} • {snapshot.TradingDate:dd MMM yyyy} • data dipilih berdasarkan waktu scan.";
    }

    sealed record TechnicalEntry(string Stage, string Text);
}
