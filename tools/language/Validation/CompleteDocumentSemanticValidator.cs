using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language.Validation;

public static class CompleteDocumentSemanticValidator
{
    public static void Check(CascodeDocument document, List<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostics);

        InterfaceContractValidator.Check(document, diagnostics);
        BenchBindingChecker.Check(document, diagnostics);
    }

    public static ValidationResult Validate(CascodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<Diagnostic>();
        Check(document, diagnostics);

        var result = new ValidationResult();
        foreach (var diagnostic in diagnostics)
        {
            var location = string.IsNullOrWhiteSpace(diagnostic.FilePath)
                ? null
                : $"{diagnostic.FilePath}:{diagnostic.Line}";
            if (diagnostic.Severity == DiagnosticSeverity.Warning)
            {
                result.AddWarning(diagnostic.Code, diagnostic.Message, location);
                continue;
            }

            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                result.AddError(diagnostic.Code, diagnostic.Message, location);
            }
        }

        return result;
    }
}
