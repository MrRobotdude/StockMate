namespace StockMate.Ui;

public static class UiKit
{
    static readonly SemaphoreSlim BusyOverlayGate = new(1, 1);
    public static readonly Color Navy = Color.FromArgb("#0B1220");
    public static readonly Color Card = Color.FromArgb("#151F32");
    public static readonly Color Muted = Color.FromArgb("#9AA8BD");
    public static readonly Color Green = Color.FromArgb("#27D17F");
    public static readonly Color Red = Color.FromArgb("#FF647C");
    public static readonly Color Blue = Color.FromArgb("#5B8CFF");
    public static readonly Color Purple = Color.FromArgb("#A98BFF");
    public static readonly Color Surface = Color.FromArgb("#101A2B");
    public static readonly Color CardStroke = Color.FromArgb("#24314A");

    public static Label Title(string text) => new() { Text = Loc.T(text), FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
    public static Label SectionTitle(string text) => new() { Text = Loc.T(text), FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
    public static Label Body(string text) => new() { Text = Loc.T(text), FontSize = 14, TextColor = Colors.White, LineHeight = 1.18 };
    public static Label Sub(string text) => new() { Text = Loc.T(text), FontSize = 14, TextColor = Muted, LineHeight = 1.18 };
    public static Label Caption(string text) => new() { Text = Loc.T(text), FontSize = 12, TextColor = Muted };
    public static Border Box(View content) => new()
    {
        Content = content, BackgroundColor = Card, Stroke = CardStroke,
        StrokeThickness = 1, Padding = 16, Margin = new Thickness(0, 1),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 }
    };
    public static Button Primary(string text) => new()
    {
        Text = Loc.T(text), BackgroundColor = Blue, TextColor = Colors.White, FontAttributes = FontAttributes.Bold,
        CornerRadius = 14, HeightRequest = 52, MinimumHeightRequest = 52,
        FontSize = 14, Padding = new Thickness(14, 0)
    };
    public static Button Secondary(string text) => new()
    {
        Text = Loc.T(text), BackgroundColor = Surface, TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold, CornerRadius = 14, HeightRequest = 52,
        MinimumHeightRequest = 52, FontSize = 14, Padding = new Thickness(14, 0)
    };
    public static Button Tertiary(string text) => new()
    {
        Text = Loc.T(text), BackgroundColor = Colors.Transparent, TextColor = Blue,
        FontAttributes = FontAttributes.Bold, CornerRadius = 12,
        HeightRequest = 48, MinimumHeightRequest = 48, FontSize = 14
    };
    public static Button Help(Page page, string titleId, string titleEn, string bodyId, string bodyEn)
    {
        var button = new Button
        {
            Text = "?", WidthRequest = 34, HeightRequest = 34, CornerRadius = 17,
            Padding = 0, FontAttributes = FontAttributes.Bold,
            BackgroundColor = Surface, TextColor = Blue
        };
        button.Clicked += async (_, _) => await AppDialog.ShowAsync(page,
            Loc.T(titleId, titleEn), Loc.T(bodyId, bodyEn));
        return button;
    }
    public static Grid Heading(Page page, string titleId, string titleEn,
        string helpId, string helpEn)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        grid.Add(Title(Loc.T(titleId, titleEn)), 0);
        grid.Add(Help(page, titleId, titleEn, helpId, helpEn), 1);
        return grid;
    }
    public static Grid SectionHeading(Page page, string titleId, string titleEn,
        string helpId, string helpEn)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 10,
            VerticalOptions = LayoutOptions.Center
        };
        grid.Add(SectionTitle(Loc.T(titleId, titleEn)), 0);
        grid.Add(Help(page, titleId, titleEn, helpId, helpEn), 1);
        return grid;
    }
    public static Border Metric(string label, string value, Color? color = null)
    {
        return Box(new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                Caption(Loc.T(label).ToUpperInvariant()),
                new Label
                {
                    Text = value, TextColor = color ?? Colors.White,
                    FontAttributes = FontAttributes.Bold, FontSize = 18
                }
            }
        });
    }
    public static Border ExpandableCard(
        string title, string summary, View detail, string? badge = null,
        Color? badgeColor = null, bool initiallyExpanded = false)
    {
        var details = new ContentView { Content = detail, IsVisible = initiallyExpanded };
        var chevron = new Label
        {
            Text = initiallyExpanded ? "▴" : "▾",
            TextColor = Blue, FontSize = 18,
            VerticalTextAlignment = TextAlignment.Center
        };
        var headerText = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                new Label { Text = Loc.T(title), TextColor = Colors.White,
                    FontAttributes = FontAttributes.Bold, FontSize = 16 },
                Sub(Loc.T(summary))
            }
        };
        var header = new Grid
        {
            ColumnDefinitions =
            [
                new(GridLength.Star),
                new(GridLength.Auto),
                new(GridLength.Auto)
            ],
            ColumnSpacing = 8
        };
        header.Add(headerText, 0);
        if (!string.IsNullOrWhiteSpace(badge))
        {
            header.Add(new Border
            {
                Content = new Label
                {
                    Text = Loc.T(badge), TextColor = badgeColor ?? Blue,
                    FontSize = 11, FontAttributes = FontAttributes.Bold
                },
                BackgroundColor = Surface, StrokeThickness = 0,
                Padding = new Thickness(9, 5),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                VerticalOptions = LayoutOptions.Center
            }, 1);
        }
        header.Add(chevron, 2);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            details.IsVisible = !details.IsVisible;
            chevron.Text = details.IsVisible ? "▴" : "▾";
        };
        header.GestureRecognizers.Add(tap);
        var content = new VerticalStackLayout { Spacing = 12, Children = { header, details } };
        return Box(content);
    }
    public static Grid Pager(Button previous, Label pageInfo, Button next)
    {
        previous.Text = "← " + Loc.T("Sebelumnya", "Previous");
        next.Text = Loc.T("Berikutnya", "Next") + " →";
        previous.HorizontalOptions = next.HorizontalOptions = LayoutOptions.Fill;
        previous.HeightRequest = next.HeightRequest = 48;
        previous.MinimumHeightRequest = next.MinimumHeightRequest = 48;
        pageInfo.HorizontalTextAlignment = TextAlignment.Center;
        pageInfo.VerticalTextAlignment = TextAlignment.Center;
        var pager = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 10,
            RowSpacing = 8
        };
        pager.Add(pageInfo, 0, 0);
        Grid.SetColumnSpan(pageInfo, 2);
        pager.Add(previous, 0, 1);
        pager.Add(next, 1, 1);
        return pager;
    }

    public static async Task RunBusyAsync(ContentPage page, string message, Func<Task> action)
    {
        // Start the work first. Fast local work must not flash a modal or interfere
        // with Shell tab navigation.
        Task work;
        try
        {
            work = action();
        }
        catch
        {
            // A synchronous callback failure must reach the caller instead of
            // escaping through an Android Java proxy callback.
            throw;
        }
        var first = await Task.WhenAny(work, Task.Delay(250));
        if (first == work)
        {
            await work;
            return;
        }

        await BusyOverlayGate.WaitAsync();
        BlockingBusyPage? overlay = null;
        var navigation = page.Navigation;
        try
        {
            // The operation may have completed while another global overlay owned
            // the gate. In that case there is nothing left to display.
            if (work.IsCompleted)
            {
                await work;
                return;
            }

            overlay = new BlockingBusyPage(message);
            await MainThread.InvokeOnMainThreadAsync(
                () => navigation.PushModalAsync(overlay, false));
            await work;
        }
        finally
        {
            if (overlay is not null)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (navigation.ModalStack.Contains(overlay))
                            await navigation.PopModalAsync(false);
                    });
                }
                catch
                {
                    // The window may have been replaced after a completed
                    // operation. Never leave the busy gate locked in that case.
                }
            }
            BusyOverlayGate.Release();
        }
    }

    sealed class BlockingBusyPage : ContentPage
    {
        public BlockingBusyPage(string message)
        {
            BackgroundColor = Color.FromArgb("#F20B1220");
            Shell.SetTabBarIsVisible(this, false);
            Content = new Grid
            {
                Padding = new Thickness(28),
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 14,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        WidthRequest = 280,
                        Children =
                        {
                            new ActivityIndicator
                            {
                                IsRunning = true, Color = Blue,
                                WidthRequest = 48, HeightRequest = 48
                            },
                            new Label
                            {
                                Text = message,
                                TextColor = Colors.White,
                                FontSize = 16,
                                FontAttributes = FontAttributes.Bold,
                                HorizontalTextAlignment = TextAlignment.Center
                            },
                            Sub(Loc.T(
                                "Proses sedang berjalan. Menu dan tombol kembali dikunci sementara.",
                                "A process is running. Navigation and back are temporarily locked."))
                        }
                    }
                }
            };
        }

        protected override bool OnBackButtonPressed() => true;
    }
    public static Border EmptyState(string icon, string title, string message) =>
        Box(new VerticalStackLayout
        {
            Spacing = 8, Padding = new Thickness(4, 14),
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                new Label { Text = icon, FontSize = 30, TextColor = Purple,
                    HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = Loc.T(title), FontSize = 17, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = Loc.T(message), FontSize = 13, TextColor = Muted,
                    HorizontalTextAlignment = TextAlignment.Center }
            }
        });
    public static VerticalStackLayout PageStack() => new()
    {
        Padding = new Thickness(16, 18, 16, 36), Spacing = 14
    };
}
