namespace Lighthouse.Mobile.Services.Api;

/// <summary>
/// Базова адреса API. Емулятор Android звертається до хоста через 10.0.2.2 (псевдо-адреса localhost
/// комп'ютера). Для фізичного телефону замініть на IP комп'ютера в локальній мережі (http://192.168.x.x:5080).
/// </summary>
public static class ApiOptions
{
    public const string BaseUrl = "http://10.0.2.2:5080/";
}