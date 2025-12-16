using Scriban;
using Scriban.Runtime;

namespace Cascode.Bench;

public static class TemplateRenderer
{
    public static string Render(string templateText, object model)
    {
        // Templates use pure Scriban syntax: {{ for }}...{{ end }}, {{ if }}...{{ end }}
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
}
