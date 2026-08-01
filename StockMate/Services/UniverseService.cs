using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using StockMate.Models;
using StockMate.Ui;

namespace StockMate.Services;

public sealed class UniverseService(AppDataService data)
{
    public const string RecoverySnapshotDate = "2026-07-25";
    static readonly string[] FallbackUniverse =
    [
        "AADI","AALI","ABBA","ABDA","ABMM","ACES","ACRO","ACST","ADCP","ADES","ADHI","ADMF",
        "ADMG","ADMR","ADRO","AEGS","AGAR","AGII","AGRO","AGRS","AHAP","AIMS","AISA","AKKU",
        "AKPI","AKRA","AKSI","ALDO","ALII","ALKA","ALMI","ALTO","AMAG","AMAN","AMAR","AMFG",
        "AMIN","AMMN","AMMS","AMOR","AMRT","ANDI","ANJT","ANTM","APEX","APIC","APII","APLI",
        "APLN","ARCI","AREA","ARGO","ARII","ARKA","ARKO","ARMY","ARNA","ARTA","ARTI","ARTO",
        "ASBI","ASDM","ASGR","ASHA","ASII","ASJT","ASLC","ASLI","ASMI","ASPI","ASPR","ASRI",
        "ASRM","ASSA","ATAP","ATIC","ATLA","AUTO","AVIA","AWAN","AXIO","AYAM","AYLS","BABP",
        "BABY","BACA","BACH","BAIK","BAJA","BALI","BANK","BAPA","BAPI","BATA","BATR","BAUT",
        "BAYU","BBCA","BBHI","BBKP","BBLD","BBMD","BBNI","BBRI","BBRM","BBSI","BBSS","BBTN",
        "BBYB","BCAP","BCIC","BCIP","BDKR","BDMN","BEBS","BEEF","BEER","BEKS","BELI","BELL",
        "BESS","BEST","BFIN","BGTG","BHAT","BHIT","BIKA","BIKE","BIMA","BINA","BINO","BIPI",
        "BIPP","BIRD","BISI","BJBR","BJTM","BKDP","BKSL","BKSW","BLES","BLOG","BLTA","BLTZ",
        "BLUE","BMAS","BMBL","BMHS","BMRI","BMSR","BMTR","BNBA","BNBR","BNGA","BNII","BNLI",
        "BOAT","BOBA","BOGA","BOLA","BOLT","BOSS","BPFI","BPII","BPTR","BRAM","BREN","BRIS",
        "BRMS","BRNA","BRPT","BRRC","BSBK","BSDE","BSIM","BSML","BSSR","BSWD","BTEK","BTEL",
        "BTON","BTPN","BTPS","BUAH","BUDI","BUKA","BUKK","BULL","BUMI","BUVA","BVIC","BWPT",
        "BYAN","CAKK","CAMP","CANI","CARE","CARS","CASA","CASH","CASS","CBDK","CBMF","CBPE",
        "CBRE","CBUT","CCSI","CDIA","CEKA","CENT","CFIN","CGAS","CHEK","CHEM","CHIP","CINT",
        "CITA","CITY","CLAY","CLEO","CLPI","CMNP","CMNT","CMPP","CMRY","CNKO","CNMA","CNTX",
        "COAL","COCO","COIN","COWL","CPIN","CPRI","CPRO","CRAB","CRSN","CSAP","CSIS","CSMI",
        "CSRA","CTBN","CTRA","CTTH","CUAN","CYBR","DAAZ","DADA","DART","DATA","DAYA","DCII",
        "DEAL","DEFI","DEPO","DEWA","DEWI","DFAM","DGIK","DGNS","DGWG","DIGI","DILD","DIVA",
        "DKFT","DKHH","DLTA","DMAS","DMMX","DMND","DNAR","DNET","DOID","DOOH","DOSS","DPNS",
        "DPUM","DRMA","DSFI","DSNG","DSSA","DUCK","DUTI","DVLA","DWGL","DYAN","EAST","ECII",
        "EDGE","EKAD","ELIT","ELPI","ELSA","ELTY","EMAS","EMDE","EMMI","EMTK","ENAK","ENRG",
        "ENVY","ENZO","EPAC","EPMT","ERAA","ERAL","ERTX","ESIP","ESSA","ESTA","ESTI","ETWA",
        "EURO","EXCL","FAPA","FAST","FASW","FILM","FIMP","FIRE","FISH","FITT","FLMC","FMII",
        "FOLK","FOOD","FORE","FORU","FPNI","FUJI","FUTR","FWCT","GAMA","GDST","GDYR","GEMA",
        "GEMS","GGRM","GGRP","GHON","GIAA","GJTL","GLOB","GLVA","GMFI","GMTD","GOLD","GOLF",
        "GOLL","GOOD","GOTO","GPRA","GPSO","GRIA","GRPH","GRPM","GSMF","GTBO","GTRA","GTSI",
        "GULA","GUNA","GWSA","GZCO","HADE","HAIS","HAJJ","HALO","HATM","HBAT","HDFA","HDIT",
        "HEAL","HELI","HERO","HEXA","HGII","HILL","HITS","HKMU","HMSP","HOKI","HOME","HOMI",
        "HOPE","HOTL","HRME","HRTA","HRUM","HUMI","HYGN","IATA","IBFN","IBOS","IBST","ICBP",
        "ICON","IDEA","IDPR","IFII","IFSH","IGAR","IIKP","IKAI","IKAN","IKBI","IKPM","IMAS",
        "IMJS","IMPC","INAF","INAI","INCF","INCI","INCO","INDF","INDO","INDR","INDS","INDX",
        "INDY","INET","INKP","INOV","INPC","INPP","INPS","INRU","INTA","INTD","INTP","IOTF",
        "IPAC","IPCC","IPCM","IPOL","IPPE","IPTV","IRRA","IRSX","ISAP","ISAT","ISEA","ISSP",
        "ITIC","ITMA","ITMG","JARR","JAST","JATI","JAWA","JAYA","JECC","JECX","JELI","JGLE",
        "JIHD","JKON","JMAS","JPFA","JRPT","JSKY","JSMR","JSPT","JTPE","KAEF","KAQI","KARW",
        "KAYU","KBAG","KBLI","KBLM","KBLV","KBRI","KDSI","KDTN","KEEN","KEJU","KETR","KIAS",
        "KICI","KIJA","KING","KINO","KIOS","KJEN","KKES","KKGI","KLAS","KLBF","KLIN","KMDS",
        "KMTR","KOBX","KOCI","KOIN","KOKA","KONI","KOPI","KOTA","KPIG","KRAS","KREN","KRYA",
        "KSIX","KUAS","LABA","LABS","LAJU","LAND","LAPD","LCGP","LCKM","LEAD","LFLO","LIFE",
        "LINK","LION","LIVE","LMAS","LMAX","LMPI","LMSH","LOPI","LPCK","LPGI","LPIN","LPKR",
        "LPLI","LPPF","LPPS","LRNA","LSIP","LTLS","LUCK","LUCY","MABA","MAGP","MAHA","MAIN",
        "MANG","MAPA","MAPB","MAPI","MARI","MARK","MASB","MAXI","MAYA","MBAP","MBMA","MBSS",
        "MBTO","MCAS","MCOL","MCOR","MDIA","MDIY","MDKA","MDKI","MDLA","MDLN","MDRN","MEDC",
        "MEDS","MEGA","MEJA","MENN","MERI","MERK","META","MFMI","MGLV","MGNA","MGRO","MHKI",
        "MICE","MIDI","MIKA","MINA","MINE","MIRA","MITI","MKAP","MKNT","MKPI","MKTR","MLBI",
        "MLIA","MLPL","MLPT","MMIX","MMLP","MNCN","MOLI","MORA","MPIX","MPMX","MPOW","MPPA",
        "MPRO","MPXL","MRAT","MREI","MSIE","MSIN","MSJA","MSKY","MSTI","MTDL","MTEL","MTFN",
        "MTLA","MTMH","MTPS","MTRA","MTSM","MTWI","MUTU","MYOH","MYOR","MYTX","NAIK","NANO",
        "NASA","NASI","NATO","NAYZ","NCKL","NELY","NEST","NETV","NFCX","NICE","NICK","NICL",
        "NIKL","NINE","NIRO","NISP","NOBU","NPGF","NRCA","NSSS","NTBK","NUSA","NZIA","OASA",
        "OBAT","OBMD","OCAP","OILS","OKAS","OLIV","OMED","OMRE","OPMS","PACK","PADA","PADI",
        "PALM","PAMG","PANI","PANR","PANS","PART","PBID","PBRX","PBSA","PCAR","PDES","PDPP",
        "PEGE","PEHA","PEVE","PGAS","PGEO","PGJO","PGLI","PGUN","PICO","PIPA","PJAA","PJHB",
        "PKPK","PLAN","PLAS","PLIN","PMJS","PMMP","PMUI","PNBN","PNBS","PNGO","PNIN","PNLF",
        "PNSE","POLA","POLI","POLL","POLU","POLY","POOL","PORT","POSA","POWR","PPGL","PPRE",
        "PPRI","PPRO","PRAY","PRDA","PRDL","PRIM","PSAB","PSAT","PSDN","PSGO","PSKT","PSSI",
        "PTBA","PTDU","PTIS","PTMP","PTMR","PTPP","PTPS","PTPW","PTRO","PTSN","PTSP","PUDP",
        "PURA","PURE","PURI","PWON","PYFA","PZZA","RAAM","RAFI","RAJA","RALS","RANC","RANS",
        "RATU","RBMS","RCCC","RDTX","REAL","RELF","RELI","RGAS","RICY","RIGS","RIMO","RISE",
        "RLCO","RMKE","RMKO","ROCK","RODA","RONY","ROTI","RSCH","RSGK","RUIS","RUNS","SAFE",
        "SAGE","SAME","SAMF","SAPX","SATU","SBAT","SBMA","SCCO","SCMA","SCNP","SCPI","SDMU",
        "SDPC","SDRA","SEMA","SFAN","SGER","SGRO","SHID","SHIP","SICO","SIDO","SILO","SIMA",
        "SIMP","SINI","SIPD","SKBM","SKLT","SKRN","SKYB","SLIS","SMAR","SMBR","SMCB","SMDM",
        "SMDR","SMGA","SMGR","SMIL","SMKL","SMKM","SMLE","SMMA","SMMT","SMRA","SMRU","SMSM",
        "SNLK","SOCI","SOFA","SOHO","SOLA","SONA","SOSS","SOTS","SOUL","SPMA","SPRE","SPTO",
        "SQMI","SRAJ","SRIL","SRSN","SRTG","SSIA","SSMS","SSTM","STAA","STAR","STRK","STTP",
        "SUGI","SULI","SUNI","SUPA","SUPR","SURE","SURI","SWAT","SWID","TALF","TAMA","TAMU",
        "TAPG","TARA","TAXI","TAYS","TBIG","TBLA","TBMS","TCID","TCPI","TDPM","TEBE","TECH",
        "TELE","TFAS","TFCO","TGKA","TGRA","TGUK","TIFA","TINS","TIRA","TIRT","TKIM","TLDN",
        "TLKM","TMAS","TMPO","TNCA","TOBA","TOOL","TOPS","TOSK","TOTL","TOTO","TOWR","TOYS",
        "TPIA","TPMA","TRAM","TRGU","TRIL","TRIM","TRIN","TRIO","TRIS","TRJA","TRON","TRST",
        "TRUE","TRUK","TRUS","TSPC","TUGU","TYRE","UANG","UCID","UDNG","UFOE","ULTJ","UNIC",
        "UNIQ","UNIT","UNSP","UNTD","UNTR","UNVR","URBN","UVCR","VAST","VERN","VICI","VICO",
        "VINS","VISI","VIVA","VKTR","VOKS","VRNA","VTNY","WAPO","WBSA","WEGE","WEHA","WGSH",
        "WICO","WIDI","WIFI","WIIM","WIKA","WINE","WINR","WINS","WIRG","WMPP","WMUU","WOMF",
        "WOOD","WOWS","WSBP","WSKT","WTON","YELO","YOII","YPAS","YULE","YUPI","ZATA","ZBRA",
        "ZINC","ZONE","ZYRX",
    ];

    static readonly HashSet<string> Speculative =
        new(["BREN","CUAN","DEWA","DOOH","INET","MINA","PTRO","RATU","PANI","DCII","DSSA","MLPT"]);

    public IReadOnlyList<string> Symbols =>
        data.State.MarketUniverse.Count >= 500
            ? data.State.MarketUniverse
            : FallbackUniverse;

    public bool UsingRecoverySnapshot =>
        data.State.MarketUniverse.Count < 500 && FallbackUniverse.Length >= 500;

    public bool HasFullUniverse => Symbols.Count >= 500;

    public string SourceLabel => UsingRecoverySnapshot
        ? Loc.T(
            $"snapshot pemulihan {RecoverySnapshotDate}",
            $"recovery snapshot {RecoverySnapshotDate}")
        : string.IsNullOrWhiteSpace(data.State.UniverseSource)
            ? Loc.T("cache lokal", "local cache")
            : data.State.UniverseSource;

    public bool IsSpeculative(string symbol) => Speculative.Contains(symbol);

    public async Task<(int Count, bool Updated, string Message)> EnsureCurrentAsync(
        bool force, CancellationToken ct, IProgress<ScanProgress>? progress = null)
    {
        if (!force && data.State.MarketUniverse.Count >= 500 &&
            data.State.UniverseUpdatedAt?.Date == DateTime.Today)
            return (data.State.MarketUniverse.Count, false, Loc.T(
                "Master IDX sudah diperbarui hari ini.",
                "The IDX master list was already refreshed today."));

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
                    "HTTP GET dimulai • timeout 20 detik • cache tetap tersedia",
                    "HTTP GET started • 20-second timeout • cache remains available")
            });
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                CookieContainer = new CookieContainer()
            };
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 Chrome/124 Mobile Safari/537.36");
            var landing = new Uri(
                "https://www.idx.co.id/id/data-pasar/data-saham/daftar-saham/");
            http.DefaultRequestHeaders.Referrer = landing;
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // The landing request establishes the same session cookies used by
            // the official stock-list page. A blocked landing page is harmless;
            // the JSON endpoints are still attempted independently.
            try
            {
                using var warmup = await http.GetAsync(landing, ct);
            }
            catch (HttpRequestException) { }

            var endpoints = new[]
            {
                "https://www.idx.co.id/primary/ListedCompany/GetCompanyProfiles?start=0&length=2000",
                "https://www.idx.co.id/primary/StockData/GetSecuritiesStock?start=0&length=2000"
            };
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            foreach (var url in endpoints)
            {
                try
                {
                    using var response = await http.GetAsync(url, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        errors.Add($"HTTP {(int)response.StatusCode}");
                        continue;
                    }
                    using var doc = JsonDocument.Parse(
                        await response.Content.ReadAsStreamAsync(ct));
                    CollectCodes(doc.RootElement, found);
                    if (found.Count >= 500) break;
                    errors.Add($"{found.Count} kode");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"{ex.GetType().Name}: {ex.Message}");
                }
            }
            progress?.Report(new()
            {
                Stage = "UNIVERSE_PARSE",
                Message = Loc.T(
                    $"Respons IDX diproses: {found.Count} kode",
                    $"IDX response processed: {found.Count} symbols"),
                Source = "IDX Listed Company",
                Total = Symbols.Count,
                ElapsedMilliseconds = (long)(DateTime.UtcNow - started).TotalMilliseconds,
                TechnicalDetail = Loc.T(
                    "Memvalidasi respons dan mengekstrak kode emiten",
                    "Validating the response and extracting issuer symbols")
            });
            var symbols = found.Where(IsValidCode).OrderBy(x => x).ToArray();
            if (symbols.Length < 500)
                throw new InvalidDataException(Loc.T(
                    $"Sumber IDX hanya mengembalikan {symbols.Length} kode ({string.Join("; ", errors.Take(2))}).",
                    $"The IDX source returned only {symbols.Length} symbols ({string.Join("; ", errors.Take(2))})."));
            data.State.UniverseUpdatedAt = DateTime.Now;
            data.State.UniverseSource = "IDX official stock list";
            await data.SetUniverseAsync(symbols);
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
                Message = UsingRecoverySnapshot
                    ? Loc.T(
                        $"Refresh IDX gagal; memakai snapshot pemulihan {count} saham",
                        $"IDX refresh failed; using the {count}-stock recovery snapshot")
                    : Loc.T(
                        $"Refresh IDX gagal; memakai cache {count} saham",
                        $"IDX refresh failed; using {count} cached stocks"),
                Source = SourceLabel,
                Total = count,
                Failed = 1,
                TechnicalDetail = $"{ex.GetType().Name}: {ex.Message}"
            });
            return (count, false,
                data.State.MarketUniverse.Count >= 500
                    ? Loc.T(
                        $"Pembaruan gagal; memakai cache {count} saham. {ex.Message}",
                        $"The update failed; using {count} cached stocks. {ex.Message}")
                    : Loc.T(
                        $"Pembaruan online gagal; sementara memakai snapshot pemulihan {RecoverySnapshotDate} berisi {count} saham. StockMate akan mencoba lagi otomatis. {ex.Message}",
                        $"The online update failed; temporarily using the {RecoverySnapshotDate} recovery snapshot with {count} stocks. StockMate will retry automatically. {ex.Message}"));
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
                    property.Name.Equals("KodeSaham", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("StockCode", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("Symbol", StringComparison.OrdinalIgnoreCase) ||
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

}
