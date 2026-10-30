namespace Krypta.Client.Configuration;

/// <summary>
/// Quello che la schermata [1] ha stabilito e che le schermate successive devono sapere.
/// </summary>
/// <param name="Host">
/// L'host <b>usato per connettersi</b>, non quello del file: se l'utente non ha spuntato
/// "Ricorda", il file non è stato riscritto e conterrebbe ancora altri valori.
/// </param>
/// <param name="Port">La porta usata per connettersi, con la stessa avvertenza.</param>
/// <param name="UseTls">Se la connessione in corso è cifrata, con la stessa avvertenza.</param>
/// <param name="Remember">La spunta "Ricorda queste impostazioni" della schermata [1].</param>
/// <param name="Config">
/// Il config come è stato letto all'avvio. Serve per il nickname precompilato, per il
/// tema, e per sapere se il file è sovrascrivibile.
/// </param>
public sealed record SessionSettings(
    string Host,
    int Port,
    bool UseTls,
    bool Remember,
    ConfigLoadResult Config)
{
    public string ServerLabel => $"{Host}:{Port}";

    public ClientTheme Theme => Config.Config.Ui.Theme;
}
