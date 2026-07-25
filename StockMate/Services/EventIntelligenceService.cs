using System.Net;
using System.Xml.Linq;
using StockMate.Models;

namespace StockMate.Services;

public sealed class EventIntelligenceService(AppDataService data)
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    static readonly string[] Positive =
    [
        "laba naik", "profit rises", "beat expectations", "dividen", "buyback",
        "kontrak baru", "new contract", "upgrade", "ekspansi", "acquisition"
    ];
    static readonly string[] Negative =
    [
        "laba turun", "profit falls", "miss expectations", "rights issue",
        "dilusi", "downgrade", "suspensi", "uma", "gugatan", "default",
        "fraud", "investigasi", "investigation"
    ];

    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        await data.LoadAsync();
        var symbols = data.State.Positions.Select(x => x.Symbol)
            .Concat(data.State.LastScan.OrderByDescending(x => x.Score).Take(20).Select(x => x.Symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();

        var jobs = new List<(string Symbol, string Query)> { ("MARKET", "IHSG OR \"Bank Indonesia\" OR rupiah") };
        jobs.AddRange(symbols.Select(x => (x, $"\"{x}\" saham OR emiten")));
        using var gate = new SemaphoreSlim(3);
        var tasks = jobs.Select(async job =>
        {
            await gate.WaitAsync(ct);
            try { return await FetchAsync(job.Symbol, job.Query, ct); }
            finally { gate.Release(); }
        });
        var fresh = (await Task.WhenAll(tasks)).SelectMany(x => x)
            .Where(x => x.PublishedAt >= DateTime.Now.AddDays(-7))
            .GroupBy(x => x.Url)
            .Select(x => x.First())
            .OrderByDescending(x => x.PublishedAt).ToList();

        data.State.EventInsights = fresh;
        data.State.EventIntelligenceUpdatedAt = DateTime.Now;
        await data.SaveAsync();
        return fresh.Count;
    }

    static async Task<List<EventInsight>> FetchAsync(string symbol, string query, CancellationToken ct)
    {
        var url = "https://news.google.com/rss/search?q=" +
                  Uri.EscapeDataString(query + " when:7d") +
                  "&hl=id&gl=ID&ceid=ID:id";
        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var feed = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        return feed.Descendants("item").Take(5).Select(item =>
        {
            var title = WebUtility.HtmlDecode((string?)item.Element("title") ?? "").Trim();
            var impact = Score(title);
            var published = DateTime.TryParse((string?)item.Element("pubDate"), out var parsed)
                ? parsed : DateTime.Now;
            return new EventInsight
            {
                Symbol = symbol,
                Title = title,
                Source = (string?)item.Element("source") ?? SourceFromTitle(title),
                Url = (string?)item.Element("link") ?? "",
                PublishedAt = published.ToLocalTime(),
                Impact = impact,
                Direction = impact > 0 ? "POSITIF" : impact < 0 ? "NEGATIF" : "NETRAL",
                Reason = impact == 0
                    ? "Judul tidak memberi sinyal dampak yang cukup jelas; tidak mengubah skor."
                    : $"Penyesuaian terbatas {impact:+#;-#;0} poin berdasarkan kata kunci pada judul berita.",
                RetrievedAt = DateTime.Now
            };
        }).ToList();
    }

    static int Score(string title)
    {
        var text = title.ToLowerInvariant();
        var positive = Positive.Count(x => text.Contains(x));
        var negative = Negative.Count(x => text.Contains(x));
        return Math.Clamp((positive - negative) * 3, -6, 6);
    }

    static string SourceFromTitle(string title)
    {
        var split = title.LastIndexOf(" - ", StringComparison.Ordinal);
        return split > 0 ? title[(split + 3)..] : "Google News";
    }

    public (int Adjustment, string Summary) Summarize(string symbol)
    {
        var relevant = data.State.EventInsights
            .Where(x => x.Symbol == "MARKET" ||
                        x.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.PublishedAt >= DateTime.Now.AddDays(-3))
            .OrderByDescending(x => x.PublishedAt).ToList();
        if (relevant.Count == 0)
            return (0, "Data isu terbaru tidak cukup; keputusan tetap berbasis teknikal.");
        var adjustment = Math.Clamp(relevant.Sum(x => x.Impact), -8, 6);
        var strongest = relevant.OrderByDescending(x => Math.Abs(x.Impact)).First();
        return (adjustment,
            $"{strongest.Direction}: {strongest.Title} ({strongest.Source}, {strongest.PublishedAt:dd MMM HH:mm}).");
    }
}
