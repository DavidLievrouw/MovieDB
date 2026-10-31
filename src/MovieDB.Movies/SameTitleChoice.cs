namespace MovieDB.Movies;

/// <summary>What the user chose after being warned that a movie with the same title and year exists.</summary>
internal enum SameTitleChoice
{
    /// <summary>Close the warning and go back to the form.</summary>
    Back = 0,

    /// <summary>Save the movie anyway.</summary>
    Save = 1,

    /// <summary>Discard the entered data and leave the form.</summary>
    Cancel = 2,
}
