# 02 — Architettura

**Stato:** bozza · **Aggiornato:** 2026-09-08

## 1. Vista d'insieme

```
      +--------------------------+        +--------------------------+
      |   Krypta.Client  (PC A)  |        |   Krypta.Client  (PC B)  |
      |         Avalonia         |        |         Avalonia         |
      +------------+-------------+        +-------------+------------+
                   |                                    |
                   |   WebSocket  wss://host:porta/ws   |
                   |   TLS fuori, testo CIFRATO E2E     |
                   |   dentro: il server non lo apre    |
                   +----------------+-------------------+
                                    |
                    +---------------v----------------+
                    |         Krypta.Server          |
                    |         ASP.NET Core           |
                    |  +--------------------------+  |
                    |  |        RoomManager       |  |
                    |  |     (solo in memoria)    |  |
                    |  |                          |  |
                    |  |   "K7X2M9" -> Room       |  |
                    |  |        |- Connessione 1  |  |
                    |  |        |- Connessione 2  |  |
                    |  |        +- ... max 20     |  |
                    |  |   "RIUNIO" -> Room       |  |
                    |  +--------------------------+  |
                    |                                |
                    |    X  nessun database          |
                    |    X  nessun file su disco     |
                    |    X  nessuna coda messaggi    |
                    |    X  NESSUNA CHIAVE           |
                    +--------------------------------+
```

Il server è un **relay senza memoria**. Riceve un messaggio, lo inoltra alle connessioni
aperte della stanza, lo dimentica. L'unico stato che mantiene è *quali connessioni sono
in quale stanza*, e quello stato vive esclusivamente in RAM.

## 2. Modello di distribuzione

**Client-server centralizzato**, con server self-hosted.

| Alternativa | Perché scartata |
|---|---|
| Peer-to-peer | Richiede NAT traversal, discovery dei peer e un meccanismo di rendezvous. Complessità sproporzionata per un MVP che gira in LAN. |
| Federato | Ha senso per identità persistenti e storico. Qui non esiste né l'una né l'altro: non c'è niente da federare. |

Il fatto che il server sia **scelto dall'utente** (prima schermata: IP e porta) recupera
gran parte del beneficio del decentramento: chiunque può far girare il proprio server e
nessun operatore centrale vede il traffico di tutti.

## 3. Struttura della solution

```
Krypta/
├── Krypta.sln
├── src/
│   ├── Krypta.Shared/          Contratto di protocollo: DTO, codici errore, limiti
│   │   ├── Protocol/
│   │   │   ├── ClientMessages.cs      join, message, command, approve, reject,
│   │   │   │                          rekey, cancel, leave, pong
│   │   │   ├── ServerMessages.cs      joined, message, system, clear, participants,
│   │   │   │                          roster, pending, waiting, rejected, rekeyed,
│   │   │   │                          left, ping, error
│   │   │   ├── CryptoTypes.cs         WrappedKey (epk/nonce/ciphertext),
│   │   │   │                          MessageEnvelope (seq/keyEpoch/nonce/ciphertext),
│   │   │   │                          destinatari rekey, roster e sala d'attesa
│   │   │   ├── ProtocolJsonContext.cs source generator System.Text.Json
│   │   │   ├── MessageTypes.cs        discriminanti type del protocollo
│   │   │   ├── SystemKinds.cs         user_joined, user_left, room_renamed, ...
│   │   │   ├── CommandNames.cs        nameroom, users, clearroom, generatekey, help
│   │   │   ├── ErrorCodes.cs
│   │   │   └── RejectionReasons.cs    rejected, timeout, room_full, room_gone
│   │   └── KryptaLimits.cs     Max partecipanti, lunghezze, timeout, rate limit
│   │
│   ├── Krypta.Server/
│   │   ├── Program.cs          Bootstrap, middleware WebSocket
│   │   ├── Rooms/
│   │   │   ├── Room.cs         Una stanza: id, nome (mutabile), partecipanti
│   │   │   ├── RoomManager.cs  Dizionario concorrente delle stanze
│   │   │   ├── Participant.cs  Una connessione: socket, nickname, coda di invio
│   │   │   └── NicknameAllocator.cs   Disambiguazione emy -> emy2 -> emy3
│   │   ├── Sockets/
│   │   │   ├── ConnectionHandler.cs   Ciclo di vita di una connessione
│   │   │   ├── MessageDispatcher.cs   Parsing e instradamento
│   │   │   └── CommandHandler.cs      /NameRoom, /Users, /ClearRoom
│   │   ├── RateLimiting/
│   │   │   ├── JoinRateLimiter.cs     Per IP: 5 tentativi / 3 min, blocco 2 min
│   │   │   └── ActionRateLimiter.cs   Per connessione: messaggi e comandi
│   │   └── appsettings.json
│   │
│   └── Krypta.Client/
│       ├── App.axaml
│       ├── Views/              ConnectionView, AccountView, RoomView, ChatView
│       ├── ViewModels/         un ViewModel per View + MainWindowViewModel
│       ├── Services/
│       │   ├── KryptaConnection.cs   Client WebSocket, invio/ricezione
│       │   ├── ConfigService.cs      Lettura/scrittura krypta.config.xml
│       │   ├── NicknameColors.cs     Colore deterministico dal nickname
│       │   ├── SessionKeys.cs        Coppia effimera ECDH, mai su disco
│       │   ├── RoomCrypto.cs         Chiave di stanza, wrap/unwrap, AES-GCM
│       │   └── ServerTrust.cs        TOFU sulle impronte TLS
│       └── krypta.config.xml
│
└── tests/
    ├── Krypta.Server.Tests/    RoomManager, limiti, ciclo di vita stanze
    └── Krypta.Protocol.Tests/  Serializzazione dei messaggi, round-trip
```

**`Krypta.Shared` è il punto chiave**: i tipi del protocollo sono definiti una volta sola
e referenziati sia dal server sia dal client. Non è possibile che le due parti vadano
fuori sincrono su un nome di campo.

## 4. Stack tecnologico

| Componente | Scelta | Motivo |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | Supportato fino a **novembre 2028**: copre le fasi 1, 2 e 3 senza migrazioni in mezzo. WebSocket e primitive crittografiche nel framework. Nulla di quanto descritto dipende dalla versione — la scelta è di finestra di supporto ([ADR-0009](adr/0009-runtime-dotnet-10.md)) |
| Server | ASP.NET Core Minimal API + `app.UseWebSockets()` | WebSocket nel framework, nessuna dipendenza esterna |
| Serializzazione | `System.Text.Json` con source generator | Veloce, senza reflection, AOT-friendly |
| Crittografia | `ECDiffieHellman` (P-256), `HKDF`, `AesGcm`, `RandomNumberGenerator` | **Tutte nella BCL**: nessuna dipendenza esterna sul percorso delle chiavi |
| TLS | Kestrel + certificato autofirmato, TOFU lato client | Nessuna CA necessaria per il self-hosting |
| Client UI | Avalonia 11 | Requisito: desktop multipiattaforma da un solo codice |
| Pattern UI | **MVVM con ReactiveUI** (`Avalonia.ReactiveUI`) | È il pattern già in uso negli altri progetti Avalonia della casa (ETG MCA): `ViewModelBase : ReactiveObject`, `RaiseAndSetIfChanged`, `ReactiveCommand`, routing con `IScreen`. Vedi [05 §7](05-client-desktop.md) |
| Client WebSocket | `System.Net.WebSockets.ClientWebSocket` | Nel framework, nessuna dipendenza |
| Config client | XML via `System.Xml.Linq` | Requisito esplicito: file `.xml` |
| Test | xUnit | Standard .NET |

Nessun pacchetto NuGet oltre alla famiglia Avalonia (compreso `Avalonia.ReactiveUI`). È
una scelta deliberata: meno dipendenze significa meno codice di terze parti che tocca i
messaggi. Vale soprattutto per il **percorso delle chiavi**, dove la regola è più stretta
e non ammette eccezioni: solo BCL ([ADR-0008](adr/0008-cifratura-e2e-chiave-all-approvazione.md)).
ReactiveUI sta nella UI e non tocca né chiavi né testo in chiaro.

**Versioni da fissare esplicitamente, non con un intervallo mobile.** Le due famiglie non
avanzano insieme: `Avalonia.ReactiveUI` si ferma a **11.3.9** mentre il core Avalonia è a
**11.3.21**, e un `11.*` lasciato libero mette insieme un set misto senza dirlo. Il set
misto è però quello **giusto**, e per una ragione di sicurezza: Avalonia 11.3.9 porta con
sé `Tmds.DBus.Protocol` 0.21.2, che ha una vulnerabilità nota di gravità alta
([GHSA-xrw6-gwf8-vvr9](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9)); il core
11.3.21 porta la 0.21.3, corretta. Allineare tutto a 11.3.9 "per coerenza" reintrodurrebbe
la vulnerabilità.

```xml
<PackageReference Include="Avalonia"                Version="11.3.21" />
<PackageReference Include="Avalonia.Desktop"        Version="11.3.21" />
<PackageReference Include="Avalonia.Themes.Fluent"  Version="11.3.21" />
<PackageReference Include="Avalonia.Fonts.Inter"    Version="11.3.21" />
<PackageReference Include="Avalonia.ReactiveUI"     Version="11.3.9"  />
<PackageReference Include="Avalonia.Diagnostics"    Version="11.3.21"
                  Condition="'$(Configuration)'=='Debug'" />
```

Verificato su `net10.0` il 2026-09-08: compila, e `dotnet build` non segnala nessuna
vulnerabilità. Va **ricontrollato** a ogni aggiornamento dei pacchetti — è l'unico punto
del progetto in cui una dipendenza transitiva può introdurre un problema di sicurezza
senza che nessuno tocchi il codice.

## 5. Modello dati (solo in memoria)

```
RoomManager
└── Rooms : ConcurrentDictionary<string, Room>     chiave = ID normalizzato in MAIUSCOLO

Room
├── Id            : string          "K7X2M9" - 6 alfanumerici, sempre maiuscolo
│                                   e' una stringa: "000042" != "42"
├── Name          : string          "RM-K7X2M9" alla nascita, mutabile con /NameRoom
├── CreatedAt     : DateTimeOffset  solo per diagnostica
├── Participants  : lista ORDINATA di Participant, protetta da lock - max 20
│                   l'ordine e' quello di ingresso: lo usa /Users
└── Pending       : lista ORDINATA di JoinRequest, stesso lock - max 10
                    NON concorre al limite di 20

JoinRequest
├── RequestId         : Guid            token opaco esposto ai client
├── RequestedNickname : string          NON ancora disambiguato
├── JoinMessage       : string?         max 200 caratteri, facoltativo
│                                       IN CHIARO: chi lo scrive non ha ancora la chiave
├── Connection        : Participant     la connessione in attesa
└── RequestedAt       : DateTimeOffset  per la scadenza a 120 s

Participant
├── ConnectionId  : Guid            interno, mai esposto ai client
├── Nickname      : string
├── PublicKey     : string          la chiave pubblica di sessione dichiarata nel join
│                                   materiale PUBBLICO: si distribuisce nel roster
├── Socket        : WebSocket
├── SendQueue     : Channel<string> serializza le scritture sul socket
└── LastSeen      : DateTimeOffset  per l'heartbeat
```

Non esiste nessuna entità `Message`. Un messaggio è un oggetto temporaneo che vive per
la durata di una chiamata di metodo.

## 6. Ciclo di vita di una stanza

```
        il client invia "join" con id K7X2M9 + la sua chiave pubblica
                        |
                        v
              la stanza K7X2M9 esiste?
                 |                |
                no                si
                 |                |
                 v                v
           crea la stanza     e' piena (20)?
           entra SUBITO:       |         |
           non c'e' nessuno   si         no
           che possa           |         |
           approvarlo          v         v
                 |     errore ROOM_FULL  la sala d'attesa e' piena (10)?
                 |                        |                   |
                 |                       si                   no
                 |                        |                   |
                 |                        v                   v
                 |          errore WAITING_ROOM_FULL    SALA D'ATTESA
                 |                                        "pending" al richiedente
                 |                                        "waiting" ai partecipanti
                 |                                            |
                 |                             +--------------+--------------+
                 |                             |              |              |
                 |                        un partecipante  timeout       il richiedente
                 |                        invia "approve"   120 s        invia "cancel"
                 |                        con la chiave       |              |
                 |                        di stanza cifrata   v              v
                 |                             |          "rejected"     FUORI
                 |                             |          reason=timeout
                 |          la stanza si e'    |
                 |          riempita nel      si -> "rejected" reason=room_full
                 |          frattempo?         |
                 |                            no
                 |                             |
                 +----------+------------------+
                            v
                  disambigua il nickname ORA (emy -> emy2)
                  aggiungi il partecipante
                  invia "joined" al client, con la chiave di stanza cifrata
                  invia "participants" e system "user_joined" a tutti
                            |
                            v
              +------> STANZA ATTIVA <------+
              |  inoltra involucri cifrati  |
              |  che non sa aprire          |
              +--------------+--------------+
                             | un partecipante esce o cade
                             v
                    rimuovi il partecipante
                    invia "participants" ai rimasti
                             |
                             v
                    la stanza e' vuota?
                       |            |
                      no            si
                       |            |
                       |            v
                       |     DISTRUGGI LA STANZA
                       |     le richieste pendenti -> "rejected" reason=room_gone
                       |     l'ID torna libero, il nome sparisce
                       +---> resta attiva
```

**Conseguenza importante:** l'ID `K7X2M9` di stamattina e l'ID `K7X2M9` di stasera non
sono la stessa stanza. Non c'è nessuna continuità, nessuna proprietà, nessun proprietario.
Chi arriva per primo su un ID libero crea una stanza nuova — e con essa **una chiave di
stanza nuova**, generata nel suo client.

Due punti del diagramma sono facili da perdere leggendo il codice invece di questo schema,
e sono entrambi decisioni ([04 §4.1bis](04-api-server.md)):

- **la disambiguazione del nickname e il controllo del limite di 20 avvengono
  all'ammissione**, non alla richiesta: fra le due la stanza cambia;
- **la distruzione della stanza chiude le richieste pendenti**, altrimenti restano appese
  a una stanza che non esiste più fino al timeout.

## 7. Concorrenza

Quattro punti dove è facile sbagliare, e come li affrontiamo:

1. **Scritture concorrenti sullo stesso socket.** Una connessione WebSocket ammette una
   sola `SendAsync` alla volta: due broadcast simultanei corrompono lo stream. Ogni
   partecipante ha quindi una propria coda di invio (`Channel<string>`) consumata da un
   unico task scrittore.
2. **Broadcast durante una disconnessione.** Il broadcast itera su una **copia** della
   lista partecipanti, così un `Remove` concorrente non fa saltare l'iterazione.
2b. **Ordine diverso in code diverse.** Le code per partecipante risolvono il punto 1 ma
   ne aprono un altro: due inoltri concorrenti accodati fuori da una sezione critica comune
   possono finire in ordine opposto in due code, e i due utenti vedono la conversazione in
   ordine diverso. L'accodamento a *tutti* i partecipanti va quindi fatto in un'unica
   sezione critica (invariante I13, [04 §4.1](04-api-server.md)) — la `SendAsync`, che è la
   parte lenta, resta comunque fuori dal lock.
3. **Creazione simultanea della stessa stanza.** Due client che entrano nello stesso
   istante su un ID nuovo devono trovarsi nella *stessa* stanza: si usa `GetOrAdd` sul
   dizionario concorrente, mai un `ContainsKey` seguito da `Add`.
4. **Ultimo che esce mentre uno entra.** La distruzione della stanza vuota va fatta sotto
   lo stesso lock del `Remove` del partecipante, e il `join` deve fallire e riprovare se
   trova una stanza già marcata come distrutta. Altrimenti un client resta agganciato a
   una stanza che il manager ha già dimenticato.
