using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StockMate.Models;
using StockMate.Ui;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StockMate.Services;

public sealed class TransactionHistoryService(AppDataService data)
{
    const int StockbitParserVersion = 2;
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
        if (string.IsNullOrWhiteSpace(text))
            return (false, Loc.T("File kosong.", "The file is empty."));

        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        var statement = isPdf
            ? await Task.Run(() => ParseStockbitPdf(text))
            : null;
        var rows = statement?.Rows ?? await Task.Run(() => Parse(text));
        if (rows.Count == 0)
            return (false, Loc.T(
                "Tidak ada transaksi BUY/SELL yang dikenali. Format yang didukung: e-Statement PDF Stockbit serta CSV/TSV transaction history.",
                "No recognizable BUY/SELL transactions were found. Supported formats are Stockbit e-Statement PDF and CSV/TSV transaction history."));

        if (statement is { TotalsValidated: false })
            return (false, Loc.T(
                "Total BUY/SELL/sales tax pada PDF tidak cocok dengan baris transaksi. File tidak diterapkan agar portofolio tidak rusak.",
                "The PDF BUY/SELL/sales-tax totals do not match its transaction rows. The file was not applied, protecting the portfolio."));

        var start = statement?.PeriodStart ?? rows.Min(x => x.Time).Date;
        var end = (statement?.PeriodEnd ?? rows.Max(x => x.Time).Date)
            .Date.AddDays(1).AddTicks(-1);
        var accountKey = statement?.AccountKey ?? "";
        // A fingerprint is immutable history, even after a later overlapping
        // statement supersedes its rows. Re-applying an older PDF would
        // otherwise roll the portfolio back to stale broker history.
        var alreadyApplied = data.State.TransactionImports
            .Where(x => x.FileFingerprint == fingerprint)
            .OrderByDescending(x => x.ParserVersion)
            .ThenByDescending(x => x.ImportedAt)
            .FirstOrDefault();
        var coveredByNewerStatement = statement is not null &&
            alreadyApplied is not null &&
            data.State.TransactionImports.Any(x =>
                x.Id != alreadyApplied.Id &&
                x.ImportedAt > alreadyApplied.ImportedAt &&
                x.Provider.Equals("STOCKBIT", StringComparison.OrdinalIgnoreCase) &&
                SameAccountKey(
                    AccountKey(x.AccountNumber, x.Sid), accountKey) &&
                x.CoverageStart.Date <= start.Date &&
                x.CoverageEnd.Date >= end.Date);
        if (coveredByNewerStatement)
            return (true, Loc.T(
                "File lama ini sudah tercakup oleh e-Statement yang diimpor lebih baru. Portofolio tidak dikembalikan ke riwayat lama.",
                "This older file is already covered by a more recently imported e-Statement. The portfolio was not rolled back to older history."));
        // PDF imports from an older parser may be reapplied once so an upgrade
        // can repair holdings already imported by v0.7.x. CSVs do not need that
        // parser migration, and a PDF processed by this parser remains exactly
        // idempotent on subsequent imports.
        if (alreadyApplied is not null &&
            (statement is null ||
             alreadyApplied.ParserVersion >= StockbitParserVersion))
            return (true, Loc.T(
                $"File ini sudah pernah diproses pada {alreadyApplied.ImportedAt:dd MMM yyyy HH:mm}. Tidak ada transaksi yang diterapkan ulang.",
                $"This file was already processed on {alreadyApplied.ImportedAt:dd MMM yyyy HH:mm}. No transactions were reapplied."));

        var batch = new TransactionImportBatch
        {
            FileName = file.FileName, CoverageStart = start, CoverageEnd = end,
            ImportedCount = rows.Count, FileFingerprint = fingerprint,
            ParserVersion = statement is null ? 1 : StockbitParserVersion,
            Provider = statement is null ? "CSV" : "STOCKBIT",
            AccountNumber = statement?.AccountNumber ?? "",
            Sid = statement?.Sid ?? "",
            ParsedBuyValue = statement?.ParsedBuyValue ??
                rows.Where(x => x.Side == "BUY").Sum(x => x.GrossValue),
            ParsedSellValue = statement?.ParsedSellValue ??
                rows.Where(x => x.Side == "SELL").Sum(x => x.GrossValue),
            ParsedSalesTax = statement?.ParsedSalesTax ??
                rows.Sum(x => x.SalesTax),
            DeclaredBuyValue = statement?.DeclaredBuyValue,
            DeclaredSellValue = statement?.DeclaredSellValue,
            DeclaredSalesTax = statement?.DeclaredSalesTax,
            TotalsValidated = statement?.TotalsValidated ?? true
        };

        // A statement is authoritative only inside its declared period. Earlier
        // versions disabled manual trades after the statement end, which made a
        // newly imported older PDF remove newer portfolio activity.
        foreach (var tx in data.State.Transactions.Where(x =>
                     x.Source == "OPENING" && x.IsActive &&
                     InCoverage(x.Time, start, end)))
        {
            tx.IsActive = false;
            tx.SupersededByImportBatchId = batch.Id;
        }

        foreach (var tx in data.State.Transactions.Where(x =>
                     x.Source == "MANUAL" && x.IsActive &&
                     InCoverage(x.Time, start, end)))
        {
            tx.IsActive = false;
            tx.SupersededByImportBatchId = batch.Id;
            batch.SupersededManualCount++;
            batch.ReconciliationDetails.Add(
                $"{tx.Time:yyyy-MM-dd HH:mm} {tx.Side} {tx.Symbol} {tx.Lots} lot @ {tx.Price:N0} → dikoreksi history");
        }

        if (statement is not null)
        {
            var batches = data.State.TransactionImports.ToDictionary(x => x.Id);
            foreach (var tx in data.State.Transactions.Where(x =>
                         x.Source == "HISTORY" && x.IsActive &&
                         InCoverage(x.Time, start, end) &&
                         SameBrokerAccount(x, batches, accountKey)))
            {
                tx.IsActive = false;
                tx.SupersededByImportBatchId = batch.Id;
                batch.ReplacedHistoryCount++;
            }
        }

        var activeHistory = statement is null
            ? data.State.Transactions
                .Where(x => x.Source == "HISTORY" && x.IsActive)
                .GroupBy(IdentityKey)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);
        var incomingOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tx in rows.OrderBy(x => x.Time))
        {
            var key = IdentityKey(tx);
            incomingOccurrences.TryGetValue(key, out var seenInFile);
            seenInFile++;
            incomingOccurrences[key] = seenInFile;
            activeHistory.TryGetValue(key, out var alreadyStored);
            if (statement is null && seenInFile <= alreadyStored)
            {
                batch.SkippedDuplicateCount++;
                continue;
            }
            tx.Source = "HISTORY";
            tx.ImportBatchId = batch.Id;
            tx.IsActive = true;
            tx.BrokerAccountKey = accountKey;
            if (tx.Fee <= 0)
            {
                var rate = tx.Side == "BUY" ? data.State.BuyFeeRate : data.State.SellFeeRate;
                tx.Fee = decimal.Round(tx.GrossValue * rate, 0);
            }
            data.State.Transactions.Add(tx);
            batch.AddedCount++;
        }
        data.State.TransactionImports.Add(batch);
        data.RebuildPositions();
        // Reuse the latest completed market snapshot immediately. A newly
        // imported holding should appear on Dashboard/Portfolio with the most
        // recent known quote (and its timestamp) instead of waiting for the
        // user to download the same closing data again.
        var latestSnapshot = data.State.MarketSnapshots
            .Where(x => x.IsComplete && x.Symbols.Count > 0)
            .OrderByDescending(x => x.TradingDate)
            .ThenByDescending(x => x.CapturedAt)
            .FirstOrDefault();
        if (latestSnapshot is not null)
            data.ApplyMarketPrices(latestSnapshot);
        data.RecalculateCash();
        await data.SaveAsync();
        var feeNote = isPdf
            ? Loc.T(
                " Biaya broker diestimasi dengan tarif pada Pengaturan; cocokkan realized resmi saat Sync Up.",
                " Broker fees are estimated using the rates in Settings; reconcile the official realized P/L during Sync Up.")
            : "";
        return (true, Loc.T(
            $"Rekonsiliasi selesai untuk {rows.Count} baris, periode {start:dd MMM yyyy}–{end:dd MMM yyyy}: " +
            $"{batch.AddedCount} transaksi aktif, {batch.ReplacedHistoryCount} transaksi statement lama diganti, " +
            $"{batch.SkippedDuplicateCount} overlap dilewati, dan {batch.SupersededManualCount} transaksi manual dalam periode dikoreksi. " +
            $"Total BUY Rp{batch.ParsedBuyValue:N0} • SELL Rp{batch.ParsedSellValue:N0} • sales tax Rp{batch.ParsedSalesTax:N2}. " +
            $"Kas dan realized resmi yang sudah direkonsiliasi tetap dipertahankan.{feeNote}",
            $"Reconciliation completed for {rows.Count} rows covering {start:dd MMM yyyy}–{end:dd MMM yyyy}: " +
            $"{batch.AddedCount} active transactions, {batch.ReplacedHistoryCount} older statement transactions replaced, " +
            $"{batch.SkippedDuplicateCount} overlapping rows skipped, and {batch.SupersededManualCount} in-period manual transactions corrected. " +
            $"Total BUY Rp{batch.ParsedBuyValue:N0} • SELL Rp{batch.ParsedSellValue:N0} • sales tax Rp{batch.ParsedSalesTax:N2}. " +
            $"Previously reconciled cash and official realized P/L were preserved.{feeNote}"));
    }

    static bool InCoverage(DateTime value, DateTime start, DateTime end) =>
        value >= start && value <= end;

    static bool SameBrokerAccount(
        TradeTransaction tx,
        IReadOnlyDictionary<Guid, TransactionImportBatch> batches,
        string accountKey)
    {
        var existingKey = tx.BrokerAccountKey;
        if (string.IsNullOrWhiteSpace(existingKey) &&
            tx.ImportBatchId is { } batchId &&
            batches.TryGetValue(batchId, out var priorBatch))
            existingKey = AccountKey(priorBatch.AccountNumber, priorBatch.Sid);
        if (string.IsNullOrWhiteSpace(existingKey))
            existingKey = tx.Note.Contains("e-Statement Stockbit",
                StringComparison.OrdinalIgnoreCase) ? "STOCKBIT" : "";
        return SameAccountKey(existingKey, accountKey);
    }

    static bool SameAccountKey(string left, string right) =>
        left == "STOCKBIT" || right == "STOCKBIT" ||
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

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

    static StockbitStatement ParseStockbitPdf(string text)
    {
        var result = new List<TradeTransaction>();
        // PdfPig may return a visual row on one line or one token per line,
        // depending on the PDF producer. Starting at two dates and allowing
        // arbitrary whitespace handles both forms without depending on layout.
        var pattern = new System.Text.RegularExpressions.Regex(
            @"(?<date>\d{2}/\d{2}/\d{4})\s+(?<due>\d{2}/\d{2}/\d{4})\s+(?<symbol>[A-Z]{4,6})\s+(?<side>[BS])\s+(?<lot>[\d,]+)\s+(?<price>[\d,]+)\s+(?<buy>[\d,.]+)\s+(?<sell>[\d,.]+)\s+(?<tax>[\d,.]+)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        foreach (System.Text.RegularExpressions.Match match in pattern.Matches(text))
        {
            if (!TryDate(match.Groups["date"].Value, out var time) ||
                !TryDate(match.Groups["due"].Value, out var due) ||
                !TryUsNumber(match.Groups["lot"].Value, out var lot) ||
                !TryUsNumber(match.Groups["price"].Value, out var price) ||
                !TryUsNumber(match.Groups["buy"].Value, out var buyValue) ||
                !TryUsNumber(match.Groups["sell"].Value, out var sellValue) ||
                !TryUsNumber(match.Groups["tax"].Value, out var tax)) continue;
            var side = match.Groups["side"].Value == "B" ? "BUY" : "SELL";
            var gross = lot * 100m * price;
            var reported = side == "BUY" ? buyValue : sellValue;
            if (Math.Abs(gross - reported) > 1m) continue;
            result.Add(new()
            {
                Time = time, DueDate = due, Symbol = match.Groups["symbol"].Value,
                Side = side,
                Lots = (int)lot, Price = price, SalesTax = tax,
                Fee = 0, AffectsCash = true,
                ExternalId = $"STOCKBIT-{time:yyyyMMdd}-{result.Count + 1:D4}",
                Note = $"Impor e-Statement Stockbit; sales tax tercatat Rp {tax:N2}; total fee diestimasi dari tarif broker"
            });
        }
        var account = MatchValue(text, @"(?is)\bClient\s+(?<value>\d{5,})\b");
        var sid = MatchValue(text, @"(?is)\bSID\s+(?<value>[A-Z0-9]{8,})\b");
        DateTime? periodStart = null, periodEnd = null;
        var period = System.Text.RegularExpressions.Regex.Match(text,
            @"(?is)\bPeriod\s+(?<start>\d{2}/\d{2}/\d{4})\s*-\s*(?<end>\d{2}/\d{2}/\d{4})");
        if (period.Success && TryDate(period.Groups["start"].Value, out var parsedStart) &&
            TryDate(period.Groups["end"].Value, out var parsedEnd))
        {
            periodStart = parsedStart.Date;
            periodEnd = parsedEnd.Date;
        }

        decimal? declaredBuy = null, declaredSell = null,
            declaredSalesTax = null;
        var totals = System.Text.RegularExpressions.Regex.Match(text,
            @"(?is)\bTOTAL\s+Rp\s*(?<buy>[\d,.]+)\s+Rp\s*(?<sell>[\d,.]+)\s+Rp\s*(?<tax>[\d,.]+)");
        if (totals.Success &&
            TryUsNumber(totals.Groups["buy"].Value, out var totalBuy) &&
            TryUsNumber(totals.Groups["sell"].Value, out var totalSell))
        {
            declaredBuy = totalBuy;
            declaredSell = totalSell;
            if (TryUsNumber(
                    totals.Groups["tax"].Value,
                    out var totalSalesTax))
                declaredSalesTax = totalSalesTax;
        }
        var parsedBuy = result.Where(x => x.Side == "BUY").Sum(x => x.GrossValue);
        var parsedSell = result.Where(x => x.Side == "SELL").Sum(x => x.GrossValue);
        var parsedSalesTax = result.Sum(x => x.SalesTax);
        var totalsValidated = (!declaredBuy.HasValue || Math.Abs(declaredBuy.Value - parsedBuy) <= 1m) &&
                              (!declaredSell.HasValue || Math.Abs(declaredSell.Value - parsedSell) <= 1m) &&
                              (!declaredSalesTax.HasValue ||
                               Math.Abs(declaredSalesTax.Value - parsedSalesTax) <= .01m);
        return new StockbitStatement(
            result, account, sid, periodStart, periodEnd,
            parsedBuy, parsedSell, parsedSalesTax,
            declaredBuy, declaredSell, declaredSalesTax,
            totalsValidated);
    }

    static string MatchValue(string text, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, pattern);
        return match.Success ? match.Groups["value"].Value.Trim() : "";
    }

    static string AccountKey(string account, string sid) =>
        string.IsNullOrWhiteSpace(account) && string.IsNullOrWhiteSpace(sid)
            ? "STOCKBIT"
            : $"STOCKBIT:{account}:{sid}";

    sealed record StockbitStatement(
        List<TradeTransaction> Rows,
        string AccountNumber,
        string Sid,
        DateTime? PeriodStart,
        DateTime? PeriodEnd,
        decimal ParsedBuyValue,
        decimal ParsedSellValue,
        decimal ParsedSalesTax,
        decimal? DeclaredBuyValue,
        decimal? DeclaredSellValue,
        decimal? DeclaredSalesTax,
        bool TotalsValidated)
    {
        public string AccountKey => TransactionHistoryService.AccountKey(
            AccountNumber, Sid);
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
