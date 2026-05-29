using Scriban;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactHtmlIndexRenderer
{
    private const string TemplateResourceName = "Luotsi.Cli.Artifacts.Templates.artifact-index.scriban";

    private static readonly Lazy<Template> TemplateInstance = new(LoadTemplate);

    public static string Render(ArtifactIndexModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return TemplateInstance.Value.Render(new ArtifactHtmlIndexTemplateModel(model, ArtifactIndexTheme.Css, IndexScript));
    }

    private static Template LoadTemplate()
    {
        using var stream = typeof(ArtifactHtmlIndexRenderer).Assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException($"Embedded artifact index template '{TemplateResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var template = Template.Parse(text, TemplateResourceName);
        if (template.HasErrors)
        {
            throw new InvalidOperationException("Artifact index template is invalid: " + string.Join("; ", template.Messages.Select(static message => message.ToString())));
        }

        return template;
    }

    private sealed record ArtifactHtmlIndexTemplateModel(
        ArtifactIndexModel Index,
        string Styles,
        string Script);

    private const string IndexScript = """
    (() => {
      const input = document.querySelector('[data-filter-input]');
      const items = Array.from(document.querySelectorAll('[data-filter-item]'));
      if (input) {
        for (const button of document.querySelectorAll('[data-filter-set]')) {
          button.addEventListener('click', () => {
            const query = button.getAttribute('data-filter-set') || '';
            input.value = query;
            input.dispatchEvent(new Event('input', { bubbles: true }));
            for (const item of document.querySelectorAll('[data-filter-set]')) {
              item.classList.toggle('active', item === button);
            }
          });
        }
        input.addEventListener('input', () => {
          const query = input.value.trim().toLowerCase();
          for (const item of items) {
            item.hidden = query.length > 0 && !item.textContent.toLowerCase().includes(query);
          }
          if (query.length === 0) {
            for (const item of document.querySelectorAll('[data-filter-set]')) {
              item.classList.remove('active');
            }
          }
        });
      }
      for (const button of document.querySelectorAll('[data-copy]')) {
        button.addEventListener('click', async () => {
          const value = button.getAttribute('data-copy') || '';
          try {
            await navigator.clipboard.writeText(value);
            const label = button.textContent;
            button.textContent = 'Copied';
            setTimeout(() => { button.textContent = label; }, 1200);
          } catch {
            button.textContent = 'Select';
          }
        });
      }
    })();
    """;
}
