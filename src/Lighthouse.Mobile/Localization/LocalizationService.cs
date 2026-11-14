using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Lighthouse.Mobile.Localization;

/// <summary>
/// Локалізація uk (за замовчуванням) і en через resx без перезапуску: екрани прив'язуються до індексатора
/// (<see cref="TranslateExtension"/>), тож після <see cref="SetLanguage"/> усі тексти оновлюються одразу.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public const string Ukrainian = "uk";
    public const string English = "en";
    private const string PreferenceKey = "app.language";

    private readonly ResourceManager _uk = new("Lighthouse.Mobile.Resources.Strings.StringsUk", typeof(LocalizationService).Assembly);
    private readonly ResourceManager _en = new("Lighthouse.Mobile.Resources.Strings.StringsEn", typeof(LocalizationService).Assembly);

    public static LocalizationService Instance { get; } = new();

    private LocalizationService()
    {
        var saved = Preferences.Default.Get(PreferenceKey, Ukrainian);
        Language = saved == English ? English : Ukrainian;
        ApplyCulture();
    }

    public string Language { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    /// <summary>Рядок за ключем; відсутній у поточній мові — береться українська, інакше видно сам ключ у дужках.</summary>
    public string Get(string key, params object[] args)
    {
        var primary = Language == English ? _en : _uk;
        var value = primary.GetString(key, CultureInfo.InvariantCulture)
                    ?? _uk.GetString(key, CultureInfo.InvariantCulture)
                    ?? $"[{key}]";

        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }

    public void SetLanguage(string code)
    {
        var language = code == English ? English : Ukrainian;
        if (language == Language)
        {
            return;
        }

        Language = language;
        Preferences.Default.Set(PreferenceKey, language);
        ApplyCulture();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    private void ApplyCulture()
    {
        var culture = new CultureInfo(Language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}