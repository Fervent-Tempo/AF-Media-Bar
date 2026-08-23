namespace AFMediaBar.Abstractions;

public interface IStringLocalizer
{
    string Get(string key, params object[] args);
}
