# ADR-0009: Runtime .NET 10 (LTS)

**Data:** 2026-09-08
**Stato:** accettata — **sostituisce la decisione D8** di [09 Parte 1](../09-domande-aperte.md)
· verifiche completate il 2026-09-08 (in fondo)

## Contesto

D8 fissava il runtime a **.NET 8 (LTS)**, e all'epoca era la scelta ovvia: LTS, Avalonia 11
lo supporta, WebSocket e primitive crittografiche sono nel framework.

Due cose sono cambiate da allora.

**La data.** .NET 8 **esce dal supporto il 10 novembre 2026**, cioè fra due mesi. La fase
1A non è ancora iniziata: nascere su un runtime che smette di ricevere patch prima che il
primo rilascio esista non è un debito tecnico, è un difetto di partenza.

**Il perimetro.** [ADR-0008](0008-cifratura-e2e-chiave-all-approvazione.md) ha spostato TLS
ed E2EE dentro la fase 1, quindi il progetto contiene ora codice crittografico e dipende
dalle implementazioni della BCL per ECDH, HKDF e AES-GCM. Quel codice deve stare su un
runtime che riceve aggiornamenti di sicurezza — non è negoziabile per un prodotto che si
chiama Krypta.

C'è anche una ragione di tempismo che non tornerà: **non esiste ancora una riga di codice.**
Oggi il cambio è una stringa nei `.csproj`. A fase 1B finita sarebbe una migrazione con
una superficie crittografica da rivalidare.

## Opzioni valutate

**A. Restare su .NET 8.** Costo immediato zero. In cambio: due mesi di supporto residuo, e
la certezza di dover migrare comunque — a lavoro fatto, con più codice da toccare e la
crittografia già scritta. Rimanda il costo e lo aumenta.

**B. Passare a .NET 9.** Da escludere: .NET 9 è STS, **già fuori supporto da maggio 2026**.
Sarebbe una migrazione verso un runtime più vecchio, in termini di supporto, di quello che
si vuole lasciare.

**C. Passare a .NET 10 (LTS).** Supportato fino a **novembre 2028**: copre l'intera fase 1,
la distribuzione (fase 2) e la verifica crittografica (fase 3) senza altre migrazioni in
mezzo. Costo oggi: il TFM nei `.csproj`, che ancora non esistono.

## Decisione

**Opzione C: `net10.0` per tutti e cinque i progetti.**

Nessuna parte di quanto specificato dipende dalla versione. Il motivo per scegliere .NET 10
non è che serva qualcosa che .NET 8 non ha — è che **la finestra di supporto deve coprire
il ciclo di vita del progetto**, e quella di .NET 8 non lo fa nemmeno per la fase 1.

Cosa **non** cambia, ed è il punto:

| | |
|---|---|
| Trasporto | ASP.NET Core Minimal API + `app.UseWebSockets()`, invariato ([ADR-0001](0001-trasporto-websocket.md)) |
| Primitive crittografiche | `ECDiffieHellman` (P-256), `HKDF`, `AesGcm`, `RandomNumberGenerator` — tutte nella BCL, stessa API ([ADR-0008](0008-cifratura-e2e-chiave-all-approvazione.md)) |
| Serializzazione | `System.Text.Json` con source generator |
| UI | Avalonia 11 + ReactiveUI (`Avalonia.ReactiveUI`) |
| Protocollo | `krypta.v1`, invariato: il runtime non lo tocca |
| Dipendenze NuGet | Solo la famiglia Avalonia. **Niente sul percorso delle chiavi**: lì vale solo la BCL |

## Conseguenze

**Più facile:** il supporto copre fase 1, 2 e 3 senza migrazioni in mezzo. L'audit
crittografico della fase 3 non dovrà essere rifatto per un cambio di runtime successivo, il
che sarebbe stato lo scenario peggiore — rivalidare codice crittografico è caro proprio
perché va rifatto per intero.

**Più difficile:** niente, oggi. La migrazione è una stringa in cinque file che ancora non
esistono. È l'unico momento in cui questa decisione è gratuita, ed è la ragione per prenderla
adesso invece che "quando serve".

**Due cose da verificare prima di creare la solution**, entrambe rapide e nessuna delle due
bloccante:

1. **Avalonia 11 su `net10.0`.** Avalonia 11 ha come target `netstandard2.0` e `net8.0`, e
   un `net10.0` li consuma senza problemi: la compatibilità in avanti del .NET moderno rende
   l'esito quasi scontato. Ma "quasi scontato" non è "verificato", e va confermato sulla
   versione di Avalonia effettivamente usata prima di considerare chiusa questa decisione —
   non ha senso scoprirlo a metà della fase 1A.
2. **Se X25519 sia entrato nella BCL.** [ADR-0008](0008-cifratura-e2e-chiave-all-approvazione.md)
   sceglie P-256 solo perché X25519 non c'era senza dipendenze esterne, e dice esplicitamente
   che la migrazione resta possibile perché il formato della chiave pubblica è opaco al
   server. Se in .NET 10 fosse disponibile, varrebbe la pena riaprire il punto. **Non è una
   ragione di questa decisione** e non va usata per giustificarla a posteriori: è solo una
   verifica che conviene fare mentre si guarda comunque il runtime nuovo.

**Da rivedere se:** Avalonia 11 non funzionasse su `net10.0`. In quel caso la scelta non
torna a .NET 8 — che resta fuori supporto — ma diventa una questione di quale versione di
Avalonia usare.

## Esito delle verifiche (2026-09-08)

Entrambe fatte lo stesso giorno, su **.NET SDK 10.0.400**. Nessuna delle due cambia la
decisione; la seconda ne conferma un'altra.

**1. Lo stack UI su `net10.0`: funziona.** Un progetto `net10.0` con `Avalonia`,
`Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` e
`Avalonia.ReactiveUI` compila senza errori, e un `ViewModelBase : ReactiveObject` con
`RaiseAndSetIfChanged`, `ReactiveCommand`, `IRoutableViewModel` e `IActivatableViewModel`
si comporta come previsto. Il rischio in [08](../08-roadmap.md) resta elencato finché non
esiste la solution vera, ma l'incertezza che motivava questa verifica è chiusa.

> *Rifatta il 2026-09-08 dopo la correzione dello stack.* La prima esecuzione usava
> `CommunityToolkit.Mvvm`, che non è il pattern di questo progetto: il pattern è ReactiveUI
> ([05 §7](../05-client-desktop.md)). La verifica è stata ripetuta con i pacchetti giusti,
> e ha fatto emergere due cose che con l'altro pacchetto non si vedevano:
>
> - **`Avalonia.ReactiveUI` è ferma a 11.3.9** mentre il core Avalonia è a 11.3.21. Un
>   `Version="11.*"` compone quindi un set misto senza dirlo: le versioni vanno **fissate
>   esplicitamente**.
> - **Il set misto è quello giusto, per sicurezza.** Allineare tutto a 11.3.9 tira dentro
>   `Tmds.DBus.Protocol` 0.21.2, con una vulnerabilità nota di gravità **alta**
>   ([GHSA-xrw6-gwf8-vvr9](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9)); con il core
>   a 11.3.21 arriva la 0.21.3, corretta, e `dotnet build` non segnala più nulla. Le
>   versioni sono in [02 §4](../02-architettura.md).

**2. X25519 non è nella BCL di .NET 10.** Fra i tipi esportati da
`System.Security.Cryptography` non compare nulla per X25519 o Ed25519: la novità
crittografica di .NET 10 è semmai il post-quantum (`MLKem`, `MLKemCng`, `MLKemOpenSsl`),
che non c'entra con questo scambio.

**Quindi [ADR-0008](0008-cifratura-e2e-chiave-all-approvazione.md) resta valido parola per
parola: P-256 non era una scelta provvisoria in attesa del runtime nuovo.** Vale la pena
averlo verificato invece che assunto — era l'unica ragione tecnica per cui questo ADR
avrebbe potuto avere una conseguenza sul disegno crittografico, e non ce l'ha.
