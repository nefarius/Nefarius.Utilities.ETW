namespace Nefarius.Utilities.ETW;

/// <summary>
///     Selects the implementation used to read ETL files.
/// </summary>
public enum EtwBackend
{
    /// <summary>
    ///     <see cref="Native" /> on Windows, <see cref="Managed" /> everywhere else.
    /// </summary>
    Auto = 0,

    /// <summary>
    ///     The Windows ETW APIs (<c>OpenTrace</c>/<c>ProcessTrace</c> and TDH). Windows only.
    /// </summary>
    Native = 1,

    /// <summary>
    ///     The portable managed ETL reader. Runs on every platform; supports offline <c>.etl</c> files only.
    /// </summary>
    Managed = 2
}
