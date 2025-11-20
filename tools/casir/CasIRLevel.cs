using System.Text.Json.Serialization;

namespace Cascode.CasIR;

/// <summary>
/// Defines the elaboration level of a CasIR document.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CasIRLevel
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
    EL
}

