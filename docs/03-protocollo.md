# 03 — Protocollo Krypta v1

**Stato:** bozza · **Aggiornato:** 2026-09-08
**Questo documento è normativo.** Se client e server sono in disaccordo, vince quello
che c'è scritto qui.

> **Completamento della fase 1B, 2026-09-08.** Le codifiche crittografiche sono fissate
> **byte per byte** in §7bis.9, con sei **vettori di test** in §7bis.10 generati con la BCL
> di .NET 10 e verificati a ritroso. Erano l'ultimo pezzo mancante: gli algoritmi erano
> decisi, il modo di metterli sul filo no — e il server, per cui quelle stringhe sono
> opache, non potrebbe accorgersi di due client che le interpretano diversamente.
>
> **Aggiunta del 2026-09-08.** Il `join` porta un **messaggio facoltativo per chi deve
> approvare** (§4.1.3), inoltrato nelle voci di `waiting` (§5.10) e mostrato solo nel
> pannello della sala d'attesa — mai nel flusso della chat
> ([ADR-0010](adr/0010-messaggio-di-richiesta.md)). È testo **in chiaro**: chi lo scrive
> non ha ancora la chiave di stanza.
>
> **Correzioni del 2026-09-08.** Quattro punti in cui il documento contraddiceva sé stesso
> dopo l'introduzione dell'E2EE ([ADR-0008](adr/0008-cifratura-e2e-chiave-all-approvazione.md)):
> l'esempio di sessione di §10 era rimasto in chiaro; i tre limiti di dimensione non erano
> conciliabili (§9.1); il contatore anti-replay si rompeva al riuso di un nickname
> (§7bis.5); la garanzia sull'ordine dei messaggi era più forte del vero (§5.3).

## 1. Trasporto

| | |
|---|---|
| Protocollo | WebSocket (RFC 6455) |
| Endpoint | **`wss://{host}:{porta}/ws`** — TLS obbligatorio, vedi §1.1 |
| Tipo di frame | **Testo** (UTF-8) |
| Payload | Un singolo oggetto JSON per frame |
| Sotto-protocollo | `krypta.v1` (header `Sec-WebSocket-Protocol`) |

Un frame = un messaggio. Niente frammentazione applicativa, niente batching.

### 1.1 Due livelli di protezione, entrambi obbligatori

Krypta protegge il traffico su **due piani distinti**, che coprono avversari diversi e
non si sostituiscono a vicenda:

| Livello | Cosa protegge | Da chi | Dove è specificato |
|---|---|---|---|
| **TLS** (`wss://`) | Tutto il traffico: metadati compresi — ID stanza, nickname, messaggi di sistema, dimensioni | Chi osserva la rete | §1.2 |
| **E2EE** | Solo il **contenuto** dei messaggi | Chi gestisce il server | §7bis |

Il punto da tenere a mente: **l'E2EE non nasconde i metadati.** Nickname, ID stanza,
ingressi, uscite e messaggi di sistema restano in chiaro nel protocollo, perché è il
server a generarli e instradarli. Senza TLS, chi sniffa la rete li legge tutti anche con
la cifratura end-to-end attiva.

E viceversa: TLS da solo lascia il server in grado di leggere ogni messaggio.

**Servono entrambi.** Vedi [ADR-0008](adr/0008-cifratura-e2e-chiave-all-approvazione.md).

### 1.2 TLS

- Il client si connette in `wss://`. Il `ws://` in chiaro è ammesso **solo** verso
  `127.0.0.1` in sviluppo, e la UI deve segnalarlo in modo evidente e permanente.
- Per un server self-hosted con certificato autofirmato, il client applica **TOFU**
  (*trust on first use*): alla prima connessione mostra l'impronta SHA-256 del certificato
  e la memorizza; se a una connessione successiva l'impronta cambia, **rifiuta e avvisa**.
- L'impronta memorizzata è per server (host:porta), non per stanza, e finisce in
  `krypta.config.xml` — è l'unica eccezione persistente introdotta dalla cifratura
  ([06 §2](06-configurazione.md)).

## 2. Forma dei messaggi

Ogni messaggio, in entrambe le direzioni, è un oggetto JSON con un campo discriminante
`type` di tipo stringa, in `lowercase`.

```json
{ "type": "nome_del_messaggio", ...campi specifici }
```

I nomi dei campi sono in `camelCase`. I campi non previsti vengono **ignorati** dal
ricevente (permette di aggiungere campi in futuro senza rompere i client vecchi).
Un `type` sconosciuto produce un errore `UNKNOWN_TYPE`, non una disconnessione.

## 3. Ciclo di vita di una connessione

```
  CLIENT                                SERVER
    |                                     |
    |------- handshake WebSocket -------->|
    |<------ 101 Switching Protocols -----|
    |                                  [CONNESSO - fuori da ogni stanza]
    |                                     |
    |------- join (id, nickname) -------->|
    |                                     |
    |                    la stanza esiste gia'?
    |                     /                     \
    |                   NO                      SI
    |                    |                       |
    |   crea la stanza, entra subito     va in SALA D'ATTESA
    |                    |                       |
    |<--- joined --------|      <--- pending ----|
    |                                            |
    |                          (ai partecipanti) |
    |                          <--- waiting -----|
    |                          <--- system (join_requested)
    |                                            |
    |                        un partecipante invia approve
    |                                            |
    |<------ joined ------------------------------
    |<------ system (user_joined) --------|  (a tutti)
    |<------ participants ----------------|  (a tutti)
    |                                  [NELLA STANZA]
    |                                     |
    |------- message (text) ------------->|
    |<------ message ---------------------|  (a tutti, mittente incluso)
    |                                     |
    |------- command (name, arg) -------->|
    |<------ system / clear --------------|  (a tutti)
    |                                     |
    |<------ ping ------------------------|  ogni 30 s
    |------- pong ----------------------->|
    |                                     |
    |------- leave ---------------------->|
    |<------ left ------------------------|
    |                                  [CONNESSO - fuori da ogni stanza]
```

**Regola fondamentale:** una connessione WebSocket può essere in **al massimo una
stanza per volta** — e in al massimo una sala d'attesa. Per cambiare stanza si fa `leave`
e poi `join`. Non serve riconnettersi.

**Gli stati possibili di una connessione sono quindi tre:**

```
   FUORI  --join su stanza nuova------------------------>  DENTRO
     |                                                       ^
     |                                                       |
     +---- join su stanza esistente ---->  IN ATTESA  -- approve
                                              |
                                              +-- reject / timeout / cancel --> FUORI
```

Chi è **in attesa** non è un partecipante: non riceve i messaggi della stanza, non
compare nel contatore, non è elencato da `/Users`. Può solo aspettare o annullare.

---

## 4. Messaggi CLIENT → SERVER

### 4.1 `join` — entra o crea una stanza

```json
{
  "type": "join",
  "roomId": "K7X2M9",
  "nickname": "emy",
  "publicKey": "BEi3k9...==",
  "joinMessage": "Hey, mi vorrei unire alla stanza"
}
```

| Campo | Tipo | Obbl. | Vincoli |
|---|---|---|---|
| `roomId` | string | sì | Esattamente 6 caratteri alfanumerici **maiuscoli**: `[A-Z0-9]{6}`. Niente minuscole, spazi, punteggiatura o accenti. Vedi §4.1.0. |
| `nickname` | string | sì | 3–25 caratteri dopo il trim. Vedi §4.1.1. |
| `publicKey` | string | sì | Chiave pubblica **effimera di sessione**, in base64 (§7bis.2). Serve a ricevere la chiave della stanza al momento dell'approvazione. Il server ne verifica solo codifica e lunghezza; il contenuto non lo riguarda. Formato non valido: `INVALID_PUBLIC_KEY` |
| `joinMessage` | string | no | Messaggio per chi dovrà approvare: 1–200 caratteri, riga singola, **in chiaro**. Vedi §4.1.3. Formato non valido: `INVALID_JOIN_MESSAGE` |

#### 4.1.0 Vincoli sull'ID stanza

**L'ID è sempre maiuscolo: `[A-Z0-9]{6}`.** Non esiste una forma minuscola.

- Esattamente **6 caratteri**, ognuno una lettera maiuscola `A-Z` o una cifra `0-9`.
  Sono ammessi ID di sole cifre (`482913`), di sole lettere (`RIUNIO`) o misti (`K7X2M9`).
- **Il server rifiuta con `INVALID_ROOM_ID` qualsiasi altra forma**, minuscole comprese.
  Non normalizza: rifiuta.
- **È il client a garantire la forma maiuscola**, convertendo il testo mentre l'utente
  digita ([05 §4](05-client-desktop.md)). L'utente può quindi scrivere in minuscolo senza
  accorgersi di nulla: il campo lo mostra già maiuscolo.
- **È una stringa, non un numero.** Anche un ID di sole cifre resta una stringa: gli zeri
  iniziali sono significativi (`"000042"` ≠ `"42"`), e non ci si fa aritmetica sopra.
- Spazi ai bordi vengono rimossi dal client prima dell'invio; spazi interni rendono l'ID
  non valido.

**Perché una sola forma canonica e non un confronto tollerante:** se `k7x2m9` e `K7X2M9`
fossero due chiavi distinte nel dizionario delle stanze, l'utente che sbaglia il caso non
riceverebbe un errore — creerebbe una **stanza nuova e vuota**, perché in Krypta ogni ID
libero è valido. Si ritroverebbe da solo credendo di essere nel posto giusto, e gli altri
non lo vedrebbero mai arrivare: un fallimento silenzioso, il peggiore possibile qui.

Avere una sola forma valida elimina il problema alla radice. Il client rende la regola
invisibile all'utente, e un client scritto male ottiene un errore chiaro invece di
spaccare la stanza in due. Vedi [ADR-0004](adr/0004-id-stanza-alfanumerico.md).

Non esiste un campo per il nome della stanza. **Una stanza appena creata si chiama
sempre `RM-{roomId}`** (es. `RM-K7X2M9`) e può essere rinominata solo dopo, con il
comando `/NameRoom` (§6.1).

**Le risposte possibili sono due**, e dipendono da una sola cosa: se la stanza esiste già.

| Situazione | Risposta | Effetto |
|---|---|---|
| La stanza **non esiste** | `joined` con `created: true` | Il client entra subito: non c'è nessuno che possa approvarlo |
| La stanza **esiste** | `pending` (§5.9) | Il client va in sala d'attesa e resta lì finché un partecipante non lo accetta |

Oppure `error` con `INVALID_ROOM_ID`, `INVALID_NICKNAME`, `ROOM_FULL`,
`WAITING_ROOM_FULL`, `ALREADY_IN_ROOM`, `ALREADY_PENDING`, `JOIN_RATE_LIMITED`.

**Nota importante:** il primo che arriva su un ID libero entra senza approvazione, ed è
inevitabile — non c'è ancora nessuno dentro. Ne consegue che chi vuole una stanza protetta
deve **crearla per primo**: se un estraneo arriva sull'ID prima di lui, si ritrova lui a
dover chiedere il permesso a uno sconosciuto. Vale la pena dirlo nella UI.

#### 4.1.1 Vincoli sul nickname

- Da **3 a 25 caratteri** dopo il trim.
- **Solo ASCII:** `[A-Za-z0-9_.-]` e lo spazio singolo interno. Niente lettere accentate,
  niente alfabeti non latini, niente emoji. Vietati i caratteri di controllo.
- Fuori da questo insieme: `INVALID_NICKNAME`.

**Perché ASCII e non "lettere Unicode".** Il nickname finisce **dentro l'AAD** dei messaggi
(§7bis.4), quindi i suoi byte esatti sono parte di un calcolo crittografico. Con Unicode,
`josé` può essere scritto in due modi diversi — `é` come singolo carattere (NFC) oppure
come `e` più un accento combinante (NFD) — che hanno byte UTF-8 diversi e **stesso aspetto
sullo schermo**. Se il server dichiarasse il nickname in una forma e il client ricostruisse
l'AAD nell'altra, la decifratura fallirebbe e ogni messaggio di quella persona apparirebbe
come *non verificato*: un allarme di sicurezza permanente, con l'aspetto di un attacco, per
una ragione tipografica.

Si poteva risolvere imponendo una normalizzazione (NFC) su server e client. Ma sarebbe una
regola in più da applicare **in modo identico in due punti**, che se dimenticata da una
parte fallisce in silenzio — la stessa categoria di problema che la forma canonica unica
dell'ID stanza elimina alla radice ([ADR-0004](adr/0004-id-stanza-alfanumerico.md)).
Vietare i non-ASCII toglie il problema invece di gestirlo, al prezzo di nickname meno
espressivi. Per un nome che vive quanto una sessione, è un prezzo basso.

**Conseguenza per l'AAD:** poiché nickname e `roomId` sono entrambi ASCII, la stringa
dell'AAD ha una sola rappresentazione possibile in UTF-8, e non serve nessuna
normalizzazione prima di calcolarla (§7bis.9).
- **Nomi riservati**, rifiutati con `INVALID_NICKNAME` (confronto senza distinzione di
  maiuscole): `STANZA`, `KRYPTA`, `SISTEMA`, `SYSTEM`, `SERVER`, `ROOM`.
  Servono a impedire che un utente si spacci per un messaggio di sistema (§5.4).

#### 4.1.2 Nickname duplicati: il server disambigua

Se il nickname richiesto è già in uso **in quella stanza**, il server **non rifiuta**:
appende il più piccolo intero ≥ 2 che rende il nome libero.

```
"emy" occupato          -> "emy2"
"emy" e "emy2" occupati -> "emy3"
```

Se il nome risultante supererebbe i 25 caratteri, la base viene **troncata** quel tanto
che basta perché il risultato stia esattamente in 25 caratteri.

Il nickname effettivamente assegnato viene restituito nel campo `nickname` della risposta
`joined`. **Il client deve usare quello**, non quello che ha digitato l'utente: da esso
dipende anche il colore mostrato (vedi [ADR-0003](adr/0003-colori-nickname-deterministici.md)).

Conseguenza: `NICKNAME_TAKEN` **non esiste** come codice di errore.

#### 4.1.3 Vincoli sul messaggio di richiesta

Il campo è **facoltativo**: una richiesta senza `joinMessage` è legittima e non va
penalizzata in nessun modo.

- Da **1 a 200 caratteri** dopo il trim. Una stringa vuota o di soli spazi equivale al
  campo assente: il server la tratta come tale, non come errore.
- **Riga singola.** Gli a capo e i caratteri di controllo sono vietati: il testo viene
  reso in una riga di pannello accanto a due pulsanti ([05 §5.1bis](05-client-desktop.md)).
- Oltre i limiti, o con caratteri di controllo: `INVALID_JOIN_MESSAGE`.
- **È testo, e va reso come testo.** Mai interpretato come HTML, markup o formattazione:
  è la stringa più ostile del protocollo, perché è l'unica scritta da qualcuno che non è
  stato ammesso da nessuno.

**Dove finisce, e dove non finisce.** Il messaggio compare **solo** nelle voci di `waiting`
(§5.10), cioè nel pannello della sala d'attesa. **Non** entra nel testo del `system`
`join_requested`, che resta generato interamente dal server.

La distinzione non è cosmetica. Il `system` finisce nella conversazione di tutti: se
contenesse il testo del richiedente, bussare a un ID diventerebbe il modo di scrivere in
una stanza in cui non si è entrati. Il pannello invece non si apre da solo (RF-C-A07), e
quindi **leggere il messaggio è una scelta di chi è dentro**. Vedi
[ADR-0010](adr/0010-messaggio-di-richiesta.md).

**Su una stanza nuova il messaggio non serve a nessuno, e il server lo ignora.** Chi crea
la stanza entra subito: non c'è nessuno che debba decidere, quindi non c'è nessuno che
legga. Il campo resta comunque valido e non produce errore — **deve** essere così, perché
al momento dell'invio il client non sa se la stanza esista: scoprirlo dalla risposta è
tutto il punto della sala d'attesa (§5.9). Un `join` che crea una stanza porta con sé un
messaggio che viene semplicemente scartato.

**Il testo predefinito non è nel protocollo.** Il client precompila il campo con qualcosa
come *"Hey, mi vorrei unire alla stanza"* e lascia modificarlo
([05 §4](05-client-desktop.md)): è una scelta di interfaccia, e cambiarla o tradurla non
tocca il contratto.

Conseguenza da tenere presente: **quasi nessuno lo modificherà.** Un campo precompilato
resta com'è nella maggior parte dei casi, quindi il valore informativo tipico di questo
campo è basso, e chi approva vedrà spesso la stessa frase identica da persone diverse. Non
è un difetto — è il motivo in più per non presentarlo mai come se identificasse qualcuno.

**Non è cifrato, e non può esserlo.** Chi è in attesa non ha ancora la chiave di stanza —
la riceve con l'approvazione (§7bis.1). Un messaggio che deve essere letto *prima*
dell'approvazione viaggia quindi in chiaro, e il gestore del server lo legge. È l'unico
contenuto scritto da un utente che l'E2EE non copre (§7bis.6), e va detto nella UI.

### 4.2 `message` — invia un messaggio nella stanza

```json
{
  "type": "message",
  "seq": 7,
  "keyEpoch": 1,
  "nonce": "9jK1vQ8bZ2xR",
  "ciphertext": "3pQ9xK2mA7...=="
}
```

| Campo | Tipo | Obbl. | Vincoli |
|---|---|---|---|
| `seq` | int | sì | Contatore **strettamente crescente per mittente**, da 1. Difesa dal replay: vedi §7bis.5 |
| `keyEpoch` | int | sì | Con quale chiave di stanza è cifrato: 1 alla nascita della stanza, +1 a ogni `/GenerateKey` (§7bis.11). Serve al ricevente per scegliere la chiave giusta |
| `nonce` | string | sì | 12 byte casuali in base64, uno diverso per ogni messaggio |
| `ciphertext` | string | sì | Testo cifrato + tag, base64. Il testo in chiaro è 1–2000 caratteri (§7bis.4) |

**Il testo in chiaro non compare nel protocollo.** Il server riceve un blob che non sa
leggere, ne verifica solo la dimensione, e lo inoltra.

Il client **non** indica il proprio nickname: lo conosce già il server, associato alla
connessione. Questo impedisce di spacciarsi per un altro utente all'interno della stanza —
e la cifratura lo rafforza, perché il nickname è legato crittograficamente al messaggio
(§7bis.4): nemmeno il server può riattribuire un messaggio a qualcun altro senza rompere
la decifratura.

Un testo che inizia con `/` **non** deve essere inviato come `message`: va convertito in
un `command` dal client (§6.2). **I comandi non sono cifrati** — li esegue il server, che
quindi deve poterli leggere. Vedi §7bis.6 per l'elenco di cosa è cifrato e cosa no.

Errori: `NOT_IN_ROOM`, `MESSAGE_TOO_LONG` (sulla dimensione del `ciphertext`),
`INVALID_ENVELOPE` (`nonce`/`seq`/`ciphertext` mancanti o malformati), `RATE_LIMITED`.

Nota: `EMPTY_MESSAGE` **non esiste più** come errore del server. Il server non può sapere
se un ciphertext contiene testo vuoto: il controllo è ora solo lato client.

### 4.3 `command` — esegui un comando di stanza

```json
{
  "type": "command",
  "name": "nameroom",
  "arg": "Coordinamento"
}
```

| Campo | Tipo | Obbl. | Vincoli |
|---|---|---|---|
| `name` | string | sì | Nome del comando **in minuscolo, senza `/`**. Il client normalizza. |
| `arg` | string | no | Argomento, già ripulito dagli spazi ai bordi. Assente per i comandi che non lo prevedono. |

I comandi disponibili sono descritti in §6. Errori: `NOT_IN_ROOM`, `UNKNOWN_COMMAND`,
`INVALID_COMMAND_ARG`, `COMMAND_RATE_LIMITED`.

### 4.4 `approve` / `reject` — accetta o rifiuta chi è in attesa

```json
{
  "type": "approve",
  "requestId": "3f9a1c4e-...",
  "wrappedKey": {
    "epk": "BPq2m...==",
    "nonce": "9jK1vQ8bZ2xR",
    "ciphertext": "kR7d...=="
  }
}
```

```json
{ "type": "reject", "requestId": "3f9a1c4e-..." }
```

| Campo | Tipo | Obbl. | Vincoli |
|---|---|---|---|
| `requestId` | string | sì | Il token opaco ricevuto nel messaggio `waiting` (§5.10). Non è il `ConnectionId`, che resta interno |
| `wrappedKey` | object | sì per `approve` | **La chiave della stanza, cifrata per il richiedente** dal client che approva (§7bis.3). Il server la inoltra senza poterla leggere: ne verifica solo la presenza e la forma |
| `keyEpoch` | int | sì per `approve` | L'epoca della chiave che si sta consegnando. Se nel frattempo qualcuno ha ruotato, non è più quella corrente: il server risponde `STALE_KEY_EPOCH` e il client rifà il wrapping con la chiave nuova (§7bis.11) |

**È il gesto centrale del design.** Approvare qualcuno e consegnargli la chiave sono la
stessa operazione: chi si fida abbastanza da farlo entrare è esattamente chi gli passa la
chiave, e il server non partecipa. Un `approve` senza `wrappedKey` è rifiutato con
`INVALID_APPROVAL` — **il server non può ammettere nessuno per conto proprio**, perché
non possiede alcuna chiave da consegnare.

Inviabili **solo da un partecipante già dentro la stanza**. Vedi §6bis.

Errori: `NOT_IN_ROOM`, `UNKNOWN_REQUEST` (token sconosciuto o già evaso),
`ROOM_FULL` (la stanza si è riempita mentre la richiesta era in coda),
`INVALID_APPROVAL` (`wrappedKey` mancante o malformato).

### 4.4bis `rekey` — rigenera la chiave della stanza

Prodotto dal comando `/GenerateKey` (§6.6). **Non** è un `command`: porta con sé un
involucro per ciascun partecipante, che nel campo `arg` non ci starebbe.

```json
{
  "type": "rekey",
  "keyEpoch": 2,
  "keys": [
    { "nickname": "luca", "wrappedKey": { "epk": "BM4tZ...==", "nonce": "9jK1...", "ciphertext": "kR7d...==" } },
    { "nickname": "sara", "wrappedKey": { "epk": "BPq7w...==", "nonce": "3xR8...", "ciphertext": "9tLm...==" } }
  ]
}
```

| Campo | Tipo | Obbl. | Vincoli |
|---|---|---|---|
| `keyEpoch` | int | sì | L'epoca **nuova**, cioè quella corrente più uno. Se non corrisponde, `STALE_KEY_EPOCH`: qualcun altro ha ruotato per primo |
| `keys` | array | sì | Un involucro per **ciascun altro partecipante**, con la stessa forma della `wrappedKey` di un `approve` (§7bis.3). Il mittente non compare: la chiave ce l'ha già |

**Il server verifica che l'elenco sia completo**, cioè che i `nickname` dei destinatari
coincidano esattamente con i partecipanti attuali meno il mittente. Se ne manca uno, o ce
n'è uno di troppo, o uno è duplicato: `INVALID_REKEY`, e la rotazione **non avviene**.

Non è formalismo. Se una rotazione potesse omettere qualcuno, sarebbe il modo per
**espellere un partecipante** senza espellerlo: resterebbe nella stanza, nel contatore e in
`/Users`, ma senza poter più leggere niente. Sarebbe un potere di un partecipante sugli
altri, in un prodotto che ha deciso di non averne
([ADR-0005](adr/0005-comandi-di-stanza-senza-ruoli.md)). Il server non può aprire gli
involucri, ma può contarli e guardare a chi sono indirizzati — e questo gli basta.

Ogni chiave effimera `epk` è **monouso**: una diversa per ogni destinatario, esattamente
come all'approvazione. Riusarne una sola per tutti farebbe condividere materiale derivato
fra persone che non devono condividerlo.

Errori: `NOT_IN_ROOM`, `INVALID_REKEY`, `STALE_KEY_EPOCH`, `REKEY_RATE_LIMITED`.

### 4.5 `cancel` — ritira la propria richiesta di ingresso

```json
{ "type": "cancel" }
```

Inviabile solo da chi è **in attesa**. Toglie la propria richiesta dalla sala d'attesa e
riporta la connessione fuori da ogni stanza. Se il mittente non è in attesa:
`error` con `NOT_PENDING`.

### 4.6 `leave` — esci dalla stanza

```json
{ "type": "leave" }
```

Se il client non è in una stanza: `error` con `NOT_IN_ROOM`. Non è fatale.

### 4.7 `pong` — risposta all'heartbeat

```json
{ "type": "pong" }
```

Va inviato entro 30 secondi dalla ricezione di un `ping`. Vedi §7.

---

## 5. Messaggi SERVER → CLIENT

### 5.1 `joined` — ingresso riuscito

```json
{
  "type": "joined",
  "roomId": "K7X2M9",
  "roomName": "RM-K7X2M9",
  "nickname": "emy2",
  "participants": 3,
  "created": false,
  "maxParticipants": 20
}
```

| Campo | Tipo | Significato |
|---|---|---|
| `roomId` | string | L'ID della stanza, come conferma |
| `roomName` | string | Nome corrente della stanza, da mostrare in alto. `RM-{roomId}` se mai rinominata |
| `nickname` | string | **Il nickname effettivamente assegnato** (§4.1.2). Può differire da quello richiesto |
| `participants` | int | Numero di partecipanti **incluso** chi è appena entrato |
| `created` | bool | `true` se questo join ha creato la stanza |
| `maxParticipants` | int | Sempre 20 nell'MVP. Inviato dal server perché il client non lo cabli |
| `keyEpoch` | int | L'epoca della chiave che stai ricevendo: `1` se hai creato tu la stanza, l'epoca corrente se sei stato ammesso (§7bis.11) |
| `wrappedKey` | object | **Presente solo se `created == false`**: la chiave della stanza cifrata per te, così come l'ha prodotta chi ti ha accettato. Il client la apre con la propria chiave privata (§7bis.3) |

Se `created == true` **non c'è nessuna `wrappedKey`**: chi crea la stanza genera lui
stesso la chiave, localmente (§7bis.2). Il server non la vede in nessuno dei due casi.

**Dopo questo messaggio il server non invia nulla di pregresso.** Nessun messaggio
storico, nessun riepilogo. Questo è il punto centrale del prodotto.

### 5.2 `participants` — il numero di partecipanti è cambiato

```json
{ "type": "participants", "count": 4 }
```

Inviato a tutti i partecipanti a ogni ingresso o uscita. Alimenta il contatore `4/20`
nell'intestazione. L'elenco dei nomi **da mostrare** si ottiene invece con `/Users` (§6.3).

### 5.2bis `roster` — le chiavi pubbliche dei presenti

```json
{
  "type": "roster",
  "keyEpoch": 2,
  "participants": [
    { "nickname": "emy",  "publicKey": "BBmcFyq...==" },
    { "nickname": "luca", "publicKey": "BIN2ENu...==" }
  ]
}
```

Inviato ai soli partecipanti, insieme al `participants`, a ogni cambio di composizione e
subito dopo il `joined`. Serve **al solo scopo di poter rigenerare la chiave**: senza le
chiavi pubbliche dei presenti, chi digita `/GenerateKey` non avrebbe per chi cifrare
(§4.4bis).

| Campo | Tipo | Significato |
|---|---|---|
| `keyEpoch` | int | L'epoca corrente della chiave di stanza |
| `participants` | array | Una voce per partecipante, in ordine di ingresso, con la chiave pubblica di sessione dichiarata nel `join` |

**Questo messaggio non va usato per disegnare l'elenco dei partecipanti nella UI.** L'elenco
si mostra solo in risposta a `/Users`, che è visibile a tutti: è la regola per cui nessuno
può controllare chi c'è di nascosto ([ADR-0006](adr/0006-partecipanti-visibili-nella-stanza.md)).

Vale la pena essere precisi sul perché questo non è un cedimento su quella regola. La lista
dei presenti **è già ricostruibile in silenzio** da qualunque client, tenendo il conto dei
`system` `user_joined` e `user_left`, che arrivano a tutti: il `roster` non aggiunge
informazione, la rende esplicita. Ciò che la regola protegge è la **simmetria della UI** —
chi vuole *vedere* chi c'è lo fa davanti agli altri — e quella resta intatta finché il
client si comporta bene. Contro un client modificato non era protetta nemmeno prima.

### 5.3 `message` — un messaggio nella stanza

```json
{
  "type": "message",
  "nickname": "emy2",
  "seq": 7,
  "keyEpoch": 1,
  "nonce": "9jK1vQ8bZ2xR",
  "ciphertext": "3pQ9xK2mA7...==",
  "sentAt": "2026-09-06T19:42:11.325Z"
}
```

| Campo | Tipo | Significato |
|---|---|---|
| `nickname` | string | Nickname effettivo del mittente, aggiunto dal server. **In chiaro** |
| `seq` | int | Il contatore del mittente, inoltrato invariato |
| `keyEpoch` | int | L'epoca della chiave, inoltrata invariata (§7bis.11) |
| `nonce` / `ciphertext` | string | L'involucro cifrato, inoltrato invariato |
| `sentAt` | string | Timestamp ISO-8601 in UTC, generato dal **server** (mai dal client). **In chiaro** e **non autenticato**: è un'indicazione, non una prova |

Inviato a tutti i partecipanti, **mittente incluso**. Il client mostra i messaggi solo
quando li riceve dal server, non quando li invia (modalità **eco**): non esiste
visualizzazione ottimistica, e quindi nemmeno un messaggio "quasi inviato" che compare
prima di esistere per gli altri.

**Sull'ordine, la garanzia va detta con precisione.** L'eco da sola non basta a renderlo
identico su tutti gli schermi: ogni partecipante ha la propria coda di invio
([04 §4.3](04-api-server.md)) e due inoltri concorrenti, se accodati fuori da una sezione
critica comune, possono finire in ordine diverso in code diverse — `luca` vedrebbe *A, B*
e `sara` *B, A*, entrambi in buona fede.

L'ordine è quindi lo stesso per tutti **a una condizione vincolante per il server**:
l'accodamento di un inoltro a tutti i partecipanti avviene in un'unica sezione critica,
così che due inoltri non possano interlacciarsi in modo diverso fra una coda e l'altra
(invariante I13, [04 §4.1](04-api-server.md)). Rispettata quella, vale:

| Garanzia | Vale? |
|---|---|
| I messaggi di uno stesso mittente arrivano nell'ordine in cui li ha inviati | ✅ Sì — TCP sotto, coda singola sopra, e il `seq` lo rende verificabile |
| Tutti i partecipanti vedono i messaggi nello stesso ordine | ✅ Sì, **se** vale I13 |
| Quell'ordine è l'ordine reale in cui le persone hanno scritto | ❌ **No.** È l'ordine di arrivo al server, che dipende dalla latenza di rete di ciascuno |
| Il server non può alterare l'ordine | ❌ **No.** Può riordinare o scartare a piacere: l'E2EE protegge il contenuto e l'autore, non la sequenza (§7bis.4) |

Le ultime due righe sono il motivo per cui `sentAt` è dichiarato **non autenticato** nella
tabella dei campi qui sopra: è un'indicazione utile a leggere, non una prova di quando
qualcosa è stato detto.

Il server **non conserva** questo messaggio dopo averlo inoltrato.

### 5.4 `system` — messaggio della stanza

Messaggio generato dal server e mostrato nella chat come se lo inviasse la stanza stessa.

```json
{
  "type": "system",
  "kind": "user_joined",
  "text": "emy2 si è unito alla stanza",
  "sentAt": "2026-09-06T19:41:02.114Z"
}
```

| Campo | Tipo | Significato |
|---|---|---|
| `kind` | string | Sottotipo stabile. Il client si basa su questo per lo stile, mai sul testo |
| `text` | string | Testo già pronto, in italiano |
| `sentAt` | string | Timestamp UTC generato dal server |
| `roomName` | string | **Solo per `kind: "room_renamed"`**: il nuovo nome, per aggiornare l'intestazione |
| `users` | string[] | **Solo per `kind: "user_list"`**: i nickname dei partecipanti |

Sottotipi:

| `kind` | Quando | Testo di esempio |
|---|---|---|
| `join_requested` | Qualcuno chiede di entrare | `sara chiede di entrare nella stanza` |
| `join_rejected` | Una richiesta è rifiutata | `luca ha rifiutato sara` |
| `user_joined` | Un partecipante entra | `emy2 si è unito alla stanza, accettato da luca` |
| `room_created` | Il primo entrato ha creato la stanza | `emy ha creato la stanza` |
| `user_left` | Un partecipante esce o cade | `luca è uscito dalla stanza` |
| `room_renamed` | Dopo `/NameRoom` | `emy2 ha rinominato la stanza in "Coordinamento"` |
| `user_list` | Risposta a `/Users` | `Partecipanti (3/20): emy2, luca, sara` |
| `room_cleared` | Dopo `/ClearRoom` | `emy2 ha svuotato la stanza` |
| `room_rekeyed` | Dopo `/GenerateKey` | `emy ha rigenerato la chiave della stanza` |

Tutti i `system` sono inviati a **tutti** i partecipanti della stanza, incluso chi ha
scatenato l'evento. Anche `user_list`: è una scelta esplicita — la richiesta dell'elenco
è visibile a tutti, nessuno può sapere chi c'è di nascosto.

I messaggi `system` **non vengono conservati** più dei messaggi normali.

### 5.5 `clear` — svuota la vista

```json
{ "type": "clear" }
```

Inviato a tutti i partecipanti dopo un `/ClearRoom`. Ogni client **svuota la propria
lista di messaggi in memoria**.

> **Attenzione a cosa significa.** Il server non ha messaggi da cancellare: non ne ha
> mai conservato nessuno ([ADR-0002](adr/0002-nessuna-persistenza.md)). `/ClearRoom` è
> un'istruzione ai client, non una cancellazione lato server. Un client modificato può
> semplicemente ignorarla. **Non è una garanzia di sicurezza, è una comodità di
> lettura.** Va detto anche nella UI.

L'ordine è vincolante: prima `clear`, poi il `system` con `kind: "room_cleared"`, così la
riga che spiega cos'è successo sopravvive allo svuotamento.

### 5.6 `left` — uscita confermata

```json
{ "type": "left", "roomId": "K7X2M9" }
```

La connessione resta aperta: il client può fare un nuovo `join`.

### 5.7 `ping` — heartbeat

```json
{ "type": "ping" }
```

### 5.8 `error` — errore

```json
{
  "type": "error",
  "code": "ROOM_FULL",
  "message": "La stanza ha già 20 partecipanti."
}
```

| Campo | Tipo | Significato |
|---|---|---|
| `code` | string | Codice stabile, in `SCREAMING_SNAKE_CASE`. **Il client si basa su questo**, mai sul testo |
| `message` | string | Testo leggibile, in italiano. Può cambiare senza preavviso |
| `retryAfterSeconds` | int | Presente solo su `JOIN_RATE_LIMITED` e `COMMAND_RATE_LIMITED` |

Un `error` è sempre diretto **al solo client** che l'ha causato: non viene mai
trasmesso agli altri partecipanti.

---

### 5.9 `pending` — sei in sala d'attesa

Risposta a un `join` su una stanza **esistente**. Inviato **solo al richiedente**.

```json
{
  "type": "pending",
  "roomId": "K7X2M9",
  "expiresInSeconds": 120
}
```

| Campo | Tipo | Significato |
|---|---|---|
| `roomId` | string | La stanza per cui si è in attesa |
| `expiresInSeconds` | int | Dopo quanto la richiesta scade se nessuno risponde |

**Il messaggio non contiene nulla sulla stanza**: né il nome, né il numero di
partecipanti, né chi c'è dentro. Chi è in attesa non è ancora dentro, e non deve poter
dedurre niente da fuori. È il punto che rende la sala d'attesa una difesa vera e non solo
un ostacolo: bussare a un ID non dice più nemmeno se quella stanza è affollata o vuota.

### 5.10 `waiting` — la sala d'attesa è cambiata

Inviato a **tutti i partecipanti** (mai a chi è in attesa) ogni volta che la coda cambia.
Contiene sempre l'elenco completo, non un delta: un client che perde un messaggio si
riallinea da solo al successivo.

```json
{
  "type": "waiting",
  "requests": [
    {
      "requestId": "3f9a1c4e-...",
      "nickname": "sara",
      "publicKey": "BEi3k9...==",
      "joinMessage": "Sono Sara, quella del preventivo",
      "waitingSeconds": 12
    }
  ]
}
```

| Campo | Tipo | Significato |
|---|---|---|
| `requestId` | string | Token opaco da usare in `approve` / `reject` |
| `nickname` | string | Il nickname **richiesto**, non ancora quello definitivo (§4.1.2) |
| `publicKey` | string | **La chiave pubblica del richiedente.** Serve al client che approva per cifrare la chiave della stanza. Da essa il client calcola il **codice di verifica** da mostrare (§7bis.7) |
| `joinMessage` | string | Il messaggio lasciato dal richiedente (§4.1.3), inoltrato **invariato**. **Assente** se non ne ha lasciato uno: il client non deve inventare un testo al suo posto |
| `waitingSeconds` | int | Da quanto la richiesta è in coda |

**È l'unico punto del protocollo in cui il messaggio viaggia**, ed è diretto ai soli
partecipanti. Non compare nel `pending` (chi attende sa già cosa ha scritto), non compare
nel `system` `join_requested` (§4.1.3), e non viene conservato da nessuna parte.

Una coda vuota si trasmette come `"requests": []`: è ciò che spegne il pulsante nella UI.

**Il `nickname` qui è ancora quello richiesto.** La disambiguazione avviene
all'ammissione, non alla richiesta, perché nel frattempo la stanza può cambiare: chi
approva vede `sara`, ma se dentro c'è già una `sara` l'ammessa diventerà `sara2`.
Il nome definitivo compare nel `system` `user_joined`.

### 5.11 `rejected` — richiesta non andata a buon fine

Inviato **solo al richiedente**. La connessione **resta aperta**: si torna alla schermata
di selezione stanza.

```json
{ "type": "rejected", "roomId": "K7X2M9", "reason": "rejected" }
```

| `reason` | Significato | Cosa mostra il client |
|---|---|---|
| `rejected` | Un partecipante ha rifiutato | "La tua richiesta è stata rifiutata." |
| `timeout` | Nessuno ha risposto entro il tempo | "Nessuno ha risposto alla tua richiesta." |
| `room_full` | Approvata, ma la stanza si è riempita nel frattempo | "La stanza si è riempita mentre attendevi." |
| `room_gone` | L'ultimo partecipante è uscito e la stanza è stata distrutta | "La stanza non esiste più." |

**Il client non deve distinguere `rejected` da `timeout` con toni diversi.** Chi rifiuta
non deve sentirsi obbligato a giustificarsi, e chi è rifiutato non deve poter dedurre se
è stato ignorato o respinto di proposito. Il testo cambia, il tono no.

### 5.12 `rekeyed` — la chiave della stanza è cambiata

Inviato a **ciascun partecipante separatamente**, con il solo involucro destinato a lui,
subito dopo un `rekey` valido (§4.4bis).

```json
{
  "type": "rekeyed",
  "keyEpoch": 2,
  "by": "emy",
  "wrappedKey": { "epk": "BM4tZ...==", "nonce": "9jK1...", "ciphertext": "kR7d...==" }
}
```

| Campo | Tipo | Significato |
|---|---|---|
| `keyEpoch` | int | L'epoca nuova. Da questo momento i messaggi si cifrano con la chiave nuova |
| `by` | string | Chi ha rigenerato la chiave, messo dal server. Serve alla riga di sistema e a sapere di chi fidarsi |
| `wrappedKey` | object | **Il tuo** involucro, inoltrato invariato da chi ha ruotato. Si apre con la propria chiave privata di sessione, come all'ammissione (§7bis.3) |

Chi ha eseguito la rotazione **non** riceve questo messaggio: la chiave nuova l'ha generata
lui. Riceve solo il `system` `room_rekeyed`, come tutti.

Subito dopo, a tutti i partecipanti, arriva un `system` con `kind: "room_rekeyed"`.
L'ordine è vincolante — prima gli involucri, poi l'annuncio — così quando la riga compare
in chat la chiave nuova è già stata consegnata.

**Se l'involucro non si apre**, il client non deve fingere che vada tutto bene: perde
l'accesso ai messaggi dell'epoca nuova, e va detto con la stessa evidenza di un messaggio
non verificato ([05 §5bis](05-client-desktop.md)). L'unica via d'uscita è uscire e
rientrare dalla stanza.

---

## 6bis. Regole della sala d'attesa

### Chi può approvare

**Qualunque partecipante già dentro.** Basta un solo `approve`: non serve unanimità né
maggioranza. Coerente con l'assenza di ruoli — non esiste un proprietario della stanza
([ADR-0005](adr/0005-comandi-di-stanza-senza-ruoli.md)) — e con la tracciabilità come
contrappeso: il `system` `user_joined` dice **chi** ha accettato.

Lo stesso vale per `reject`: basta uno. Il primo che risponde chiude la richiesta, e gli
altri ricevono `UNKNOWN_REQUEST` se rispondono dopo.

### Ciclo di vita di una richiesta

```
   join su stanza esistente
            |
            v
     [IN ATTESA]  --- approve (un partecipante qualsiasi) ---> DENTRO
         |  |  |
         |  |  +----- reject --------------------------------> FUORI
         |  +-------- timeout: 120 s senza risposta ----------> FUORI
         +----------- cancel (il richiedente ritira) ---------> FUORI
                      oppure: l'ultimo partecipante esce ----->  FUORI (room_gone)
```

### Vincoli

| Regola | Valore | Motivo |
|---|---|---|
| Sala d'attesa per stanza | max **10** richieste | Senza un tetto, la coda è un canale di disturbo verso tutti i partecipanti |
| Timeout di una richiesta | **120 s** | Chi bussa non deve restare appeso a tempo indeterminato |
| Chi è in attesa conta nel limite di 20? | **No** | Il limite riguarda i partecipanti. Il controllo si fa **all'ammissione** |
| Stanza già piena (20) | `join` rifiutato subito con `ROOM_FULL` | Inutile mettere in coda chi non potrebbe comunque entrare |
| Stanza che si riempie durante l'attesa | `approve` fallisce, richiedente riceve `rejected` con `room_full` | La verifica del limite va rifatta al momento dell'ammissione, non della richiesta |
| Ultimo partecipante che esce | la stanza è distrutta, le richieste pendenti ricevono `room_gone` | Coerente con il ciclo di vita: senza partecipanti la stanza non esiste |
| Cosa può fare chi è in attesa | solo `cancel` e `pong` | `message`, `command`, `approve` e `leave` restituiscono `NOT_IN_ROOM` o `NOT_PENDING` |
| Messaggio allegato alla richiesta | facoltativo, max **200 caratteri**, in chiaro | Dà a chi approva qualcosa su cui decidere. Visibile **solo nel pannello**, mai nel flusso della chat: altrimenti bussare a un ID diventa un modo per scrivere in una stanza in cui non si è entrati ([ADR-0010](adr/0010-messaggio-di-richiesta.md)) |

### Cosa la sala d'attesa non protegge

Chi approva vede **un nickname e un messaggio che il richiedente ha scritto da sé**
(§4.1.3). Nessuno dei due è verificabile: chi conosce l'ID può presentarsi con il nome
giusto e scrivere la frase giusta, e scriverla non gli costa niente.

Il messaggio serve a **distinguere fra persone che si aspettano** — il Marco atteso
dall'omonimo capitato lì — non l'amico dall'estraneo. Anzi, va tenuto presente il verso
opposto: dà a chi approva la **sensazione** di essere informato senza dargli un solo
elemento verificabile in più, ed è esattamente così che funziona l'inganno.

La sala d'attesa alza la barriera da "chi indovina l'ID entra" a "chi indovina l'ID deve
convincere qualcuno", che è molto, ma non è autenticazione. L'unica cosa che verifica
davvero qualcosa resta il **codice di verifica** (§7bis.7), che il messaggio non
sostituisce. Vedi [07 §4.3](07-sicurezza-e-privacy.md) e
[ADR-0010](adr/0010-messaggio-di-richiesta.md).

---

## 6. Comandi

### 6.0 Regole generali

- Un comando è un messaggio digitato che inizia con `/`.
- **Il nome del comando è insensibile a maiuscole e minuscole**: `/NameRoom`,
  `/nameroom` e `/NAMEROOM` sono lo stesso comando. Il client lo normalizza in
  minuscolo prima di inviarlo.
- Per scrivere un messaggio che inizia davvero con `/`, si raddoppia: `//comando`
  viene inviato come testo normale `/comando`.
- **Ogni partecipante può eseguire ogni comando.** Non esistono ruoli, proprietari o
  amministratori: la stanza non ha proprietario ([ADR-0002](adr/0002-nessuna-persistenza.md)).
  Il contrappeso è la tracciabilità: `/NameRoom` e `/ClearRoom` producono un `system`
  che dice **chi** li ha eseguiti.
- **Limite di frequenza:** massimo 3 comandi ogni 10 secondi per connessione. Oltre,
  `COMMAND_RATE_LIMITED`.
- **`/Help` è l'unico comando che non passa dal server** (§6.5): il client lo esegue da
  solo. Non è quindi soggetto al limite di frequenza e non consuma banda.
- **`/GenerateKey` non viaggia come `command`** (§6.6): il client lo traduce in un `rekey`
  (§4.4bis), perché deve portare un involucro per ogni partecipante. Ha un limite di
  frequenza proprio, **per stanza**: 1 ogni 30 secondi.

### 6.1 `/NameRoom <nome>` — rinomina la stanza

```
/NameRoom Coordinamento
```

| | |
|---|---|
| `arg` | Il nuovo nome |
| Vincoli | Da 1 a **25 caratteri**: alfanumerici (`A-Z`, `a-z`, `0-9`) **e spazio**. Nessuna punteggiatura, nessun accento |
| Effetto | Cambia il nome della stanza per tutti i partecipanti, subito |
| Risposta | `system` con `kind: "room_renamed"` e campo `roomName`, a tutti |
| Errore | `INVALID_COMMAND_ARG` se il nome è vuoto, troppo lungo o contiene caratteri non ammessi |

Regole sugli spazi, applicate **prima** del controllo di lunghezza:

- gli spazi iniziali e finali vengono rimossi;
- **due o più spazi consecutivi diventano uno solo**;
- un nome fatto di soli spazi è vuoto, quindi non valido.

Senza queste regole `"A" + 24 spazi` sarebbe un nome valido di 25 caratteri che appare
vuoto nell'intestazione, e `"Team    A"` e `"Team A"` sarebbero nomi diversi
indistinguibili sullo schermo. La normalizzazione la fa **il server**, ed è il nome
normalizzato quello che torna nel campo `roomName`.

Il nome vive quanto la stanza: quando esce l'ultimo partecipante la stanza viene
distrutta e il nome sparisce con lei. Chi rientrerà su quell'ID troverà di nuovo
`RM-K7X2M9`.

**L'ID resta l'unico modo per collegarsi.** Il nome è solo un'etichetta di
visualizzazione: non è indicizzato, non è cercabile, non permette di trovare la stanza.

### 6.2 Parsing lato client

Il client fa il parsing **prima** di inviare, così un comando scritto male non arriva
nemmeno al server:

```
testo digitato: "/NameRoom  Coordinamento  "
                 |
                 +-- inizia con "/" e non con "//"
                 |
       nome  = "nameroom"     (fino al primo spazio, minuscolo)
       arg   = "Coordinamento" (il resto, trim)
                 |
                 v
  {"type":"command","name":"nameroom","arg":"Coordinamento"}
```

Se il nome del comando non è tra quelli noti, il client mostra un errore **locale**
("Comando sconosciuto: /pippo") senza contattare il server.

La validazione dell'argomento viene fatta **sia** dal client (per un errore immediato)
**sia** dal server (perché un client modificato può inviare qualsiasi cosa).

### 6.3 `/Users` — elenca i partecipanti

```
/Users
```

| | |
|---|---|
| `arg` | Nessuno. Se presente, viene ignorato |
| Effetto | Il server invia a **tutti** i partecipanti l'elenco dei nickname |
| Risposta | `system` con `kind: "user_list"`, campo `users`, testo già formattato |

```json
{
  "type": "system",
  "kind": "user_list",
  "text": "Partecipanti (3/20): emy2, luca, sara",
  "users": ["emy2", "luca", "sara"],
  "sentAt": "2026-09-06T19:45:00.000Z"
}
```

L'ordine è quello di ingresso nella stanza. L'elenco è costruito al momento della
richiesta e non viene conservato da nessuna parte.

### 6.4 `/ClearRoom` — svuota la chat

```
/ClearRoom
```

| | |
|---|---|
| `arg` | Nessuno |
| Effetto | Ogni client svuota la propria lista di messaggi |
| Risposta | `clear` a tutti, seguito da `system` con `kind: "room_cleared"` |

Il client **deve** chiedere conferma prima di inviarlo: è distruttivo per tutti i
partecipanti, non solo per chi lo digita. Vedi [05 §5.4](05-client-desktop.md).

### 6.5 `/Help` — elenca i comandi

```
/Help
```

| | |
|---|---|
| `arg` | Nessuno |
| Effetto | Il client mostra **a sé stesso** l'elenco dei comandi e la loro sintassi |
| Traffico di rete | **Nessuno.** Il comando non viene mai inviato al server |

È l'unico comando interamente lato client. Il server non lo conosce: se un client
modificato inviasse `{"type":"command","name":"help"}`, riceverebbe `UNKNOWN_COMMAND`,
ed è corretto così.

Il risultato è un messaggio **locale**, visibile solo a chi lo ha digitato: chiedere
aiuto non deve disturbare la conversazione degli altri. Va reso con lo stile dei messaggi
locali, distinto dai `system` che arrivano davvero dal server ([05 §5.4](05-client-desktop.md)).

Serve perché i comandi non sono altrimenti scopribili: non c'è nessun punto della UI che
li elenchi, e senza `/Help` l'unico modo per conoscerli sarebbe sbagliarne uno.

### 6.6 `/GenerateKey` — rigenera la chiave della stanza

```
/GenerateKey
```

| | |
|---|---|
| `arg` | Nessuno |
| Effetto | Da questo momento la stanza usa una **chiave nuova**. Chi è uscito prima non può più leggere niente |
| Sul filo | **Non** viene inviato come `command`: il client produce un `rekey` (§4.4bis) |
| Risposta | Un `rekeyed` a ciascun partecipante, poi un `system` `room_rekeyed` a tutti |
| Errori | `INVALID_REKEY`, `STALE_KEY_EPOCH`, `REKEY_RATE_LIMITED` |

Come tutti gli altri, è **eseguibile da chiunque** e annuncia chi lo ha eseguito. Cosa fa
il client, in ordine:

```
1. genera 32 byte casuali = chiave di stanza NUOVA
2. per ogni altro partecipante, preso dall'ultimo roster (§5.2bis):
     coppia effimera monouso -> ECDH -> HKDF -> AES-GCM sulla chiave nuova
3. invia un solo rekey con tutti gli involucri e keyEpoch = corrente + 1
4. solo quando il server conferma, passa a cifrare con la chiave nuova
```

**Il passo 4 è quello che si sbaglia.** Se il client passasse alla chiave nuova prima della
conferma, e il `rekey` venisse rifiutato — perché nel frattempo ha ruotato qualcun altro, o
perché è entrato un partecipante e l'elenco non è più completo — si ritroverebbe a cifrare
con una chiave che nessuno possiede, e i suoi messaggi apparirebbero a tutti come **non
verificati** senza che nessun attacco sia in corso.

**Che cosa questo comando non fa.** Non caccia nessuno: chi è nella stanza resta nella
stanza e riceve la chiave nuova, anche chi non piace a chi digita il comando. Serve a
tagliare fuori **chi è già uscito**, e a niente altro — l'elenco dei destinatari deve essere
completo, e il server lo verifica (§4.4bis). Va detto anche nella UI, perché "rigenera la
chiave" può facilmente essere scambiato per "butta fuori qualcuno".

**Costo da conoscere:** una rotazione produce un involucro per partecipante, quindi fino a
19 operazioni crittografiche sul client che la esegue e 19 messaggi dal server. È il motivo
del limite di frequenza, che qui è **per stanza** e non per connessione: venti partecipanti
che ruotano a turno sarebbero venti rotazioni comunque.

---

## 7bis. Cifratura end-to-end

### 7bis.1 Il modello in una frase

**Ogni stanza ha una chiave simmetrica, generata dal client che la crea e consegnata a
ogni nuovo arrivato dal client che lo accetta.** Il server inoltra involucri che non sa
aprire.

```
   emy crea la stanza
     |
     +--> genera 32 byte casuali = CHIAVE DI STANZA  (mai inviata al server)
     |
   sara chiede di entrare, allegando la sua chiave pubblica
     |
   emy accetta:
     |
     +--> cifra la CHIAVE DI STANZA con la pubblica di sara
     |
     +--> la manda al server dentro l'approve
              |
              +--> il server la inoltra a sara senza poterla leggere
                       |
                       +--> sara la apre con la sua privata
                            ora emy e sara condividono la chiave
```

### 7bis.2 Primitive

| Ruolo | Algoritmo | Disponibilità |
|---|---|---|
| Scambio chiavi | **ECDH su P-256** | `ECDiffieHellman` nella BCL, multipiattaforma |
| Derivazione | **HKDF-SHA256** | `HKDF.DeriveKey` nella BCL |
| Cifratura | **AES-256-GCM** | `AesGcm` nella BCL, accelerata in hardware |
| Casualità | `RandomNumberGenerator` | Mai `Random` |

Nessuna dipendenza esterna: tutto è nel framework, coerente con
[ADR-0001](adr/0001-trasporto-websocket.md).

> **Su P-256 invece di X25519.** X25519 sarebbe la scelta moderna preferibile, ma non è
> nella BCL: richiederebbe libsodium (NSec) o BouncyCastle, cioè una dipendenza esterna
> sul percorso delle chiavi — esattamente ciò che il progetto evita altrove. P-256 con
> ECDH è solido e implementato dal sistema operativo. Se in futuro si accetta la
> dipendenza, si può migrare: il formato di `publicKey` è opaco al server, quindi il
> cambio non tocca il server.

> **Ed è qui che serviva SHA-256.** Non per cifrare — non può, è a senso unico — ma dentro
> HKDF, per trasformare il segreto condiviso ECDH in una chiave di lunghezza giusta. La
> cifratura vera la fa AES-256-GCM.

### 7bis.3 Consegna della chiave (key wrapping)

Chi approva esegue, **nel proprio client**:

```
1. genera una coppia effimera monouso  (epk_priv, epk_pub)
2. shared  = ECDH(epk_priv, publicKey_del_richiedente)
3. wrapKey = HKDF-SHA256(shared, salt = roomId, info = "krypta-key-wrap-v1", 32 byte)
4. nonce   = 12 byte casuali
5. ct      = AES-256-GCM(wrapKey, nonce, chiaveDiStanza, aad = roomId)
6. invia { epk: epk_pub, nonce, ciphertext: ct }
```

Il richiedente, ricevuto `wrappedKey` dentro `joined`, esegue l'inverso con la propria
chiave privata di sessione.

La coppia effimera al punto 1 è **monouso per ogni approvazione**: due persone accettate
dallo stesso partecipante non condividono nessun materiale derivato.

### 7bis.4 Cifratura dei messaggi

```
nonce = 12 byte casuali, nuovi per ogni messaggio
aad   = UTF8( roomId + "|" + nickname_del_mittente + "|" + seq )
ct    = AES-256-GCM(chiaveDiStanza, nonce, testo, aad)
```

**L'AAD lega il messaggio al suo mittente.** Chi riceve costruisce l'AAD usando il
`nickname` e il `seq` che il **server** ha dichiarato: se il server riattribuisse un
messaggio a un'altra persona, il tag GCM non verificherebbe e la decifratura fallirebbe.

È una proprietà che vale la pena avere esplicitamente: **il server può cancellare o
riordinare messaggi, ma non può falsificarne l'autore.** Un messaggio che non decifra va
mostrato come *non verificato*, mai come testo normale, e mai silenziosamente scartato.

Nonce casuali a 96 bit: con AES-GCM il limite prudenziale è nell'ordine di 2³² messaggi
sotto la stessa chiave. Una stanza effimera da 20 persone non ci si avvicina.

### 7bis.5 Difesa dal replay

Ogni mittente numera i propri messaggi con `seq`, crescente da 1 e incluso nell'AAD.
Chi riceve tiene l'ultimo `seq` visto per ciascun nickname e **scarta** ogni messaggio con
`seq` minore o uguale.

Senza questo, il server potrebbe reinviare un vecchio ciphertext: decifrerebbe
correttamente, perché la chiave è la stessa. Il contatore è la difesa minima e costa una
riga.

**Il contatore si azzera quando il nickname cambia proprietario.** I nickname sono
riusabili: quando `luca` esce, il nome torna libero e un altro partecipante può entrare
chiamandosi `luca`, ripartendo — correttamente — da `seq: 1`. Un ricevente che avesse
ancora in memoria "ultimo seq visto per luca = 40" scarterebbe **tutti** i messaggi del
nuovo luca come replay, e li mostrerebbe come non verificati: un falso allarme
indistinguibile da un attacco vero.

La regola è quindi:

- alla ricezione di un `system` con `kind: "user_left"`, il ricevente **elimina** la voce
  del contatore per quel nickname;
- un `seq` per un nickname mai visto (o appena eliminato) è sempre accettato, qualunque
  sia il suo valore;
- il contatore vive quanto la stanza: uscendo si azzera insieme alla chiave.

Il costo è dichiarato: nella finestra fra l'uscita di `luca` e l'ingresso del nuovo, il
server può rigiocare **una** volta i vecchi messaggi di `luca` fingendo che sia rientrato.
Per evitarlo servirebbe legare il contatore all'identità crittografica invece che al
nickname — cioè alla chiave pubblica di sessione, che il ricevente però non conosce per
chi era già dentro quando è arrivato. È il compromesso che l'assenza di identità
persistenti impone: si sceglie il falso allarme sistematico o la finestra di replay, e
si sceglie la seconda.

### 7bis.6 Cosa è cifrato e cosa no

**Da leggere prima di descrivere Krypta come "cifrato".**

| Elemento | E2EE? | Chi lo vede |
|---|---|---|
| **Testo dei messaggi** | ✅ Sì | Solo i partecipanti |
| **Messaggio di richiesta d'ingresso** (`joinMessage`) | ❌ **No** | Anche il server |
| Nickname | ❌ No | Anche il server |
| ID stanza | ❌ No | Anche il server |
| Nome della stanza (`/NameRoom`) | ❌ No | Anche il server |
| Elenco partecipanti (`/Users`) | ❌ No | Anche il server |
| Messaggi di sistema | ❌ No | Anche il server |
| Ingressi, uscite, richieste in attesa | ❌ No | Anche il server |
| Orario e dimensione dei messaggi | ❌ No | Anche il server |

**La riga sul `joinMessage` è quella che sorprende, ed è inevitabile.** Chi è in attesa non
ha ancora la chiave di stanza — la riceve nel momento dell'approvazione (§7bis.1) — quindi
un messaggio che deve essere letto *prima* di quel momento non può essere cifrato con essa.
È l'unico contenuto **scritto da un utente** in questa tabella: tutto il resto sono
metadati generati dal server. Va detto nella UI accanto al campo, perché è controintuitivo
in un'applicazione che altrove non fa passare nulla in chiaro.

La ragione per il resto è strutturale: **i messaggi di sistema li genera il server**, e un
server che non possiede la chiave non può produrre testo cifrato. Cifrare i nickname
significherebbe rinunciare alla sala d'attesa, a `/Users` e agli annunci di ingresso —
cioè a funzionalità decise apposta.

È esattamente il motivo per cui **TLS è obbligatorio** (§1.1): copre tutto ciò che questa
tabella lascia scoperto verso chi osserva la rete. Restano visibili al **gestore del
server**, e questo è un limite dichiarato del design.

### 7bis.7 Codice di verifica

Da ogni chiave pubblica di sessione si deriva un codice leggibile:

```
codice = prime 7 byte di SHA-256(publicKey)  ->  16 cifre decimali
         mostrate in 4 gruppi:  4821 9037 5164 2288
```

Il richiedente lo vede nella schermata di attesa; chi approva lo vede accanto alla
richiesta. **Se coincidono, non c'è nessuno in mezzo.**

**Perché serve.** Un server malevolo può sostituire la `publicKey` del richiedente nel
messaggio `waiting` con una propria: chi approva cifrerebbe la chiave di stanza per il
server, che la leggerebbe e la reinoltrerebbe al vero destinatario. Tutti avrebbero la
stessa chiave di stanza, tutto funzionerebbe, e il server leggerebbe tutto.

Nessun controllo automatico può rilevarlo — nemmeno un'impronta della chiave di stanza,
che in quell'attacco coinciderebbe per tutti. **L'unico rimedio è confrontare il codice
fuori banda**, per esempio a voce, nella stessa telefonata in cui ci si passa l'ID.

Il confronto è **facoltativo**: quasi nessuno lo farà, ed è accettabile finché il server
è il proprio. Va però reso disponibile e spiegato, perché è l'unica cosa che rende l'E2EE
robusto anche contro un server di terzi.

Sedici cifre sono circa 53 bit: adeguati contro un avversario che deve agire in linea nel
momento dell'approvazione, non contro chi macina collisioni per mesi. È un compromesso
consapevole fra sicurezza e leggibilità al telefono.

### 7bis.8 Limiti dichiarati

Da non nascondere in nessuna descrizione del prodotto:

1. **Nessuna forward secrecy.** La chiave di stanza vive quanto la stanza. Chi la ottiene
   e ha registrato il traffico legge tutta la conversazione. Accettabile perché le stanze
   sono effimere e non esiste archivio da decifrare a posteriori — ma è una rinuncia.
2. **Chi esce conserva la chiave — fino alla prima rotazione.** Non c'è rotazione
   *automatica* all'uscita di un partecipante: finché nessuno digita `/GenerateKey`
   (§7bis.11), chi è stato dentro può decifrare il traffico successivo se riesce a
   registrarlo. Dopo una rotazione non può più, e non c'è modo di rimediare dal suo lato:
   il suo involucro non esiste. Il limite non è chiuso, è diventato **manuale**
   ([ADR-0011](adr/0011-rotazione-chiave-di-stanza.md)).
3. **Qualunque partecipante può cedere la chiave.** Coerente con l'assenza di ruoli, e
   comunque irrisolvibile: chi legge può sempre copiare.
4. **Chi crea la stanza genera la chiave.** Se il suo client è compromesso, lo è tutto.
5. **I metadati non sono protetti dal server** (§7bis.6).

### 7bis.9 Codifiche, byte per byte

**Questa sezione è la parte più normativa del documento.** Tutto ciò che sta qui è un
accordo **fra client**: il server non può accorgersi se due client lo interpretano
diversamente, perché per lui sono stringhe opache. Un disaccordo su una sola di queste
righe non dà un errore — dà un ECDH che produce segreti diversi, o due codici di verifica
che non coincidono mai. E il codice di verifica esiste apposta per essere confrontato a
voce: se due implementazioni lo calcolano in modo diverso, quel confronto smette di
significare qualcosa senza che nessuno se ne accorga.

#### Chiavi pubbliche (`publicKey`, `wrappedKey.epk`)

| | |
|---|---|
| Formato | Punto **non compresso X9.63**: `0x04` seguito da `X` e `Y`, 32 byte ciascuno, big-endian, con zero-padding a sinistra |
| Lunghezza | **65 byte** esatti |
| Sul filo | base64 standard con padding di quei 65 byte, cioè **88 caratteri**, sempre inizianti per `B` — è il primo byte `0x04` |
| In .NET | `ECParameters.Q.X` e `.Q.Y` da `ExportParameters(false)`. **Non** `ExportSubjectPublicKeyInfo()`, che produce un DER più lungo e diverso |

**Chi riceve una chiave pubblica deve validarla come punto della curva prima di usarla.**
In .NET si ottiene costruendo gli `ECParameters` e chiamando `Validate()`, o lasciando
fallire `ECDiffieHellman.Create(parameters)`. Saltare il controllo espone a un *invalid
curve attack*: un punto scelto apposta, fuori dalla curva, può far trapelare la propria
chiave privata attraverso il risultato dell'ECDH. Il server non può farlo al posto dei
client — per lui la chiave è opaca, e deve restarlo.

#### Codice di verifica (§7bis.7)

```
h      = SHA-256( i 65 byte DECODIFICATI della chiave pubblica )
v      = primi 7 byte di h, letti come intero BIG-ENDIAN
codice = (v mod 10^16), reso in 16 cifre con zeri iniziali, in 4 gruppi da 4
```

Due punti da non sbagliare, entrambi silenziosi se sbagliati: l'hash si calcola sui **byte
decodificati**, mai sulla stringa base64; e i 7 byte si leggono **big-endian**. Lo
zero-padding a 16 cifre è obbligatorio — un codice che ogni tanto ne ha 15 è illeggibile
al telefono, che è l'unico posto in cui serve.

#### Involucri AES-256-GCM

Vale per la `wrappedKey` e per i messaggi.

| | |
|---|---|
| Cifrario | AES-256-GCM con **tag di 16 byte**. In .NET: `new AesGcm(key, 16)`, con la lunghezza del tag esplicita |
| `nonce` | 12 byte da `RandomNumberGenerator`, base64 (16 caratteri). **Nuovo a ogni operazione** |
| `ciphertext` | base64 della **concatenazione: prima il ciphertext, poi il tag** |

L'ordine della concatenazione va dichiarato perché l'API .NET tiene ciphertext e tag in
due buffer separati: qualcuno deve decidere come stanno insieme sul filo, e chi decifra
deve sapere che gli ultimi 16 byte sono il tag.

#### Derivazione della chiave di wrapping

```
shared  = ECDH(epk_priv, publicKey_del_richiedente)   -> 32 byte
wrapKey = HKDF-SHA256(ikm  = shared,
                      salt = UTF8(roomId),
                      info = UTF8("krypta-key-wrap-v1"),
                      L    = 32)
```

**`shared` è il segreto ECDH grezzo**, cioè la sola coordinata X del punto condiviso: in
.NET si ottiene con `DeriveRawSecretAgreement`. **Non** con `DeriveKeyFromHash`, che
applica già un hash suo e produrrebbe un `ikm` diverso. È l'errore più facile da fare qui,
e compila benissimo.

#### AAD

| Operazione | AAD |
|---|---|
| Wrapping della chiave | `UTF8(roomId)` |
| Messaggio | `UTF8(roomId + "|" + nickname + "|" + seq)` |

Il `nickname` è quello **effettivo** dichiarato dal server nel messaggio ricevuto, e `seq`
è scritto in decimale invariante, senza zeri iniziali e senza separatori: `1`, non `01`.
Il separatore è la barra verticale, che non può comparire né in un nickname (§4.1.1) né in
un `roomId` (§4.1.0) — quindi la concatenazione non è ambigua.

#### Cosa può verificare il server

Solo la forma — ma può farlo per intero, perché tutte le dimensioni sono fisse:

| Campo | Controllo | Errore |
|---|---|---|
| `publicKey`, `epk` | base64 valido, **65 byte**, primo byte `0x04` | `INVALID_PUBLIC_KEY` / `INVALID_APPROVAL` |
| `wrappedKey.nonce` | base64 valido, **12 byte** | `INVALID_APPROVAL` |
| `wrappedKey.ciphertext` | base64 valido, **48 byte** esatti: 32 di chiave più 16 di tag | `INVALID_APPROVAL` |
| `message.nonce` | base64 valido, 12 byte | `INVALID_ENVELOPE` |
| `message.ciphertext` | base64 valido, da 17 byte a `MaxCiphertextBytes` | `INVALID_ENVELOPE` / `MESSAGE_TOO_LONG` |
| `message.seq` | intero maggiore o uguale a 1 | `INVALID_ENVELOPE` |

La riga sulla `wrappedKey.ciphertext` è più forte di quanto sembri: **la chiave di stanza
è sempre di 32 byte**, quindi il suo involucro è sempre di 48. Il server non può aprirlo,
ma può accorgersi che non ha la forma giusta — ed è tutto quello che gli serve per
rifiutare un `approve` malformato (§4.4) senza possedere nessuna chiave.

### 7bis.10 Vettori di test

Generati con la BCL di **.NET 10.0.400** e verificati a ritroso: reimportando le chiavi dai
valori qui sotto si riottengono tutti i risultati, e i due involucri si decifrano. Vanno
riprodotti da un test automatico prima di considerare finita la fase 1B, e riprodotti
**tutti**: ciascuno copre un punto in cui due implementazioni possono divergere in
silenzio.

**Chiavi** — pubblicate per intero, chiave privata compresa: sono chiavi di prova e non
vanno usate per altro.

```
sara (il richiedente)
  D  = 95227847d4e93ae7caaa8ee920bc210560301fa80bd48842cb870f8792f6d852
  Qx = 199c172a88ce5e23ddf00c57b70c2a6068ebc34d64763e67d3c4a8602ed68d20
  Qy = 740eaf49caf5878cad60d82e6d5b9e2ebbb94d5d7a3cd14d93257adae3b0e335

epk (coppia effimera monouso di chi approva)
  D  = 16c4ed0eaade1d4a9c54e3f596f836bf8cedf9f6ff4dd5eae093970c342e4020
  Qx = 837610db9772a497ea55b7116b7b720725b3169f9495234376960f1d771b6e59
  Qy = fdb236bfa7e862166a701c7117ff1ab5c948b6caae7692651fac9e0da7d87efe
```

**1. Codifica della chiave pubblica**

```
punto  = 04 199c172a88ce5e23ddf00c57b70c2a6068ebc34d64763e67d3c4a8602ed68d20
            740eaf49caf5878cad60d82e6d5b9e2ebbb94d5d7a3cd14d93257adae3b0e335
base64 = BBmcFyqIzl4j3fAMV7cMKmBo68NNZHY+Z9PEqGAu1o0gdA6vScr1h4ytYNgubVueLru5TV16PNFNkyV62uOw4zU=
```

**2. Codice di verifica**

```
SHA-256(punto) = 4f1e6f6dc3efd94e6c2d91b65ecb37250fad82243084d8e8d1cff64488b17d99
primi 7 byte   = 4f 1e 6f 6d c3 ef d9   ->   big-endian, poi mod 10^16
codice         = 2269 9870 9190 2425
```

**3. ECDH e derivazione** (con `roomId` = `K7X2M9`)

```
shared  = 6af37f0ffbe684396d6aafc42d84910b00f1688fb6394c60ce24605a4206d662
salt    = 4b3758324d39                          (UTF8 "K7X2M9")
info    = 6b72797074612d6b65792d777261702d7631   (UTF8 "krypta-key-wrap-v1")
wrapKey = 57d554b5ad7e785e1ea7f2e63fd4d3952f11a8b1fa70287d8f7a55dae3198786
```

Il segreto ECDH deve venire identico calcolato **dai due lati**: è il primo test da
scrivere, ed è quello che fallisce se la codifica del punto non coincide.

**4. Wrapping della chiave di stanza**

```
roomKey        = 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f
nonce          = 0102030405060708090a0b0c     -> base64  AQIDBAUGBwgJCgsM
aad            = 4b3758324d39                 (UTF8 "K7X2M9")
ciphertext+tag = 551c67adc58a9dd09346c416cbb230dd96aafe1861b805a66e2d322b855ea512
                 383b1ce4e41f64cf9468ddd964d3a1dc
sul filo       = VRxnrcWKndCTRsQWy7Iw3Zaq/hhhuAWmbi0yK4VepRI4Oxzk5B9kz5Ro3dlk06Hc
```

**5. Cifratura di un messaggio**

```
testo          = "ciao"                       (6369616f)
aad            = "K7X2M9|emy|1"               (4b3758324d397c656d797c31)
nonce          = 0b0a090807060504030201ff     -> base64  CwoJCAcGBQQDAgH/
ciphertext+tag = 7f796903202f931e26d854de5bf322ce7016d33e
sul filo       = f3lpAyAvkx4m2FTeW/MiznAW0z4=
```

**6. I due casi che devono FALLIRE**

Sono i più importanti dei sei, perché verificano una **garanzia** invece di un formato:
sono la proprietà G10 di [07 §2](07-sicurezza-e-privacy.md), quella per cui il server può
cancellare o riordinare messaggi ma non può falsificarne l'autore.

Si riprende il vettore 5 e si cambia **solo l'AAD**, lasciando intatti nonce e ciphertext:

| Cosa sta simulando | AAD | Esito atteso |
|---|---|---|
| Il server riattribuisce il messaggio a `luca` | `K7X2M9` + `|luca|1` | `CryptographicException` — la decifratura **fallisce** |
| Il server rigioca il messaggio con un altro `seq` | `K7X2M9` + `|emy|2` | `CryptographicException` — la decifratura **fallisce** |

Un'implementazione in cui questi due casi **riescono** ha un bug nell'AAD e ha perso la
garanzia G10, pur superando tutti i test positivi. È il motivo per cui vanno scritti: un
test che prova solo il caso che funziona non se ne accorgerebbe mai.

### 7bis.11 Rotazione della chiave (epoche)

La chiave di stanza non è per forza una sola per tutta la vita della stanza. Ogni
`/GenerateKey` (§6.6) ne introduce una nuova, e le chiavi si numerano con un'**epoca**:

```
   stanza creata           epoca 1   (chiave generata da chi ha creato la stanza)
   /GenerateKey            epoca 2   (chiave generata da chi ha digitato il comando)
   /GenerateKey            epoca 3
```

| | |
|---|---|
| Valore iniziale | `1`, alla nascita della stanza |
| Chi lo incrementa | Chiunque digiti `/GenerateKey`: la sua richiesta porta `keyEpoch = corrente + 1` |
| Chi tiene il conto | **Il server**, che rifiuta con `STALE_KEY_EPOCH` un `rekey` o un `approve` che porti un'epoca non corrente |
| Dove viaggia | In `joined`, `roster`, `rekeyed` e in ogni `message` |

#### Come il ricevente sceglie la chiave

Il campo `keyEpoch` di un messaggio dice con quale chiave è stato cifrato. Il client tiene
**due** chiavi: quella corrente e la precedente.

```
keyEpoch == epoca corrente     -> decifra con la chiave corrente
keyEpoch == epoca precedente   -> decifra con la chiave precedente, se non e' scaduta
qualunque altro valore         -> messaggio NON VERIFICATO
```

**La chiave precedente si tiene per 60 secondi** (`PreviousKeyWindowSeconds`), poi si azzera
in memoria come tutto il resto. Serve per i messaggi **in volo**: fra il momento in cui uno
ruota e quello in cui gli altri hanno ricevuto e aperto il proprio involucro, viaggiano
messaggi cifrati con la chiave vecchia. Senza la finestra apparirebbero tutti come *non
verificati* — un allarme di sicurezza a ogni rotazione, per un motivo che non è un attacco.

È lo stesso errore già evitato con il contatore `seq` al riuso di un nickname (§7bis.5), e
vale la pena notare che è la stessa forma: **un meccanismo di difesa che, senza una regola
di transizione, spara falsi positivi proprio nel momento normale.**

#### Perché l'epoca non entra nell'AAD

Sembrerebbe naturale legarla al messaggio come si fa con nickname e `seq` (§7bis.4). Non
serve, ed è meglio di no.

Non serve perché **l'epoca è già autenticata dal fatto stesso di selezionare la chiave**: se
un server ostile cambiasse il `keyEpoch` di un messaggio, il ricevente proverebbe a
decifrarlo con la chiave sbagliata e il tag GCM fallirebbe da solo. Il peggio che ottiene è
un falso "non verificato", cioè un disturbo, non una falsificazione.

Ed è meglio di no perché tenerla fuori lascia **l'AAD invariata**, e quindi lascia validi i
sei vettori di test di §7bis.10, che altrimenti andrebbero rigenerati tutti.

#### Il `seq` non si azzera

Il contatore per mittente continua a crescere attraverso le epoche: `…, 6, 7` in epoca 1 e
`8, 9, …` in epoca 2. Azzerarlo darebbe di nuovo il problema di §7bis.5, con i primi
messaggi della nuova epoca scartati come replay. La regola resta una sola: **`seq`
strettamente crescente per mittente**, e si elimina solo alla ricezione di `user_left`.

#### Approvazione durante una rotazione

Chi approva un nuovo arrivato gli consegna la chiave **corrente**, e dichiara quale epoca è
nel campo `keyEpoch` dell'`approve` (§4.4). Se nel frattempo qualcuno ha ruotato, quella
non è più l'epoca corrente: il server risponde `STALE_KEY_EPOCH`, e il client rifà il
wrapping con la chiave nuova prima di riprovare.

Senza questo controllo l'ammesso riceverebbe una chiave già scaduta e non leggerebbe niente
di ciò che arriva — con l'aria di un problema di crittografia invece che di una corsa fra
due operazioni.

#### Cosa la rotazione dà e cosa no

| | |
|---|---|
| ✅ Taglia fuori **chi è già uscito** | Il suo involucro non esiste, e la chiave nuova non è derivabile dalla vecchia |
| ✅ **Guarisce** da una chiave compromessa | Chi aveva l'epoca *N* non ottiene la *N+1* |
| ❌ Non dà forward secrecy **dentro** un'epoca | Chi ottiene la chiave corrente legge tutto il traffico di quell'epoca che sia riuscito a registrare |
| ❌ Non impedisce a un partecipante di cedere la chiave | Irrisolvibile: chi legge può copiare |
| ⚠️ **Riapre la finestra MITM a ogni rotazione** | L'esclusione vale se le chiavi pubbliche del `roster` sono autentiche. Un server ostile può sostituirle, come all'ingresso (§7bis.7). Prima quella finestra si apriva una volta per partecipante; ora si riapre a ogni comando |

L'ultima riga va letta insieme alla prima: **la rotazione riduce la superficie verso gli ex
partecipanti e la aumenta verso un server ostile.** Sono due avversari diversi, e chi usa
un server proprio ci guadagna e basta.

---

## 7. Heartbeat

- Il server invia `ping` a ogni connessione ogni **30 secondi**.
- Il client deve rispondere `pong`.
- Se il server non riceve né un `pong` né un altro messaggio per **60 secondi**,
  considera la connessione morta: chiude il socket, rimuove il partecipante dalla stanza
  e invia agli altri `system` con `kind: "user_left"` più `participants`.
- Il client, se non riceve nessun `ping` per **90 secondi**, considera il server
  irraggiungibile.

Questo è ciò che rende affidabili il contatore e l'elenco dei partecipanti anche quando
un client scompare senza chiudere il socket (crash, cavo staccato, PC sospeso).

## 8. Codici di errore

| Codice | Quando | Cosa fa il client |
|---|---|---|
| `INVALID_ROOM_ID` | ID non conforme a `[A-Z0-9]{6}` — minuscole comprese | Errore sotto il campo ID. Non dovrebbe accadere: il client converte in maiuscolo mentre si digita |
| `INVALID_NICKNAME` | Fuori dai vincoli di §4.1.1, o nome riservato | Torna alla schermata nickname |
| `ROOM_FULL` | La stanza ha già 20 partecipanti | Messaggio, resta sulla schermata stanza |
| `ALREADY_IN_ROOM` | `join` mentre si è già in una stanza | Bug del client: fare `leave` prima |
| `ALREADY_PENDING` | `join` mentre si è già in attesa altrove | Bug del client: fare `cancel` prima |
| `WAITING_ROOM_FULL` | La sala d'attesa ha già 10 richieste | "Troppe richieste in attesa. Riprova fra poco." |
| `UNKNOWN_REQUEST` | `approve`/`reject` su una richiesta inesistente o già evasa | Ignora: qualcun altro ha già risposto. Nessun errore visibile |
| `NOT_PENDING` | `cancel` senza essere in attesa | Bug del client |
| `NOT_IN_ROOM` | `message`, `command` o `leave` fuori da una stanza | Bug del client: torna alla schermata stanza |
| `MESSAGE_TOO_LONG` | Involucro cifrato oltre la dimensione massima | Mostra il limite, non svuota la casella |
| `INVALID_ENVELOPE` | `nonce`, `seq` o `ciphertext` mancanti o malformati | Bug del client, logga |
| `INVALID_PUBLIC_KEY` | `publicKey` malformata nel `join` | Bug del client |
| `INVALID_JOIN_MESSAGE` | `joinMessage` oltre 200 caratteri, o con a capo o caratteri di controllo (§4.1.3) | Errore sotto il campo, senza perdere il testo digitato. Il client valida prima: non dovrebbe accadere |
| `INVALID_APPROVAL` | `approve` senza `wrappedKey` valida | Bug del client. **Il server non può supplire**: non ha chiavi |
| `INVALID_REKEY` | `rekey` i cui destinatari non coincidono esattamente con i partecipanti attuali meno il mittente (§4.4bis) | Rifà la rotazione partendo dall'ultimo `roster`. Se persiste, è un bug del client |
| `STALE_KEY_EPOCH` | `rekey` o `approve` con un'epoca che non è più corrente: qualcuno ha ruotato nel frattempo | **Non è un errore da mostrare**: si rifà il wrapping con la chiave nuova e si riprova, una volta sola |
| `REKEY_RATE_LIMITED` | Più di una rotazione ogni 30 secondi **nella stessa stanza** | Avvisa con `retryAfterSeconds`: la chiave è già stata rigenerata da poco |
| `RATE_LIMITED` | Troppi messaggi in poco tempo | Blocca l'invio per qualche secondo e avvisa |
| `JOIN_RATE_LIMITED` | Più di 5 tentativi di `join` in 3 minuti dallo stesso IP | Mostra il blocco e il conto alla rovescia di `retryAfterSeconds` |
| `UNKNOWN_COMMAND` | Comando non riconosciuto dal server | Messaggio locale in chat |
| `INVALID_COMMAND_ARG` | Argomento mancante o non valido | Messaggio locale in chat con il vincolo violato |
| `COMMAND_RATE_LIMITED` | Più di 3 comandi in 10 secondi | Avvisa, con `retryAfterSeconds` |
| `MALFORMED` | JSON non valido o frame oltre il limite | Bug del client, logga |
| `UNKNOWN_TYPE` | `type` non riconosciuto | Ignora |
| `SERVER_SHUTDOWN` | Il server si sta spegnendo | Torna alla schermata di connessione |

Nessun errore applicativo chiude la connessione, tranne `SERVER_SHUTDOWN`.

## 9. Limiti (`Krypta.Shared/KryptaLimits.cs`)

| Costante | Valore | Note |
|---|---|---|
| `MaxParticipantsPerRoom` | 20 | Requisito esplicito |
| `MaxPendingPerRoom` | 10 | Capienza della sala d'attesa |
| `JoinApprovalTimeoutSeconds` | 120 | Dopo, la richiesta scade da sola |
| `JoinMessageMaxLength` | 200 | Caratteri del messaggio di richiesta (§4.1.3). Facoltativo, riga singola, **in chiaro** |
| `RoomIdPattern` | `^[A-Z0-9]{6}$` | Sempre maiuscolo. Il client converte, il server rifiuta il resto |
| `RoomNameMaxLength` | 25 | Alfanumerici **e spazio**, spazi normalizzati |
| `NicknameMinLength` | 3 | |
| `NicknameMaxLength` | 25 | Include l'eventuale numero di disambiguazione |
| `DefaultRoomNamePrefix` | `"RM-"` | Nome iniziale: `RM-{roomId}` |
| `MessageMaxLength` | 2000 | Caratteri **del testo in chiaro**, contati come li conta .NET (`string.Length`, cioè unità di codice UTF-16). Lo verifica il client: il server non può |
| `MaxCiphertextBytes` | 8192 | Limite sull'involucro cifrato, **misurato sui byte decodificati** (§9.1), non sulla stringa base64. È l'unico limite di contenuto che il server può controllare |
| `MaxFrameBytes` | 16384 | Dimensione dell'intero frame WebSocket in byte. Un frame più grande viene rifiutato **senza essere deserializzato** |
| `RoomKeyBytes` | 32 | AES-256 |
| `RekeyMinIntervalSeconds` | 30 | Fra due `/GenerateKey`, **per stanza** e non per connessione |
| `PreviousKeyWindowSeconds` | 60 | Per quanto il ricevente tiene la chiave dell'epoca precedente, per i messaggi in volo (§7bis.11) |
| `NonceBytes` | 12 | AES-GCM, casuale per messaggio |
| `KeyWrapInfo` | `"krypta-key-wrap-v1"` | Etichetta HKDF |
| `VerificationCodeDigits` | 16 | Codice di verifica, 4 gruppi da 4 |
| `RateLimitMessages` / `WindowSeconds` | 10 / 5 | Per connessione |
| `RateLimitCommands` / `WindowSeconds` | 3 / 10 | Per connessione |
| `JoinRateLimitAttempts` | 5 | …in `JoinRateLimitWindowSeconds`, **per IP** |
| `JoinRateLimitWindowSeconds` | 180 | 3 minuti |
| `JoinRateLimitBlockSeconds` | 120 | 2 minuti di blocco |
| `PingIntervalSeconds` | 30 | |
| `ConnectionTimeoutSeconds` | 60 | |

### 9.1 Come si incastrano i tre limiti di dimensione

I tre limiti misurano cose diverse, e vanno tenuti separati o si contraddicono: il primo
è in caratteri e lo applica solo il client, gli altri due sono in byte e li applica solo
il server.

```
  2000 caratteri UTF-16                     <- MessageMaxLength, solo lato client
        |  al massimo 3 byte per carattere
        v
  6000 byte di testo in chiaro
        |  + 16 byte di tag GCM
        v
  6016 byte di involucro                    <- MaxCiphertextBytes (8192), lato server
        |  base64: 4 caratteri ogni 3 byte
        v
  8024 caratteri di ciphertext base64
        |  + nonce, nickname, seq, sentAt, nomi dei campi JSON
        v
  ~8,2 KB di frame                          <- MaxFrameBytes (16384), lato server
```

Tre conseguenze da non perdere in implementazione:

1. **`MaxCiphertextBytes` si misura sui byte decodificati**, non sulla stringa base64.
   Misurarlo sul base64 restringerebbe il limite di un terzo senza dirlo, e un client
   che rispetta i 2000 caratteri riceverebbe `MESSAGE_TOO_LONG` per un testo legittimo.
2. **Il caso peggiore non è il testo latino.** Con caratteri a 3 byte in UTF-8 — CJK,
   cirillico, greco — 2000 caratteri sono 6000 byte, tre volte la lunghezza in caratteri.
   Il margine sotto 8192 c'è, ma esiste solo perché il limite è sui byte decodificati.
3. **Il server non può verificare `MessageMaxLength`**, e non deve provarci: non conosce
   il testo in chiaro. Un client modificato può inviare un testo più lungo, purché
   l'involucro stia nel limite. Non è un problema — il danno massimo è un messaggio lungo.

## 10. Esempio completo di sessione

Le righe `-->` sono client → server, le `<--` server → client. Le righe fra parentesi
**non sono traffico**: sono le operazioni che il client esegue da solo, ed è esattamente
la parte che il server non vede.

**Lato di chi crea la stanza (emy):**

```
    (genera la coppia effimera di sessione: epk_priv resta nel client)

--> {"type":"join","roomId":"K7X2M9","nickname":"emy","publicKey":"BEi3k9...=="}

    (nessun joinMessage: la stanza non esiste, non c'e' nessuno a cui scrivere.
     Il client puo' inviarlo lo stesso, e il server lo ignora)
<-- {"type":"joined","roomId":"K7X2M9","roomName":"RM-K7X2M9","nickname":"emy","participants":1,"created":true,"maxParticipants":20}

    (created == true, quindi nessuna wrappedKey: genera 32 byte casuali con
     RandomNumberGenerator = CHIAVE DI STANZA, che non lascia mai il client)

<-- {"type":"system","kind":"room_created","text":"emy ha creato la stanza","sentAt":"..."}

    (luca chiede di entrare)
<-- {"type":"waiting","requests":[{"requestId":"3f9a1c4e-...","nickname":"luca","publicKey":"BPq2m8...==","joinMessage":"Sono Luca, porto io i documenti","waitingSeconds":0}]}
<-- {"type":"system","kind":"join_requested","text":"luca chiede di entrare nella stanza","sentAt":"..."}

    (il testo del system NON contiene il joinMessage, ed e' una regola: il system
     finisce nella conversazione di tutti, il messaggio solo nel pannello)

    (dalla publicKey di luca deriva il codice di verifica da mostrare accanto alla
     richiesta: 7734 1290 6605 4417)
    (al clic su Accetta: coppia effimera monouso -> ECDH -> HKDF-SHA256 -> AES-256-GCM
     sulla chiave di stanza. Se il wrapping fallisce, l'approve NON parte)

--> {"type":"approve","requestId":"3f9a1c4e-...","wrappedKey":{"epk":"BM4tZ...==","nonce":"9jK1vQ8bZ2xR","ciphertext":"kR7d...=="}}
<-- {"type":"waiting","requests":[]}
<-- {"type":"system","kind":"user_joined","text":"luca si è unito alla stanza, accettato da emy","sentAt":"..."}
<-- {"type":"participants","count":2}

--> {"type":"command","name":"nameroom","arg":"Coordinamento"}
<-- {"type":"system","kind":"room_renamed","text":"emy ha rinominato la stanza in \"Coordinamento\"","roomName":"Coordinamento","sentAt":"..."}

    (cifra "ciao": aad = "K7X2M9|emy|1", nonce nuovo, AES-256-GCM con la chiave di stanza)

--> {"type":"message","seq":1,"keyEpoch":1,"nonce":"7bQ2xM9vK1zR","ciphertext":"3pQ9xK2mA7...=="}
<-- {"type":"message","nickname":"emy","seq":1,"keyEpoch":1,"nonce":"7bQ2xM9vK1zR","ciphertext":"3pQ9xK2mA7...==","sentAt":"..."}

    (eco: il client decifra il proprio messaggio come quello di chiunque altro,
     e solo allora lo mostra)

    (luca esce; emy vuole che non legga piu' niente e digita /GenerateKey)
<-- {"type":"system","kind":"user_left","text":"luca e' uscito dalla stanza","sentAt":"..."}
<-- {"type":"participants","count":1}
<-- {"type":"roster","keyEpoch":1,"participants":[{"nickname":"emy","publicKey":"BEi3k9...=="}]}

    (genera 32 byte nuovi e li cifra per ogni ALTRO partecipante: qui nessuno,
     perche' emy e' rimasto solo. L'elenco keys e' vuoto, ed e' corretto)

--> {"type":"rekey","keyEpoch":2,"keys":[]}
<-- {"type":"system","kind":"room_rekeyed","text":"emy ha rigenerato la chiave della stanza","sentAt":"..."}

    (solo ORA passa a cifrare con la chiave nuova: mai prima della conferma)

--> {"type":"message","seq":2,"keyEpoch":2,"nonce":"K2mA7x9pQ3vR","ciphertext":"8wT4nB6...=="}
<-- {"type":"message","nickname":"emy","seq":2,"keyEpoch":2,"nonce":"K2mA7x9pQ3vR","ciphertext":"8wT4nB6...==","sentAt":"..."}

    (seq continua da 1 a 2 attraverso il cambio di epoca: non si azzera)

--> {"type":"command","name":"users"}
<-- {"type":"system","kind":"user_list","text":"Partecipanti (2/20): emy, luca","users":["emy","luca"],"sentAt":"..."}

--> {"type":"command","name":"clearroom"}
<-- {"type":"clear"}
<-- {"type":"system","kind":"room_cleared","text":"emy ha svuotato la stanza","sentAt":"..."}

<-- {"type":"ping"}
--> {"type":"pong"}

--> {"type":"leave"}
<-- {"type":"left","roomId":"K7X2M9"}

    (azzera la chiave di stanza in memoria)
```

**Lato di chi chiede di entrare (luca):**

```
    (genera la propria coppia effimera di sessione)

--> {"type":"join","roomId":"K7X2M9","nickname":"luca","publicKey":"BPq2m8...==","joinMessage":"Sono Luca, porto io i documenti"}
<-- {"type":"pending","roomId":"K7X2M9","expiresInSeconds":120}

    (il pending non rimanda indietro il messaggio: luca sa gia' cosa ha scritto)

    (mostra il proprio codice di verifica: 7734 1290 6605 4417, lo stesso che emy
     vede accanto alla richiesta. Se coincidono, nessuno si è messo in mezzo)
    (attesa: in questa fase non arriva niente della stanza — né nome, né conteggio,
     né messaggi)

<-- {"type":"joined","roomId":"K7X2M9","roomName":"RM-K7X2M9","nickname":"luca","participants":2,"created":false,"maxParticipants":20,"wrappedKey":{"epk":"BM4tZ...==","nonce":"9jK1vQ8bZ2xR","ciphertext":"kR7d...=="}}

    (created == false: apre la wrappedKey con la propria chiave privata di sessione
     e ottiene la chiave di stanza. Da qui in poi può decifrare)

<-- {"type":"system","kind":"user_joined","text":"luca si è unito alla stanza, accettato da emy","sentAt":"..."}
<-- {"type":"system","kind":"room_renamed","text":"emy ha rinominato la stanza in \"Coordinamento\"","roomName":"Coordinamento","sentAt":"..."}

    (arriva il messaggio di emy: ricostruisce aad = "K7X2M9|emy|1" dai campi
     dichiarati dal server, verifica il tag GCM e controlla che seq sia crescente
     per il mittente "emy")

<-- {"type":"message","nickname":"emy","seq":1,"keyEpoch":1,"nonce":"7bQ2xM9vK1zR","ciphertext":"3pQ9xK2mA7...==","sentAt":"..."}
```

**Lato di chi viene rifiutato (sara):**

```
--> {"type":"join","roomId":"K7X2M9","nickname":"sara","publicKey":"BHt7w2...=="}

    (sara ha cancellato il testo predefinito e non ha scritto niente:
     il campo e' semplicemente assente, ed e' una richiesta legittima)
<-- {"type":"pending","roomId":"K7X2M9","expiresInSeconds":120}
<-- {"type":"rejected","roomId":"K7X2M9","reason":"rejected"}

    (nessuna chiave è mai stata consegnata: il rifiuto non lascia niente da azzerare.
     La connessione resta aperta: si può riprovare o cambiare stanza)
```

**Il punto dell'esempio:** in nessuna riga di questo scambio compare la stringa `ciao`.
Il server ha inoltrato l'intera conversazione senza mai poterla leggere — e i nickname,
l'ID stanza, il nome scelto e gli orari sono invece lì, in chiaro, come dice §7bis.6.
## 11. Versionamento

Il sotto-protocollo dichiarato nell'handshake è `krypta.v1`. Se il formato cambia in modo
incompatibile, il server dovrà accettare più sotto-protocolli e il client dichiarare
quelli che sa parlare. Finché si aggiungono solo campi opzionali o nuovi `kind`/comandi,
la versione resta `v1`.

**Nota per i client:** un `kind` di `system` sconosciuto va mostrato usando il solo campo
`text`, senza errori. È il meccanismo che permette di aggiungere comandi senza aggiornare
tutti i client.
