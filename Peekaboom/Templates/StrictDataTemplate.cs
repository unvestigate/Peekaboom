using System.Runtime.CompilerServices;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Metadata;

namespace Peekaboom.Templates;

public class StrictDataTemplate : ITypedDataTemplate, IRecyclingDataTemplate
{
    private readonly ConditionalWeakTable<Control, object> mBuilt = new();

    [DataType]
    public Type? DataType { get; set; }

    [Content]
    [TemplateContent]
    public object? Content { get; set; }

    public bool Match(object? data) => DataType == null || DataType.IsInstanceOfType(data);

    public Control? Build(object? data) => Build(data, null);

    public Control? Build(object? data, Control? existing)
    {
        if (existing != null && mBuilt.TryGetValue(existing, out _))
        {
            return existing;
        }

        Control? view = TemplateContent.Load(Content)?.Result;

        if (view != null)
        {
            mBuilt.Add(view, this);
        }

        return view;
    }
}
