using System.Globalization;
using System.Text;

namespace MovieDB.Movies;

/// <summary>One column in the sort order of the overview.</summary>
/// <param name="Field">The sort column.</param>
/// <param name="Descending">Whether the sort direction is descending.</param>
internal readonly record struct SortKey(OverviewSortField Field, bool Descending);

/// <summary>
/// The view state of the overview, as kept in the URL (<c>?q=&amp;sort=&amp;page=&amp;size=</c>), where
/// <c>sort</c> is a comma-separated list such as <c>title,releaseYear:desc</c>. A direction is only written when it
/// differs from the column default. Unknown values fall back to the defaults, and defaults are left out of the URL.
/// </summary>
/// <param name="Search">The trimmed title search; empty when there is none.</param>
/// <param name="Sorts">The sort order, most significant column first. Never empty, no column twice.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The page size (one of <see cref="PageSizes"/>).</param>
internal sealed record OverviewState(string Search, IReadOnlyList<SortKey> Sorts, int Page, int PageSize)
{
    /// <summary>The maximum length of the search text.</summary>
    public const int MaxSearchLength = 100;

    /// <summary>The allowed page sizes.</summary>
    public static readonly IReadOnlyList<int> PageSizes = [25, 50, 100];

    /// <summary>The default page size.</summary>
    public const int DefaultPageSize = 50;

    private static readonly Dictionary<string, OverviewSortField> SortNames = Enum.GetValues<OverviewSortField>()
        .ToDictionary(field => ToUrlName(field), StringComparer.OrdinalIgnoreCase);

    private static readonly SortKey[] DefaultSorts =
    [
        new(OverviewSortField.WatchedOn, DefaultDescending(OverviewSortField.WatchedOn)),
    ];

    /// <summary>The default view: newest viewing first, 50 per page.</summary>
    public static OverviewState Default { get; } = new("", DefaultSorts, 1, DefaultPageSize);

    /// <summary>The most significant sort column.</summary>
    public SortKey PrimarySort => Sorts[0];

    /// <inheritdoc />
    public bool Equals(OverviewState? other) =>
        other is not null
        && Search == other.Search
        && Page == other.Page
        && PageSize == other.PageSize
        && Sorts.SequenceEqual(other.Sorts);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Search);
        hash.Add(Page);
        hash.Add(PageSize);
        foreach (var key in Sorts)
        {
            hash.Add(key);
        }

        return hash.ToHashCode();
    }

    /// <summary>The position (0-based) of a column in the sort order, or -1 when the view is not sorted on it.</summary>
    /// <param name="field">The sort column.</param>
    /// <returns>The position, or -1.</returns>
    public int SortIndexOf(OverviewSortField field)
    {
        for (var i = 0; i < Sorts.Count; i++)
        {
            if (Sorts[i].Field == field)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Drops the search, keeping the sort order. Goes back to the first page.</summary>
    /// <returns>The new state.</returns>
    public OverviewState WithoutSearch() => this with { Search = "", Page = 1 };

    /// <summary>Sorts on a single column only, in its default direction. Goes back to the first page.</summary>
    /// <param name="field">The sort column.</param>
    /// <returns>The new state.</returns>
    public OverviewState SortedOn(OverviewSortField field) =>
        this with
        {
            Sorts = [new SortKey(field, DefaultDescending(field))],
            Page = 1,
        };

    /// <summary>Reverses the direction of the most significant column. Goes back to the first page.</summary>
    /// <returns>The new state.</returns>
    public OverviewState WithPrimaryDirectionReversed() =>
        this with
        {
            Sorts = [.. Sorts.Select((key, i) => i == 0 ? key with { Descending = !key.Descending } : key)],
            Page = 1,
        };

    /// <summary>
    /// The effect of clicking a column header. A plain click sorts on that column alone (reversing it when it is
    /// already the only sort column). An additive click (CTRL) adds the column to the sort order, then reverses it,
    /// then removes it again.
    /// </summary>
    /// <param name="field">The clicked column.</param>
    /// <param name="additive">Whether CTRL (or the Meta key) was held.</param>
    /// <returns>The new state, back on the first page.</returns>
    public OverviewState WithHeaderClick(OverviewSortField field, bool additive)
    {
        var index = SortIndexOf(field);
        IReadOnlyList<SortKey> next;
        if (!additive)
        {
            next =
                Sorts.Count == 1 && index == 0
                    ? [Sorts[0] with { Descending = !Sorts[0].Descending }]
                    : [new SortKey(field, DefaultDescending(field))];
        }
        else if (index < 0)
        {
            next = [.. Sorts, new SortKey(field, DefaultDescending(field))];
        }
        else if (Sorts[index].Descending == DefaultDescending(field))
        {
            next = [.. Sorts.Select((key, i) => i == index ? key with { Descending = !key.Descending } : key)];
        }
        else
        {
            next = [.. Sorts.Where((_, i) => i != index)];
            if (next.Count == 0)
            {
                next = DefaultSorts;
            }
        }

        return this with
        {
            Sorts = next,
            Page = 1,
        };
    }

    /// <summary>The direction a column sorts in when it is first chosen: dates descending, everything else ascending.</summary>
    /// <param name="field">The sort column.</param>
    /// <returns><c>true</c> when the default direction is descending.</returns>
    public static bool DefaultDescending(OverviewSortField field) => field == OverviewSortField.WatchedOn;

    /// <summary>Normalises search text: trimmed and capped at <see cref="MaxSearchLength"/> characters.</summary>
    /// <param name="search">The raw text.</param>
    /// <returns>The normalised text, empty when there is none.</returns>
    public static string NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim() ?? "";
        return trimmed.Length <= MaxSearchLength ? trimmed : trimmed[..MaxSearchLength].TrimEnd();
    }

    /// <summary>Builds a state from raw query-string values, falling back to the defaults for anything invalid.</summary>
    /// <param name="q">The <c>q</c> value.</param>
    /// <param name="sort">The <c>sort</c> value: comma-separated <c>column[:asc|:desc]</c> items.</param>
    /// <param name="page">The <c>page</c> value.</param>
    /// <param name="size">The <c>size</c> value.</param>
    /// <returns>The view state.</returns>
    public static OverviewState FromQuery(string? q, string? sort, string? page, string? size)
    {
        var sorts = ParseSorts(sort);
        var pageNumber =
            int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p >= 1 ? p : 1;
        var pageSize =
            int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var s) && PageSizes.Contains(s)
                ? s
                : DefaultPageSize;

        return new OverviewState(NormalizeSearch(q), sorts, pageNumber, pageSize);
    }

    private static IReadOnlyList<SortKey> ParseSorts(string? sort)
    {
        var keys = new List<SortKey>();
        foreach (
            var item in (sort ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        )
        {
            var parts = item.Split(':', 2);
            if (!SortNames.TryGetValue(parts[0], out var field) || keys.Exists(key => key.Field == field))
            {
                continue;
            }

            var descending =
                parts.Length == 2
                    ? parts[1].ToLowerInvariant() switch
                    {
                        "desc" => true,
                        "asc" => false,
                        _ => DefaultDescending(field),
                    }
                    : DefaultDescending(field);
            keys.Add(new SortKey(field, descending));
        }

        return keys.Count == 0 ? DefaultSorts : keys;
    }

    /// <summary>The URL name of a sort column (camelCase member name).</summary>
    /// <param name="field">The sort column.</param>
    /// <returns>The URL name.</returns>
    public static string ToUrlName(OverviewSortField field)
    {
        var name = field.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>The query string of this state, with defaults omitted. Empty, or starting with <c>?</c>.</summary>
    /// <returns>The query string.</returns>
    public string ToQueryString()
    {
        var builder = new StringBuilder();

        void Add(string name, string value) =>
            builder
                .Append(builder.Length == 0 ? '?' : '&')
                .Append(name)
                .Append('=')
                .Append(Uri.EscapeDataString(value));

        if (Search.Length > 0)
        {
            Add("q", Search);
        }

        if (!Sorts.SequenceEqual(DefaultSorts))
        {
            // Column names and directions only contain URL-safe characters, so the commas stay readable.
            var items = Sorts.Select(key =>
            {
                var sort = key.Descending ? "desc" : "asc";
                return key.Descending == DefaultDescending(key.Field)
                    ? ToUrlName(key.Field)
                    : $"{ToUrlName(key.Field)}:{sort}";
            });
            builder.Append(builder.Length == 0 ? '?' : '&').Append("sort=").AppendJoin(',', items);
        }

        if (Page != 1)
        {
            Add("page", Page.ToString(CultureInfo.InvariantCulture));
        }

        if (PageSize != DefaultPageSize)
        {
            Add("size", PageSize.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
