namespace StockMate.Ui;

public static class AppDialog
{
    public static async Task<string?> SelectSymbolAsync(
        Page owner, IEnumerable<string> symbols, string title = "Pilih kode saham")
    {
        var all = symbols
            .Select(x => x.Trim().ToUpperInvariant().Replace(".JK", ""))
            .Where(x => x.Length is >= 4 and <= 6)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        if (all.Count == 0)
        {
            await ShowAsync(owner, "Universe belum tersedia",
                "Jalankan Sync Up universe IDX terlebih dahulu agar kode saham dapat dipilih tanpa typo.");
            return null;
        }

        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closingFromButton = false;
        var page = new ContentPage
        {
            Title = Loc.T(title),
            BackgroundColor = UiKit.Navy
        };
        var search = new SearchBar
        {
            Placeholder = Loc.T("Cari kode, contoh TLKM"),
            TextColor = Colors.White,
            PlaceholderColor = UiKit.Muted,
            BackgroundColor = UiKit.Surface
        };
        var list = new VerticalStackLayout { Spacing = 8 };
        var scroll = new ScrollView { Content = list };
        void Render(string? query)
        {
            list.Children.Clear();
            var matches = string.IsNullOrWhiteSpace(query)
                ? all.Take(40)
                : all.Where(x => x.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).Take(40);
            foreach (var symbol in matches)
            {
                var button = UiKit.Secondary(symbol);
                button.Clicked += async (_, _) =>
                {
                    closingFromButton = true;
                    await CloseModalAsync(owner.Navigation);
                    completion.TrySetResult(symbol);
                };
                list.Children.Add(button);
            }
            if (list.Children.Count == 0)
                list.Children.Add(UiKit.EmptyState("⌕", "Kode tidak ditemukan",
                    "Pilih hanya kode yang tersedia di universe IDX."));
        }
        search.TextChanged += (_, e) => Render(e.NewTextValue);
        var cancel = UiKit.Tertiary("Batal");
        cancel.Clicked += async (_, _) =>
        {
            closingFromButton = true;
            await CloseModalAsync(owner.Navigation);
            completion.TrySetResult(null);
        };
        var layout = new Grid
        {
            Padding = new Thickness(18),
            RowDefinitions =
            [
                new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)
            ]
        };
        layout.Add(search, 0, 0);
        layout.Add(scroll, 0, 1);
        layout.Add(cancel, 0, 2);
        page.Content = layout;
        page.Disappearing += (_, _) =>
        {
            if (!closingFromButton)
                completion.TrySetResult(null);
        };
        Render(null);
        await owner.Navigation.PushModalAsync(new NavigationPage(page));
        search.Focus();
        return await completion.Task;
    }

    public static Task ShowAsync(Page owner, string title, string message,
        string button = "Tutup", bool danger = false) =>
        ShowCoreAsync(owner, title, message, button, null, danger);

    public static async Task<bool> ConfirmAsync(Page owner, string title,
        string message, string confirm = "Lanjutkan", string cancel = "Batal",
        bool danger = false) =>
        await ShowCoreAsync(owner, title, message, confirm, cancel, danger);

    public static async Task<string?> PromptAsync(Page owner, string title,
        string message, string initialValue = "", Keyboard? keyboard = null)
    {
        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closingFromButton = false;
        var entry = new Entry
        {
            Text = initialValue, Keyboard = keyboard ?? Keyboard.Default,
            TextColor = Colors.White, PlaceholderColor = UiKit.Muted,
            BackgroundColor = UiKit.Surface, HeightRequest = 50
        };
        var page = new ContentPage { BackgroundColor = Color.FromArgb("#B3000712") };
        var save = UiKit.Primary(Loc.T("Simpan"));
        var cancel = UiKit.Secondary(Loc.T("Batal"));
        save.Clicked += async (_, _) =>
        {
            closingFromButton = true;
            await CloseModalAsync(page.Navigation);
            completion.TrySetResult(entry.Text);
        };
        cancel.Clicked += async (_, _) =>
        {
            closingFromButton = true;
            await CloseModalAsync(page.Navigation);
            completion.TrySetResult(null);
        };
        var card = UiKit.Box(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label { Text = Loc.T(title), FontSize = 21, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White },
                UiKit.Sub(Loc.T(message)), entry, save, cancel
            }
        });
        card.Margin = new Thickness(24);
        page.Content = new Grid { VerticalOptions = LayoutOptions.Center, Children = { card } };
        page.Disappearing += (_, _) =>
        {
            if (!closingFromButton)
                completion.TrySetResult(null);
        };
        await owner.Navigation.PushModalAsync(page);
        entry.Focus();
        return await completion.Task;
    }

    static async Task<bool> ShowCoreAsync(Page owner, string title, string message,
        string confirm, string? cancel, bool danger)
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closingFromButton = false;
        var page = new ContentPage { BackgroundColor = Color.FromArgb("#B3000712") };
        var primary = UiKit.Primary(Loc.T(confirm));
        primary.BackgroundColor = danger ? UiKit.Red : UiKit.Blue;
        primary.Clicked += async (_, _) =>
        {
            closingFromButton = true;
            await CloseModalAsync(page.Navigation);
            completion.TrySetResult(true);
        };
        var buttons = new VerticalStackLayout { Spacing = 8, Children = { primary } };
        if (cancel is not null)
        {
            var secondary = UiKit.Secondary(Loc.T(cancel));
            secondary.Clicked += async (_, _) =>
            {
                closingFromButton = true;
                await CloseModalAsync(page.Navigation);
                completion.TrySetResult(false);
            };
            buttons.Children.Add(secondary);
        }
        var card = UiKit.Box(new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new Label { Text = Loc.T(title), FontSize = 22, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White },
                new Label { Text = Loc.T(message), FontSize = 14, TextColor = UiKit.Muted,
                    LineBreakMode = LineBreakMode.WordWrap },
                buttons
            }
        });
        card.Margin = new Thickness(24);
        page.Content = new Grid
        {
            VerticalOptions = LayoutOptions.Center,
            Children = { card }
        };
        page.Disappearing += (_, _) =>
        {
            if (!closingFromButton)
                completion.TrySetResult(false);
        };
        await owner.Navigation.PushModalAsync(page);
        return await completion.Task;
    }

    static async Task CloseModalAsync(INavigation navigation)
    {
        try
        {
            if (navigation.ModalStack.Count > 0)
                await navigation.PopModalAsync();
        }
        catch
        {
            // A root-page replacement can invalidate an old navigation stack.
            // The completion source is already resolved, so this callback must
            // not escape into Android as JavaProxyThrowable.
        }
    }
}
