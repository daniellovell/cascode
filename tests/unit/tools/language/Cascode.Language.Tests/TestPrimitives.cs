using System.Collections.Generic;
using Cascode.Language;

namespace Cascode.Language.Tests;

public static class TestPrimitives
{
    public static PrimitiveDefinition GetLevel1Nmos() =>
        new()
        {
            Name = "NMOS_Level1",
            Kind = "nmos",
            Device = "nmos_level1",
            SizeParameter = "primSize",
            Params = new Dictionary<string, string>
            {
                ["W"] = "primSize.W",
                ["L"] = "primSize.L",
                ["m"] = "primSize.M",
            },
        };
}
