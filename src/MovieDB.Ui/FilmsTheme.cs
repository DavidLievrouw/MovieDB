using MudBlazor;

namespace MovieDB.Ui;

/// <summary>The MudBlazor theme: the dark-first palette, radii and self-hosted fonts of PLAN.md §8.1.</summary>
public static class FilmsTheme
{
    private static readonly string[] BodyFont =
    [
        "Inter",
        "system-ui",
        "-apple-system",
        "Segoe UI",
        "Roboto",
        "sans-serif",
    ];

    private static readonly string[] HeadingFont =
    [
        "Space Grotesk",
        "Inter",
        "system-ui",
        "-apple-system",
        "Segoe UI",
        "sans-serif",
    ];

    /// <summary>The theme instance to give to <c>MudThemeProvider</c>.</summary>
    public static MudTheme Instance { get; } =
        new()
        {
            PaletteDark = new PaletteDark
            {
                Primary = "#22D3EE",
                PrimaryContrastText = "#04121A",
                Secondary = "#FFC83D",
                SecondaryContrastText = "#1A1300",
                Tertiary = "#FF7A1A",
                TertiaryContrastText = "#1A0A00",
                Background = "#070B14",
                BackgroundGray = "#0E1626",
                Surface = "#0E1626",
                AppbarBackground = "#0E1626",
                AppbarText = "#E6EDF7",
                DrawerBackground = "#0E1626",
                DrawerText = "#E6EDF7",
                TextPrimary = "#E6EDF7",
                TextSecondary = "#9AA8BF",
                TextDisabled = "#5F6E87",
                ActionDefault = "#9AA8BF",
                Divider = "#22304A",
                DividerLight = "#1A2740",
                LinesDefault = "#22304A",
                LinesInputs = "#3A4A66",
                TableLines = "#22304A",
                TableStriped = "#101A2D",
                TableHover = "#142036",
                Success = "#34D399",
                Warning = "#FBBF24",
                Error = "#F87171",
                Info = "#60A5FA",
            },
            PaletteLight = new PaletteLight
            {
                Primary = "#0E7490",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#B7791F",
                SecondaryContrastText = "#FFFFFF",
                Tertiary = "#C2410C",
                TertiaryContrastText = "#FFFFFF",
                Background = "#F4F7FB",
                BackgroundGray = "#E8EEF6",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#0E1626",
                DrawerBackground = "#FFFFFF",
                DrawerText = "#0E1626",
                TextPrimary = "#0E1626",
                TextSecondary = "#4A5A72",
                ActionDefault = "#4A5A72",
                Divider = "#C8D3E2",
                DividerLight = "#DCE4F0",
                LinesDefault = "#C8D3E2",
                LinesInputs = "#8696AE",
                TableLines = "#D5DEEB",
                TableStriped = "#F0F4FA",
                TableHover = "#E6EEF8",
                Success = "#047857",
                Warning = "#B45309",
                Error = "#C62828",
                Info = "#1D4ED8",
            },
            LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px", AppbarHeight = "56px" },
            Typography = new Typography
            {
                Default = new DefaultTypography { FontFamily = BodyFont },
                H1 = new H1Typography { FontFamily = HeadingFont },
                H2 = new H2Typography { FontFamily = HeadingFont },
                H3 = new H3Typography { FontFamily = HeadingFont },
                H4 = new H4Typography { FontFamily = HeadingFont },
                H5 = new H5Typography { FontFamily = HeadingFont },
                H6 = new H6Typography { FontFamily = HeadingFont },
                Button = new ButtonTypography { FontFamily = BodyFont, TextTransform = "none" },
            },
        };
}
