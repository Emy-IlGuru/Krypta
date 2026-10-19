# 01 — Requisiti

**Stato:** bozza · **Aggiornato:** 2026-09-08

Priorità secondo **MoSCoW**: `Must` (indispensabile per l'MVP), `Should` (importante ma
rinviabile), `Could` (se avanza tempo), `Won't` (esplicitamente fuori dall'MVP).

## 1. Requisiti funzionali — Server

### 1.1 Connessioni e stanze

| ID | Requisito | Priorità |
|---|---|---|
| RF-S-01 | Il server resta in ascolto su indirizzo e porta configurabili e accetta connessioni WebSocket. | Must |
| RF-S-02 | Un client può chiedere di entrare in una stanza indicando un **ID di 6 caratteri alfanumerici** e un **nickname**. | Must |
| RF-S-02b | L'ID è **sempre maiuscolo**: `[A-Z0-9]{6}`. Il server **rifiuta** con `INVALID_ROOM_ID` qualsiasi altra forma, minuscole comprese: non normalizza. L'ID resta una **stringa**, mai un numero. | Must |
| RF-S-03 | Se la stanza **non esiste**, il server la crea con nome `RM-{roomId}` e vi inserisce il client **subito, senza approvazione**: non c'è nessuno che possa approvarlo. | Must |
| RF-S-04 | Se la stanza **esiste**, il client va in **sala d'attesa** e non entra finché un partecipante non lo accetta. Una volta ammesso, **non riceve alcun messaggio precedente**. | Must |
| RF-S-05 | Il server **non memorizza** i messaggi: li inoltra ai partecipanti connessi e li scarta. Nessuna scrittura su disco, nessuna coda, nessun buffer di replay. | Must |
| RF-S-06 | Un messaggio ricevuto viene inoltrato a **tutti** i partecipanti della stanza, mittente incluso (eco). | Must |
| RF-S-07 | Una stanza ha un limite di **20 partecipanti**. Oltre, l'ingresso è rifiutato con `ROOM_FULL`. | Must |
| RF-S-08 | La stanza esiste finché ha **almeno un partecipante**. All'uscita dell'ultimo viene distrutta, con il suo nome, e l'ID torna disponibile. | Must |
| RF-S-09 | A ogni ingresso o uscita il server invia a tutti il **nuovo numero** di partecipanti. | Must |
| RF-S-10 | Il server **non espone** l'elenco delle stanze esistenti. | Must |
| RF-S-11 | Il server rileva le disconnessioni non pulite tramite heartbeat e libera il posto nella stanza, annunciando l'uscita. | Must |
| RF-S-11b | Finestra del partecipante fantasma: su disconnessione brusca (caduta TCP senza FIN/RST), il socket resta registrato sul server fino al timeout dell'heartbeat (max 60 s). Un rientro immediato con lo stesso nickname viene trattato come connessione separata (riceve numero automatico es. `emy2` e va in sala d'attesa); il server **non** ammette subentri di sessione (session takeover) per preservare la sicurezza crittografica. | Must |

### 1.1bis Sala d'attesa

| ID | Requisito | Priorità |
|---|---|---|
| RF-S-A01 | Chi chiede di entrare in una stanza esistente riceve `pending` e resta in sala d'attesa. | Must |
| RF-S-A02 | Il messaggio `pending` **non contiene nulla della stanza**: né nome, né numero di partecipanti, né contenuto. Chi è fuori non deve dedurre nulla. | Must |
| RF-S-A03 | Chi è in attesa **non riceve** i messaggi della stanza, non compare nel contatore, non è elencato da `/Users`. | Must |
| RF-S-A04 | **Qualunque partecipante** può accettare o rifiutare. Basta uno: nessun ruolo, nessuna maggioranza. | Must |
| RF-S-A05 | I partecipanti ricevono l'elenco completo delle richieste (`waiting`) a ogni cambiamento, con nickname richiesto e attesa. | Must |
| RF-S-A06 | Ogni richiesta genera un `system` `join_requested` visibile a tutti i partecipanti. | Must |
| RF-S-A07 | Accettazione e rifiuto generano un `system` che dice **chi** ha deciso. | Must |
| RF-S-A08 | Un `requestId` è evaso una volta sola: la seconda risposta riceve `UNKNOWN_REQUEST`. | Must |
| RF-S-A09 | Il nickname viene disambiguato **all'ammissione**, non alla richiesta. | Must |
| RF-S-A10 | Il limite di 20 partecipanti si ricontrolla **all'ammissione**: se la stanza si è riempita, il richiedente riceve `rejected` con `room_full`. | Must |
| RF-S-A11 | Una richiesta senza risposta **scade dopo 120 secondi** con `reason: "timeout"`. | Must |
| RF-S-A12 | La sala d'attesa contiene al massimo **10 richieste**; oltre, `WAITING_ROOM_FULL`. Non concorre al limite di 20. | Must |
| RF-S-A13 | Se la stanza è già piena, il `join` è rifiutato subito con `ROOM_FULL` senza mettere in coda. | Must |
| RF-S-A14 | Alla distruzione della stanza, le richieste pendenti ricevono `rejected` con `room_gone`. Nessuna richiesta orfana. | Must |
| RF-S-A15 | Chi è in attesa può solo inviare `cancel` e `pong`. Ogni altro messaggio riceve `NOT_IN_ROOM` o `NOT_PENDING`. | Must |
| RF-S-A16 | Il server **non conserva** traccia delle richieste evase: nessuna lista di chi ha provato a entrare, né degli accettati né dei rifiutati. | Must |
| RF-S-A17 | Il `join` può portare un **`joinMessage` facoltativo**: 1–200 caratteri, riga singola, senza caratteri di controllo. Fuori vincoli: `INVALID_JOIN_MESSAGE`. Vuoto o di soli spazi equivale ad assente, e **non è un errore**. | Must |
| RF-S-A18 | Il server inoltra il `joinMessage` **invariato** nelle voci di `waiting`, e **mai** nel testo del `system` `join_requested`: quel testo lo genera interamente il server ([ADR-0010](adr/0010-messaggio-di-richiesta.md)). | Must |
| RF-S-A19 | Il `joinMessage` di un `join` che **crea** la stanza viene scartato senza errore: non c'è nessuno che debba approvare, e il client non poteva sapere che la stanza non esistesse. | Must |
| RF-S-A20 | Il `joinMessage` **non compare mai nei log**, a nessun livello: è contenuto scritto da un utente, come l'argomento di `/NameRoom`. | Must |

### 1.1ter Cifratura

| ID | Requisito | Priorità |
|---|---|---|
| RF-X-01 | Il server espone **`wss://`**. Il `ws://` in chiaro è ammesso solo verso `127.0.0.1` in sviluppo. | Must |
| RF-X-02 | Il client applica **TOFU** sul certificato: impronta SHA-256 mostrata al primo collegamento e memorizzata in `KnownServers`. | Must |
| RF-X-03 | Impronta cambiata → **connessione rifiutata** con avviso esplicito. Mai un proseguimento silenzioso. | Must |
| RF-X-04 | Ogni client genera una **coppia effimera ECDH P-256** per connessione, mai riusata, mai su disco. | Must |
| RF-X-05 | Chi crea la stanza genera la **chiave di stanza** (32 byte) con `RandomNumberGenerator`, nel proprio client. | Must |
| RF-X-06 | Chi approva **cifra la chiave di stanza** per il richiedente e la allega all'`approve`. Il server la inoltra senza poterla leggere. | Must |
| RF-X-07 | Il server rifiuta un `approve` privo di `wrappedKey` valida: **non può ammettere nessuno da solo**. | Must |
| RF-X-08 | I messaggi viaggiano cifrati con **AES-256-GCM**, nonce nuovo per messaggio, AAD `roomId\|nickname\|seq`. | Must |
| RF-X-09 | L'AAD lega il messaggio al mittente: un server che lo riattribuisce fa **fallire la decifratura**. | Must |
| RF-X-10 | Ogni mittente numera i messaggi con `seq` crescente; il ricevente scarta i `seq` non crescenti (anti-replay). | Must |
| RF-X-10b | Il ricevente **elimina il contatore `seq` di un nickname quando riceve `user_left`** per quel nickname. Senza, il partecipante successivo che riusa quel nome riparte da `seq: 1` e viene scartato per intero come replay ([03 §7bis.5](03-protocollo.md)). | Must |
| RF-X-11 | Un messaggio che non decifra è mostrato come **non verificato**: mai come testo normale, mai scartato in silenzio. | Must |
| RF-X-12 | Dalla chiave pubblica si deriva un **codice di verifica a 16 cifre**, mostrato a chi attende e a chi approva. | Must |
| RF-X-13 | L'intestazione della chat mostra lo **stato di cifratura**; su connessione in chiaro l'avviso è permanente. | Must |
| RF-X-14 | Chiavi private, chiave di stanza e testo in chiaro non sono **mai** scritti su disco né loggati, nemmeno in sviluppo. | Must |
| RF-X-15 | La chiave di stanza viene azzerata in memoria all'uscita dalla stanza, insieme alla chiave dell'epoca precedente e ai contatori `seq`. | Should |
| RF-X-17 | **`/GenerateKey`** genera nel client una chiave di stanza nuova, cifrata **una volta per ciascun altro partecipante** con la sua chiave pubblica di sessione ([ADR-0011](adr/0011-rotazione-chiave-di-stanza.md)). | Must |
| RF-X-18 | Il server verifica che i destinatari di un `rekey` coincidano **esattamente** con i partecipanti attuali meno il mittente; altrimenti `INVALID_REKEY`. Senza, una rotazione parziale diventerebbe un modo per espellere qualcuno. | Must |
| RF-X-19 | Il server conserva la `publicKey` di ogni partecipante per la durata della permanenza e la distribuisce ai soli partecipanti nel messaggio `roster`. | Must |
| RF-X-20 | Ogni messaggio dichiara il `keyEpoch` con cui è cifrato. Il ricevente tiene la chiave dell'**epoca precedente per 60 secondi**, per non segnare come non verificati i messaggi in volo durante una rotazione. | Must |
| RF-X-21 | Il client passa a cifrare con la chiave nuova **solo dopo la conferma del server**: prima, rischierebbe di cifrare con una chiave che nessuno possiede. | Must |
| RF-X-22 | Un `approve` con un'epoca non corrente riceve `STALE_KEY_EPOCH`; il client rifà il wrapping con la chiave corrente e riprova, senza mostrare un errore. | Must |
| RF-X-23 | Il `seq` **non si azzera** al cambio di epoca: resta strettamente crescente per mittente. | Must |
| RF-X-16 | Le primitive vengono **solo dalla BCL** (`ECDiffieHellman`, `HKDF`, `AesGcm`, `RandomNumberGenerator`). Nessuna crittografia scritta a mano. | Must |

### 1.2 Nickname

| ID | Requisito | Priorità |
|---|---|---|
| RF-S-12 | Il nickname è di **3–25 caratteri**. Fuori range: `INVALID_NICKNAME`. | Must |
| RF-S-12b | Il nickname è **solo ASCII**: `[A-Za-z0-9_.-]` più lo spazio singolo interno. Niente accenti, alfabeti non latini o emoji. Il nickname entra nell'AAD dei messaggi, e due forme Unicode identiche a vedersi ma diverse in byte farebbero fallire la decifratura ([03 §4.1.1](03-protocollo.md)). | Must |
| RF-S-13 | I nomi riservati (`STANZA`, `KRYPTA`, `SISTEMA`, `SYSTEM`, `SERVER`, `ROOM`) sono rifiutati, senza distinzione di maiuscole. Impediscono di falsificare messaggi di sistema. | Must |
| RF-S-14 | Se il nickname è già in uso nella stanza, il server **appende il più piccolo intero ≥ 2 che lo rende libero** (`emy` → `emy2` → `emy3`), troncando la base se si superano i 25 caratteri. Non rifiuta mai per omonimia. | Must |
| RF-S-15 | Il nickname effettivo è restituito nel campo `nickname` di `joined`. | Must |
| RF-S-16 | Il nickname del mittente è aggiunto **dal server** a ogni messaggio inoltrato, mai preso dal payload del client. | Must |

### 1.3 Messaggi di sistema

| ID | Requisito | Priorità |
|---|---|---|
| RF-S-17 | All'ingresso di un partecipante il server invia a tutti un `system` `user_joined`: *"{nick} si è unito alla stanza"*. | Must |
| RF-S-18 | All'uscita (volontaria o per caduta) il server invia a tutti un `system` `user_left`: *"{nick} è uscito dalla stanza"*. | Must |
| RF-S-19 | I messaggi di sistema non sono conservati più di quelli normali. | Must |

### 1.4 Comandi

| ID | Requisito | Priorità |
|---|---|---|
| RF-S-20 | **`/NameRoom <nome>`** rinomina la stanza. Il nome è di 1–25 caratteri **alfanumerici o spazio**; altrimenti `INVALID_COMMAND_ARG`. | Must |
| RF-S-20b | Il server **normalizza gli spazi** del nome prima di validarne la lunghezza: rimuove quelli ai bordi e riduce le sequenze a uno solo. Senza, `"A"` seguito da 24 spazi sarebbe un nome valido che appare vuoto. | Must |
| RF-S-21 | Dopo una rinomina il server invia a tutti un `system` `room_renamed` con il campo `roomName` e il testo *"{nick} ha rinominato la stanza in «X»"*. | Must |
| RF-S-22 | Il nome della stanza è un'etichetta: **non** rende la stanza cercabile o elencabile. L'ID resta l'unico modo per collegarsi. | Must |
| RF-S-23 | **`/Users`** fa inviare a **tutti** i partecipanti un `system` `user_list` con l'elenco dei nickname, in ordine di ingresso. | Must |
| RF-S-24 | L'elenco è costruito al momento della richiesta e non viene conservato. | Must |
| RF-S-25 | **`/ClearRoom`** fa inviare a tutti un `clear`, seguito da un `system` `room_cleared` con *"{nick} ha svuotato la stanza"*. | Must |
| RF-S-26 | Il server non ha messaggi da cancellare: `clear` è un'istruzione ai client. Va documentato, non presentato come garanzia di sicurezza. | Must |
| RF-S-27 | **Ogni partecipante può eseguire ogni comando.** Non esistono ruoli né proprietari. | Must |
| RF-S-27b | **`/GenerateKey`** rigenera la chiave della stanza. Non viaggia come `command` ma come `rekey`, ed è limitato a **una rotazione ogni 30 secondi per stanza** (`REKEY_RATE_LIMITED`). | Must |
| RF-S-27c | Dopo una rotazione riuscita il server invia un `system` `room_rekeyed` che dice **chi** l'ha eseguita, come per ogni altro comando che cambia stato. | Must |
| RF-S-28 | Ogni comando che modifica lo stato annuncia **chi** lo ha eseguito. | Must |
| RF-S-29 | Un comando sconosciuto produce `UNKNOWN_COMMAND`, diretto al solo mittente. | Must |

### 1.5 Protezione e robustezza

| ID | Requisito | Priorità |
|---|---|---|
| RF-S-30 | Limite di frequenza sui messaggi: 10 ogni 5 secondi per connessione. | Should |
| RF-S-31 | Limite di frequenza sui comandi: 3 ogni 10 secondi per connessione. | Should |
| RF-S-32 | **Limite sui tentativi di `join`: oltre 5 in 3 minuti dallo stesso IP, blocco di 2 minuti** con `JOIN_RATE_LIMITED` e `retryAfterSeconds`. Contano tutti i tentativi, riusciti e falliti. | Must |
| RF-S-33 | Ogni messaggio in ingresso è validato: formato, lunghezza, stato della connessione. Un messaggio non valido produce un errore, non un crash. | Must |
| RF-S-34 | Un frame oltre **16384 byte** (`MaxFrameBytes`) viene rifiutato senza essere deserializzato. Il limite sull'involucro cifrato è un altro, 8192 byte **decodificati**: i tre limiti di dimensione sono conciliati in [03 §9.1](03-protocollo.md). | Must |
| RF-S-35 | Il server espone un endpoint di health check che **non** rivela numero di stanze o di utenti. | Should |
| RF-S-36 | Log degli eventi operativi **senza mai** il contenuto dei messaggi, e senza IP e nickname sulla stessa riga. | Must |
| RF-S-37 | Protezione della stanza con password. | Won't (MVP) |

## 2. Requisiti funzionali — Client

### 2.1 Connessione e identità

| ID | Requisito | Priorità |
|---|---|---|
| RF-C-01 | All'avvio il client legge `krypta.config.xml` e precompila IP e porta. | Must |
| RF-C-02 | La prima schermata permette di **modificare IP e porta** prima di connettersi. | Must |
| RF-C-03 | Il client mostra l'esito del tentativo di connessione (in corso / riuscita / fallita con motivo). | Must |
| RF-C-04 | La seconda schermata chiede **solo un nickname** (3–25 caratteri). Nessuna password. | Must |
| RF-C-05 | Il client avvisa in anticipo che un nome già in uso riceverà un numero (es. `emy2`). | Should |
| RF-C-06 | Dopo il `join` il client usa **il nickname restituito dal server**, non quello digitato, anche per il colore. | Must |

### 2.2 Stanza

| ID | Requisito | Priorità |
|---|---|---|
| RF-C-07 | La terza schermata chiede l'**ID della stanza** (6 caratteri alfanumerici) e non mostra nessun elenco. | Must |
| RF-C-07b | La schermata [3] include un pulsante **Genera ID** che compila il campo con un ID casuale conforme (`[A-Z0-9]{6}`) usando `RandomNumberGenerator`. Mitiga l'uso di ID deboli/prevedibili e il rischio di collisione o corsa all'ID (host hijacking). | Must |
| RF-C-08 | Il campo ID **converte in maiuscolo mentre si digita** e scarta i caratteri non validi. È il client a garantire la forma `[A-Z0-9]{6}` richiesta dal server: senza la conversione, chi digita in minuscolo riceverebbe un errore incomprensibile. | Must |
| RF-C-09 | Non esiste un campo per il nome della stanza: si assegna dopo, con `/NameRoom`. | Must |
| RF-C-10 | Su `JOIN_RATE_LIMITED` il client mostra il blocco con conto alla rovescia da `retryAfterSeconds` e disabilita il pulsante finché non scade. | Must |

### 2.2bis Sala d'attesa

| ID | Requisito | Priorità |
|---|---|---|
| RF-C-A01 | Alla risposta `pending` il client mostra la schermata **[3b] In attesa**, con l'ID stanza, un indicatore di attesa e un conto alla rovescia. | Must |
| RF-C-A02 | La schermata [3b] **non mostra nulla della stanza** oltre all'ID. | Must |
| RF-C-A03 | Il pulsante **Annulla** invia `cancel` e riporta alla schermata di selezione stanza. | Must |
| RF-C-A04 | I messaggi di esito (`rejected`, `timeout`) hanno testi diversi ma **tono neutro**: chi è respinto non deve poter dedurre se è stato ignorato o rifiutato di proposito. | Should |
| RF-C-A05 | Nella chat, un pulsante **Attesa (N)** compare quando ci sono richieste pendenti, e scompare quando la coda si svuota. | Must |
| RF-C-A06 | Il pulsante apre un pannello con l'elenco: nickname richiesto, tempo di attesa, **Accetta** e **Rifiuta** per ciascuno. | Must |
| RF-C-A07 | Il pannello **non si apre da solo** all'arrivo di una richiesta: coprirebbe la conversazione e sarebbe sfruttabile per renderla inutilizzabile. | Must |
| RF-C-A08 | Il pannello si ridisegna dall'elenco completo di `waiting`, senza applicare delta. | Must |
| RF-C-A09 | `UNKNOWN_REQUEST` **non viene mostrato come errore**: significa che un altro ha già risposto. La riga sparisce e basta. | Must |
| RF-C-A10 | Il pannello dichiara esplicitamente che chiunque può decidere e che la decisione sarà visibile a tutti. | Should |
| RF-C-A11 | La schermata [3] ha un campo **messaggio**, precompilato con *"Hey, mi vorrei unire alla stanza"* e modificabile. Svuotarlo è legittimo: il campo non viene inviato. | Must |
| RF-C-A12 | Accanto al campo, il client avverte che il messaggio **viaggia in chiaro** e che lo legge anche chi gestisce il server: è l'unico testo scritto dall'utente che l'E2EE non copre. | Must |
| RF-C-A13 | Il pannello della sala d'attesa mostra il `joinMessage` **come testo**, mai interpretato come markup, troncato con ellissi se non entra e leggibile per intero al passaggio del mouse. Se assente, non mostra nulla al suo posto. | Must |
| RF-C-A14 | La UI **non presenta il messaggio come un'identificazione**: chi conosce l'ID può scrivere la frase giusta a costo zero. Serve a distinguere fra persone attese, non a decidere di chi fidarsi ([07 §4.3](07-sicurezza-e-privacy.md)). | Must |

### 2.3 Chat

| ID | Requisito | Priorità |
|---|---|---|
| RF-C-11 | In alto: **nome stanza + ID**, pulsante **Lascia stanza**, e in piccolo il **numero di partecipanti**. | Must |
| RF-C-12 | L'intestazione si aggiorna quando arriva un `system` `room_renamed`. | Must |
| RF-C-13 | Ogni messaggio è reso in due righe: `-- nickname ---` poi il testo. **L'intestazione si ripete a ogni messaggio**, anche consecutivo dello stesso utente. | Must |
| RF-C-14 | Lo stacco fra messaggi è un margine, non una riga vuota di testo: copiando la conversazione non si portano dietro righe vuote. | Should |
| RF-C-15 | A ogni nickname è assegnato un colore **deterministico**: stesso nickname, stesso colore su tutti i client. | Must |
| RF-C-16 | I messaggi di sistema usano lo stesso formato con mittente `STANZA`, in grigio fisso e testo attenuato. | Must |
| RF-C-17 | Un `kind` di `system` sconosciuto viene mostrato usando il solo campo `text`, senza errori. | Must |
| RF-C-18 | In basso: casella di testo e pulsante **Invia**. `Invio` invia, `Shift+Invio` va a capo. | Must |
| RF-C-19 | Il messaggio inviato compare **solo quando torna dal server** (eco). La casella si svuota subito. | Must |
| RF-C-20 | L'area messaggi scorre in fondo all'arrivo di un nuovo messaggio **solo se** l'utente è già in fondo. | Should |
| RF-C-20b | All'arrivo di un `system` `user_left`, il client propone un'**azione rapida contestuale per `/GenerateKey`** (promemoria discreto/pulsante "Rigenera chiave"), ricordando che chi è uscito conserva la chiave fino a nuova rotazione. | Must |

### 2.4 Comandi lato client

| ID | Requisito | Priorità |
|---|---|---|
| RF-C-21 | Un testo che inizia con `/` viene interpretato come comando: nome fino al primo spazio (minuscolo), argomento il resto. | Must |
| RF-C-22 | `//` a inizio testo invia un messaggio normale che inizia con `/`. | Should |
| RF-C-23 | Un comando sconosciuto produce un messaggio **locale** in chat che rimanda a `/Help`, senza contattare il server. | Must |
| RF-C-23b | **`/Help`** mostra un messaggio **locale** con i comandi e la loro sintassi. Non viene mai inviato al server. È l'unico modo per scoprire i comandi: nessun altro punto della UI li elenca. | Must |
| RF-C-23c | I messaggi locali usano il mittente riservato `KRYPTA`, con colore distinto da `STANZA`: chi legge deve capire a colpo d'occhio che quella riga l'ha vista solo lui. | Must |
| RF-C-24 | Gli argomenti sono validati anche lato client, per un errore immediato. | Should |
| RF-C-25 | **`/ClearRoom` chiede conferma** con dialogo esplicito: svuota la chat di tutti, non solo la propria. | Must |
| RF-C-26 | Alla ricezione di `clear` il client svuota la propria lista messaggi. | Must |
| RF-C-27 | I messaggi locali di errore sono visivamente distinti dai `system` del server: non devono far credere che li abbiano visti anche gli altri. | Should |

### 2.5 Ciclo di vita

| ID | Requisito | Priorità |
|---|---|---|
| RF-C-28 | **Lascia stanza** chiede **conferma esplicita** prima di procedere; confermato, invia `leave` e riporta alla schermata di selezione stanza, mantenendo la connessione. | Must |
| RF-C-29 | Alla chiusura della finestra (`Window.Closing`), se il client è in una stanza attiva intercetta l'evento e richiede **conferma modale** (*"Vuoi davvero uscire? La conversazione andrà persa definitivamente"*). Se confermato, si disconnette in modo pulito. | Must |
| RF-C-30 | Se il server cade, il client lo segnala, scarta i messaggi e torna alla schermata di connessione senza chiudersi. | Must |
| RF-C-30b | In caso di disconnessione brusca della rete, il client avvisa che un rientro immediato potrebbe scontrarsi con la sessione fantasma sul server (~60 s), ricevendo un nickname numerato e richiedendo nuova ammissione. | Should |
| RF-C-31 | Il client non scrive mai i messaggi su disco. | Must |
| RF-C-32 | Riconnessione automatica dopo caduta della rete. | Could |

## 3. Requisiti non funzionali

| ID | Categoria | Requisito |
|---|---|---|
| RNF-01 | Prestazioni | Latenza di consegna in LAN < 100 ms dal `send` alla visualizzazione sugli altri client. |
| RNF-02 | Prestazioni | Il client resta reattivo durante tutte le operazioni di rete. |
| RNF-03 | Scalabilità | Il server regge almeno **50 stanze da 20 utenti** (1000 connessioni) su hardware desktop. |
| RNF-04 | Affidabilità | Un client che si comporta male non deve degradare le altre stanze. |
| RNF-05 | Privacy | Nessun dato personale scritto su disco, né lato server né lato client. |
| RNF-06 | Sicurezza | Ogni input dal client è considerato non fidato e validato prima dell'uso, **anche quello già validato dal client**. |
| RNF-07 | Portabilità | Client compilabile su Windows, Linux e macOS dallo stesso codice Avalonia. |
| RNF-08 | Manutenibilità | Il contratto di protocollo vive in `Krypta.Shared`, usato da server e client: un solo punto di verità. |
| RNF-09 | Osservabilità | Log strutturati sul server con livello configurabile, default `Information`. |
| RNF-10 | Accessibilità | Navigazione completa da tastiera; contrasto dei colori dei nickname conforme a WCAG AA. |

## 4. Piattaforme supportate

| Piattaforma | MVP | Note |
|---|---|---|
| Windows 10/11 (x64) | ✅ | Piattaforma di sviluppo primaria |
| Linux (x64) | ✅ | Stesso binario Avalonia, da verificare |
| macOS | ⚠️ | Supportato da Avalonia, non testato nell'MVP |
| Android / iOS | ❌ | Fuori scope per scelta |
| Web | ❌ | Fuori scope |

## 5. Vincoli

- **Runtime: .NET 10 (LTS)**, supportato fino a novembre 2028. Nulla di quanto specificato
  richiede una versione piuttosto che un'altra: la scelta è di **finestra di supporto**, non
  di funzionalità, e copre le fasi 1, 2 e 3 senza migrazioni in mezzo
  ([ADR-0009](adr/0009-runtime-dotnet-10.md)).
- **Esecuzione locale.** Server e client sulla stessa macchina o LAN. Nessuna esposizione
  su Internet e nessun DNS nell'MVP.
- **TLS sì, CA no.** Il certificato del server è **autofirmato**, generato da uno script, e
  il client lo accetta con TOFU (RF-X-02). Non serve un'autorità di certificazione, ma
  **il certificato serve**: dalla fase 1B l'endpoint è `wss://` e il `ws://` in chiaro
  resta ammesso solo verso `127.0.0.1` in sviluppo
  ([ADR-0008](adr/0008-cifratura-e2e-chiave-all-approvazione.md)).
- **Nessuna infrastruttura esterna.** Nessun database, broker o servizio cloud. Il server
  è un singolo processo con stato solo in memoria.
- **Nessuna dipendenza da account di terze parti.**
