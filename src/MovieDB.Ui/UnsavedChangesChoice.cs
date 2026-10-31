namespace MovieDB.Ui;

/// <summary>What the user chose in the unsaved-changes dialog.</summary>
public enum UnsavedChangesChoice
{
    /// <summary>Stay on the page and keep editing.</summary>
    Stay = 0,

    /// <summary>Save the changes, then continue.</summary>
    Save = 1,

    /// <summary>Throw the changes away and continue.</summary>
    Discard = 2,
}
