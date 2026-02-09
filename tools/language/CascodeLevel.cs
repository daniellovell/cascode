namespace Cascode.Language;

/// <summary>
/// Defines the elaboration level of an Cascode document.
/// </summary>
public enum CascodeLevel
{
    /// <summary>
    /// High Level: Slots are represented as instances; values/params may be symbolic.
    /// </summary>
    HL,

    /// <summary>
    /// Mid Level: Slots replaced by concrete motif types; params may be symbolic.
    /// </summary>
    ML,

    /// <summary>
    /// Electrical Level: All params are numeric; ready for SPICE emission.
    /// </summary>
    EL,
}
