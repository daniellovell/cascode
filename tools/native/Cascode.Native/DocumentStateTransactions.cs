using Cascode.Language;

namespace Cascode.Native;

internal static class DocumentStateTransactions
{
    public static T Commit<T>(DocumentState state, Func<DocumentState, T> mutation)
    {
        var draft = Clone(state);
        var result = mutation(draft);
        state.SourceText = draft.SourceText;
        state.Document = draft.Document;
        state.CircuitName = draft.CircuitName;
        state.Revision = draft.Revision;
        state.ChangedEntities = draft.ChangedEntities;
        return result;
    }

    public static DocumentState Clone(DocumentState state)
    {
        var sourceText = SerializeSource(state.Document);
        var read = CascodeReader.TryParse(sourceText, "<native-transaction>");
        if (!read.Success || read.Document is null)
        {
            throw new ApiException(
                "CASAPI-INVALID-REQUEST",
                "Could not clone the current document state."
            );
        }

        return new DocumentState
        {
            DocumentId = state.DocumentId,
            SourceText = state.SourceText,
            Document = read.Document,
            CircuitName = state.CircuitName,
            Revision = state.Revision,
            ChangedEntities = state.ChangedEntities.ToArray(),
        };
    }

    private static string SerializeSource(CascodeDocument document)
    {
        using var writer = new StringWriter();
        CascodeWriter.Write(document, writer);
        return writer.ToString();
    }
}
