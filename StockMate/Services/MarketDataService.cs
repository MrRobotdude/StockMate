using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using StockMate.Models;
using StockMate.Ui;

namespace StockMate.Services;

public sealed class MarketDataService
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<List<Candle>> GetCandlesAsync(string symbol, bool intraday, CancellationToken ct)
    {
        var interval = intraday ? "15m" : "1d";
        var range = intraday ? "5d" : "6mo";
        HttpResponseMessage? response = null;
        foreach (var host in new[] { "query1.finance.yahoo.com", "query2.finance.yahoo.com" })
        {
            var url = $"https://{host}/v8/finance/chart/{symbol}.JK?interval={interval}&range={range}&events=div%2Csplits";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 Chrome/126 Mobile Safari/537.36");
            request.Headers.Accept.ParseAdd("application/json,text/plain,*/*");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                throw new MarketRateLimitException(retryAfter);
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                response.Dispose();
                response = null;
                continue;
            }
            break;
        }
        if (response is null)
            throw new MarketAccessForbiddenException();
        using (response)
        {
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var chart = doc.RootElement.GetProperty("chart");
            if (chart.TryGetProperty("error", out var error) &&
                error.ValueKind is not JsonValueKind.Null)
                throw new HttpRequestException(Loc.T(
                    $"Sumber harga mengembalikan error: {error}",
                    $"The price source returned an error: {error}"));
            var result = chart.GetProperty("result")[0];
            return ParseCandles(result);
        }
    }

    static List<Candle> ParseCandles(JsonElement result)
    {
        var timestamps = result.GetProperty("timestamp");
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");
        var candles = new List<Candle>();
        for (var i = 0; i < timestamps.GetArrayLength(); i++)
        {
            if (closes[i].ValueKind == JsonValueKind.Null) continue;
            candles.Add(new Candle
            {
                Time = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).LocalDateTime,
                Open = ReadDecimal(opens[i]),
                High = ReadDecimal(highs[i]),
                Low = ReadDecimal(lows[i]),
                Close = ReadDecimal(closes[i]),
                Volume = volumes[i].ValueKind == JsonValueKind.Null ? 0 : volumes[i].GetInt64()
            });
        }
        return candles;
    }

    static decimal ReadDecimal(JsonElement e) =>
        e.ValueKind == JsonValueKind.Null ? 0 : Convert.ToDecimal(e.GetDouble());
}

public sealed class MarketAccessForbiddenException()
    : HttpRequestException(Loc.T(
        "Akses harga ditolak sementara (HTTP 403) pada kedua endpoint. Retry otomatis akan memakai sesi request baru.",
        "Price access was temporarily denied (HTTP 403) on both endpoints. Automatic retry will use a new request session."))
{
}

public sealed class MarketRateLimitException(TimeSpan? retryAfter)
    : HttpRequestException(Loc.T(
        "Sumber data membatasi request (HTTP 429).",
        "The data source is rate-limiting requests (HTTP 429)."))
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
