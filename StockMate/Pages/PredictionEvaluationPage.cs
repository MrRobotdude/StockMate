using StockMate.Models;
using StockMate.Services;
using StockMate.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace StockMate.Pages;

public sealed class PredictionEvaluationPage : ContentPage
{
    readonly AppDataService _data;
    readonly ScanEngine _engine;
    readonly EvaluationExportService _exporter;
    readonly SearchBar _search = new()
    {
        Placeholder = Loc.T(
            "Cari kode saham, misalnya HOPE…",
            "Search a stock symbol, for example HOPE…")
    };
    readonly Picker _statusFilter = new()
    {
        Title = Loc.T("Semua status", "All statuses")
    };
    readonly VerticalStackLayout _summary = new() { Spacing = 10 };
    readonly VerticalStackLayout _items = new() { Spacing = 12 };
    readonly Label _resultInfo = UiKit.Sub("");
    readonly Label _pageInfo = UiKit.Sub("");
    readonly Button _previous =
        UiKit.Secondary("← " + Loc.T("Sebelumnya", "Previous"));
    readonly Button _next =
        UiKit.Secondary(Loc.T("Berikutnya", "Next") + " →");
    int _page = 1;
    const int PageSize = 8;
    long _rowsRevision = -1;
    long _summaryRevision = -1;
    List<EvaluationRow> _cachedRows = [];
    CancellationTokenSource? _searchDebounce;

    public PredictionEvaluationPage(
        AppDataService data, ScanEngine engine)
    {
        _data = data;
        _engine = engine;
        _exporter = App.Services.GetRequiredService<EvaluationExportService>();
        Title = Loc.T("Evaluasi", "Evaluation");
        BackgroundColor = UiKit.Navy;

        _statusFilter.ItemsSource = new[]
        {
            Loc.T("Semua status", "All statuses"),
            Loc.T("Realisasi T+1 tersedia", "T+1 actual available"),
            Loc.T("Menunggu T+1", "Waiting for T+1"),
            Loc.T("Order terisi & berjalan", "Filled & open"),
            Loc.T("Swing selesai", "Swing completed"),
            Loc.T("Tidak dieksekusi", "Not executed"),
            Loc.T("T+1 positif", "Positive T+1"),
            Loc.T("T+1 negatif", "Negative T+1")
        };
        _statusFilter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => DebounceSearch();
        _statusFilter.SelectedIndexChanged += (_, _) =>
        {
            _page = 1;
            Render();
        };
        _previous.Clicked += (_, _) =>
        {
            if (_page <= 1) return;
            _page--;
            Render();
        };
        _next.Clicked += (_, _) =>
        {
            if (!_next.IsEnabled) return;
            _page++;
            Render();
        };

        var filters = new Grid
        {
            ColumnDefinitions =
            [
                new(GridLength.Star),
                new(GridLength.Star)
            ],
            ColumnSpacing = 8
        };
        filters.Add(_search, 0);
        filters.Add(_statusFilter, 1);

        var root = UiKit.PageStack();
        root.Children.Add(UiKit.Heading(
            this,
            "Evaluasi prediksi",
            "Prediction evaluation",
            "Setiap rekomendasi setelah closing dipasangkan dengan candle hari bursa berikutnya. Hasil T+1 tidak menunggu target atau stop swing tercapai, dan rekomendasi berulang untuk saham yang sama tetap dicatat sebagai kejadian terpisah.",
            "Each post-close recommendation is paired with the next trading day's candle. T+1 results do not wait for the swing target or stop, and repeated recommendations for the same stock remain separate observations."));
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                UiKit.SectionTitle(Loc.T(
                    "Cara membaca halaman ini",
                    "How to read this page")),
                UiKit.Sub(Loc.T(
                    "SEBELUM adalah data yang tersedia saat keputusan dibuat. PREDIKSI adalah order untuk hari bursa berikutnya. SESUDAH adalah realisasi T+1. HASIL SWING adalah status lanjutan sampai target, stop, atau batas waktu.",
                    "BEFORE is the data available when the decision was made. PREDICTION is the order for the next trading day. AFTER is the T+1 actual. SWING RESULT continues until target, stop, or the time limit."))
            }
        }));
        root.Children.Add(_summary);
        var export = UiKit.Secondary(Loc.T(
            "Ekspor data evaluasi CSV",
            "Export evaluation data CSV"));
        export.Clicked += async (_, _) => await ExportAsync();
        root.Children.Add(export);
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { _resultInfo, filters }
        }));
        root.Children.Add(_items);
        root.Children.Add(UiKit.Pager(
            _previous, _pageInfo, _next));
        Content = new ScrollView { Content = root };

        Appearing += async (_, _) =>
        {
            try
            {
                // A completed download is enough to resolve T+1. Requiring the
                // user to run analysis again was the reason new market data
                // could be visible while evaluation still looked stale.
                await _engine.UpdatePredictionHistoryAsync();
                Render();
            }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync(
                    this,
                    Loc.T("Evaluasi gagal", "Evaluation failed"),
                    Loc.T(
                        $"Riwayat tidak dapat diperbarui: {ex.Message}",
                        $"The history could not be updated: {ex.Message}"),
                    danger: true);
                Render();
            }
        };
        Disappearing += (_, _) =>
        {
            _searchDebounce?.Cancel();
            _searchDebounce?.Dispose();
            _searchDebounce = null;
        };
    }

    async void DebounceSearch()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;
        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;
            _page = 1;
            Render();
        }
        catch (System.OperationCanceledException) { }
    }

    async Task ExportAsync()
    {
        try
        {
            var result = await _exporter.ExportAsync();
            if (result.Rows == 0)
            {
                await AppDialog.ShowAsync(
                    this,
                    Loc.T("Belum ada data", "No data yet"),
                    Loc.T(
                        "Belum ada rekomendasi yang dapat diekspor.",
                        "There are no recommendations to export yet."));
                return;
            }
            await Share.Default.RequestAsync(new ShareFileRequest(
                Loc.T(
                    $"Data evaluasi StockMate • {result.Rows} baris",
                    $"StockMate evaluation data • {result.Rows} rows"),
                new ShareFile(result.Path)));
        }
        catch (Exception ex)
        {
            await AppDialog.ShowAsync(
                this,
                Loc.T("Ekspor gagal", "Export failed"),
                Loc.T(
                    $"Data evaluasi tidak dapat diekspor: {ex.Message}",
                    $"Evaluation data could not be exported: {ex.Message}"),
                danger: true);
        }
    }

    void Render()
    {
        var allRows = Rows();
        if (_summaryRevision != _data.Revision)
        {
            RenderSummary(allRows);
            _summaryRevision = _data.Revision;
        }

        IEnumerable<EvaluationRow> filtered = allRows;
        var term = _search.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            filtered = filtered.Where(x =>
                x.Prediction.Symbol.Contains(
                    term, StringComparison.OrdinalIgnoreCase));
        filtered = _statusFilter.SelectedIndex switch
        {
            1 => filtered.Where(x =>
                x.Prediction.NextTradingDate.HasValue),
            2 => filtered.Where(x =>
                !x.Prediction.NextTradingDate.HasValue &&
                x.Prediction.Outcome != "CANCELLED"),
            3 => filtered.Where(x =>
                x.Prediction.EntryFilledAt.HasValue &&
                x.Prediction.Outcome == "PENDING"),
            4 => filtered.Where(x =>
                IsFinal(x.Prediction.Outcome)),
            5 => filtered.Where(x =>
                x.Prediction.Outcome is "NOT_FILLED" or "CANCELLED"),
            6 => filtered.Where(x =>
                x.Prediction.NextDayReturnPercent > 0),
            7 => filtered.Where(x =>
                x.Prediction.NextDayReturnPercent < 0),
            _ => filtered
        };

        var rows = filtered.ToList();
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(rows.Count / (double)PageSize));
        _page = Math.Clamp(_page, 1, totalPages);
        var visible = rows
            .Skip((_page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        _resultInfo.Text = Loc.T(
            $"{rows.Count} rekomendasi cocok dengan filter. Rekomendasi berulang untuk kode yang sama ditampilkan satu per satu berdasarkan tanggal sinyal.",
            $"{rows.Count} recommendations match the filter. Repeated recommendations for the same symbol are shown separately by signal date.");
        _pageInfo.Text = Loc.T(
            $"Halaman {_page}/{totalPages} • {rows.Count} rekomendasi",
            $"Page {_page}/{totalPages} • {rows.Count} recommendations");
        _previous.IsEnabled = _page > 1;
        _next.IsEnabled = _page < totalPages;
        _items.Children.Clear();
        if (visible.Count == 0)
        {
            _items.Children.Add(UiKit.EmptyState(
                "◇",
                Loc.T("Belum ada evaluasi", "No evaluations yet"),
                allRows.Count == 0
                    ? Loc.T(
                        "Jalankan analisis closing untuk menyimpan rekomendasi pertama.",
                        "Run a closing analysis to save the first recommendation.")
                    : Loc.T(
                        "Ubah pencarian atau filter status.",
                        "Change the search or status filter.")));
            return;
        }

        foreach (var row in visible)
            _items.Children.Add(BuildCard(row));
    }

    List<EvaluationRow> Rows()
    {
        if (_rowsRevision == _data.Revision)
            return _cachedRows;
        _cachedRows = _data.State.ScanHistory
            .SelectMany(run => (run.Predictions ?? [])
                .Select(prediction =>
                    new EvaluationRow(run, prediction)))
            .OrderByDescending(x => SignalDate(x.Prediction))
            .ThenByDescending(x => x.Prediction.PredictedAt)
            .ThenBy(x => x.Prediction.Symbol)
            .ToList();
        _rowsRevision = _data.Revision;
        return _cachedRows;
    }

    void RenderSummary(IReadOnlyCollection<EvaluationRow> rows)
    {
        _summary.Children.Clear();
        if (rows.Count == 0)
        {
            _summary.Children.Add(UiKit.EmptyState(
                "◎",
                Loc.T("Belum ada prediksi", "No predictions yet"),
                Loc.T(
                    "Halaman ini akan terisi setelah shortlist BUY pertama disimpan.",
                    "This page will populate after the first BUY shortlist is saved.")));
            return;
        }

        var observed = rows
            .Where(x => x.Prediction.NextTradingDate.HasValue)
            .ToList();
        var filled = observed
            .Where(x => x.Prediction.EntryFilledAt.HasValue &&
                        x.Prediction.NextDayReturnPercent.HasValue)
            .ToList();
        var positive = filled.Count(x =>
            x.Prediction.NextDayReturnPercent > 0);
        var hitRate = filled.Count == 0
            ? 0
            : positive * 100m / filled.Count;
        var average = filled.Count == 0
            ? 0
            : filled.Average(x =>
                x.Prediction.NextDayReturnPercent!.Value);
        var waiting = rows.Count(x =>
            !x.Prediction.NextTradingDate.HasValue &&
            x.Prediction.Outcome != "CANCELLED");
        var notExecuted = rows.Count(x =>
            x.Prediction.Outcome is "NOT_FILLED" or "CANCELLED");

        var metrics = new Grid
        {
            ColumnDefinitions =
            [
                new(GridLength.Star),
                new(GridLength.Star)
            ],
            RowDefinitions =
            [
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto)
            ],
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        metrics.Add(UiKit.Metric(
            Loc.T("TOTAL REKOMENDASI", "TOTAL RECOMMENDATIONS"),
            rows.Count.ToString("N0")), 0, 0);
        metrics.Add(UiKit.Metric(
            Loc.T("T+1 TERSEDIA", "T+1 AVAILABLE"),
            $"{observed.Count:N0}/{rows.Count:N0}",
            observed.Count == rows.Count
                ? UiKit.Green : UiKit.Blue), 1, 0);
        metrics.Add(UiKit.Metric(
            Loc.T("POSITIF T+1", "POSITIVE T+1"),
            filled.Count == 0 ? "—" : $"{hitRate:0.0}%",
            filled.Count == 0
                ? UiKit.Muted
                : hitRate >= 50 ? UiKit.Green : UiKit.Red), 0, 1);
        metrics.Add(UiKit.Metric(
            Loc.T("RATA-RATA T+1", "AVERAGE T+1"),
            filled.Count == 0
                ? "—"
                : $"{average:+0.00;-0.00;0.00}%",
            filled.Count == 0
                ? UiKit.Muted
                : average >= 0 ? UiKit.Green : UiKit.Red), 1, 1);
        metrics.Add(UiKit.Metric(
            Loc.T("MENUNGGU T+1", "WAITING FOR T+1"),
            waiting.ToString("N0"),
            waiting == 0 ? UiKit.Green : UiKit.Purple), 0, 2);
        metrics.Add(UiKit.Metric(
            Loc.T("TIDAK DIEKSEKUSI", "NOT EXECUTED"),
            notExecuted.ToString("N0"),
            UiKit.Muted), 1, 2);
        _summary.Children.Add(metrics);

        var training = _data.State.Strategy.Training;
        var strategyVersions = rows
            .Select(x => x.Run.StrategyVersion)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        _summary.Children.Add(UiKit.Box(UiKit.Sub(
            training is not
                { QualityGatePassed: true, Status: "READY_FOR_FORWARD_TEST" }
                ? Loc.T(
                    $"Mesin aktif masih rule-based dan belum merupakan model ML tervalidasi. Statistik di atas adalah forward observation aplikasi, bukan jaminan akurasi. Versi dalam histori: {string.Join(", ", strategyVersions)}.",
                    $"The active engine is still rule-based and is not a validated ML model. The statistics above are the app's forward observations, not a guarantee of accuracy. Versions in history: {string.Join(", ", strategyVersions)}.")
                : Loc.T(
                    $"Parameter rule-based aktif v{_data.State.Strategy.Version} • {training.OutOfSampleFolds} fold walk-forward • {training.OutOfSampleTrades} trade OOS. Bobot ML runtime belum dipakai. Versi dalam histori: {string.Join(", ", strategyVersions)}.",
                    $"Active rule-based parameters v{_data.State.Strategy.Version} • {training.OutOfSampleFolds} walk-forward folds • {training.OutOfSampleTrades} OOS trades. Runtime ML weights are not in use. Versions in history: {string.Join(", ", strategyVersions)}."))));
    }

    View BuildCard(EvaluationRow row)
    {
        var p = row.Prediction;
        var signalDate = SignalDate(p);
        var status = StatusText(p);
        var statusColor = StatusColor(p);
        var summary = p.NextTradingDate.HasValue
            ? p.NextDayReturnPercent.HasValue
                ? Loc.T(
                    $"{signalDate:dd MMM} → {p.NextTradingDate:dd MMM} · T+1 {p.NextDayReturnPercent:+0.00;-0.00;0.00}% net",
                    $"{signalDate:dd MMM} → {p.NextTradingDate:dd MMM} · T+1 {p.NextDayReturnPercent:+0.00;-0.00;0.00}% net")
                : Loc.T(
                    $"{signalDate:dd MMM} → {p.NextTradingDate:dd MMM} · {status}",
                    $"{signalDate:dd MMM} → {p.NextTradingDate:dd MMM} · {status}")
            : p.Outcome == "CANCELLED"
                ? Loc.T(
                    $"{signalDate:dd MMM} · dibatalkan sebelum eksekusi",
                    $"{signalDate:dd MMM} · cancelled before execution")
                : Loc.T(
                    $"{signalDate:dd MMM} → hari bursa berikutnya belum tersedia",
                    $"{signalDate:dd MMM} → next trading day unavailable");

        var detail = new VerticalStackLayout { Spacing = 14 };
        detail.Children.Add(BuildBeforeSection(row));
        detail.Children.Add(BuildPredictionSection(row));
        detail.Children.Add(BuildAfterSection(row));
        return UiKit.ExpandableCard(
            $"{p.Symbol} · {signalDate:dd MMM yyyy}",
            summary,
            detail,
            status,
            statusColor,
            initiallyExpanded: false);
    }

    View BuildBeforeSection(EvaluationRow row)
    {
        var p = row.Prediction;
        var signalDate = SignalDate(p);
        var reasons = Loc.English &&
                      !string.IsNullOrWhiteSpace(p.ReasonsEn)
            ? p.ReasonsEn
            : p.Reasons;
        var risks = Loc.English &&
                    !string.IsNullOrWhiteSpace(p.RisksEn)
            ? p.RisksEn
            : p.Risks;
        var eventSummary = Loc.English &&
                           !string.IsNullOrWhiteSpace(p.EventSummaryEn)
            ? p.EventSummaryEn
            : p.EventSummary;

        var content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                SectionLabel(
                    "1 · SEBELUM",
                    "1 · BEFORE",
                    UiKit.Blue),
                new Label
                {
                    Text = Loc.T(
                        $"SETUP UTAMA · {(string.IsNullOrWhiteSpace(p.PrimarySetup) ? "data versi lama" : p.PrimarySetup)}",
                        $"PRIMARY SETUP · {(!string.IsNullOrWhiteSpace(p.PrimarySetupEn) ? p.PrimarySetupEn : string.IsNullOrWhiteSpace(p.PrimarySetup) ? "older-version data" : p.PrimarySetup)}"),
                    TextColor = UiKit.Green,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 15
                },
                UiKit.Caption(Loc.T(
                    $"Status riset {ResearchStatusText(p.ResearchStatus)} • rezim {p.MarketRegime} • IHSG 20 hari {p.MarketReturn20Percent:+0.0;-0.0;0.0}% • breadth {p.MarketBreadth20Percent:0}%",
                    $"Research status {ResearchStatusText(p.ResearchStatus)} • {p.MarketRegime} regime • 20-day JCI {p.MarketReturn20Percent:+0.0;-0.0;0.0}% • {p.MarketBreadth20Percent:0}% breadth")),
                UiKit.Sub(Loc.T(
                    $"{Loc.Session(p.DataSession)} • {signalDate:dd MMM yyyy} • data yang tersedia ketika keputusan dibuat",
                    $"{Loc.Session(p.DataSession)} • {signalDate:dd MMM yyyy} • data available when the decision was made")),
                Metrics(
                    (
                        Loc.T("OPEN", "OPEN"),
                        PriceOrUnavailable(p.SignalOpen),
                        null
                    ),
                    (
                        Loc.T("HIGH", "HIGH"),
                        PriceOrUnavailable(p.SignalHigh),
                        null
                    ),
                    (
                        Loc.T("LOW", "LOW"),
                        PriceOrUnavailable(p.SignalLow),
                        null
                    ),
                    (
                        Loc.T("CLOSE", "CLOSE"),
                        PriceOrUnavailable(p.SignalClose),
                        Colors.White
                    ),
                    (
                        Loc.T("SKOR TEKNIKAL", "TECHNICAL SCORE"),
                        $"{(p.TechnicalScore > 0 ? p.TechnicalScore : p.Score)}/100",
                        UiKit.Blue
                    ),
                    (
                        Loc.T("PENYESUAIAN ISU", "EVENT ADJUSTMENT"),
                        $"{p.EventAdjustment:+#;-#;0}",
                        p.EventAdjustment < 0
                            ? UiKit.Red
                            : p.EventAdjustment > 0
                                ? UiKit.Green
                                : UiKit.Muted
                    )),
                SmallHeading(
                    "Bukti yang mendukung",
                    "Supporting evidence"),
                UiKit.Sub(string.IsNullOrWhiteSpace(reasons)
                    ? Loc.T(
                        "Detail bukti tidak tersimpan pada prediksi versi lama.",
                        "Evidence details were not stored by the older prediction version.")
                    : reasons),
                SmallHeading(
                    "Data yang menentang / risiko",
                    "Contrary evidence / risks"),
                UiKit.Sub(string.IsNullOrWhiteSpace(risks)
                    ? Loc.T(
                        "Detail risiko tidak tersimpan pada prediksi versi lama.",
                        "Risk details were not stored by the older prediction version.")
                    : risks),
                SmallHeading(
                    "Isu yang diketahui saat itu",
                    "Events known at the time"),
                UiKit.Sub(string.IsNullOrWhiteSpace(eventSummary)
                    ? Loc.T(
                        "Tidak ada ringkasan isu yang dibekukan pada prediksi ini.",
                        "No event summary was frozen with this prediction.")
                    : eventSummary)
            }
        };
        return SectionSurface(content);
    }

    View BuildPredictionSection(EvaluationRow row)
    {
        var p = row.Prediction;
        var signalDate = SignalDate(p);
        var score = p.Score > 0
            ? p.Score
            : p.TechnicalScore;
        var content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                SectionLabel(
                    "2 · PREDIKSI HARI BURSA BERIKUTNYA",
                    "2 · NEXT-TRADING-DAY PREDICTION",
                    UiKit.Purple),
                new Label
                {
                    Text = p.SuggestedLots > 0
                        ? Loc.T(
                            $"BUY LIMIT {p.Symbol} • tepat {p.SuggestedLots} lot @ Rp{p.StartPrice:N0}",
                            $"BUY LIMIT {p.Symbol} • exactly {p.SuggestedLots} lots @ Rp{p.StartPrice:N0}")
                        : Loc.T(
                            $"BUY LIMIT {p.Symbol} @ Rp{p.StartPrice:N0} • jumlah lot tidak tersimpan pada prediksi lama",
                            $"BUY LIMIT {p.Symbol} @ Rp{p.StartPrice:N0} • lot count was not stored by the older prediction"),
                    TextColor = Colors.White,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold
                },
                UiKit.Sub(Loc.T(
                    $"Dibuat {p.PredictedAt:dd MMM yyyy HH:mm} setelah data {signalDate:dd MMM}. Berlaku Good for Day pada hari bursa berikutnya; batalkan bila opening di atas Rp{p.StartPrice:N0} dan jangan menaikkan limit.",
                    $"Created at {p.PredictedAt:dd MMM yyyy HH:mm} from {signalDate:dd MMM} data. Valid Good for Day on the next trading day; cancel if the open is above Rp{p.StartPrice:N0} and do not raise the limit.")),
                Metrics(
                    (
                        Loc.T("LIMIT MAKSIMUM", "MAXIMUM LIMIT"),
                        $"Rp{p.StartPrice:N0}",
                        UiKit.Blue
                    ),
                    (
                        Loc.T("JUMLAH", "QUANTITY"),
                        p.SuggestedLots > 0
                            ? Loc.Lots(p.SuggestedLots)
                            : "—",
                        Colors.White
                    ),
                    (
                        Loc.T("STOP", "STOP"),
                        $"Rp{p.StopLoss:N0}",
                        UiKit.Red
                    ),
                    (
                        Loc.T("TARGET", "TARGET"),
                        $"Rp{p.Target1:N0}",
                        UiKit.Green
                    ),
                    (
                        Loc.T("RISK/REWARD NET", "NET RISK/REWARD"),
                        p.RiskReward > 0
                            ? $"{p.RiskReward:0.00}×"
                            : "—",
                        UiKit.Blue
                    ),
                    (
                        Loc.T("SKOR GABUNGAN", "COMBINED SCORE"),
                        $"{score}/100",
                        score >= _data.State.Strategy.BuyScore
                            ? UiKit.Green : UiKit.Muted
                    )),
                UiKit.Caption(Loc.T(
                    $"Strategi v{row.Run.StrategyVersion} • batas holding simulasi {Math.Max(1, p.MaximumHoldingDays)} hari bursa. Hasil T+1 tetap dicatat lebih dulu.",
                    $"Strategy v{row.Run.StrategyVersion} • simulation holding limit {Math.Max(1, p.MaximumHoldingDays)} trading days. The T+1 result is still recorded first."))
            }
        };
        return SectionSurface(content);
    }

    View BuildAfterSection(EvaluationRow row)
    {
        var p = row.Prediction;
        var signalDate = SignalDate(p);
        var content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                SectionLabel(
                    "3 · SESUDAH / REALISASI T+1",
                    "3 · AFTER / T+1 ACTUAL",
                    UiKit.Green)
            }
        };
        if (!p.NextTradingDate.HasValue)
        {
            content.Children.Add(UiKit.Sub(
                p.Outcome == "CANCELLED"
                    ? Loc.T(
                        "Rekomendasi dibatalkan sebelum opening. Pergerakan pasar T+1 akan tetap ditampilkan setelah candle hari bursa berikutnya berhasil diunduh.",
                        "The recommendation was cancelled before the open. The T+1 market move will still be shown after the next trading day's candle is downloaded.")
                    : Loc.T(
                        $"Belum ada candle setelah {signalDate:dd MMM yyyy} dalam data yang berhasil diunduh. Candle tanggal sinyal tidak dipakai sebagai realisasi agar tidak terjadi look-ahead.",
                        $"There is no candle after {signalDate:dd MMM yyyy} in the downloaded data yet. The signal-date candle is not reused as an actual result, preventing look-ahead.")));
            return SectionSurface(content);
        }

        content.Children.Add(UiKit.Sub(Loc.T(
            $"Hari bursa aktual: {p.NextTradingDate:dddd, dd MMMM yyyy}",
            $"Actual trading day: {p.NextTradingDate:dddd, dd MMMM yyyy}")));
        content.Children.Add(Metrics(
            (
                Loc.T("OPEN", "OPEN"),
                PriceOrUnavailable(p.NextOpen),
                null
            ),
            (
                Loc.T("HIGH", "HIGH"),
                PriceOrUnavailable(p.NextHigh),
                UiKit.Green
            ),
            (
                Loc.T("LOW", "LOW"),
                PriceOrUnavailable(p.NextLow),
                UiKit.Red
            ),
            (
                Loc.T("CLOSE", "CLOSE"),
                PriceOrUnavailable(p.NextClose),
                Colors.White
            )));
        content.Children.Add(new Label
        {
            Text = EntrySummary(p),
            TextColor = p.EntryFilledAt.HasValue
                ? UiKit.Green
                : p.Outcome == "CANCELLED"
                    ? UiKit.Purple
                    : UiKit.Muted,
            FontAttributes = FontAttributes.Bold
        });

        var nextReturnColor = p.NextDayReturnPercent switch
        {
            > 0 => UiKit.Green,
            < 0 => UiKit.Red,
            _ => UiKit.Muted
        };
        content.Children.Add(Metrics(
            (
                Loc.T("STATUS T+1", "T+1 STATUS"),
                StatusText(p),
                StatusColor(p)
            ),
            (
                Loc.T("RETURN T+1 NET", "NET T+1 RETURN"),
                PercentOrUnavailable(p.NextDayReturnPercent),
                nextReturnColor
            ),
            (
                Loc.T("CLOSE VS SINYAL", "CLOSE VS SIGNAL"),
                PercentOrUnavailable(
                    p.NextDayMarketReturnPercent),
                !p.NextDayMarketReturnPercent.HasValue
                    ? UiKit.Muted
                    : p.NextDayMarketReturnPercent >= 0
                        ? UiKit.Green : UiKit.Red
            ),
            (
                Loc.T("MAKSIMUM / MINIMUM", "MAXIMUM / MINIMUM"),
                p.NextDayMaximumGainPercent.HasValue &&
                p.NextDayMaximumLossPercent.HasValue
                    ? $"{p.NextDayMaximumGainPercent:+0.00;-0.00;0.00}% / " +
                      $"{p.NextDayMaximumLossPercent:+0.00;-0.00;0.00}%"
                    : "—",
                Colors.White
            )));
        if (p.NextDayStatus is "STOP" or "TARGET")
            content.Children.Add(UiKit.Caption(Loc.T(
                "Jika target dan stop sama-sama berada dalam satu candle harian, evaluator menganggap stop tersentuh lebih dulu agar hasil tidak terlalu optimistis.",
                "If target and stop both fall inside one daily candle, the evaluator assumes the stop was touched first to avoid an overly optimistic result.")));

        content.Children.Add(SmallHeading(
            "4 · Hasil swing",
            "4 · Swing result"));
        content.Children.Add(UiKit.Sub(SwingSummary(p)));
        return SectionSurface(content);
    }

    static View SectionSurface(View content) => new Border
    {
        Content = content,
        BackgroundColor = UiKit.Surface,
        Stroke = UiKit.CardStroke,
        StrokeThickness = 1,
        Padding = 12,
        StrokeShape =
            new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = 14
            }
    };

    static Label SectionLabel(
        string id, string en, Color color) => new()
    {
        Text = Loc.T(id, en),
        TextColor = color,
        FontAttributes = FontAttributes.Bold,
        FontSize = 12
    };

    static Label SmallHeading(string id, string en) => new()
    {
        Text = Loc.T(id, en),
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        FontSize = 13
    };

    static Grid Metrics(
        params (string Label, string Value, Color? Color)[] values)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new(GridLength.Star),
                new(GridLength.Star)
            ],
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        for (var row = 0; row < (values.Length + 1) / 2; row++)
            grid.RowDefinitions.Add(
                new RowDefinition(GridLength.Auto));
        for (var index = 0; index < values.Length; index++)
        {
            var metric = values[index];
            var box = new Border
            {
                BackgroundColor = UiKit.Card,
                StrokeThickness = 0,
                Padding = 10,
                StrokeShape =
                    new Microsoft.Maui.Controls.Shapes.RoundRectangle
                    {
                        CornerRadius = 12
                    },
                Content = new VerticalStackLayout
                {
                    Spacing = 3,
                    Children =
                    {
                        new Label
                        {
                            Text = metric.Label,
                            TextColor = UiKit.Muted,
                            FontSize = 10
                        },
                        new Label
                        {
                            Text = metric.Value,
                            TextColor = metric.Color ??
                                        Colors.White,
                            FontSize = 14,
                            FontAttributes =
                                FontAttributes.Bold
                        }
                    }
                }
            };
            grid.Add(box, index % 2, index / 2);
        }
        return grid;
    }

    static DateTime SignalDate(PredictionRecord prediction) =>
        prediction.SignalDate == default
            ? prediction.PredictedAt.Date
            : prediction.SignalDate.Date;

    static bool IsFinal(string? outcome) =>
        outcome is "TARGET" or "STOP" or "TIME_EXIT";

    static string StatusText(PredictionRecord prediction)
    {
        if (prediction.Outcome == "CANCELLED")
            return Loc.T("DIBATALKAN", "CANCELLED");
        if (!prediction.NextTradingDate.HasValue)
            return Loc.T("MENUNGGU T+1", "WAITING FOR T+1");
        return prediction.NextDayStatus switch
        {
            "NOT_FILLED" => Loc.T(
                "ORDER TIDAK TERISI",
                "ORDER NOT FILLED"),
            "OPEN" => Loc.T(
                "TERISI · MASIH BERJALAN",
                "FILLED · STILL OPEN"),
            "TARGET" => Loc.T(
                "TARGET TERCAPAI T+1",
                "TARGET HIT ON T+1"),
            "STOP" => Loc.T(
                "STOP TERSENTUH T+1",
                "STOP HIT ON T+1"),
            "CANCELLED" => Loc.T(
                "DIBATALKAN",
                "CANCELLED"),
            _ => Loc.T(
                "REALISASI T+1 TERSEDIA",
                "T+1 ACTUAL AVAILABLE")
        };
    }

    static Color StatusColor(PredictionRecord prediction) =>
        prediction.Outcome == "CANCELLED"
            ? UiKit.Purple
            : prediction.NextDayStatus switch
            {
                "TARGET" => UiKit.Green,
                "STOP" => UiKit.Red,
                "OPEN" => UiKit.Blue,
                "NOT_FILLED" => UiKit.Muted,
                _ => UiKit.Purple
            };

    static string EntrySummary(PredictionRecord prediction) =>
        prediction.NextDayNoteCode switch
        {
            "OPEN_ABOVE_LIMIT" => Loc.T(
                $"Tidak dieksekusi: opening Rp{prediction.NextOpen:N0} berada di atas limit Rp{prediction.StartPrice:N0}. Order dibatalkan, meskipun low kemudian mungkin menyentuh limit.",
                $"Not executed: the Rp{prediction.NextOpen:N0} open was above the Rp{prediction.StartPrice:N0} limit. The order was cancelled even if the later low may have touched the limit."),
            "OPEN_BELOW_STOP" => Loc.T(
                $"Tidak dieksekusi: opening Rp{prediction.NextOpen:N0} sudah berada pada/di bawah stop Rp{prediction.StopLoss:N0}; setup batal sebelum entry.",
                $"Not executed: the Rp{prediction.NextOpen:N0} open was already at/below the Rp{prediction.StopLoss:N0} stop; the setup was invalid before entry."),
            "LIMIT_NOT_TOUCHED" => Loc.T(
                $"Order GFD tidak menyentuh limit Rp{prediction.StartPrice:N0}.",
                $"The GFD order did not touch the Rp{prediction.StartPrice:N0} limit."),
            "FILLED_BELOW_LIMIT" => Loc.T(
                $"Order terisi pada opening yang lebih murah: Rp{prediction.FilledPrice:N0}, dari batas maksimum Rp{prediction.StartPrice:N0}.",
                $"The order filled at a cheaper open: Rp{prediction.FilledPrice:N0}, versus the Rp{prediction.StartPrice:N0} maximum."),
            "FILLED_AT_LIMIT" => Loc.T(
                $"Order terisi Rp{prediction.FilledPrice:N0}.",
                $"The order filled at Rp{prediction.FilledPrice:N0}."),
            "CANCELLED_BEFORE_OPEN" => Loc.T(
                "Rekomendasi dibatalkan sebelum opening; tidak ada transaksi simulasi.",
                "The recommendation was cancelled before the open; no simulated trade occurred."),
            _ when prediction.EntryFilledAt.HasValue => Loc.T(
                $"Order terisi Rp{prediction.FilledPrice:N0}.",
                $"The order filled at Rp{prediction.FilledPrice:N0}."),
            _ => Loc.T(
                "Tidak ada transaksi yang dieksekusi.",
                "No trade was executed.")
        };

    static string SwingSummary(PredictionRecord prediction) =>
        prediction.Outcome switch
        {
            "TARGET" => Loc.T(
                $"Selesai: target Rp{prediction.EvaluationPrice:N0} tercapai • return net {prediction.ReturnPercent:+0.00;-0.00;0.00}%.",
                $"Completed: the Rp{prediction.EvaluationPrice:N0} target was reached • net return {prediction.ReturnPercent:+0.00;-0.00;0.00}%."),
            "STOP" => Loc.T(
                $"Selesai: keluar pada Rp{prediction.EvaluationPrice:N0} setelah stop • return net {prediction.ReturnPercent:+0.00;-0.00;0.00}%.",
                $"Completed: exited at Rp{prediction.EvaluationPrice:N0} after the stop • net return {prediction.ReturnPercent:+0.00;-0.00;0.00}%."),
            "TIME_EXIT" => Loc.T(
                $"Selesai karena batas holding pada Rp{prediction.EvaluationPrice:N0} • return net {prediction.ReturnPercent:+0.00;-0.00;0.00}%.",
                $"Completed at the holding limit at Rp{prediction.EvaluationPrice:N0} • net return {prediction.ReturnPercent:+0.00;-0.00;0.00}%."),
            "NOT_FILLED" => Loc.T(
                "Tidak ada hasil swing karena order GFD tidak dieksekusi.",
                "There is no swing result because the GFD order was not executed."),
            "CANCELLED" => Loc.T(
                "Tidak ada hasil swing karena rekomendasi dibatalkan sebelum entry.",
                "There is no swing result because the recommendation was cancelled before entry."),
            _ when prediction.EntryFilledAt.HasValue &&
                   prediction.LatestObservedDate.HasValue => Loc.T(
                $"Masih berjalan • observasi terbaru {prediction.LatestObservedDate:dd MMM yyyy} close Rp{prediction.LatestObservedClose:N0} • mark-to-market net {prediction.LatestOpenReturnPercent:+0.00;-0.00;0.00}%.",
                $"Still open • latest observation {prediction.LatestObservedDate:dd MMM yyyy} close Rp{prediction.LatestObservedClose:N0} • net mark-to-market {prediction.LatestOpenReturnPercent:+0.00;-0.00;0.00}%."),
            _ => Loc.T(
                "Menunggu realisasi hari bursa berikutnya.",
                "Waiting for the next trading day's actual.")
        };

    static string PriceOrUnavailable(decimal value) =>
        value > 0 ? $"Rp{value:N0}" : "—";

    static string PriceOrUnavailable(decimal? value) =>
        value is > 0 ? $"Rp{value:N0}" : "—";

    static string PercentOrUnavailable(decimal? value) =>
        value.HasValue
            ? $"{value:+0.00;-0.00;0.00}%"
            : "—";

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

    sealed record EvaluationRow(
        ScanRun Run, PredictionRecord Prediction);
}
