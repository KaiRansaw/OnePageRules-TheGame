namespace OprGodot.Screens;

/// <summary>
/// A screen that depends on Settings (Kai, 2026-11-18: "any settings changed are instantly put into effect once you
/// click back from the settings menu"). App calls <see cref="SettingsChanged"/> on every open screen of this kind when
/// the Settings screen closes.
/// </summary>
public interface ISettingsAware
{
    void SettingsChanged();
}
