namespace StockMate.Ui;

public static class Loc
{
    public static bool English { get; private set; }
    public static void Use(string? code) => English =
        string.Equals(code, "en", StringComparison.OrdinalIgnoreCase);
    public static string T(string id, string en) => English ? en : id;
}
