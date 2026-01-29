using System.Collections.Generic;
using Cascode.ACIR;

namespace Cascode.ACIR.Tests;

public static class TestPrimitives
{
    public static PrimitiveDefinition GetLevel1Nmos() =>
        new()
        {
            Name = "Level1_NMOS",
            Kind = "nmos",
            Device = "level1_nmos",
            SizeParameter = "primSize",
            Params = new Dictionary<string, string>
            {
                ["W"] = "primSize.W",
                ["L"] = "primSize.L",
                ["m"] = "primSize.M",
            },
        };
}
