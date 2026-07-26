using StockMate.Models;

namespace StockMate.Ui;

public static class Loc
{
    static readonly IReadOnlyDictionary<string, string> EnglishText =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Pilih kode saham"] = "Choose stock symbol",
            ["Universe belum tersedia"] = "Universe is unavailable",
            ["Jalankan Sync Up universe IDX terlebih dahulu agar kode saham dapat dipilih tanpa typo."] =
                "Run IDX universe Sync Up first so a valid stock symbol can be selected.",
            ["Cari kode, contoh TLKM"] = "Search a symbol, for example TLKM",
            ["Kode tidak ditemukan"] = "Symbol not found",
            ["Pilih hanya kode yang tersedia di universe IDX."] =
                "Choose a symbol available in the IDX universe.",
            ["Batal"] = "Cancel",
            ["Simpan"] = "Save",
            ["Tutup"] = "Close",
            ["Lanjutkan"] = "Continue",
            ["Sebelumnya"] = "Previous",
            ["Berikutnya"] = "Next",
            ["Simpan pengaturan"] = "Save settings",
            ["Sertakan saham spekulatif"] = "Include speculative stocks",
            ["Maksimal alokasi scanner Rp500 ribu"] = "Maximum scanner allocation Rp500 thousand",
            ["Scan closing otomatis"] = "Automatic closing scan",
            ["Tarik & analisis 07.00 • cek 12.15 & 16.30 • retry bila closing belum siap"] =
                "Fetch & analyze at 07:00 • check at 12:15 & 16:30 • retry when closing data is not ready",
            ["Analisis isu opening & closing"] = "Opening & closing event analysis",
            ["Gratis • sebelum analisis & sekitar 08.45 • portofolio + kandidat teratas"] =
                "Free • before analysis & around 08:45 • portfolio + top candidates",
            ["Izinkan jadwal presisi Android"] = "Allow Android exact alarms",
            ["Atur penggunaan baterai background"] = "Configure background battery use",
            ["Periksa notifikasi progres"] = "Check progress notifications",
            ["Jeda request data (milidetik)"] = "Data request delay (milliseconds)",
            ["Reset hasil & checkpoint scanner"] = "Reset scanner results & checkpoints",
            ["Reset transaction history"] = "Reset transaction history",
            ["Reset seluruh aplikasi"] = "Reset the entire app",
            ["Pilih hanya data yang memang ingin dihapus. Tindakan dikonfirmasi sebelum dijalankan."] =
                "Select only the data you intend to remove. Every action requires confirmation.",
            ["Nomor versi berasal langsung dari APK yang sedang terpasang."] =
                "The version number comes directly from the installed APK.",
            ["Mengambil master emiten dari IDX…"] = "Fetching the issuer master from IDX…",
            ["Master diperbarui"] = "Master updated",
            ["Menggunakan cache"] = "Using cache",
            ["Gagal"] = "Failed",
            ["Pilih daftar kode saham IDX"] = "Choose an IDX symbol list",
            ["Universe diperbarui"] = "Universe updated",
            ["Pilih strategy.json"] = "Choose strategy.json",
            ["Tidak valid"] = "Invalid",
            ["Konfigurasi strategi tidak lolos validasi."] =
                "The strategy configuration did not pass validation.",
            ["Validasi training gagal"] = "Training validation failed",
            ["Artefak training harus memiliki minimal 3 fold out-of-sample, 30 trade, dan fingerprint data."] =
                "The training artifact must contain at least 3 out-of-sample folds, 30 trades, and a data fingerprint.",
            ["Berhasil"] = "Success",
            ["Ambil ulang data?"] = "Fetch the data again?",
            ["Gunakan hanya jika data snapshot gagal/tidak lengkap. Ini akan memakai request internet lagi."] =
                "Use this only when the snapshot failed or is incomplete. It will make internet requests again.",
            ["Ambil ulang"] = "Fetch again",
            ["Sembunyikan proses teknis"] = "Hide technical process",
            ["Lihat proses teknis"] = "Show technical process",
            ["Menghentikan scanner dengan aman…"] = "Stopping the scanner safely…",
            ["Pilih scan siang atau malam."] = "Choose a midday or evening scan.",
            ["Belum berjalan"] = "Not running",
            ["Tahap dan saham yang diproses akan tampil di sini."] =
                "The current stage and stock will appear here.",
            ["Batch belum dimulai."] = "The batch has not started.",
            ["Hentikan scan"] = "Stop scan",
            ["Semua proses"] = "All processes",
            ["Semua rekomendasi"] = "All recommendations",
            ["Urutkan"] = "Sort",
            ["Cari kode saham…"] = "Search stock symbol…",
            ["Belum ada hasil"] = "No results yet",
            ["Mulai scan sesi terakhir untuk mencari peluang di seluruh IDX."] =
                "Scan the latest session to find opportunities across the IDX.",
            ["Hasil kandidat akan muncul setelah data yang cukup berhasil dianalisis."] =
                "Candidates will appear after enough market data has been analyzed.",
            ["Belum ada prediksi"] = "No predictions yet",
            ["Hanya shortlist rekomendasi nyata yang dicatat. Jalankan analisis untuk membuat baseline."] =
                "Only actionable shortlisted recommendations are recorded. Run an analysis to create a baseline.",
            ["Evaluasi prediksi"] = "Prediction evaluation",
            ["Cari posisi / Search position…"] = "Search position…",
            ["Semua tindakan"] = "All actions",
            ["Portofolio masih kosong"] = "Portfolio is empty",
            ["Impor e-Statement atau catat transaksi untuk mulai memantau posisi."] =
                "Import an e-Statement or record a transaction to start tracking positions.",
            ["Buka halaman posisi"] = "Open position page",
            ["Jumlah lot"] = "Number of lots",
            ["Jumlah yang benar-benar dieksekusi"] = "Number of lots actually executed",
            ["Harga transaksi"] = "Transaction price",
            ["Harga buy/sell per lembar"] = "Buy/sell price per share",
            ["Harga per lembar"] = "Price per share",
            ["Catatan"] = "Note",
            ["Alasan transaksi (opsional)"] = "Transaction reason (optional)",
            ["Data tidak valid"] = "Invalid data",
            ["Lot dan harga harus berupa angka."] = "Lots and price must be numeric.",
            ["Transaksi tersimpan"] = "Transaction saved",
            ["Tidak dapat disimpan"] = "Could not save",
            ["Stop loss"] = "Stop loss",
            ["Take profit"] = "Take profit",
            ["Isi 0 untuk menghapus"] = "Enter 0 to clear",
            ["Harga rata-rata"] = "Average price",
            ["Harga pasar"] = "Market price",
            ["Nilai posisi"] = "Position value",
            ["Modal posisi"] = "Position cost",
            ["Keputusan belum tersedia"] = "Decision unavailable",
            ["Harga dan untung/rugi tetap sudah diperbarui saat simbol berhasil. Rekomendasi lengkap dibuat setelah analisis selesai."] =
                "Price and profit/loss were updated for successful symbols. A complete recommendation is created after analysis finishes.",
            ["Skor teknikal"] = "Technical score",
            ["Penyesuaian isu"] = "Event adjustment",
            ["Skor gabungan"] = "Combined score",
            ["Harga limit order"] = "Limit order price",
            ["Batal jika opening di atas"] = "Cancel if opening is above",
            ["Jumlah tambah"] = "Lots to add",
            ["Jumlah jual"] = "Lots to sell",
            ["Target utama"] = "Primary target",
            ["Trailing stop belum disarankan pada kondisi harga saat ini."] =
                "A trailing stop is not recommended under the current conditions.",
            ["Buy / Tambah posisi"] = "Buy / Add position",
            ["Sell / Kurangi posisi"] = "Sell / Reduce position",
            ["Detail belum tersedia"] = "Details unavailable",
            ["Jalankan analisis snapshot terbaru untuk membuat keputusan saham ini."] =
                "Analyze the latest snapshot to create a decision for this stock.",
            ["Harga snapshot"] = "Snapshot price",
            ["Dana dialokasikan"] = "Allocated funds",
            ["Target 1"] = "Target 1",
            ["Target 2"] = "Target 2",
            ["Alasan keputusan"] = "Decision rationale",
            ["Fakta yang mendukung setup"] = "Evidence supporting the setup",
            ["Risiko & pembatalan"] = "Risk & invalidation",
            ["Isu & peristiwa terbaru"] = "Latest events & issues",
            ["Catat BUY"] = "Record BUY",
            ["Stop loss dan take profit harus berupa angka."] =
                "Stop loss and take profit must be numeric.",
            ["Belum ada histori. Scan pertama akan disimpan sebagai baseline evaluasi."] =
                "There is no history yet. The first scan will be stored as the evaluation baseline.",
            ["Prediksi tersimpan dan menunggu scan berikutnya untuk dievaluasi."] =
                "Predictions are stored and waiting for the next scan to be evaluated.",
            ["Strategi aktif belum berasal dari trainer tervalidasi. Aplikasi mengevaluasi hasil, tetapi tidak mengubah bobot sendiri."] =
                "The active strategy does not come from a validated trainer yet. The app evaluates results but does not adjust its own weights.",
            ["Impor Transaction History"] = "Import transaction history",
            ["Tidak ada transaksi"] = "No transactions",
            ["Ubah filter atau impor history."] = "Change filters or import history.",
            ["Sumber"] = "Source",
            ["Status"] = "Status",
            ["Aktif"] = "Active",
            ["Dikoreksi"] = "Corrected",
            ["AKTIF"] = "ACTIVE",
            ["DIKOREKSI"] = "CORRECTED",
            ["Halaman"] = "Page",
            ["Data awal"] = "Initial data",
            ["Impor e-Statement untuk memulai"] = "Import an e-Statement to begin",
            ["Simpan dan validasi"] = "Save and validate",
            ["Strategi manual/bawaan"] = "Manual/built-in strategy",
            ["File strategi bisa diimpor tanpa publish ulang APK."] =
                "A strategy file can be imported without republishing the APK.",
            ["fallback lokal"] = "local fallback",
            ["belum pernah"] = "never",
            ["Data isu belum tersedia."] = "Event data is unavailable.",
            ["Target belum valid."] = "The target is not valid yet.",
            ["Target profit dibatalkan karena skenario cut loss aktif."] =
                "The profit target is cancelled because the cut-loss scenario is active.",
            ["Jalankan scan lengkap sebelum bertindak."] =
                "Run a complete scan before taking action.",
            ["NO TRADE"] = "NO TRADE",
            ["PANTAU"] = "WATCH",
            ["TUNGGU"] = "WAIT",
            ["Menyiapkan aplikasi…"] = "Preparing the app…",
            ["Coba lagi"] = "Try again",
            ["Memuat portofolio dan pengaturan…"] =
                "Loading portfolio and settings…",
            ["Masukkan data portofolio kamu"] = "Enter your portfolio data",
            ["Impor e-Statement Stockbit"] = "Import Stockbit e-Statement",
            ["Format: e-Statement PDF Stockbit atau Transaction History CSV/TSV."] =
                "Format: Stockbit e-Statement PDF or Transaction History CSV/TSV.",
            ["Pilih e-Statement PDF Stockbit"] =
                "Choose a Stockbit e-Statement PDF",
            ["Membaca dan memvalidasi e-Statement…"] =
                "Reading and validating the e-Statement…",
            ["Impor gagal"] = "Import failed",
            ["Impor berhasil"] = "Import successful",
            ["Masukkan saldo kas, bukan total equity atau nilai saham."] =
                "Enter the cash balance, not total equity or stock value.",
            ["Gunakan periode yang sama dengan e-Statement. Tanda minus berarti rugi."] =
                "Use the same period as the e-Statement. A minus sign means a loss.",
            ["Jumlah lot perolehan"] = "Acquisition lot count",
            ["Harga IPO / average perolehan"] =
                "IPO / average acquisition price",
            ["Total biaya perolehan (opsional)"] =
                "Total acquisition fee (optional)",
            ["Jenis perolehan"] = "Acquisition type",
            ["Transfer masuk"] = "Incoming transfer",
            ["Perolehan lain"] = "Other acquisition",
            ["Untuk IPO, lot boleh lebih besar dari minimum. Sisanya menjadi posisi aktif."] =
                "For an IPO, the lot count may exceed the minimum. The remainder becomes an active position.",
            ["Kas tersedia"] = "Available cash",
            ["Realized P/L resmi Stockbit"] =
                "Official Stockbit realized P/L",
            ["Risiko normal per transaksi"] = "Normal risk per trade",
            ["Batas rugi bulanan"] = "Monthly loss limit",
            ["Fee beli (%)"] = "Buy fee (%)",
            ["Fee jual (%)"] = "Sell fee (%)",
            ["Reset data scanner?"] = "Reset scanner data?",
            ["Snapshot, rekomendasi, histori evaluasi, dan checkpoint scanner akan dihapus. Transaction history dan Sync Up tetap aman."] =
                "Snapshots, recommendations, evaluation history, and scanner checkpoints will be removed. Transaction history and Sync Up remain safe.",
            ["Reset scanner"] = "Reset scanner",
            ["Scanner direset"] = "Scanner reset",
            ["Data scanner telah dikosongkan. Scan berikutnya akan mengambil snapshot baru."] =
                "Scanner data has been cleared. The next scan will fetch a new snapshot.",
            ["Reset transaction history?"] = "Reset transaction history?",
            ["Semua transaksi, posisi, input IPO, realized, dan rekonsiliasi kas akan dihapus. Universe dan hasil scanner tetap ada."] =
                "All transactions, positions, IPO inputs, realized P/L, and cash reconciliation will be removed. The universe and scanner results remain.",
            ["Hapus history"] = "Delete history",
            ["History direset"] = "History reset",
            ["Impor kembali e-Statement dan jalankan Sync Up untuk membangun portofolio."] =
                "Import the e-Statement again and run Sync Up to rebuild the portfolio.",
            ["Reset seluruh aplikasi?"] = "Reset the entire app?",
            ["Semua transaksi, Sync Up, universe, snapshot, rekomendasi, dan pengaturan akan kembali ke kondisi awal."] =
                "All transactions, Sync Up data, universe, snapshots, recommendations, and settings will return to their initial state.",
            ["Reset semuanya"] = "Reset everything",
            ["Aplikasi direset"] = "App reset",
            ["Semua data lokal telah dikosongkan. Onboarding awal akan dibuka kembali."] =
                "All local data has been cleared. Initial onboarding will open again.",
            ["Progres juga tampil pada notifikasi Android."] =
                "Progress is also shown in Android notifications.",
            ["Progres tetap tampil di notifikasi Android saat aplikasi ditutup."] =
                "Progress remains visible in Android notifications while the app is closed.",
            ["Scan dibatalkan."] = "Scan cancelled.",
            ["Shortlist rekomendasi"] = "Recommendation shortlist",
            ["Semua hasil analisis"] = "All analysis results",
            ["Bisa dieksekusi"] = "Executable",
            ["Spekulatif"] = "Speculative",
            ["Non-spekulatif"] = "Non-speculative",
            ["Semua"] = "All",
            ["Tambah"] = "Add",
            ["Tahan"] = "Hold",
            ["Kurangi/Jual"] = "Reduce/Sell",
            ["Untung"] = "Profit",
            ["Rugi"] = "Loss",
            ["Belum ada harga"] = "No price",
            ["Stop terancam"] = "Stop at risk",
            ["Cari kode / Search symbol…"] = "Search symbol…",
            ["Sumber / Source"] = "Source",
            ["Periode / Period"] = "Period",
            ["Urutkan / Sort"] = "Sort",
            ["StockMate tidak menggunakan portofolio contoh. Impor e-Statement Stockbit untuk mulai membangun posisi dan riwayat transaksi."] =
                "StockMate does not use a sample portfolio. Import a Stockbit e-Statement to build your positions and transaction history.",
            ["Lengkapi perolehan saham yang tidak muncul sebagai BUY (misalnya penjatahan IPO), lalu masukkan Trading Balance Stockbit saat ini."] =
                "Complete stock acquisitions that do not appear as BUY transactions (for example an IPO allocation), then enter the current Stockbit Trading Balance.",
            ["Menyimpan dan memvalidasi…"] = "Saving and validating…",
            ["Semua sumber"] = "All sources",
            ["Semua waktu"] = "All time",
            ["7 hari"] = "7 days",
            ["30 hari"] = "30 days",
            ["Bulan ini"] = "This month",
            ["Tahun ini"] = "This year",
            ["Terbaru"] = "Newest",
            ["Terlama"] = "Oldest",
            ["Nilai terbesar"] = "Largest value",
            ["Nilai terkecil"] = "Smallest value",
            ["Kode A–Z"] = "Symbol A–Z",
            ["Fee terbesar"] = "Largest fee",
            ["Riwayat transaksi"] = "Transaction history",
            ["Pilih e-Statement PDF atau CSV/TSV"] =
                "Choose an e-Statement PDF or CSV/TSV",
            ["History diterapkan"] = "History applied",
            ["Harga order jual"] = "Sell order price",
            ["Harga acuan"] = "Reference price",
            ["Buka untuk melihat rencana"] = "Open to view the plan",
            ["Rencana risiko & eksekusi"] = "Risk & execution plan",
            ["Kapan keputusan berubah"] = "When the decision changes",
            ["Kondisi pembatalan rekomendasi"] = "Recommendation invalidation",
            ["Atur stop loss & take profit"] = "Set stop loss & take profit",
            ["Jumlah lot yang benar-benar dieksekusi"] =
                "Number of lots actually executed",
            ["Tahap berikutnya: Sync Up saldo kas Stockbit saat ini."] =
                "Next: Sync Up the current Stockbit cash balance.",
            ["Impor strategi"] = "Import strategy",
            ["Bagikan strategi"] = "Share strategy",
            ["Perbarui master IDX"] = "Update IDX master",
            ["Bagikan universe"] = "Share universe",
            ["Impor CSV/TXT"] = "Import CSV/TXT",
            ["Universe IDX"] = "IDX universe",
            ["Strategi analisis"] = "Analysis strategy",
            ["Tersimpan"] = "Saved",
            ["Menyimpan pengaturan…"] = "Saving settings…",
            ["Pengaturan"] = "Settings",
            ["Hentikan"] = "Stop",
            ["Proses gagal"] = "Process failed",
            ["Analisis"] = "Analysis",
            ["Analisis dibatalkan."] = "Analysis cancelled.",
            ["Analisis gagal"] = "Analysis failed",
            ["Peluang terbaik"] = "Best opportunities",
            ["Ambil / perbarui data"] = "Fetch / update data",
            ["Analisis snapshot"] = "Analyze snapshot",
            ["Paksa ambil ulang data"] = "Force data refresh",
            ["Prioritas rekomendasi"] = "Recommendation priority",
            ["Risk/reward terbaik"] = "Best risk/reward",
            ["Potensi kenaikan target"] = "Largest target upside",
            ["Risiko harga terkecil"] = "Smallest price risk",
            ["Lot rekomendasi terbesar"] = "Largest recommended lot count",
            ["Harga termurah"] = "Lowest price",
            ["Gagal / retry"] = "Failed / retry",
            ["Positif"] = "Positive",
            ["Negatif"] = "Negative",
            ["Netral"] = "Neutral"
        };

    public static bool English { get; private set; }

    public static void Use(string? code) => English =
        string.Equals(code, "en", StringComparison.OrdinalIgnoreCase);

    public static string T(string id, string en) => English ? en : id;

    public static string T(string text) =>
        English && EnglishText.TryGetValue(text, out var translated)
            ? translated
            : text;

    public static string Confidence(string code) => code.ToUpperInvariant() switch
    {
        "HIGH" or "TINGGI" => T("TINGGI", "HIGH"),
        "MEDIUM" or "SEDANG" => T("SEDANG", "MEDIUM"),
        _ => T("RENDAH", "LOW")
    };

    public static string Lots(int lots) =>
        T($"{lots} lot", $"{lots} lots");

    public static string Verdict(string? verdict) =>
        (verdict ?? "").ToUpperInvariant() switch
        {
            "BUY AREA" => T("BELI", "BUY"),
            "PANTAU — LOT 0" => T("PANTAU — 0 LOT", "WATCH — 0 LOTS"),
            "WATCH" => T("PANTAU", "WATCH"),
            "WAIT" => T("TUNGGU", "WAIT"),
            _ => verdict ?? ""
        };

    public static string Session(string? session) =>
        (session ?? "").Contains("1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(session, "LUNCH", StringComparison.OrdinalIgnoreCase)
            ? T("Closing Sesi 1", "Session 1 close")
            : T("Closing Sesi 2", "Session 2 close");

    public static string Direction(string? direction) =>
        (direction ?? "").ToUpperInvariant() switch
        {
            "POSITIF" => T("POSITIF", "POSITIVE"),
            "NEGATIF" => T("NEGATIF", "NEGATIVE"),
            _ => T("NETRAL", "NEUTRAL")
        };

    public static string Outcome(string? outcome) =>
        (outcome ?? "").ToUpperInvariant() switch
        {
            "NOT_FILLED" => T("TIDAK TERISI", "NOT FILLED"),
            "CANCELLED" => T("DIBATALKAN", "CANCELLED"),
            "TARGET" => T("TARGET TERCAPAI", "TARGET HIT"),
            "STOP" => T("STOP TERSENTUH", "STOP HIT"),
            "TIME_EXIT" => T("KELUAR BATAS WAKTU", "TIME EXIT"),
            _ => T("MENUNGGU", "PENDING")
        };

    public static string Action(PortfolioDecision decision)
    {
        var lots = Math.Max(0, decision.ActionLots);
        return decision.ActionCode switch
        {
            "SELL_ALL" => T($"JUAL SEMUA {lots} LOT", $"SELL ALL {lots} LOTS"),
            "REDUCE" => T($"KURANGI {lots} LOT", $"REDUCE {lots} LOTS"),
            "TAKE_PROFIT" => T($"AMBIL UNTUNG {lots} LOT", $"TAKE PROFIT {lots} LOTS"),
            "AVERAGE_DOWN" => T(
                $"AVERAGE DOWN {decision.SuggestedLots} LOT",
                $"AVERAGE DOWN {decision.SuggestedLots} LOTS"),
            "ADD" => T(
                $"TAMBAH {decision.SuggestedLots} LOT",
                $"ADD {decision.SuggestedLots} LOTS"),
            "WATCH_NO_ADD" => T("PANTAU — JANGAN TAMBAH", "WATCH — DO NOT ADD"),
            "HOLD_NO_ADD" => T("TAHAN — JANGAN TAMBAH", "HOLD — DO NOT ADD"),
            _ => T("TAHAN", "HOLD")
        };
    }

    public static bool IsSellAction(string? code) =>
        code is "SELL_ALL" or "REDUCE" or "TAKE_PROFIT";

    public static bool IsBuyAction(string? code) =>
        code is "ADD" or "AVERAGE_DOWN";
}
