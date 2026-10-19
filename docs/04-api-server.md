# 04 — Krypta.Server (API)

**Stato:** bozza · **Aggiornato:** 2026-09-08

## 0. Il server non possiede chiavi

Prima di tutto il resto, perché vincola ogni sezione che segue: **il server non genera,
non riceve e non conserva alcuna chiave di cifratura.** Non può leggere i messaggi, non
può ammettere nessuno da solo, non può falsificare un mittente.

| Cosa passa dal server | Cosa il server ne fa |
|---|---|
| `publicKey` nel `join` | Verifica codifica e lunghezza. La inoltra nel `waiting` |
| `wrappedKey` nell'`approve` | Verifica che ci sia e sia ben formata. La inoltra nel `joined` |
| `nonce` + `ciphertext` dei messaggi | Verifica la dimensione. Inoltra invariati |
| `joinMessage` nel `join` | Verifica lunghezza e forma. Lo inoltra nel `waiting` — **in chiaro, e lo legge**: chi lo scrive non ha ancora la chiave di stanza (§4.1bis) |
| `publicKey` dei partecipanti | La **conserva** finché il partecipante è nella stanza e la distribuisce nel `roster`. È materiale pubblico, e serve ai client per poter rigenerare la chiave |
| `keys[]` di un `rekey` | Verifica che i destinatari siano esattamente i partecipanti meno il mittente, e che ogni involucro sia ben formato. Consegna a ciascuno il **suo**. Non ne apre nessuno |

Tre conseguenze pratiche che ricorrono in tutto il documento:

1. **Il server non può più validare il contenuto dei messaggi.** Niente controllo di
   testo vuoto, niente lunghezza del testo in chiaro, niente rimozione dei caratteri di
   controllo: sono controlli passati interamente al client. `EMPTY_MESSAGE` sparisce.
2. **Un `approve` senza `wrappedKey` va rifiutato**, non completato d'ufficio. È la
   proprietà che impedisce al server di infilare qualcuno in una stanza.
3. **Nessun materiale crittografico finisce nei log**, a nessun livello, in nessun caso.

Restano invece in chiaro nickname, ID stanza, nome stanza, messaggi di sistema e
sala d'attesa: li genera o li instrada il server. È TLS a proteggerli sulla rete, non
l'E2EE. Vedi [03 §7bis.6](03-protocollo.md).

## 1. Responsabilità

Il server fa **quattro cose**, e nient'altro:

1. Sta in ascolto e accetta connessioni WebSocket.
2. Tiene in memoria quali connessioni sono in quale stanza, creando e distruggendo le
   stanze secondo il ciclo di vita.
3. Inoltra ogni messaggio ricevuto a tutti i partecipanti della stanza del mittente.
4. Esegue i comandi di stanza — `/NameRoom`, `/Users`, `/ClearRoom` — e annuncia gli eventi
   (ingressi, uscite, comandi). Instrada inoltre le rotazioni della chiave (`/GenerateKey`,
   che viaggia come `rekey`) **senza poterle aprire**: ne verifica solo i destinatari.

Tutto ciò che il server **non** fa è altrettanto vincolante: non memorizza messaggi, non
scrive su disco, non espone l'elenco delle stanze, non autentica nessuno, non ha un
concetto di utente che sopravviva alla connessione, non ha ruoli né proprietari di stanza.

## 2. Endpoint

| Metodo | Percorso | Descrizione |
|---|---|---|
| `GET` (upgrade) | `/ws` | Handshake WebSocket, sotto-protocollo `krypta.v1`. Unico canale applicativo |
| `GET` | `/health` | `200 OK` con `{"status":"ok","uptimeSeconds":1234}`. **Non espone numero di stanze né di utenti** |

Non esiste `GET /rooms`: sarebbe una directory delle stanze, cioè esattamente ciò che il
prodotto rifiuta.

## 3. Configurazione (`appsettings.json`)

```json
{
  "Krypta": {
    "Host": "127.0.0.1",
    "Port": 5000,
    "MaxRooms": 500,
    "MaxParticipantsPerRoom": 20,

    "MaxPendingPerRoom": 10,
    "JoinApprovalTimeoutSeconds": 120,

    "MaxCiphertextBytes": 8192,
    "MaxFrameBytes": 16384,

    "RateLimitMessages": 10,
    "RateLimitMessagesWindowSeconds": 5,
    "RateLimitCommands": 3,
    "RateLimitCommandsWindowSeconds": 10,

    "JoinRateLimitAttempts": 5,
    "JoinRateLimitWindowSeconds": 180,
    "JoinRateLimitBlockSeconds": 120,

    "PingIntervalSeconds": 30,
    "ConnectionTimeoutSeconds": 60
  },
  "Kestrel": {
    "Endpoints": {
      "Wss": {
        "Url": "https://127.0.0.1:5000",
        "Certificate": { "Path": "krypta.pfx", "Password": "" }
      }
    }
  },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```

`Host` a `127.0.0.1` è il default deliberato: il server **non** è raggiungibile dalla
rete finché non lo si cambia esplicitamente in `0.0.0.0`.

**Sul certificato.** Dalla fase 1B l'endpoint è `wss://`, con un certificato autofirmato
generato da uno script e riconosciuto dai client via TOFU ([03 §1.2](03-protocollo.md)).
Non serve un'autorità di certificazione, ma il certificato serve: la sezione `Kestrel`
non è opzionale, e un server che parte senza deve rifiutarsi di ascoltare su un indirizzo
diverso da `127.0.0.1` invece di ripiegare in silenzio su `ws://` in chiaro. La password
del `.pfx` non va messa qui in un deployment vero: si passa da variabile d'ambiente o
user-secrets.

I valori di `MaxCiphertextBytes` e `MaxFrameBytes` sono configurabili ma **non
indipendenti**: il secondo deve restare comodamente sopra al base64 del primo, per il
calcolo di [03 §9.1](03-protocollo.md). Abbassare `MaxFrameBytes` senza rifare quel conto
fa rifiutare messaggi legittimi con `MALFORMED` invece che con `MESSAGE_TOO_LONG`.

## 4. Componenti

### 4.1 `RoomManager`

Il cuore del server. Unico proprietario dello stato.

```csharp
public sealed class RoomManager
{
    // Aggiunge il partecipante, creando la stanza (nome "RM-{id}") se non esiste.
    // Assegna il nickname effettivo, disambiguandolo se necessario.
    JoinResult Join(
        string roomId,
        string requestedNickname,
        string publicKey,
        Participant p,
        string? joinMessage = null);

    // Rimuove il partecipante e distrugge la stanza se rimane vuota.
    void Leave(Participant p);

    // Inoltra l'involucro cifrato a tutti i partecipanti della stanza del mittente.
    // Il server non conosce il testo: nonce, ciphertext e seq passano invariati,
    // e l'unica cosa che aggiunge sono il nickname (dalla connessione) e sentAt.
    void Broadcast(Participant sender, MessageEnvelope envelope);

    // Invia un messaggio di sistema a tutti i partecipanti della stanza.
    void BroadcastSystem(Room room, string kind, string text, object? extra = null);

    // Comandi.
    CommandResult Rename(Participant p, string newName);
    IReadOnlyList<string> ListNicknames(Room room);   // in ordine di ingresso
    void ClearRoom(Participant p);
}
```

Nessuna eccezione per il flusso normale: `JoinResult` e `CommandResult` rappresentano
o il successo o uno dei codici di errore del protocollo.

**Invarianti da garantire in ogni ordine di esecuzione:**

| # | Invariante |
|---|---|
| I1 | Una stanza presente nel dizionario ha sempre **almeno un** partecipante. |
| I2 | Una stanza non ha mai **più di 20** partecipanti. |
| I3 | Un partecipante è in **al massimo una** stanza. |
| I4 | Due `Join` concorrenti sullo stesso ID inesistente producono **una sola** stanza, condivisa dai due client. |
| I5 | Nessun messaggio viene **memorizzato**: non esiste struttura del server che lo trattenga dopo l'inoltro. L'unica permanenza ammessa è il transito nelle code di invio (§4.3), che è il tempo di scrittura sul socket e non un archivio: nulla vi si accumula per essere riletto, e una coda che cresce oltre 100 messaggi fa cadere la connessione invece di trattenerli. |
| I13 | L'accodamento di un inoltro a **tutti** i partecipanti avviene in un'unica sezione critica: due inoltri concorrenti non possono interlacciarsi in ordine diverso fra code diverse. È ciò che rende vera la garanzia di ordine di [03 §5.3](03-protocollo.md). |
| I14 | La `KeyEpoch` di una stanza è **strettamente crescente** e cambia solo dentro il lock, insieme alla consegna degli involucri: due `rekey` concorrenti non possono produrre la stessa epoca, e il secondo riceve `STALE_KEY_EPOCH`. |
| I15 | Una rotazione o avviene **per tutti i partecipanti o per nessuno**: se l'elenco dei destinatari non copre esattamente i presenti meno il mittente, l'epoca non avanza e nessun `rekeyed` viene inviato. È ciò che impedisce di usare la rotazione per escludere qualcuno ([ADR-0011](adr/0011-rotazione-chiave-di-stanza.md)). |
| I6 | Nessun nickname è duplicato all'interno della stessa stanza — garantito dall'allocatore (§4.2), non da un rifiuto. |
| I7 | L'ordine della lista partecipanti è l'ordine di ingresso: è quello che `/Users` restituisce. |
| I9 | Una connessione è **o** partecipante **o** in attesa **o** fuori: mai due cose insieme. |
| I10 | Un `requestId` viene evaso **una volta sola**: la seconda risposta riceve `UNKNOWN_REQUEST`. |
| I11 | La sala d'attesa non contiene mai più di 10 richieste, e non concorre al limite di 20 partecipanti. |
| I12 | Se una stanza viene distrutta, tutte le sue richieste pendenti vengono chiuse con `room_gone`. Non esistono richieste orfane. |
| I8 | Il nome della stanza è sempre non vuoto: `RM-{id}` finché non rinominato. |

I1 e I4 insieme sono la parte delicata: la distruzione della stanza vuota e l'inserimento
di un nuovo partecipante devono essere mutuamente esclusivi. Un `Join` che ottiene una
stanza già marcata come distrutta deve **riprovare**, non fallire.

**Su I5, una precisazione che vale la pena scrivere.** La formulazione precedente diceva
"nessun messaggio conservato oltre la durata della chiamata a `Broadcast`", ed era in
contraddizione con le code di invio: un messaggio accodato per un client lento sopravvive
eccome alla chiamata. La contraddizione era nella parola, non nel design — la coda è un
buffer di trasmissione, non un archivio: è per destinatario, si svuota da sola, non è
indicizzata, nessuno la rilegge, e non sopravvive alla disconnessione. Quello che I5 vieta
è la **memorizzazione**: nessuna struttura del server trattiene un messaggio per poterlo
consegnare a qualcuno che non era collegato, che è il punto del prodotto.

**Su I13, il costo va conosciuto.** Accodare a tutti sotto un'unica sezione critica
significa tenere il lock della stanza per il tempo di venti `TryWrite` su altrettanti
`Channel` — operazioni non bloccanti su una coda in memoria, quindi nell'ordine dei
microsecondi. È accettabile proprio perché la scrittura sul socket, che è la parte lenta,
avviene **fuori** dal lock, nel task scrittore di ciascun partecipante. Il lock non deve
mai racchiudere una `SendAsync`.

### 4.1bis La sala d'attesa nel `RoomManager`

```csharp
// Esito di un join: entrato subito (stanza nuova), messo in attesa, o errore.
JoinResult Join(
    string roomId,
    string requestedNickname,
    string publicKey,
    Participant p,
    string? joinMessage = null);

// Un partecipante risponde a una richiesta pendente.
ApprovalResult Approve(
    Participant approver,
    Guid requestId,
    WrappedKey wrappedKey,
    int keyEpoch);
ApprovalResult Reject(Participant approver, Guid requestId);

// Il richiedente ritira la propria richiesta.
void Cancel(Participant pending);
```

```
Room
├── Participants : lista ordinata, max 20
└── Pending      : lista ordinata di JoinRequest, max 10

JoinRequest
├── RequestId        : Guid          esposto ai client come token opaco
├── RequestedNickname: string        NON ancora disambiguato
├── JoinMessage      : string?       max 200 caratteri, IN CHIARO, mai loggato
├── Connection       : Participant   la connessione in attesa
└── RequestedAt      : DateTimeOffset
```

**I quattro punti delicati**, tutti da eseguire sotto il lock della stanza:

1. **La disambiguazione del nickname avviene all'ammissione, non alla richiesta.**
   Fra la richiesta e l'approvazione la stanza cambia: chi approva vede `sara`, ma se nel
   frattempo è entrata un'altra `sara`, l'ammessa diventa `sara2`. Disambiguare al momento
   della richiesta produrrebbe nomi sbagliati o collisioni.

2. **Il limite di 20 si ricontrolla all'ammissione.** Una richiesta accodata quando la
   stanza aveva 15 partecipanti può essere approvata quando ne ha 20: l'`approve` fallisce
   e il richiedente riceve `rejected` con `room_full`.

3. **Un `requestId` si evade una volta sola** (I10). Due partecipanti che cliccano
   contemporaneamente non devono ammettere due volte la stessa persona: la rimozione dalla
   coda e l'inserimento fra i partecipanti sono una sola operazione atomica. Il secondo
   riceve `UNKNOWN_REQUEST`, che il client **non mostra come errore**.

4. **La distruzione della stanza chiude le richieste pendenti** (I12). Quando esce
   l'ultimo partecipante, chi era in attesa riceve `rejected` con `room_gone`: altrimenti
   resterebbe appeso a una stanza che non esiste più, fino al timeout.

5. **Il `joinMessage` si valida ma non si interpreta.** Il server ne controlla lunghezza e
   assenza di caratteri di controllo ([03 §4.1.3](03-protocollo.md)), lo tiene nella
   `JoinRequest` e lo inoltra invariato nelle voci di `waiting`. Non lo normalizza oltre il
   trim, non lo tronca, non lo filtra: se non è valido lo rifiuta con
   `INVALID_JOIN_MESSAGE`, e se è valido passa esattamente quei caratteri.

   **Il punto da non sbagliare è dove *non* deve finire.** Il testo del `system`
   `join_requested` lo compone il server e contiene solo il nickname. Concatenarci il
   `joinMessage` — anche "solo per comodità di lettura" — trasformerebbe la sala d'attesa
   in un canale di testo verso una stanza in cui il mittente non è entrato, ed è la ragione
   per cui [ADR-0010](adr/0010-messaggio-di-richiesta.md) esiste. C'è un test apposta.

Un `BackgroundService` (`PendingExpiryService`) scade le richieste oltre i 120 secondi,
con lo stesso percorso di un `reject` ma `reason: "timeout"`. Con la richiesta sparisce
anche il suo messaggio: non resta da nessuna parte, come per i rifiuti (RF-S-A16).

**La stanza non vede nessuna riga di sistema.** Alla scadenza i partecipanti ricevono solo
la coda aggiornata, come per un `cancel`: `join_rejected` dice sempre **chi** ha deciso
(RF-S-A07) e qui non ha deciso nessuno, e il protocollo non ha un `kind` per la scadenza
([03 §5.4](03-protocollo.md)). Aggiungerne uno sarebbe una modifica al protocollo, non un
dettaglio di implementazione.

**Il passo della scansione è di 5 secondi, non di 30 come l'heartbeat.** La scadenza è un
tempo che l'utente vede scorrere — la schermata [3b] ne fa un conto alla rovescia
([05 §4bis](05-client-desktop.md)) — e con il passo dell'heartbeat una richiesta potrebbe
morire a 150 secondi, lasciando il contatore a zero per mezzo minuto. La scansione passa
comunque dal lock del `RoomManager`, quindi una scadenza concorrente a un'approvazione non
può evadere due volte la stessa richiesta (I10).

### 4.2 `NicknameAllocator`

```csharp
// Restituisce il nickname effettivo, garantendo l'unicita' nella stanza.
//   "emy" libero            -> "emy"
//   "emy" occupato          -> "emy2"
//   "emy" e "emy2" occupati -> "emy3"
static string Allocate(string requested, IEnumerable<string> taken);
```

Regole:

- Il confronto è **senza distinzione di maiuscole**: se c'è `Emy`, la richiesta `emy`
  diventa `emy2`. Altrimenti due nomi che si leggono uguali starebbero nella stessa
  stanza, e il senso della disambiguazione andrebbe perso.
- Il suffisso parte da `2` e cresce finché non trova un nome libero.
- Se `base + suffisso` supera i 25 caratteri, **la base viene troncata** quel tanto che
  basta perché il risultato stia esattamente in 25.
- Va eseguito **dentro il lock della stanza**, insieme all'inserimento: calcolarlo prima
  e inserire dopo permette a due `join` simultanei di ricevere lo stesso nome.

### 4.3 `Participant`

```csharp
public sealed class Participant
{
    public Guid ConnectionId { get; }        // interno, mai inviato ai client
    public string? Nickname { get; }          // quello EFFETTIVO, assegnato al join
    public string? PublicKey { get; }         // presente solo dentro o in attesa
    public Room? CurrentRoom { get; }
    public DateTimeOffset LastSeen { get; }

    private readonly Channel<string> _sendQueue;

    public bool TryEnqueue(string json);      // false => coda piena, disconnessione
    public Task RunWriterAsync(CancellationToken ct);  // unico task che scrive sul socket
}
```

`WebSocket.SendAsync` non è thread-safe: due broadcast simultanei sullo stesso socket
producono uno stream corrotto e la disconnessione del client. La coda serializza le
scritture.

Se la coda supera 100 messaggi non inviati, il client è troppo lento e viene disconnesso:
meglio perderlo che accumulare memoria per lui.

### 4.4 `ConnectionHandler`

```
accetta il socket
  -> crea il Participant
  -> avvia il task scrittore (consuma la coda)
  -> avvia il ciclo di lettura
       per ogni frame:
         se supera MaxFrameBytes  -> errore MALFORMED, scarta
         deserializza             -> se fallisce, errore MALFORMED
         aggiorna LastSeen
         se e' un "join"        -> verifica il JoinRateLimiter sull'IP remoto (§4.6)
                                   PRIMA di ogni tentativo, non solo del primo
         instrada al MessageDispatcher
  -> alla chiusura (pulita o brusca), in un blocco finally:
       RoomManager.Leave(participant)
         -> system "user_left" + participants agli altri
         -> distruzione della stanza se vuota
       chiude la coda, ferma lo scrittore, rilascia il socket
```

Il blocco di uscita **deve** stare in un `finally`: è l'unico punto che garantisce che
una stanza non resti con un partecipante fantasma, e che gli altri vedano l'uscita.

### 4.5 `CommandHandler`

Riceve `{"type":"command","name":...,"arg":...}` già deserializzato.

| `name` | Validazione server | Effetto | Broadcast |
|---|---|---|---|
| `nameroom` | spazi normalizzati, poi: non vuoto, ≤ 25, solo `[A-Za-z0-9 ]` | `room.Name = arg normalizzato` | `system` `room_renamed` con campo `roomName` |
| `users` | nessuna | costruisce la lista al momento | `system` `user_list` con campo `users` |
| `clearroom` | nessuna | nessuno stato da modificare | `clear`, **poi** `system` `room_cleared` |
| altro | — | — | `error` `UNKNOWN_COMMAND` al solo mittente |

Tre punti non negoziabili:

1. **Il server rivalida sempre**, anche ciò che il client ha già validato. Un client
   modificato può inviare qualsiasi `arg`.
2. **Il nickname nel testo del `system` lo mette il server**, prendendolo da
   `participant.Nickname`. Mai dal payload.
3. **L'ordine `clear` → `system`** è vincolante: al contrario, la riga che spiega chi ha
   svuotato la stanza verrebbe cancellata subito dopo essere apparsa.

Su `clearroom` va ricordato in ogni discussione di prodotto: **il server non cancella
niente, perché non ha niente da cancellare.** È un'istruzione ai client, che un client
modificato può ignorare. È una comodità di lettura, non una garanzia.

### 4.6 `JoinRateLimiter`

Difende dall'enumerazione degli ID (vedi [07 §4](07-sicurezza-e-privacy.md)).

```
chiave  = indirizzo IP remoto
finestra = 180 s scorrevoli
soglia   = 5 tentativi di join
blocco   = 120 s
```

- **Contano tutti i tentativi**, riusciti e falliti. Contare solo i falliti renderebbe
  l'enumerazione gratuita: chi scansiona riceve per lo più `joined`, non errori.
- **Il controllo si fa a ogni messaggio `join`**, non una volta per connessione. La
  formulazione precedente di §4.4 diceva "solo al primo join" e apriva un buco netto: una
  connessione WebSocket può fare `leave` e `join` all'infinito ([03 §3](03-protocollo.md)),
  quindi bastava un socket solo per scandire lo spazio degli ID senza mai incontrare il
  limite. Il contatore è per IP e per tentativo, indipendente da quante connessioni
  vengano aperte e da quanto a lungo restino aperte.
- **Nemmeno riconnettersi aiuta**, per la stessa ragione: la chiave è l'IP, non il socket.
  Chi dispone di molti IP resta fuori dalla portata di questa difesa — è un limite noto e
  dichiarato in [07 §4.3](07-sicurezza-e-privacy.md), non qualcosa che il rate limiting
  possa risolvere.
- Superata la soglia: `error` con `JOIN_RATE_LIMITED` e `retryAfterSeconds`, per tutta
  la durata del blocco. La connessione **non** viene chiusa.
- Le voci scadute vanno ripulite periodicamente, altrimenti il dizionario cresce senza
  limite: è di fatto un piccolo memory leak con superficie di attacco.

**Effetto sull'utente legittimo:** cambiare stanza 5 volte in 3 minuti è insolito ma
possibile. Se dai test emerge che dà fastidio, il numero è configurabile — ma va alzato
consapevolmente, perché è l'unica difesa attiva sull'MVP.

### 4.7 `ActionRateLimiter`

Per connessione, non per IP: 10 messaggi ogni 5 secondi, 3 comandi ogni 10 secondi.
Il limite sui comandi conta soprattutto per `/ClearRoom` e `/Users`, che generano
traffico verso tutta la stanza.

### 4.8 `HeartbeatService`

`BackgroundService` che ogni 30 secondi invia `ping` a tutte le connessioni attive e
chiude quelle il cui `LastSeen` è più vecchio di 60 secondi — passando dal normale
percorso di uscita, così gli altri partecipanti ricevono `user_left` e `participants`.

**Finestra del partecipante fantasma e session takeover.** Se un client si disconnette bruscamente
(es. perdita connettività di rete senza chiusura TCP pulita), il server non può rilevarlo
istantaneamente: il partecipante resta presente per un massimo di 60 secondi. Se lo stesso utente
si riconnette immediatamente con lo stesso nickname e chiede di entrare nella stessa stanza, il server
tratta la connessione come **indipendente**:
- assegna un nickname numerato (es. `emy2`) poiché `emy` risulta ancora connesso;
- accoda la richiesta in sala d'attesa (la stanza è viva);
- **non ammette subentri forzati (session takeover)**. Consentire a una nuova connessione di "rubare"
  il posto di una precedente basandosi solo su IP e nickname aprirebbe una grave vulnerabilità di
  impersonificazione su reti condivise (NAT, Wi-Fi aziendale). La pulizia spetta unicamente
  all'`HeartbeatService`. All'espulsione del socket zombie, gli altri client ricevono regolarmente
  `user_left`.

## 5. Generazione dell'ID stanza

Nell'MVP **l'ID lo sceglie l'utente**: lo digita nel client. Il server **valida e basta**,
contro il pattern `^[A-Z0-9]{6}$`. Tutto ciò che non corrisponde — minuscole comprese —
riceve `INVALID_ROOM_ID`.

**Il server non normalizza: rifiuta.** Esiste una sola forma valida dell'ID, ed è quella
maiuscola. La conversione è responsabilità del client, che la fa mentre l'utente digita
([05 §4](05-client-desktop.md)), così il vincolo resta invisibile.

La ragione è che un confronto tollerante applicato in modo incoerente sarebbe disastroso
qui: se `k7x2m9` e `K7X2M9` finissero in due chiavi diverse del dizionario, otterremmo
due gruppi convinti di essere nella stessa conversazione che non si vedono — e **senza
nessun errore**, perché ogni ID libero è valido e produce una stanza nuova. Con una sola
forma canonica il problema non può proprio presentarsi, e il dizionario può usare
`StringComparer.Ordinal` senza ambiguità.

**Il pulsante "Genera" sta nel client, e il server non se ne accorge.** Non è più
un'ipotesi futura: è RF-C-07b, obbligatorio, in [1A.5](08-roadmap.md). L'ID si genera con
`RandomNumberGenerator` — mai con `Random` — e il client lo scrive nel campo come se
l'utente lo avesse digitato.

**Il client non può, e non deve, riprovare finché non trova un ID libero.** Non esiste
nessun modo di chiedere al server se una stanza esiste, ed è voluto (RF-S-10): un endpoint
del genere sarebbe un oracolo di esistenza, cioè esattamente ciò che la sala d'attesa e lo
spazio da 36⁶ servono a negare. La collisione la risolve il `join` stesso — se l'ID è già
occupato si finisce in sala d'attesa invece di creare la stanza — e con 2,18 miliardi di
combinazioni è un caso che si nota, non un caso da prevenire.

## 6. Logging

| Evento | Livello | Cosa si logga |
|---|---|---|
| Avvio / arresto server | Information | Host, porta, configurazione |
| Connessione aperta / chiusa | Debug | Solo il ConnectionId. **Non l'IP**: insieme alla riga di ingresso direbbe quale IP era in quale stanza (§6.1) |
| Stanza creata / distrutta | Information | Solo l'ID stanza |
| Ingresso / uscita | Debug | ConnectionId + ID stanza |
| Comando eseguito | Debug | Nome del comando + ID stanza. **Mai l'argomento**: il nome scelto per una stanza è contenuto |
| Rate limit scattato | Warning | Tipo di limite. L'IP **solo** per il limite sui join, dove è l'oggetto stesso dell'evento — e senza ID stanza sulla riga |
| Errore di protocollo | Warning | Codice errore, ConnectionId |
| Eccezione non gestita | Error | Stack trace |

**Divieti assoluti**, validi anche a livello `Debug` e anche in sviluppo:

- Mai il **contenuto** dei messaggi.
- Mai **IP e nickname sulla stessa riga**: insieme sono un identificatore.
- Mai l'**elenco dei partecipanti** prodotto da `/Users`.
- Mai l'**argomento** di `/NameRoom`.
- Mai il **`joinMessage`**. È in chiaro e il server lo legge, il che lo rende l'unico
  contenuto d'utente che *potrebbe* finire in un log per distrazione: proprio per questo va
  vietato esplicitamente. Della richiesta si logga al più che è arrivata, mai cosa diceva.
- Mai **materiale crittografico**: chiavi pubbliche, `wrappedKey`, nonce, ciphertext.
  Nemmeno troncati, nemmeno come impronta.

Un log che permette di ricostruire chi ha detto cosa, o chi era in quale stanza, vanifica
l'intero prodotto.

### 6.1 Il `ConnectionId` ricuce ciò che le regole separano

La regola "mai IP e nickname sulla stessa riga" protegge meno di quanto sembri, perché il
`ConnectionId` compare su entrambe le righe che contano:

```
[Debug] Connessione aperta   ConnectionId=7f3a…  IP=192.168.1.31
[Debug] Ingresso             ConnectionId=7f3a…  Room=K7X2M9
```

Nessuna delle due riga viola il divieto, ma insieme dicono **quale IP era in quale stanza
e quando**: esattamente il dato che il prodotto promette di non produrre. Una `join` su
un campo comune la fa chiunque abbia il file, e a livello `Debug` su file quel file esiste.

Tre regole che chiudono il buco senza rinunciare alla diagnostica:

1. **L'IP si logga solo dove serve davvero**, cioè quando scatta il `JoinRateLimiter` —
   che è l'unico caso in cui l'IP *è* l'oggetto dell'evento. La riga "connessione aperta"
   non lo include: per correlare un errore a una connessione basta il `ConnectionId`.
2. **`ConnectionId` e ID stanza non compaiono mai insieme a livello `Information`.** Il
   livello di default in produzione resta `Information` proprio perché a quel livello
   nessuna riga collega una connessione a una stanza.
3. **Il `ConnectionId` è per connessione e non sopravvive ad essa**: non è un identificativo
   di utente, non va riusato fra riconnessioni, e non va derivato da nulla di stabile
   (IP, nickname, chiave pubblica). Un `Guid.NewGuid()` per socket, e basta.

Resta un residuo dichiarato: chi ha accesso al server **mentre** la conversazione è in
corso vede tutto in RAM, log o non log. Il logging protegge dal futuro, non dal presente.

## 7. Test da scrivere

### Stanze e ciclo di vita
| Test | Verifica |
|---|---|
| `Join_StanzaInesistente_LaCreaConNomeDefault` | RF-S-03, nome `RM-K7X2M9`, `created == true` |
| `Join_StanzaEsistente_NonInviaStorico` | RF-S-04 — il nuovo entrato non riceve nessun `message` |
| `Join_Ventunesimo_RestituisceRoomFull` | I2 |
| `Join_ConcorrenteStessoId_CreaUnaSolaStanza` | I4 — 50 task in parallelo |
| `Leave_UltimoPartecipante_DistruggeLaStanza` | RF-S-08 |
| `Leave_UltimoPartecipante_ScartaIlNomePersonalizzato` | I8 — rientrando si trova `RM-…` |
| `Disconnessione_Brusca_LiberaIlPostoEAnnunciaUscita` | RF-S-11, RF-S-18 |
| `Heartbeat_ClientMuto_VieneDisconnessoDopo60s` | RF-S-11 |
| `RoomIdConZeriIniziali_Preservato` | `"000042"` non diventa `"42"`: l'ID è una stringa |
| `RoomId_Minuscolo_Rifiutato` | `k7x2m9` → `INVALID_ROOM_ID`: il server non normalizza |
| `RoomId_ConSpaziOSimboli_Rifiutato` | `INVALID_ROOM_ID` |
| `RoomId_SoleLettereOSoleCifre_Accettato` | `RIUNIO` e `482913` sono entrambi validi |

### Nickname
| Test | Verifica |
|---|---|
| `Nickname_Duplicato_RiceveSuffissoNumerico` | `emy` → `emy2` |
| `Nickname_DuplicatoDueVolte_Incrementa` | `emy`, `emy2` → `emy3` |
| `Nickname_DuplicatoIgnorandoMaiuscole` | `Emy` presente → `emy` diventa `emy2` |
| `Nickname_LungoAlLimite_VieneTroncatoPerIlSuffisso` | 25 caratteri esatti nel risultato |
| `Nickname_Riservato_Rifiutato` | `STANZA`, `SYSTEM`… → `INVALID_NICKNAME` |
| `Nickname_ConAccentiOEmoji_Rifiutato` | RF-S-12b — `josé`, `Ωmega`, `luca😀` → `INVALID_NICKNAME` |
| `Nickname_NfcEdNfdDelloStessoNome_EntrambiRifiutati` | Le due forme Unicode non devono poter entrare **per nessuna via**: è la ragione del vincolo ASCII |
| `Nickname_ConcorrenteStessoNome_NonProduceDuplicati` | I6 sotto concorrenza |

### Messaggi e comandi
| Test | Verifica |
|---|---|
| `Broadcast_RaggiungeTutti_MittenteIncluso` | RF-S-06 |
| `Broadcast_NonRaggiungeAltreStanze` | Isolamento tra stanze |
| `Broadcast_ConcorrenteOrdineIdenticoInTutteLeCode` | I13 — due inoltri simultanei, 20 code: nessuna coppia di code li riceve in ordine opposto |
| `Broadcast_UsaIlNicknameDelServer_NonQuelloDelPayload` | RF-S-16 — anti-impersonazione |
| `NameRoom_Valido_RinominaEAnnuncia` | RF-S-20, RF-S-21 |
| `NameRoom_ConSpazi_Accettato` | `Coordinamento Team` è valido |
| `NameRoom_SpaziNormalizzati` | `"  Team   A  "` → `"Team A"` |
| `NameRoom_SoliSpazi_Rifiutato` | Dopo la normalizzazione è vuoto |
| `NameRoom_ConPunteggiatura_Rifiutato` | `Team-A!` → `INVALID_COMMAND_ARG` |
| `NameRoom_Oltre25CaratteriDopoNormalizzazione_Rifiutato` | La lunghezza si misura **dopo** aver ridotto gli spazi |
| `Users_RestituisceOrdineDiIngresso` | I7 |
| `Users_VisibileATuttiIPartecipanti` | RF-S-23 |
| `ClearRoom_InviaClearPrimaDelSystem` | Ordine vincolante di §4.5 |
| `ClearRoom_AnnunciaChiLoHaEseguito` | RF-S-28 |
| `ComandoSconosciuto_RestituisceUnknownCommand_SoloAlMittente` | RF-S-29 |

### Sala d'attesa
| Test | Verifica |
|---|---|
| `Join_StanzaNuova_EntraSenzaApprovazione` | Il primo crea ed entra: non c'è nessuno che possa approvarlo |
| `Join_StanzaEsistente_VaInAttesa` | Risposta `pending`, non `joined` |
| `Pending_NonRiceveMessaggiDellaStanza` | Chi attende non vede niente di dentro |
| `Pending_NonCompareNelContatore` | Il conteggio partecipanti non cambia |
| `Pending_NonCompareInUsers` | `/Users` elenca solo i partecipanti |
| `Approve_DaQualsiasiPartecipante_Ammette` | Non serve un ruolo |
| `Approve_Concorrente_AmmetteUnaVoltaSola` | I10 — due `approve` simultanei sullo stesso `requestId` |
| `Approve_StanzaPienaNelFrattempo_RifiutaConRoomFull` | Il limite si ricontrolla all'ammissione |
| `Approve_NicknameDisambiguatoAllAmmissione` | Chi approva vede `sara`, entra `sara2` |
| `Reject_ChiudeLaRichiesta` | Il richiedente riceve `rejected` con `rejected` |
| `Timeout_DopoDueMinuti_ChiudeLaRichiesta` | `reason: "timeout"` |
| `Cancel_RitiraLaPropriaRichiesta` | Torna fuori da ogni stanza |
| `SalaAttesa_Undicesima_RestituisceWaitingRoomFull` | I11 |
| `UltimoPartecipanteEsce_RichiestePendentiChiuseConRoomGone` | I12 — nessuna richiesta orfana |
| `Pending_NonPuoInviareMessaggiOComandi` | `NOT_IN_ROOM` |
| `Pending_MessaggioPendingNonRivelaNienteDellaStanza` | Niente nome, niente conteggio |
| `JoinMessage_InoltratoInvariatoNelWaiting` | RF-S-A18 — carattere per carattere |
| `JoinMessage_NonCompareNelSystemJoinRequested` | RF-S-A18 — **il test che protegge dall'abuso**: il testo del `system` lo genera solo il server |
| `JoinMessage_Assente_NonRompeNulla` | RF-S-A17 — il campo è facoltativo |
| `JoinMessage_VuotoOSoliSpazi_TrattatoComeAssente` | RF-S-A17 — non è un errore |
| `JoinMessage_Oltre200Caratteri_RestituisceInvalidJoinMessage` | RF-S-A17 |
| `JoinMessage_ConACapoOCaratteriDiControllo_Rifiutato` | RF-S-A17 — deve stare in una riga |
| `JoinMessage_SuStanzaNuova_ScartatoSenzaErrore` | RF-S-A19 — il client non poteva saperlo |
| `JoinMessage_NonCompareNeiLog` | RF-S-A20 — scansione dei log a livello `Debug` |

### Cifratura (lato server: ciò che il server *non* deve poter fare)
| Test | Verifica |
|---|---|
| `Server_NonPuoDecifrareUnMessaggio` | Nessuna chiave in nessuna struttura del server |
| `Approve_SenzaWrappedKey_Rifiutato` | `INVALID_APPROVAL`: il server non ammette nessuno da solo |
| `Approve_ConWrappedKeyMalformata_Rifiutato` | Verifica di forma, non di contenuto |
| `WrappedKey_InoltrataInvariata` | Byte per byte, dal mittente al destinatario |
| `PublicKey_InoltrataInvariataNelWaiting` | Il server non la sostituisce |
| `Ciphertext_InoltratoInvariato` | Nessuna trasformazione |
| `Messaggio_SenzaNonceOSeq_Rifiutato` | `INVALID_ENVELOPE` |
| `Log_NonContengonoMaterialeCrittografico` | Scansione dei log a livello `Debug` |
| `Rekey_ElencoCompleto_RuotaEIncrementaEpoca` | RF-X-17 — ogni partecipante riceve il suo `rekeyed` |
| `Rekey_DestinatarioMancante_Rifiutato` | I15 — **il test che impedisce l'espulsione mascherata**: l'epoca non avanza |
| `Rekey_DestinatarioDiTroppoODuplicato_Rifiutato` | RF-X-18 |
| `Rekey_Concorrente_UnaSolaRotazioneVaABuonFine` | I14 — il secondo riceve `STALE_KEY_EPOCH` |
| `Rekey_MittenteNonRiceveInvolucro` | La chiave nuova l'ha generata lui |
| `Approve_ConEpocaVecchia_RestituisceStaleKeyEpoch` | RF-X-22 — corsa fra ammissione e rotazione |
| `Rekey_SecondaRotazioneIn30s_RestituisceRekeyRateLimited` | Limite **per stanza**, non per connessione |
| `Roster_ContieneLeChiaviPubblicheDeiSoliPartecipanti` | RF-X-19 — mai quelle di chi è in attesa |
| `Rekey_InvolucriInoltratiInvariati` | Byte per byte, e ciascuno al destinatario giusto |

### Protezione
| Test | Verifica |
|---|---|
| `JoinRateLimit_SestoTentativoIn3Minuti_Bloccato` | RF-S-32 |
| `JoinRateLimit_ContaAncheITentativiRiusciti` | Il punto centrale della difesa |
| `JoinRateLimit_LeaveEJoinRipetuti_SullaStessaConnessione_Bloccati` | §4.6 — il limite è per tentativo, non per connessione: un solo socket non deve poter scandire lo spazio degli ID |
| `JoinRateLimit_RiconnessioneStessoIp_NonAzzeraIlContatore` | La chiave è l'IP, non il socket |
| `JoinRateLimit_DopoIlBlocco_TornaAConsentire` | `retryAfterSeconds` rispettato |
| `CommandRateLimit_QuartoComandoIn10s_Bloccato` | RF-S-31 |
| `FrameOltre16384Byte_NonVieneDeserializzato` | RF-S-34 — il limite del frame, non quello dell'involucro |
| `Ciphertext_Oltre8192ByteDecodificati_RestituisceMessageTooLong` | [03 §9.1](03-protocollo.md) — misurato sui byte decodificati, non sul base64 |
| `Ciphertext_Da2000CaratteriCjk_Accettato` | Il caso peggiore realistico (3 byte per carattere) deve stare nel limite |
| `Log_NonCorrelanoIpEStanza` | §6.1 — nessun campo comune fra la riga con l'IP e quella con l'ID stanza |
