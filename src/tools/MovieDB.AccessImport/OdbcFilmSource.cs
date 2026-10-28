using System.Data.Odbc;
using System.Globalization;

namespace MovieDB.AccessImport;

/// <summary>Reads <c>tblFilms</c> read-only through the 64-bit Access ODBC driver (Windows only).</summary>
internal sealed class OdbcFilmSource : IFilmSource
{
    private const string Query =
        "SELECT [FilmID], [FilmTitel], [Genre], [Duur], [Jaar], [Regisseur], [Datum], [Totaal], [Film], [Beeld], "
        + "[Muziek], [Geluid], [Extras], [Gezien op], [Opmerkingen] FROM [tblFilms] ORDER BY [FilmID]";

    private readonly string _path;

    public OdbcFilmSource(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public IReadOnlyList<SourceFilm> ReadFilms()
    {
        if (!File.Exists(_path))
        {
            throw new ImportException($"The Access file '{Path.GetFileName(_path)}' was not found.");
        }

        var connectionString = $"Driver={{Microsoft Access Driver (*.mdb, *.accdb)}};Dbq={_path};ReadOnly=1;";
        using var connection = new OdbcConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Query;
        using var reader = command.ExecuteReader();

        var films = new List<SourceFilm>();
        while (reader.Read())
        {
            films.Add(
                new SourceFilm(
                    FilmId: Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                    Title: Text(reader, 1),
                    Genre: Text(reader, 2),
                    Duration: Text(reader, 3),
                    Year: Text(reader, 4),
                    Director: Text(reader, 5),
                    WatchedOn: reader.IsDBNull(6)
                        ? null
                        : Convert.ToDateTime(reader.GetValue(6), CultureInfo.InvariantCulture),
                    OverallScore: Number(reader, 7),
                    StoryScore: Number(reader, 8),
                    PictureScore: Number(reader, 9),
                    MusicScore: Number(reader, 10),
                    SoundScore: Number(reader, 11),
                    Extras: Text(reader, 12),
                    Medium: Text(reader, 13),
                    Review: Text(reader, 14)
                )
            );
        }

        return films;
    }

    private static string? Text(OdbcDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);

    private static int? Number(OdbcDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Convert.ToInt32(reader.GetValue(index), CultureInfo.InvariantCulture);
}
