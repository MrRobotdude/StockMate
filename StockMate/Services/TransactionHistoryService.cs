using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StockMate.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StockMate.Services;

public sealed class TransactionHistoryService(AppDataService data)
{
    static readonly string[] DateNames = ["date", "tanggal", "transaction date", "trade date", "time", "waktu", "trade time", "tanggal transaksi"];
    static readonly string[] SymbolNames = ["symbol", "stock", "kode saham", "code", "ticker", "stock code", "kode"];
    static readonly string[] SideNames = ["side", "type", "tipe", "action", "buy/sell", "transaction type", "jenis transaksi"];
    static readonly string[] LotNames = ["lot", "lots", "qty lot", "quantity lot", "volume lot"];
    static readonly string[] ShareNames = ["shares", "qty", "quantity", "jumlah saham", "volume", "matched quantity"];
    static readonly string[] PriceNames = ["price", "harga", "trade price", "average price", "matched price", "execution price"];
    static readonly string[] FeeNames = ["fee", "fees", "broker fee", "commission", "biaya", "total fee"];
    static readonly string[] IdNames = ["transaction id", "trade id", "order id", "id", "reference", "trade no", "order no"];

    public async Task<(bool Ok, string Message)> ImportAsync(FileResult file)
    {
        await using var stream = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        var isPdf = file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        var text = isPdf
            ? await Task.Run(() => ExtractPdfText(bytes))
            : Encoding.UTF8.GetString(bytes);
        if (string.IsNullOrWhiteSpace(text)) return (false, "File kosong.");

        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        var rows = await Task.Run(() => isPdf ? ParseStockbitPdf(text) : Parse(text));
        if (rows.Count == 0)
            return (false, "Tidak ada transaksi BUY/SELL yang dikenali. Format yang didukung: e-Statement PDF Stockbit serta CSV/TSV transaction history.");

        var start = rows.Min(x => x.Time).Date;
        var end = rows.Max(x => x.Time).Date.AddDays(1).AddTicks(-1);
        var batch = new TransactionImportBatch
        {
            FileName = file.FileName, CoverageStart = start, CoverageEnd = end,
            ImportedCount = rows.Count, FileFingerprint = fingerprint
        };

        // The seeded/opening portfolio is only a temporary baseline. Once an
        // authoritative broker history exists, it must not double the holdings.
        foreach (var tx in data.State.Transactions.Where(x => x.Source == "OPENING" && x.IsActive))
        {
            tx.IsActive = false;
            tx.SupersededByImportBatchId = batch.Id;
        }

        // A broker history import is an explicit portfolio reconciliation. Manual rows
        // are retained only as an audit trail, but none may continue affecting the
        // reconciled portfolio. This also removes test/correction trades dated after
        // the last row in the statement.
        foreach (var tx in data.State.Transactions.Where(x =>
                     x.Source == "MANUAL" && x.IsActive))
        {
            tx.IsActive = false;
            tx.SupersededByImportBatchId = batch.Id;
            batch.SupersededManualCount++;
            batch.ReconciliationDetails.Add(
                $"{tx.Time:yyyy-MM-dd HH:mm} {tx.Side} {tx.Symbol} {tx.Lots} lot @ {tx.Price:N0} → dikoreksi history");
        }

        // Merge as a multiset instead of replacing an overlapping date range.
        // This makes re-imports idempotent while preserving two genuinely
        // identical executions that occur more than once in one statement.
        var activeHistory = data.State.Transactions
            .Where(x => x.Source == "HISTORY" && x.IsActive)
            .GroupBy(IdentityKey)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var incomingOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tx in rows.OrderBy(x => x.Time))
        {
            var key = IdentityKey(tx);
            incomingOccurrences.TryGetValue(key, out var seenInFile);
            seenInFile++;
            incomingOccurrences[key] = seenInFile;
            activeHistory.TryGetValue(key, out var alreadyStored);
            if (seenInFile <= alreadyStored)
            {
                batch.SkippedDuplicateCount++;
                continue;
            }
            tx.Source = "HISTORY";
            tx.ImportBatchId = batch.Id;
            tx.IsActive = true;
            if (tx.Fee <= 0)
            {
                var rate = tx.Side == "BUY" ? data.State.BuyFeeRate : data.State.SellFeeRate;
                tx.Fee = decimal.Round(tx.GrossValue * rate, 0);
            }
            data.State.Transactions.Add(tx);
            batch.AddedCount++;
        }
        data.State.TransactionImports.Add(batch);
        // Importing transaction rows must not erase an official cash or realized
        // reconciliation. The stored opening balance remains the anchor and cash
        // is recalculated only from genuinely new rows.
        data.RebuildPositions();
        data.RecalculateCash();
        await data.SaveAsync();
        var feeNote = isPdf
            ? " Biaya broker diestimasi dengan tarif pada Pengaturan; cocokkan realized resmi saat Sync Up."
            : "";
        return (true,
            $"Impor selesai untuk {rows.Count} baris {start:dd MMM yyyy}–{end:dd MMM yyyy}: " +
            $"{batch.AddedCount} transaksi baru, {batch.SkippedDuplicateCount} overlap dilewati. " +
            $"{batch.SupersededManualCount} transaksi manual dinonaktifkan dan tidak lagi memengaruhi portofolio. " +
            $"Kas dan realized resmi yang sudah direkonsiliasi tetap dipertahankan.{feeNote}");
    }

    static string IdentityKey(TradeTransaction tx)
    {
        var external = tx.ExternalId?.Trim();
        if (!string.IsNullOrWhiteSpace(external) &&
            !external.StartsWith("STOCKBIT-", StringComparison.OrdinalIgnoreCase))
            return $"ID|{external.ToUpperInvariant()}";
        return string.Join("|",
            tx.Time.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            tx.Symbol.Trim().ToUpperInvariant(),
            tx.Side.Trim().ToUpperInvariant(),
            tx.Lots.ToString(CultureInfo.InvariantCulture),
            tx.Price.ToString("0.####", CultureInfo.InvariantCulture),
            tx.DueDate?.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "",
            tx.SalesTax.ToString("0.####", CultureInfo.InvariantCulture));
    }

    static string ExtractPdfText(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (text.Length > 0) text.AppendLine();
            text.Append(ContentOrderTextExtractor.GetText(page));
        }
        return text.ToString();
    }

    static List<TradeTransaction> ParseStockbitPdf(string text)
    {
        var result = new List<TradeTransaction>();
        var pattern = new System.Text.RegularExpressions.Regex(
            @"(?m)^\s*(?<date>\d{2}/\d{2}/\d{4})\s+(?<due>\d{2}/\d{2}/\d{4})\s+(?<symbol>[A-Z]{4,6})\s+(?<side>[BS])\s+(?<lot>[\d,]+)\s+(?<price>[\d,]+)\s+(?<buy>[\d,.]+)\s+(?<sell>[\d,.]+)\s+(?<tax>[\d,.]+)\s*$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        foreach (System.Text.RegularExpressions.Match match in pattern.Matches(text))
        {
            if (!TryDate(match.Groups["date"].Value, out var time) ||
                !TryDate(match.Groups["due"].Value, out var due) ||
                !TryUsNumber(match.Groups["lot"].Value, out var lot) ||
                !TryUsNumber(match.Groups["price"].Value, out var price) ||
                !TryUsNumber(match.Groups["tax"].Value, out var tax)) continue;
            result.Add(new()
            {
                Time = time, DueDate = due, Symbol = match.Groups["symbol"].Value,
                Side = match.Groups["side"].Value == "B" ? "BUY" : "SELL",
                Lots = (int)lot, Price = price, SalesTax = tax,
                Fee = 0, AffectsCash = true,
                ExternalId = $"STOCKBIT-{time:yyyyMMdd}-{result.Count + 1:D4}",
                Note = $"Impor e-Statement Stockbit; sales tax tercatat Rp {tax:N2}; total fee diestimasi dari tarif broker"
            });
        }
        return result;
    }

    static bool TryUsNumber(string value, out decimal number) =>
        decimal.TryParse(value.Trim().Replace(",", ""), NumberStyles.Number,
            CultureInfo.InvariantCulture, out number);

    static List<TradeTransaction> Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return [];
        var headerIndex = Array.FindIndex(lines, line =>
        {
            var normalized = Normalize(line);
            return DateNames.Any(x => normalized.Contains(Normalize(x))) &&
                   SymbolNames.Any(x => normalized.Contains(Normalize(x)));
        });
        if (headerIndex < 0) return [];
        var delimiter = DetectDelimiter(lines[headerIndex]);
        var headers = Split(lines[headerIndex], delimiter).Select(Normalize).ToArray();
        int date = Find(headers, DateNames), symbol = Find(headers, SymbolNames),
            side = Find(headers, SideNames), lot = Find(headers, LotNames),
            shares = Find(headers, ShareNames), price = Find(headers, PriceNames),
            fee = Find(headers, FeeNames), id = Find(headers, IdNames);
        if (date < 0 || symbol < 0 || side < 0 || (lot < 0 && shares < 0) || price < 0) return [];

        var result = new List<TradeTransaction>();
        foreach (var line in lines.Skip(headerIndex + 1))
        {
            var cells = Split(line, delimiter);
            if (cells.Count <= new[] { date, symbol, side, price, Math.Max(lot, shares) }.Max()) continue;
            var sideValue = cells[side].Trim().ToUpperInvariant();
            if (sideValue.Contains("BUY") || sideValue is "B" or "BELI") sideValue = "BUY";
            else if (sideValue.Contains("SELL") || sideValue is "S" or "JUAL") sideValue = "SELL";
            else continue;
            if (!TryDate(cells[date], out var time) || !TryDecimal(cells[price], out var priceValue)) continue;
            int lots;
            if (lot >= 0 && TryDecimal(cells[lot], out var lotValue)) lots = (int)lotValue;
            else if (shares >= 0 && TryDecimal(cells[shares], out var shareValue)) lots = (int)(shareValue / 100);
            else continue;
            if (lots <= 0 || priceValue <= 0) continue;
            TryDecimal(fee >= 0 && fee < cells.Count ? cells[fee] : "0", out var feeValue);
            result.Add(new TradeTransaction
            {
                Time = time, Symbol = cells[symbol].Trim().ToUpperInvariant().Replace(".JK", ""),
                Side = sideValue, Lots = lots, Price = priceValue, Fee = Math.Abs(feeValue),
                ExternalId = id >= 0 && id < cells.Count ? cells[id].Trim() : "",
                AffectsCash = true, Note = "Impor Transaction History"
            });
        }
        return result;
    }

    static char DetectDelimiter(string header) =>
        new[] { ',', ';', '\t' }.OrderByDescending(x => header.Count(c => c == x)).First();
    static int Find(string[] headers, string[] names) =>
        Array.FindIndex(headers, h => names.Any(n => h == Normalize(n)));
    static string Normalize(string value) => value.Trim().Trim('"').ToLowerInvariant();
    static List<string> Split(string line, char delimiter)
    {
        var values = new List<string>(); var current = new StringBuilder(); var quoted = false;
        foreach (var c in line)
        {
            if (c == '"') quoted = !quoted;
            else if (c == delimiter && !quoted) { values.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        values.Add(current.ToString()); return values;
    }
    static bool TryDate(string value, out DateTime date)
    {
        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy", "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy", "dd MMM yyyy HH:mm:ss", "dd MMM yyyy"];
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces, out date) ||
               DateTime.TryParse(value.Trim(), new CultureInfo("id-ID"), DateTimeStyles.AllowWhiteSpaces, out date);
    }
    static bool TryDecimal(string value, out decimal number)
    {
        var clean = value.Trim().Replace("Rp", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "");
        return decimal.TryParse(clean, NumberStyles.Any, new CultureInfo("id-ID"), out number) ||
               decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }
}
