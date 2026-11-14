namespace Lighthouse.Mobile.Localization;

/// <summary>Розмітка XAML: <c>Text="{loc:Translate Btn_SignIn}"</c>. Прив'язка оновлюється при зміні мови.</summary>
[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Mode = BindingMode.OneWay,
        Path = $"[{Key}]",
        Source = LocalizationService.Instance
    };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}