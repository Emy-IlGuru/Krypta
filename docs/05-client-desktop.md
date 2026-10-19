# 05 — Krypta.Client (desktop)

**Stato:** bozza · **Aggiornato:** 2026-09-08

Client desktop in **Avalonia 11** su **.NET 10**, pattern **MVVM**. Nessuna versione
mobile o web.

## 1. Flusso delle schermate

```
   [1] Connessione  --connesso-->  [2] Account  --nickname-->  [3] Stanza
        ^                                                          |
        |                                                       join
        |                                          +---------------+---------------+
        |                                          |                               |
        |                                 stanza nuova                    stanza esistente
        |                                          |                               |
        |                                          |                               v
        |                                          |                    [3b] In attesa
        |                                          |                     |         |
        |                                          |             approvato    rifiutato/
        |                                          |                  |      timeout/annulla
        |                                          v                  |            |
        +--------------------------------------  [4] Chat  <----------+            |
                                                    ^  |                           |
                                        "Lascia stanza"|                           |
                                                    |  +--> torna a [3] <----------+
                                                    |
                                              server caduto --> [1]
```

Le cinque schermate vivono nella **stessa finestra**: la finestra principale ospita un
`ContentControl` che mostra la View corrispondente allo stato corrente. Non si aprono
finestre nuove.

"Lascia stanza" torna alla schermata **[3]**, non alla connessione: il WebSocket resta
aperto e si cambia stanza senza riconnettersi.

---

## 2. Schermata [1] — Connessione

```
+--------------------------------------------------+
|                                                  |
|                    K R Y P T A                   |
|                                                  |
|   Indirizzo server                               |
|   +------------------------------------------+   |
|   | 127.0.0.1                                |   |
|   +------------------------------------------+   |
|                                                  |
|   Porta                                          |
|   +------------------------------------------+   |
|   | 5000                                     |   |
|   +------------------------------------------+   |
|                                                  |
|   [ ] Ricorda queste impostazioni                |
|                                                  |
|                              +---------------+   |
|                              |   Connetti    |   |
|                              +---------------+   |
|                                                  |
|   (area messaggio di stato)                      |
+--------------------------------------------------+
```

| Elemento | Comportamento |
|---|---|
| Indirizzo server | Precompilato da `krypta.config.xml`. IPv4, IPv6 o hostname |
| Porta | Precompilata da config. Solo cifre, 1–65535 |
| Ricorda | Se spuntata, al connect riuscito riscrive host e porta nel file di config |
| Connetti | Disabilitato se i campi non sono validi. Durante il tentativo mostra "Connessione in corso…" e si disabilita |
| Stato | Verde: connesso. Rosso: errore con il motivo (host irraggiungibile / porta chiusa / timeout / sotto-protocollo rifiutato) |

**Timeout di connessione:** 5 secondi.

---

## 3. Schermata [2] — Account

```
+--------------------------------------------------+
|                                                  |
|   Connesso a 127.0.0.1:5000        [Disconnetti] |
|                                                  |
|   Come vuoi farti chiamare?                      |
|                                                  |
|   +------------------------------------------+   |
|   | emy                                      |   |
|   +------------------------------------------+   |
|   Da 3 a 25 caratteri.                           |
|                                                  |
|   Se il nome e' gia' in uso nella stanza,        |
|   verra' aggiunto un numero (es. emy2).          |
|                                                  |
|   Il nickname vale solo per questa sessione.     |
|   Nessuna password, nessun account.              |
|                                                  |
|                              +---------------+   |
|                              |    Avanti     |   |
|                              +---------------+   |
+--------------------------------------------------+
```

| Elemento | Comportamento |
|---|---|
| Nickname | Precompilato dal config. Validato lato client: 3–25 caratteri, caratteri ammessi, **nomi riservati esclusi** (§4.1.1 del protocollo) |
| Anteprima colore | Il nickname è mostrato già colorato con il colore che avrà in chat |
| Avvertenza | La riga sul numero automatico va mostrata sempre, non solo dopo che è successo: evita che l'utente si sorprenda di chiamarsi `emy2` |
| Avanti | Disabilitato finché il nickname non è valido. Non contatta il server |
| Disconnetti | Chiude il socket e torna a [1] |

**Nota:** il nickname digitato qui è una *richiesta*. Quello effettivo arriva dal server
nel campo `nickname` di `joined` e può avere un numero appeso. Da quel momento il client
usa **solo** quello — anche per calcolare il proprio colore.

---

## 4. Schermata [3] — Selezione stanza

```
+--------------------------------------------------+
|                                                  |
|   Connesso a 127.0.0.1:5000  ·  emy   [Cambia]   |
|                                                  |
|   ID della stanza                                |
|                                                  |
|   +----------------------------+  +----------+   |
|   |         K 7 X 2 M 9        |  |  Genera  |   |
|   +----------------------------+  +----------+   |
|   Sei caratteri: lettere o numeri.               |
|                                                  |
|   Messaggio per chi ti dovra' accettare          |
|   +------------------------------------------+   |
|   | Hey, mi vorrei unire alla stanza         |   |
|   +------------------------------------------+   |
|   Lo leggono solo i partecipanti, dal loro       |
|   pannello. Non e' cifrato: lo vede anche chi    |
|   gestisce il server.                    23/200  |
|                                                  |
|   Se la stanza esiste, entri.                    |
|   Se non esiste, la crei.                        |
|   In entrambi i casi non vedrai i messaggi       |
|   scambiati prima del tuo ingresso.              |
|                                                  |
|                              +---------------+   |
|                              |     Entra     |   |
|                              +---------------+   |
|                                                  |
|   (area messaggio di errore)                     |
+--------------------------------------------------+
```

| Elemento | Comportamento |
|---|---|
| ID stanza | Campo grande, monospace, **6 caratteri alfanumerici**. Incollando testo misto, i caratteri non validi (spazi, punteggiatura, accenti) vengono scartati |
| Genera | Pulsante accanto al campo ID. Genera un ID casuale conforme `[A-Z0-9]{6}` con `RandomNumberGenerator`. Evita ID deboli o prevedibili e mitiga la corsa all'ID |
| Maiuscole | **Il campo converte in maiuscolo mentre si digita.** L'utente può battere `k7x2m9` e vedere comparire `K7X2M9`: la regola diventa invisibile |
| Messaggio | **Precompilato** con *"Hey, mi vorrei unire alla stanza"*, modificabile, max 200 caratteri su **una riga sola** (l'`Invio` non va a capo: conferma). Svuotarlo è legittimo — il campo semplicemente non viene inviato |
| Avvertenza sul messaggio | **Obbligatoria e sempre visibile**, non dietro un'icona: dice che il testo non è cifrato. È l'unico testo scritto dall'utente che l'E2EE non copre, e in un'app che altrove cifra tutto la cosa è controintuitiva ([03 §7bis.6](03-protocollo.md)) |
| Contatore | `23/200`, come nella casella della chat. Rosso a 200 |
| Entra | Disabilitato finché l'ID non ha 6 caratteri validi. Invia `join` con l'ID già maiuscolo e, se non vuoto, il `joinMessage` |

**Il campo messaggio si mostra sempre, anche se la stanza non esiste ancora.** Il client
non può sapere in anticipo se sta per creare la stanza o per bussare: lo scopre solo dalla
risposta, ed è esattamente ciò che la sala d'attesa deve garantire — se la UI potesse dire
"questa stanza esiste, scrivi un messaggio", il campo diventerebbe uno strumento per
sapere quali ID sono attivi. Se il `join` crea la stanza, il messaggio viene scartato dal
server senza errore ([03 §4.1.3](03-protocollo.md)).

**Cosa il testo accanto al campo non deve dire.** Non deve suggerire che il messaggio
serva a farsi riconoscere o a dimostrare chi si è: chi conosce l'ID può scrivere la frase
giusta senza alcuno sforzo. Serve a **coordinarsi fra persone che si aspettano**, ed è
tutto ([ADR-0010](adr/0010-messaggio-di-richiesta.md)).

**La conversione in maiuscolo è a carico del client, ed è obbligatoria.** Il protocollo
ammette una sola forma, `[A-Z0-9]{6}`, e il server rifiuta tutto il resto: se il campo non
convertisse, ogni utente che digita in minuscolo riceverebbe `INVALID_ROOM_ID` senza
capire perché. Convertendo durante la digitazione, il vincolo non si vede mai.

Il font monospace non è estetica: distingue `0` da `O` e `1` da `I`, che in un font
proporzionale sono quasi identici. Un ID trascritto male non dà errore — porta in una
stanza nuova e vuota.

**Perché il pulsante «Genera» e la regola «crea prima, condividi poi».** Chi crea una stanza
genera la chiave di cifratura ed è il primo approvatore. Se due persone concordano un ID
prima che la stanza sia stata effettivamente aperta (es. via messaggio: *"ci vediamo su
Krypta stanza ABC123 fra 5 minuti"*), un terzo incomodo o uno scanner può inviare il `join`
per primo, insediandosi come creatore/proprietario di fatto della stanza (corsa all'ID / host
hijacking). Generando l'ID tramite il pulsante «Genera» (`RandomNumberGenerator`) ed entrando
**prima** di comunicare l'ID, la stanza nasce protetta. L'ID generato è copiabile con un clic
dall'intestazione della chat per invitarne altri.

**Non c'è nessun campo per il nome della stanza.** Una stanza nuova nasce come
`RM-K7X2M9` e si rinomina dopo, dall'interno, con `/NameRoom` (§5.5).

Errori da gestire:

| Codice | Messaggio |
|---|---|
| `ROOM_FULL` | "La stanza è piena (20/20)." |
| `JOIN_RATE_LIMITED` | "Troppi tentativi di ingresso. Riprova tra **N** secondi." con conto alla rovescia da `retryAfterSeconds` e pulsante disabilitato finché non scade |
| `INVALID_ROOM_ID` | Sotto il campo (non dovrebbe accadere: il client valida prima) |

**Non c'è nessun elenco di stanze, nessun suggerimento, nessuna cronologia degli ID
visitati.** L'unico modo per conoscere un ID è che qualcuno te lo dica.

Il testo esplicativo deve dire anche cosa succede se la stanza esiste già:

> Se la stanza non esiste, la crei ed entri subito.
> Se esiste, dovrai aspettare che qualcuno già dentro ti accetti.

---

## 4bis. Schermata [3b] — In attesa di approvazione

Compare quando il `join` riceve `pending` invece di `joined`.

```
+--------------------------------------------------+
|                                                  |
|   Connesso a 127.0.0.1:5000  ·  luca             |
|                                                  |
|                                                  |
|                    #K7X2M9                       |
|                                                  |
|          In attesa di approvazione...            |
|                                                  |
|              ( o o o o o o )                     |
|                                                  |
|          Qualcuno gia' nella stanza deve         |
|          accettare la tua richiesta.             |
|                                                  |
|                Scade fra 1:47                    |
|                                                  |
|                              +---------------+   |
|                              |    Annulla    |   |
|                              +---------------+   |
|                                                  |
+--------------------------------------------------+
```

Sotto il conto alla rovescia, il **codice di verifica** della propria chiave di sessione:

```
|          Il tuo codice di verifica               |
|            4821 9037 5164 2288                   |
|   Chi ti accetta vede lo stesso codice.          |
|   Confrontarlo a voce esclude che qualcuno       |
|   si sia messo in mezzo.                         |
```

| Elemento | Comportamento |
|---|---|
| ID stanza | L'unica cosa che il client sa: nome, partecipanti e contenuto **non arrivano** in questa fase |
| Messaggio inviato | Ripetuto in piccolo, se ce n'era uno: serve a ricordare cosa vedranno di te. Viene dalla **copia locale**, non dal server — il `pending` non lo rimanda indietro ([03 §5.10](03-protocollo.md)) |
| Codice di verifica | 16 cifre in 4 gruppi, derivate dalla propria chiave pubblica ([03 §7bis.7](03-protocollo.md)). Selezionabile per la lettura a voce. Il testo deve spiegare **a cosa serve**, non solo mostrarlo |
| Indicatore | Animazione di attesa indeterminata. Nessuna barra di progresso: non c'è nulla che progredisca |
| Conto alla rovescia | Da `expiresInSeconds`, formato `m:ss`. A zero il client mostra l'esito di timeout senza aspettare il server |
| Annulla | Invia `cancel`, torna a [3] |
| Esito | `joined` → [4]. `rejected` → [3] con il messaggio corrispondente al `reason` |

**Questa schermata non deve mostrare niente della stanza.** È la ragione per cui il
messaggio `pending` non contiene né il nome né il numero di partecipanti: chi è in attesa
non è dentro, e non deve poter dedurre nulla da fuori. Un client che mostrasse "3
partecipanti" mentre aspetti trasformerebbe la sala d'attesa in uno strumento di
ricognizione — bussi a un ID e scopri se quella stanza è attiva.

**Sui messaggi di esito, tono neutro.** `rejected` e `timeout` producono testi diversi ma
non toni diversi: chi rifiuta non deve sentirsi in dovere di giustificarsi, e chi è
rifiutato non deve poter dedurre se è stato respinto di proposito o solo ignorato.

---

## 5. Schermata [4] — Chat

```
+----------------------------------------------------------+
| Coordinamento #K7X2M9   [Attesa (2)]  3/20  [Lascia stanza]|  <- fascia 1
+----------------------------------------------------------+
|                                                          |
|  -- STANZA ---                                           |
|  luca si e' unito alla stanza                            |
|                                                          |
|  -- emy ---                                              |
|  ci vediamo alle 18                                      |
|                                                          |
|  -- luca ---                                             |  <- fascia 2
|  ok, porto io i documenti                                |     (scorrevole)
|                                                          |
|  -- STANZA ---                                           |
|  Partecipanti (3/20): emy, luca, sara                    |
|                                                          |
|  -- emy ---                                              |
|  a dopo                                                  |
|                                                          |
+----------------------------------------------------------+
| +----------------------------------------+ +-----------+ |  <- fascia 3
| | Scrivi un messaggio...                 | |   Invia   | |
| +----------------------------------------+ +-----------+ |
+----------------------------------------------------------+
```

### 5.1 Fascia 1 — intestazione

| Elemento | Posizione | Dettagli |
|---|---|---|
| Nome stanza | Sinistra | `RM-K7X2M9` finché non rinominata. Si aggiorna dal campo `roomName` di `system`/`room_renamed` |
| ID stanza | Dopo il nome | `#K7X2M9`, monospace, colore attenuato. **Cliccabile per copiarlo** — è il modo naturale per invitare qualcuno |
| **Attesa (N)** | Al centro-destra | **Pulsante nuovo.** Compare solo quando `N > 0`, con un accento visivo che lo renda notabile. Apre il pannello §5.1bis |
| Numero partecipanti | Destra, piccolo | `3/20`, colore attenuato. Aggiornato dal messaggio `participants` |
| Lascia stanza | Estrema destra | **Chiede conferma esplicita** ("Abbandonare la stanza? La cronologia andrà persa definitivamente.") prima di inviare `leave` e tornare a [3] |

Il contatore non è cliccabile: per l'elenco dei nomi si usa `/Users`, che è visibile a
tutti per scelta.

### 5.1bis Pannello della sala d'attesa

Si apre dal pulsante **Attesa (N)**. Non è una finestra separata: è un riquadro che scende
sotto l'intestazione, sopra l'area messaggi.

```
+----------------------------------------------------------+
| Coordinamento #K7X2M9   [Attesa (2)]  3/20  [Lascia stanza]|
+----------------------------------------------------------+
|  In attesa di entrare                              [x]   |
|                                                          |
|   sara      da 0:12        [ Accetta ]  [ Rifiuta ]      |
|     "Sono Sara, quella del preventivo"                   |
|     codice 4821 9037 5164 2288                           |
|                                                          |
|   marco     da 0:03        [ Accetta ]  [ Rifiuta ]      |
|     "Hey, mi vorrei unire alla stanza"                   |
|     codice 7734 1290 6605 4417                           |
|                                                          |
|   Il messaggio lo scrive chi bussa: non prova niente.    |
|   Accettando consegni a questa persona la chiave         |
|   della stanza. Se puoi, fatti leggere il codice.        |
|   Chiunque puo' decidere: verra' mostrato a tutti chi.   |
+----------------------------------------------------------+
|                                                          |
|  -- STANZA ---                                           |
|  sara chiede di entrare nella stanza                     |
```

| Elemento | Comportamento |
|---|---|
| Elenco | Dal messaggio `waiting`, che arriva **sempre completo**: il pannello si ridisegna, non applica delta |
| Nickname | Quello *richiesto*. Se in stanza c'è già una `sara`, l'ammessa diventerà `sara2`: il nome definitivo compare nel `system` `user_joined` |
| Messaggio | Il `joinMessage` della richiesta, reso **come testo** e mai come markup — è la stringa più ostile del protocollo, l'unica scritta da qualcuno che nessuno ha ammesso. Su una riga, con ellissi se non entra e il testo intero al passaggio del mouse. **Se assente, non si mostra niente**: nessun segnaposto tipo "nessun messaggio", che darebbe l'idea di una mancanza |
| Attesa | `waitingSeconds` in formato `m:ss`, incrementato localmente fra un `waiting` e il successivo |
| Codice di verifica | Derivato dalla `publicKey` della richiesta. Deve coincidere con quello che il richiedente vede sul proprio schermo: se coincide, nessuno si è messo in mezzo ([03 §7bis.7](03-protocollo.md)) |
| Accetta | **Cifra la chiave di stanza** per quella persona e invia `approve` con la `wrappedKey`. Se il wrapping fallisce, l'`approve` **non parte**: si mostra un errore, mai un'ammissione senza chiave |
| Rifiuta | Invia `reject`. Nessuna operazione crittografica |
| Dopo il clic | Entrambi si disabilitano in attesa del `waiting` aggiornato |
| Testo di avviso | Deve dire che **accettare significa consegnare la chiave**. È la conseguenza vera del gesto, e senza scriverla l'utente non ha modo di intuirla |
| `UNKNOWN_REQUEST` | **Non è un errore da mostrare.** Significa che un altro partecipante ha già risposto: la riga sparisce e basta |
| Riga esplicativa | Fissa in fondo al pannello: dice che il messaggio non prova niente, che chiunque può decidere e che la decisione è pubblica. Va scritta, non lasciata intuire |
| Chiusura | La `[x]` chiude il pannello; il pulsante resta con il conteggio |
| Coda vuota | `"requests": []` chiude il pannello e nasconde il pulsante |

**Il pannello non si apre da solo all'arrivo di una richiesta.** Coprirebbe la
conversazione a ogni tentativo di ingresso, e con dieci richieste in coda diventerebbe un
modo per rendere la chat inutilizzabile da fuori. Il segnale è il pulsante che compare,
più la riga `join_requested` nel flusso dei messaggi: due avvisi passivi, nessuna
interruzione.

**Ed è il pannello l'unico posto dove il messaggio del richiedente può comparire.** La riga
`join_requested` nel flusso contiene solo il nickname, sempre, perché il suo testo lo
compone il server. Se contenesse il `joinMessage`, chiunque conosca l'ID potrebbe scrivere
nella conversazione di una stanza in cui non è entrato — e i due avvisi passivi
diventerebbero un canale attivo verso tutti. Aprire il pannello resta una scelta:
**leggere il messaggio di uno sconosciuto è un atto volontario**, esattamente come vederlo
in coda ([ADR-0010](adr/0010-messaggio-di-richiesta.md)).

### 5.2 Fascia 2 — formato dei messaggi

Ogni messaggio occupa **due righe**, più uno stacco verticale prima del successivo:

```
-- {nickname} ---
{testo}
```

- Prima riga: `--`, spazio, nickname, spazio, `---`.
- **Il nickname è colorato** (§6). I trattini restano nel colore di testo attenuato,
  così il nome risalta.
- Il testo va a capo automaticamente e non si tronca mai.
- Il testo è **selezionabile e copiabile**, non modificabile.
- Gli a capo inseriti con `Shift+Invio` sono preservati.
- **L'intestazione si ripete a ogni messaggio**, anche se lo stesso utente scrive più
  volte di fila. Nessun raggruppamento in stile Discord.

**Lo stacco tra un messaggio e l'altro è un margine, non una riga vuota di testo.**
Visivamente è identico; la differenza è che selezionando e copiando la conversazione non
ci si porta dietro righe vuote.

### 5.3 Messaggi di sistema

I `system` (§5.4 del protocollo) usano **lo stesso formato a due righe**, con mittente
riservato `STANZA`:

```
-- STANZA ---
luca si è unito alla stanza
```

| Aspetto | Regola |
|---|---|
| Nome mittente | Sempre `STANZA`, in un colore **fisso e grigio**, mai preso dalla palette |
| Testo | Corsivo o colore attenuato, per distinguerlo dai messaggi veri a colpo d'occhio |
| Provenienza | Il testo arriva già pronto dal campo `text`: il client non lo compone |
| `kind` sconosciuto | Si mostra comunque, usando il solo `text`. Mai un errore |

`STANZA` è tra i **nomi riservati** che il server rifiuta come nickname: senza quel
divieto, un utente potrebbe chiamarsi `STANZA` e fabbricare falsi messaggi di sistema.

**I messaggi locali usano invece il mittente `KRYPTA`**, anch'esso riservato, con un
colore diverso da `STANZA`. Sono le righe che il client genera da solo — risposta a
`/Help`, comando sconosciuto, argomento non valido — e **non le ha viste nessun altro**.
La distinzione visiva conta: senza, l'utente può credere di aver mandato in chat a tutti
un messaggio d'errore che invece ha visto solo lui.

Riassumendo i tre mittenti possibili nell'area messaggi:

| Mittente | Origine | Chi lo vede |
|---|---|---|
| un nickname | un partecipante | tutta la stanza |
| `STANZA` | il server (`system`) | tutta la stanza |
| `KRYPTA` | il client stesso | **solo tu** |

Il `kind` `user_list` ha un campo `users` con l'array dei nickname. Il client può usarlo
per una resa migliore (una lista invece di una riga), ma **il fallback sul solo `text`
deve funzionare**.

### 5.4 Fascia 3 — composizione e comandi

| Elemento | Comportamento |
|---|---|
| Casella di testo | Multiriga, cresce fino a 5 righe poi scorre. Placeholder "Scrivi un messaggio…". Ha il focus all'ingresso nella stanza |
| `Invio` | Invia |
| `Shift+Invio` | A capo |
| Invia (pulsante) | Stesso effetto di `Invio`. Disabilitato se il testo è vuoto o solo spazi |
| Contatore | Compare oltre i 1800 caratteri: `1834/2000`, rosso a 2000 |
| Dopo l'invio | La casella si svuota **subito**. Il messaggio compare però solo quando torna dal server (modalità eco) |

**Riconoscimento dei comandi**, prima dell'invio:

```
il testo inizia con "//"  -> messaggio normale, si toglie una barra
il testo inizia con "/"   -> comando: nome fino al primo spazio (minuscolo),
                             argomento = il resto (trim)
altrimenti                -> messaggio normale
```

| Comando | Argomento | Validazione lato client | Va al server? |
|---|---|---|---|
| `/NameRoom` | Nuovo nome | 1–25 caratteri: alfanumerici **e spazio**. Spazi ai bordi rimossi, spazi doppi ridotti a uno. Se vuoto o non valido: errore locale con il motivo | Sì |
| `/Users` | Nessuno | — | Sì |
| `/ClearRoom` | Nessuno | **Chiede conferma** prima di inviare | Sì |
| `/GenerateKey` | Nessuno | **Chiede conferma**, spiegando cosa fa e cosa non fa | Sì, come `rekey` |
| `/Help` | Nessuno | — | **No: eseguito localmente** |

Comportamenti richiesti:

- **`/Help`** → messaggio locale con l'elenco dei comandi e la sintassi. Non viene mai
  inviato al server. È l'unico modo per scoprire i comandi: non c'è nessun altro punto
  della UI che li elenchi. Testo proposto:

  ```
  -- KRYPTA ---
  Comandi disponibili:
    /NameRoom <nome>   rinomina la stanza (max 25 caratteri, lettere numeri e spazi)
    /Users             mostra a tutti l'elenco dei partecipanti
    /ClearRoom         svuota la chat sullo schermo di tutti
    /GenerateKey       rigenera la chiave: chi e' gia' uscito non legge piu'
    /Help              mostra questo messaggio (solo a te)
  Per scrivere un messaggio che inizia con "/", usa "//".
  ```

- **Comando sconosciuto** → messaggio locale in chat ("Comando sconosciuto: `/pippo`.
  Scrivi `/Help` per l'elenco."), senza contattare il server e senza svuotare la casella
  di testo.
- **Conferma per `/ClearRoom`** → finestra di dialogo: "Svuotare la chat per **tutti** i
  partecipanti? I messaggi spariranno dallo schermo di tutti." con Annulla / Svuota.
  È distruttivo per gli altri, non solo per chi lo digita.
- **Conferma per `/GenerateKey`** → il testo deve dire cosa fa **e cosa non fa**:
  "Rigenerare la chiave della stanza? Chi è già uscito non potrà più leggere i messaggi
  successivi. **Chi è dentro adesso resta dentro** e riceve la chiave nuova." con
  Annulla / Rigenera.

  La seconda frase non è di cortesia. *"Rigenera la chiave"* si legge facilmente come
  *"butta fuori qualcuno"*, e un utente che lo credesse userebbe il comando aspettandosi
  un'espulsione che non avviene — vedendosi restare in stanza la persona che voleva
  togliere, senza capire perché. Il comando non caccia nessuno: taglia fuori chi è **già**
  uscito ([03 §6.6](03-protocollo.md)).
- **Azione rapida su `user_left`** → all'arrivo del messaggio `system` *"{nick} è uscito dalla stanza"*,
  il client mostra un chip/pulsante discreto: `[Rigenera chiave]`. Cliccandoci si apre il dialogo di conferma
  di `/GenerateKey`. Impedisce che la protezione post-uscita resti disattesa perché gli utenti dimenticano
  di digitare il comando a mano.
- **Conferma chiusura finestra (`Window.Closing`)** → se l'utente tenta di chiudere la finestra (`Alt+F4`,
  pulsante `X` dell'OS) mentre si trova nella stanza attiva ([4]) o in attesa ([3b]), il client intercetta
  l'evento e mostra un dialogo modale di conferma:
  *"Se chiudi la finestra abbandonerai la stanza e la conversazione andrà persa definitivamente. Vuoi uscire?"*
  con **Annulla** ed **Esci**. Impedisce la distruzione accidentale e istantanea della sessione.
- **`clear` in arrivo** → svuota `Messages`. Subito dopo arriva il `system`
  `room_cleared` che dice chi è stato: quella riga resta visibile.
- I messaggi locali di errore (comando sconosciuto, argomento non valido) sono **solo per
  chi ha digitato** e non passano dal server. Vanno resi come `system` ma con un colore
  diverso, per non far credere che li abbiano visti anche gli altri.

### 5.5 Scorrimento

All'arrivo di un nuovo messaggio la vista scorre in fondo **solo se** l'utente era già in
fondo. Se stava leggendo più in alto, la posizione non si muove e compare un indicatore
"nuovi messaggi" cliccabile.

---

## 5bis. Crittografia lato client

Il client è l'unico punto del sistema che tratta chiavi in chiaro. Regole vincolanti:

| Momento | Cosa fa il client |
|---|---|
| Avvio della connessione | Genera una **coppia effimera di sessione** (ECDH P-256). Mai riusata fra sessioni, mai scritta su disco |
| `join` | Allega la chiave pubblica |
| Creazione stanza (`created: true`) | Genera **32 byte casuali** con `RandomNumberGenerator`: è la chiave di stanza |
| Ammissione (`created: false`) | Apre la `wrappedKey` con la propria privata e ottiene la chiave di stanza |
| Approvazione di un altro | Cifra la chiave di stanza con la pubblica del richiedente e la allega all'`approve` |
| Invio messaggio | Cifra con AES-256-GCM, `seq` crescente, nonce nuovo |
| Ricezione messaggio | Decifra, **verifica il tag** e controlla che `seq` sia crescente per quel mittente |
| `system` `user_left` | **Elimina il contatore `seq` di quel nickname**, che torna libero e può essere riusato da un altro partecipante ([03 §7bis.5](03-protocollo.md)) |
| `/GenerateKey` | Genera 32 byte nuovi, li cifra per **ogni altro partecipante** usando l'ultimo `roster`, invia il `rekey`, e passa alla chiave nuova **solo dopo la conferma** |
| `rekeyed` in arrivo | Apre il proprio involucro, sposta la chiave corrente a "precedente" e adotta la nuova. La precedente scade dopo 60 secondi |
| Uscita dalla stanza | Azzera la chiave di stanza, **quella dell'epoca precedente** e tutti i contatori `seq` in memoria |

### Messaggi che non decifrano

Un messaggio il cui tag GCM non verifica, o il cui `seq` non è crescente, **non va mai
mostrato come testo normale e non va mai scartato in silenzio**. Va reso con un'evidenza
esplicita:

```
-- KRYPTA ---
Un messaggio da "luca" non e' stato verificato ed e' stato scartato.
```

Le due cause possibili sono entrambe da far vedere: corruzione, oppure un server che sta
riattribuendo o riproponendo messaggi. Nasconderlo toglierebbe all'utente l'unico segnale
che ha.

**Proprio per questo il contatore va azzerato su `user_left`.** Se non lo fosse, il
partecipante successivo che riusa un nickname libero — cosa del tutto normale, il server
lo permette — vedrebbe **ogni** suo messaggio finire in questa riga rossa. L'avviso più
importante dell'applicazione diventerebbe rumore di fondo, e la prima volta che segnala un
attacco vero nessuno ci farebbe caso.

### Indicatore di cifratura

L'intestazione della chat mostra sempre lo stato, senza doverlo cercare:

| Stato | Indicatore |
|---|---|
| `wss://` + E2EE attivi | Lucchetto chiuso, discreto |
| `ws://` in chiaro (solo `127.0.0.1`, sviluppo) | **Avviso permanente e visibile**: "Connessione non cifrata" |

L'avviso in chiaro non è un badge decorativo: finché la fase 1B non è completa, è l'unica
cosa che impedisce a qualcuno di usare Krypta credendolo cifrato.

### Cosa non fare

- Mai scrivere su disco chiavi private, chiave di stanza o testo in chiaro.
- Mai loggare materiale crittografico, nemmeno in sviluppo.
- Mai inviare un `approve` se il wrapping della chiave è fallito.
- Mai usare `Random`: solo `RandomNumberGenerator`.
- Mai riusare un nonce con la stessa chiave.

## 6. Colori dei nickname

Colore **deterministico**, derivato dal solo nickname, senza coordinamento col server.
Stesso nome, stesso colore su tutti i client.

```csharp
public static Color ForNickname(string nickname)
{
    // 1. Normalizza: minuscolo invariante + trim.
    //    "Emy", "emy" e " emy " devono dare lo stesso colore.
    var key = nickname.Trim().ToLowerInvariant();

    // 2. Hash stabile FNV-1a a 32 bit sui byte UTF-8.
    //    NON usare string.GetHashCode(): in .NET e' randomizzato
    //    a ogni avvio del processo, quindi darebbe colori diversi
    //    su client diversi. E' il punto critico dell'algoritmo.
    uint hash = 2166136261u;
    foreach (byte b in Encoding.UTF8.GetBytes(key))
    {
        hash ^= b;
        hash *= 16777619u;
    }

    // 3. Indice nella palette del tema corrente (20 voci per entrambi i temi).
    //    Le due palette hanno la stessa lunghezza: lo stesso nickname prende
    //    lo stesso indice in chiaro e in scuro, e cambia solo la tinta.
    return Palette(theme)[(int)(hash % (uint)Palette(theme).Length)];
}
```

**Si calcola sempre sul nickname effettivo**, quello assegnato dal server. `emy` ed
`emy2` sono nomi diversi e ricevono quindi colori diversi: la disambiguazione numerica
si vede anche dal colore, non solo dalla cifra finale.

**Due palette da 20 colori**, una per tema. Stessi indici, tinte diverse: un colore
leggibile su fondo scuro non lo è su fondo chiaro, quindi **il colore dipende dal nickname
e dal tema**. Sono rese entrambe nel mockup: [`docs/assets/mockup-interfaccia.html`](assets/mockup-interfaccia.html).

I contrasti sono misurati sul fondo dell'area messaggi del rispettivo tema — `#0E1116` per
lo scuro, `#FFFFFF` per il chiaro — e sono tutti sopra il 4.5:1 di WCAG AA (RNF-10).

| # | Scuro | Contr. | Chiaro | Contr. | | # | Scuro | Contr. | Chiaro | Contr. |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `#E06C75` | 5.92 | `#DA1B29` | 5.04 | | 11 | `#B5CEA8` | 11.13 | `#1D4A1C` | 10.25 |
| 2 | `#98C379` | 9.38 | `#3E5F17` | 7.36 | | 12 | `#FFCB6B` | 12.61 | `#8C6D06` | 4.88 |
| 3 | `#E6A4B4` | 9.31 | `#C9264D` | 5.43 | | 13 | `#56B6C2` | 7.98 | `#19535A` | 8.66 |
| 4 | `#82AAFF` | 8.23 | `#0A53EE` | 6.03 | | 14 | `#7FD1B9` | 10.56 | `#1F7159` | 5.89 |
| 5 | `#61AFEF` | 8.00 | `#065290` | 8.02 | | 15 | `#F78C6C` | 8.04 | `#D03609` | 4.98 |
| 6 | `#C678DD` | 6.42 | `#AD23D6` | 5.27 | | 16 | `#DDBBAA` | 10.59 | `#7F4D18` | 7.05 |
| 7 | `#89DDFF` | 12.47 | `#054E6C` | 9.08 | | 17 | `#B0A4DA` | 8.26 | `#6240DA` | 6.43 |
| 8 | `#C3E88D` | 13.73 | `#32680C` | 6.73 | | 18 | `#60DC75` | 10.80 | `#098144` | 4.96 |
| 9 | `#E5C07B` | 10.95 | `#614A0A` | 8.43 | | 19 | `#CB80B8` | 6.59 | `#BA2594` | 5.56 |
| 10 | `#D19A66` | 7.68 | `#A74C24` | 5.67 | | 20 | `#DADC60` | 12.96 | `#4C4D0A` | 8.85 |

Minimo: **5.92** sullo scuro, **4.88** sul chiaro. La coppia percettivamente più vicina è a
**ΔE 15.4** sullo scuro e **11.3** sul chiaro (in entrambi i casi i due verdi 2 e 8).

**Da dove vengono i colori 17–20.** Le prime sedici tinte sono quelle originarie. Le quattro
aggiunte non sono scelte a occhio: sono state messe nei **quattro intervalli di tinta più
larghi** che la palette lasciava scoperti — indaco (253°), verde (130°), magenta (315°) e
chartreuse (61°) — mantenendo saturazione e luminosità nella gamma delle altre, così da non
introdurre colori che stonano. Aggiungerle non ha stretto la palette: la coppia più vicina
resta la stessa di prima, con la stessa distanza.

**La palette chiara non è la scura schiarita.** Ogni tinta conserva l'angolo di colore ma
viene saturata e scurita fino a superare AA sul bianco. I contrasti sono deliberatamente
**scaglionati** invece che uniformi: portarli tutti allo stesso valore appiattirebbe le
differenze di luminosità, e i tre verdi diventerebbero indistinguibili fra loro.

I colori di `STANZA` e `KRYPTA` **non** vengono dalla palette: sono due tinte fisse
riservate — grigio per i messaggi di sistema, un accento distinto per quelli locali.
Se passassero dalla stessa funzione degli utenti, potrebbero capitare dello stesso colore
di un partecipante e diventare indistinguibili da un messaggio vero.

**Venti colori per venti posti non tolgono le collisioni**, ed è bene non aspettarselo:
l'indice esce da un hash, non da una distribuzione ordinata, quindi due nickname possono
finire sullo stesso colore anche con la stanza mezza vuota. Il guadagno reale, con 20
partecipanti, è questo:

| Palette | Colori distinti attesi | Probabilità di zero collisioni |
|---|---|---|
| 16 colori | 11.6 | ~0 |
| **20 colori** | **12.8** | 0,0000023% |

Un colore distinto in più, in media. Vale la pena averlo — costa quattro voci in una
tabella — ma **non cambia la natura della cosa**: le collisioni restano la norma, e la
difesa vera è che il nickname è sempre scritto accanto al messaggio. Il colore aiuta a
scorrere la conversazione, non a identificare chi parla.

> **Attenzione al cambio di modulo.** Passando da 16 a 20 voci, `hash % Palette.Length`
> cambia per **tutti** i nickname, non solo per quelli che finirebbero nelle tinte nuove:
> `emy` non avrà più il colore che aveva prima. Non è un problema — niente è persistente e
> il colore non è un'identità — ma va saputo, perché durante lo sviluppo dà l'impressione
> che qualcosa si sia rotto.

---

## 7. Struttura MVVM

**Pattern: ReactiveUI**, lo stesso già in uso negli altri progetti Avalonia della casa
(ETG MCA). Non è una scelta presa qui: è la convenzione di casa, e questo documento la
registra perché il codice le assomigli senza doverla dedurre.

### 7.1 Le convenzioni

```csharp
// La classe base non fa altro che questo.
public class ViewModelBase : ReactiveObject { }

// Proprietà: campo privato con underscore e PascalCase, RaiseAndSetIfChanged nel set.
private string _RoomId = "";
public string RoomId
{
    get => _RoomId;
    set => this.RaiseAndSetIfChanged(ref _RoomId, value);
}

// Comandi: ReactiveCommand, creati nel costruttore.
public ReactiveCommand<Unit, Unit> JoinCommand { get; }
public RoomViewModel() { JoinCommand = ReactiveCommand.Create(Join); }
```

| Elemento | Convenzione |
|---|---|
| Classe base | `ViewModelBase : ReactiveObject` |
| Proprietà notificate | `this.RaiseAndSetIfChanged(ref _Campo, value)` |
| Campi privati di supporto | `_PascalCase` con underscore iniziale — **non** `_camelCase` |
| Comandi | `ReactiveCommand<Unit, Unit>`, o `<string, Unit>` se prendono un parametro |
| Navigazione | `IScreen` + `IRoutableViewModel` con `UrlPathSegment`, e `RoutedViewHost` nella finestra |
| Ciclo di vita | `IActivatableViewModel` con `ViewModelActivator`, e un `CompositeDisposable` che libera le sottoscrizioni |
| View ↔ ViewModel | `ViewLocator : IDataTemplate`, per convenzione di nome: `…ViewModel` → `…View` |
| Binding | `AvaloniaUseCompiledBindingsByDefault` a `true` nel `.csproj` |
| Avvio | `AppBuilder.Configure<App>().UsePlatformDetect().UseReactiveUI()` |

**Due avvertenze pratiche**, entrambe verificate su `net10.0` il 2026-09-08:

- `public IScreen? HostScreen { get; }` produce il warning **CS8766** con `<Nullable>enable</Nullable>`,
  perché `IRoutableViewModel` dichiara `HostScreen` non annullabile. È innocuo e presente
  anche in ETG MCA. Va deciso una volta: o si dichiara `IScreen HostScreen` senza `?`, o
  si accetta il warning — l'importante è non lasciarlo comparire a caso in metà dei file.
- Le sottoscrizioni fatte in `WhenActivated` vanno aggiunte al `CompositeDisposable`.
  Nella schermata [4] ce ne sono diverse (contatore partecipanti, coda d'attesa, conto
  alla rovescia), e non liberarle tiene in vita il ViewModel dopo un `leave`.

### 7.2 I ViewModel

```
MainWindowViewModel : ViewModelBase, IScreen
├── Router      : RoutingState      la navigazione fra [1]..[4]
├── Connection  : KryptaConnection  condivisa da tutti i ViewModel
│
├── ConnectionViewModel   Host, Port, Remember, ConnectCommand, StatusMessage
├── AccountViewModel      Nickname, NicknameColor, ContinueCommand
├── RoomViewModel         RoomId, JoinMessage, JoinCommand, ErrorMessage, RetryCountdown
├── PendingViewModel      RoomId, SentJoinMessage, ExpiresIn, CancelCommand
└── ChatViewModel         RoomName, RoomId, ParticipantCount, MaxParticipants,
                          MyNickname, Messages : ObservableCollection<ChatEntry>,
                          Waiting : ObservableCollection<WaitingRequest>,
                                    (Nickname, JoinMessage?, VerificationCode,
                                     WaitingSeconds, RequestId)
                          IsWaitingPanelOpen, ApproveCommand, RejectCommand,
                          Draft, SendCommand, LeaveCommand
```

`ChatEntry` è il tipo unico per l'area messaggi, con tre varianti: messaggio utente,
messaggio di sistema, messaggio locale (errori di comando). Un solo `DataTemplate` con
selettore, così l'ordine cronologico è garantito da una sola collezione.

`KryptaConnection` è l'unico punto che tocca il WebSocket. Espone eventi
(`MessageReceived`, `SystemReceived`, `ClearReceived`, `ParticipantsChanged`,
`RoomRenamed`, `PendingStarted`, `WaitingChanged`, `Rejected`, `Disconnected`,
`ErrorReceived`) e metodi (`ConnectAsync`, `JoinAsync`, `ApproveAsync`, `RejectAsync`,
`CancelAsync`, `SendMessageAsync`, `SendCommandAsync`, `LeaveAsync`, `DisconnectAsync`).

**Regola:** tutti gli eventi vengono marshalati sul thread UI con `Dispatcher.UIThread`
prima di toccare la `ObservableCollection`. È l'errore più comune in Avalonia e produce
crash intermittenti difficili da riprodurre. Con ReactiveUI l'alternativa idiomatica è
`.ObserveOn(RxApp.MainThreadScheduler)` sulla sottoscrizione: vanno bene entrambe, purché
in tutto il progetto se ne usi **una sola**, altrimenti non si capisce più quali percorsi
siano già stati marshalati.

## 8. Caduta del server e della rete

### 8.1 Disconnessione dal server
Alla `Disconnected` inattesa (chiusura del socket, errore di rete o server spento):

1. Si scarta l'intera collezione dei messaggi e le chiavi di stanza (niente resta in memoria).
2. Si torna a [1] con "Connessione al server persa."
3. Host, porta e nickname restano precompilati per facilitare il rientro.

Nessuna riconnessione automatica invisibile nell'MVP.

### 8.2 Caduta brusca della rete e partecipanti fantasma
In caso di caduta improvvisa della connessione (Wi-Fi interrotto o cambio rete senza `close` TCP pulito):

- **Lato server:** il vecchio socket resta registrato per un massimo di 60 secondi prima che l'`HeartbeatService` ne constati l'inattività ed emetta `user_left`.
- **Rientro immediato dell'utente:** se l'utente si ricollega subito alla stessa stanza prima dei 60 secondi:
  1. Il nickname originario è ancora occupato dal suo "fantasma", quindi riceve il nome incrementato (es. `emy` diventa `emy2`).
  2. La stanza è ancora viva, quindi finisce in sala d'attesa e deve essere ri-approvato da chi è dentro.
  3. All'ammissione, non essendoci persistenza, lo schermo locale della chat riparte pulito.
- **Comportamento del client:** il client informa chiaramente l'utente di questa finestra (*"Connessione persa. Se rientri subito potresti comparire temporaneamente con un numero (es. emy2) finché il server non rimuove la sessione precedente"*), evitando che l'utente pensi a un malfunzionamento o a un furto d'identità. Il server non consente il subentro forzato (session takeover) per preservare la sicurezza crittografica.

## 9. Persistenza lato client

**Nessuna.** Il client non scrive mai su disco:

- ❌ i messaggi (né in chiaro né cifrati)
- ❌ gli ID delle stanze visitate
- ❌ i nickname visti nella stanza, o gli elenchi restituiti da `/Users`
- ❌ i nomi assegnati alle stanze
- ❌ log applicativi contenenti dati di chat

Le scritture ammesse su disco sono **due**, entrambe in `krypta.config.xml`
([06 — Configurazione](06-configurazione.md)), e hanno regole diverse:

| Cosa | Quando viene scritto | Perché |
|---|---|---|
| Host, porta, nickname digitato | **Solo** se l'utente ha spuntato "Ricorda queste impostazioni" | È una comodità, e come tale si può rifiutare |
| `KnownServers` — le impronte SHA-256 dei certificati | **Sempre**, anche senza "Ricorda" | È una misura di sicurezza: senza memorizzare l'impronta, il TOFU non rileverebbe mai un certificato sostituito ([03 §1.2](03-protocollo.md)) |

**La seconda riga è una deroga vera a "nessuna persistenza", e va detta invece che
nascosta.** Il file registra a quali server ci si è collegati — un dato che prima non
finiva su disco, e che dice qualcosa su dove l'utente è stato. È il prezzo del TOFU, e si
paga in cambio dell'unica difesa contro un certificato sostituito.

Due conseguenze vincolanti:

- L'utente va **avvisato** la prima volta che un'impronta viene memorizzata, non messo
  davanti al fatto compiuto.
- Deve esistere una **voce di menu per ripulire `KnownServers`**, e deve funzionare senza
  toccare il resto del file. Un dato che non si può cancellare non è accettabile in questo
  prodotto.

Restano fuori da qualsiasi scrittura, sempre e comunque: chiavi private, chiave di stanza,
testo dei messaggi, ID delle stanze visitate, nomi delle stanze, nickname altrui.
