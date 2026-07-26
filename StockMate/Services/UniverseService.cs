using System.Text.Json;
using System.Net.Http.Headers;
using StockMate.Models;
using StockMate.Ui;

namespace StockMate.Services;

public sealed class UniverseService(AppDataService data)
{
    static readonly string[] FallbackUniverse =
    [
        "AADI","ACES","ADMR","ADRO","AKRA","AMMN","AMRT","ANTM","ARTO","ASII",
        "AVIA","BBCA","BBNI","BBRI","BBTN","BDMN","BFIN","BMRI","BRIS","BRMS",
        "BRPT","BSDE","BTPS","BUKA","BUMI","CMRY","CPIN","CTRA","DOID","DSNG",
        "ELSA","EMTK","ENRG","ERAA","ESSA","EXCL","GOTO","HEAL","HRUM","ICBP",
        "INCO","INDF","INDY","INKP","INTP","ISAT","ITMG","JPFA","JSMR","KLBF",
        "LSIP","MAPA","MAPI","MBMA","MDKA","MEDC","MIKA","MNCN","MTEL","MYOR",
        "NCKL","PGAS","PGEO","PTBA","PWON","RAJA","SCMA","SIDO","SMDR","SMGR",
        "SMRA","SRTG","SSIA","TBIG","TINS","TKIM","TLKM","TOWR","TPIA","UNTR",
        "UNVR","WIFI","WOOD","AUTO","BJBR","BJTM","CLEO","DMAS","ELPI","GJTL",
        "IMAS","ISSP","MARK","PNBN","PRDA","RALS","SMSM","TAPG","ULTJ"
    ];

    static readonly HashSet<string> Speculative =
        new(["BREN","CUAN","DEWA","DOOH","INET","MINA","PTRO","RATU","PANI","DCII","DSSA","MLPT"]);

    public IReadOnlyList<string> Symbols =>
        data.State.MarketUniverse.Count > 0 ? data.State.MarketUniverse : FallbackUniverse;

    public bool HasFullUniverse => data.State.MarketUniverse.Count >= 500;

    public bool IsSpeculative(string symbol) => Speculative.Contains(symbol);

    public async Task<(int Count, bool Updated, string Message)> EnsureCurrentAsync(
        bool force, CancellationToken ct, IProgress<ScanProgress>? progress = null)
    {
        if (!force && data.State.MarketUniverse.Count > 500 &&
            data.State.UniverseUpdatedAt > DateTime.Now.AddDays(-7))
            return (data.State.MarketUniverse.Count, false, Loc.T(
                "Master IDX masih baru.",
                "The IDX master list is still current."));

        try
        {
            var started = DateTime.UtcNow;
            progress?.Report(new()
            {
                Stage = "UNIVERSE_REQUEST",
                Message = Loc.T(
                    "Menghubungi master emiten IDX",
                    "Requesting the IDX issuer master"),
                Source = "IDX Listed Company",
                Total = Symbols.Count,
                TechnicalDetail = Loc.T(
                    "HTTP GET dimulai • timeout 15 detik • cache tetap tersedia",
                    "HTTP GET started • 15-second timeout • cache remains available")
            });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 Chrome/124 Mobile Safari/537.36");
            http.DefaultRequestHeaders.Referrer = new Uri("https://www.idx.co.id/id/data-pasar/data-saham/daftar-saham/");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var url = "https://www.idx.co.id/primary/ListedCompany/GetCompanyProfiles?start=0&length=2000";
            using var response = await http.GetAsync(url, ct);
            progress?.Report(new()
            {
                Stage = "UNIVERSE_PARSE",
                Message = Loc.T(
                    $"Respons IDX diterima: HTTP {(int)response.StatusCode}",
                    $"IDX response received: HTTP {(int)response.StatusCode}"),
                Source = "IDX Listed Company",
                Total = Symbols.Count,
                ElapsedMilliseconds = (long)(DateTime.UtcNow - started).TotalMilliseconds,
                TechnicalDetail = Loc.T(
                    "Memvalidasi respons dan mengekstrak kode emiten",
                    "Validating the response and extracting issuer symbols")
            });
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectCodes(doc.RootElement, found);
            var symbols = found.Where(IsValidCode).OrderBy(x => x).ToArray();
            if (symbols.Length < 500)
                throw new InvalidDataException(Loc.T(
                    $"Sumber hanya mengembalikan {symbols.Length} kode.",
                    $"The source returned only {symbols.Length} symbols."));
            await data.SetUniverseAsync(symbols);
            data.State.UniverseUpdatedAt = DateTime.Now;
            data.State.UniverseSource = "IDX Listed Company";
            await data.SaveAsync();
            progress?.Report(new()
            {
                Stage = "UNIVERSE_READY",
                Message = Loc.T(
                    $"Universe siap: {symbols.Length} saham",
                    $"Universe ready: {symbols.Length} stocks"),
                Source = "IDX Listed Company",
                Total = symbols.Length,
                ElapsedMilliseconds = (long)(DateTime.UtcNow - started).TotalMilliseconds,
                TechnicalDetail = Loc.T(
                    "Universe disimpan; pembentukan batch dapat dimulai",
                    "Universe saved; batch creation can begin")
            });
            return (symbols.Length, true, Loc.T(
                "Master IDX berhasil diperbarui.",
                "The IDX master list was updated."));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var count = Symbols.Count;
            progress?.Report(new()
            {
                Stage = "UNIVERSE_FALLBACK",
                Message = Loc.T(
                    $"Refresh IDX gagal; memakai cache {count} saham",
                    $"IDX refresh failed; using {count} cached stocks"),
                Source = Loc.T("cache lokal", "local cache"),
                Total = count,
                Failed = 1,
                TechnicalDetail = $"{ex.GetType().Name}: {ex.Message}"
            });
            return (count, false,
                data.State.MarketUniverse.Count > 0
                    ? Loc.T(
                        $"Pembaruan gagal; memakai cache {count} saham. {ex.Message}",
                        $"The update failed; using {count} cached stocks. {ex.Message}")
                    : Loc.T(
                        $"Pembaruan gagal; sementara memakai fallback {count} saham. Impor universe bila perlu. {ex.Message}",
                        $"The update failed; temporarily using {count} fallback stocks. Import a universe if needed. {ex.Message}"));
        }
    }

    static void CollectCodes(JsonElement node, HashSet<string> output)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("KodeEmiten", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("ticker", StringComparison.OrdinalIgnoreCase))
                {
                    var value = property.Value.ToString().Trim().ToUpperInvariant();
                    if (IsValidCode(value)) output.Add(value);
                }
                CollectCodes(property.Value, output);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) CollectCodes(child, output);
    }

    static bool IsValidCode(string value) =>
        value.Length is >= 4 and <= 6 && value.All(char.IsLetterOrDigit);

    public async Task<int> ImportAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        var symbols = text
            .Split([',',';','\t','\r','\n',' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().Trim('"'))
            .Where(x => x.All(c => char.IsLetterOrDigit(c) || c == '.'));
        await data.SetUniverseAsync(symbols);
        return data.State.MarketUniverse.Count;
    }

    public async Task ExportAsync(string path) =>
        await File.WriteAllLinesAsync(path, Symbols);
}
