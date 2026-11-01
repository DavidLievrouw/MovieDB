using System.Globalization;
using MovieDB.Common;

namespace MovieDB.Infrastructure;

/// <summary>
/// Computes <c>movies.directors_key</c>: the director ids, sorted numerically ascending and joined with commas. It is
/// the single place that builds the key, used by the save command and the import loader. Because director names are
/// unique per collation, the same set of directors in any order always gives the same key.
/// </summary>
public static class DirectorsKey
{
    /// <summary>The column length of <c>movies.directors_key</c>; five ids of at most 10 digits fit.</summary>
    public const int MaxLength = 64;

    /// <summary>Builds the key of a set of directors.</summary>
    /// <param name="ids">The director ids, in any order.</param>
    /// <returns>The key, for example <c>12,57</c>.</returns>
    public static string Of(IEnumerable<DirectorId> ids) =>
        string.Join(',', ids.Select(id => id.Value).Order().Select(v => v.ToString(CultureInfo.InvariantCulture)));
}
