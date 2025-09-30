using Scriban;
using Scriban.Runtime;

namespace Cascode.Bench;

public static class TemplateRenderer
{
    public static string Render(string templateText, object model)
    {
        // Convert simple Jinja/Liquid control blocks to Scriban script blocks
        templateText = Preprocess(templateText);
        var template = Template.Parse(templateText);
        if (template.HasErrors)
        {
            var msg = string.Join("; ", template.Messages.Select(m => m.Message));
            throw new InvalidOperationException($"Template parse error: {msg}");
        }

        var ctx = new TemplateContext
        {
            StrictVariables = false,
            MemberRenamer = member => member.Name,
        };
        var scriptObj = new ScriptObject();
        scriptObj.Import(model, renamer: member => member.Name);
        ctx.PushGlobal(scriptObj);
        return template.Render(ctx);
    }

    private static string Preprocess(string text)
    {
        // Convert a subset of {% ... %} blocks to {{ ... }}
        // Specific end/else replacements first, then generic block closers
        return text
            .Replace("{% endif %}", "{{ end }}", StringComparison.Ordinal)
            .Replace("{% endfor %}", "{{ end }}", StringComparison.Ordinal)
            .Replace("{% else %}", "{{ else }}", StringComparison.Ordinal)
            .Replace("{% for", "{{ for", StringComparison.Ordinal)
            .Replace("{% if", "{{ if", StringComparison.Ordinal)
            .Replace("%}", "}}", StringComparison.Ordinal);
    }
}
