using AFMediaBar.Components.Abstractions;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets.Schema5;

internal interface ISchema5ComponentCodec
{
    string TypeId { get; }
    bool TryToSchema5(IComponentSettings source, out WidgetSettings settings);
    bool TryFromSchema5(WidgetSettings source, out IComponentSettings settings);
}

internal abstract class Schema5ComponentCodec<TComponentSettings, TSchemaSettings> : ISchema5ComponentCodec
    where TComponentSettings : class, IComponentSettings
    where TSchemaSettings : WidgetSettings
{
    public abstract string TypeId { get; }

    public bool TryToSchema5(IComponentSettings source, out WidgetSettings settings)
    {
        settings = source is TComponentSettings typed ? ToSchema5(typed) : null!;
        return settings is not null;
    }

    public bool TryFromSchema5(WidgetSettings source, out IComponentSettings settings)
    {
        settings = source is TSchemaSettings typed ? FromSchema5(typed)! : null!;
        return settings is not null;
    }

    protected abstract TSchemaSettings ToSchema5(TComponentSettings settings);
    protected abstract IComponentSettings? FromSchema5(TSchemaSettings settings);
}
