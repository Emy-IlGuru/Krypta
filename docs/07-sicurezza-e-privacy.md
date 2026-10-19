# 07 — Sicurezza e privacy

**Stato:** bozza · **Aggiornato:** 2026-09-08

> Documento centrale del progetto. La privacy in Krypta non è una funzionalità: è
> l'architettura.

## 1. Stato della cifratura

> **Cambiato il 2026-09-07.** La cifratura era rinviata alle fasi 2 e 3. Ora **TLS ed
> E2EE sono entrambi obbligatori e dentro la fase 1**: nessun uso reale è ammesso prima
> che siano completi. Vedi [ADR-0008](adr/0008-cifratura-e2e-chiave-all-approvazione.md).

Krypta protegge su due piani, che coprono avversari diversi e non si sostituiscono:

| Livello | Protegge | Da chi |
|---|---|---|
| **TLS** (`wss://`) | Tutto il traffico, metadati compresi | Chi osserva la rete |
| **E2EE** | Il **contenuto** dei messaggi | Chi gestisce il server |

**Avvertenza valida finché la fase 1B non è completa.** Esistono soltanto le fondamenta
e il contratto condiviso: la cifratura non è ancora implementata, quindi Krypta non
protegge da nessuno dei due avversari. L'avvertenza va mostrata **nella UI**, non solo
qui, e va rimossa da entrambe solo a implementazione fatta e verificata.

E anche a lavoro finito, una cosa resta vera e va detta senza giri di parole:
**l'E2EE copre i contenuti, non i metadati.** Nickname, ID stanza, ingressi, uscite e
messaggi di sistema restano visibili al gestore del server, perché è il server a
generarli. Vedi [03 §7bis.6](03-protocollo.md) per la tabella completa.

## 2. Cosa protegge Krypta, per costruzione

Queste garanzie derivano dall'architettura, non da configurazioni che si possono
sbagliare:

| # | Garanzia | Perché è vera |
|---|---|---|
| G1 | **Non esiste un archivio da compromettere.** | Il server non scrive i messaggi da nessuna parte: nessun database da esfiltrare, nessun backup da rubare. I log operativi esistono e sopravvivono, ma per costruzione non contengono messaggi, nickname accanto a IP, né elenchi di partecipanti ([04 §6](04-api-server.md)) — il che li rende poco utili a ricostruire una conversazione, non inesistenti. |
| G2 | **Chi entra dopo non può leggere il prima.** | Il server non ha copia dei messaggi passati: anche volendo, non potrebbe inviarli. |
| G3 | **Nessuna identità persistente.** | Nessun numero di telefono, email, password o ID dispositivo. Il nickname esiste per la durata della connessione. |
| G4 | **Nessuna rubrica letta.** | Il client desktop non accede a contatti, telefono o social. |
| G5 | **Nessuna directory delle stanze.** | Il server non espone quali stanze esistono. Chi non ha l'ID non sa nemmeno che una conversazione è in corso. |
| G6 | **L'elenco dei partecipanti non esiste come dato.** | Ingressi e uscite sono annunciati e `/Users` elenca i presenti: **dentro la stanza si sa chi c'è.** Ma quell'elenco viene costruito al momento della richiesta, non è memorizzato, non è esportabile e sparisce con la stanza. Ed è simmetrico: chi lo chiede lo fa davanti a tutti, nessuno può controllare chi c'è di nascosto. |
| G7 | **Nessun operatore centrale.** | L'utente sceglie il server. Non esiste un'infrastruttura che veda il traffico di tutti gli utenti. |
| G8 | **La conversazione muore con la stanza.** | Uscito l'ultimo partecipante, nessuna struttura del server fa più riferimento alla stanza, al suo nome o ai suoi partecipanti: non c'è niente da leggere e niente da consegnare. La memoria vera e propria la libera il garbage collector quando gli pare — è una precisazione, non una scappatoia, ma va scritta invece di promettere un azzeramento istantaneo che il runtime non garantisce. |
| G9 | **Il server non può leggere i messaggi *della stanza*.** | La chiave di stanza è generata da un client e consegnata da un client. Il server inoltra involucri cifrati che non sa aprire ([03 §7bis](03-protocollo.md)). **Il messaggio di richiesta d'ingresso è fuori da questa garanzia** e lo legge: chi lo scrive non ha ancora la chiave, ed è l'unico contenuto d'utente in chiaro nel sistema ([ADR-0010](adr/0010-messaggio-di-richiesta.md)). |
| G10 | **Il server non può falsificare l'autore di un messaggio.** | Il nickname del mittente è legato crittograficamente al ciphertext tramite l'AAD: riattribuire un messaggio fa fallire la decifratura. |
| G11 | **Il server non può ammettere nessuno da solo.** | Ogni ammissione richiede una `wrappedKey`, cioè una chiave che il server non possiede. Un `approve` senza di essa è rifiutato. |

## 3. Modello di minaccia

### 3.1 Da chi proteggiamo, e quanto

| Avversario | Protetti? | Note |
|---|---|---|
| Chi ottiene accesso al server **dopo** la conversazione | ✅ Sì | Non c'è niente da trovare: né messaggi né elenco di chi c'era. L'elenco di `/Users` esiste solo nell'istante in cui viene chiesto |
| Chi chiede i dati con un ordine giudiziario | ✅ Sì | Non esistono dati da consegnare. Il server non può produrre ciò che non ha mai scritto |
| Chi ruba il PC di un partecipante a conversazione finita | ✅ In gran parte | L'applicazione non scrive niente su disco, a parte le impronte TLS ([05 §9](05-client-desktop.md)). Ma **il sistema operativo può**: file di swap, ibernazione, dump di crash possono contenere pagine di memoria con testo in chiaro o chiavi, e Krypta non ha modo di impedirlo. Chi ha bisogno di questa garanzia deve cifrare il disco — è la stessa raccomandazione di qualsiasi app di messaggistica |
| Chi cerca stanze provando ID a caso | ✅ Sì | **La sala d'attesa**: conoscere l'ID non basta per entrare. Più rate limiting e spazio ampio. Vedi §4 |
| Chi conosce l'ID ma non è invitato | ✅ In gran parte | Deve farsi accettare da un partecipante, e la richiesta è visibile a tutti. **Non è però autenticazione**: vedi §4.3 |
| Chi osserva il traffico di rete | ✅ Sì (a fase 1B fatta) | TLS copre tutto, contenuti e metadati |
| L'amministratore del server — **contenuti** | ✅ Sì (a fase 1B fatta) | E2EE: la chiave non passa mai dal server. **Unica eccezione: il messaggio di richiesta d'ingresso**, che è in chiaro perché chi lo scrive non ha ancora la chiave ([03 §4.1.3](03-protocollo.md)) |
| L'amministratore del server — **metadati** | ❌ No | Nickname, ID stanza, ingressi/uscite, orari e dimensioni restano visibili. Vedi §5 |
| Un partecipante della stanza | ❌ No | Chi è dentro legge tutto, vede chi c'è (`/Users`), e può copiare o fotografare. `/ClearRoom` non lo impedisce: vedi §4bis.1 |
| **Un ex partecipante, dopo un `/GenerateKey`** | ✅ Sì | La chiave nuova non è derivabile dalla vecchia e il suo involucro non esiste ([ADR-0011](adr/0011-rotazione-chiave-di-stanza.md)). Ma la rotazione è **manuale**: senza, resta ❌ |
| Malware sul PC di un partecipante | ❌ No | Fuori dal modello di minaccia di qualsiasi app di messaggistica |

### 3.2 Cosa esplicitamente NON copriamo

- **Sicurezza dell'endpoint.** Se il PC è compromesso, nessuna app può fare niente.
- **Un partecipante malevolo.** Non esiste modo tecnico di impedire a chi legge un
  messaggio di conservarlo.
- **Anonimato *dentro* la stanza.** Ingressi, uscite e `/Users` rendono noto chi c'è.
  È una scelta di prodotto: Krypta protegge dall'archivio, non dagli altri partecipanti.
  Chi vuole restare non identificabile deve scegliere un nickname che non lo identifichi.
- **Uso improprio dei comandi.** Non essendoci ruoli, qualunque partecipante può
  rinominare la stanza o svuotare la chat di tutti. Vedi §4.2.
- **Analisi del traffico.** Anche cifrando i contenuti, chi osserva la rete vede *che*
  qualcuno parla con quel server, quando e quanto.
- **Anonimato a livello di rete.** Il server vede l'indirizzo IP dei client. Krypta non
  è Tor e non lo diventerà.

## 4. Il problema dello spazio degli ID

> **Riscritta il 2026-09-07.** L'introduzione della sala d'attesa cambia la natura del
> problema, e la versione precedente di questa sezione conteneva **due errori di calcolo**
> corretti qui sotto.

### 4.0 Le tre difese, in ordine di efficacia

| # | Difesa | Contro cosa | Peso reale |
|---|---|---|---|
| 1 | **Sala d'attesa** | Conoscere l'ID non basta più per entrare | **Decisiva** |
| 2 | **Rate limiting sui `join` per IP** | Rende impraticabile la scansione | Alto |
| 3 | Ampiezza dello spazio (36⁶) | Rende impraticabile l'indovinare a caso | Modesto da solo |

L'ordine conta, perché è l'opposto di quello che diceva la versione precedente di questo
documento.

### 4.1 Il calcolo, fatto bene

L'ID di 6 caratteri alfanumerici maiuscoli dà **36⁶ = 2.176.782.336** combinazioni.

Il rate limiting consente al massimo 5 tentativi ogni 180 secondi per IP, cioè **2.400 al
giorno** se si resta esattamente sotto soglia (1.440 se si fa scattare ogni volta il blocco
da 2 minuti).

**Ma il tempo per provare *tutti* gli ID non è la metrica giusta.** Quello che conta è
quanti tentativi servono per trovare **una qualsiasi** stanza attiva, cioè
`spazio ÷ stanze attive`. Con 50 stanze aperte:

| Scenario | Tentativi attesi | Tempo |
|---|---|---|
| Senza rate limiting, a 1.000 tentativi/s | ~43,5 milioni | **~12 ore** |
| Con rate limiting, 2.400/giorno da un IP | ~43,5 milioni | **~50 anni** |

È questo il confronto che dice qualcosa. **Il rate limiting fa quasi tutto il lavoro**;
l'ampiezza dello spazio, da sola, non protegge granché — senza limite, un server con
qualche decina di stanze attive si mappa in una notte.

> **Correzione.** La versione precedente diceva "1.200 tentativi al giorno" e "cinque
> milioni di anni": il primo numero era sbagliato, il secondo lo era di circa tre ordini
> di grandezza (sarebbero stati ~2.500 anni per l'enumerazione completa). Soprattutto,
> misurava la cosa sbagliata — il tempo per esaurire lo spazio invece del tempo per
> trovare una stanza. Le conclusioni che ne traeva erano di conseguenza troppo ottimistiche.

**E resta vero che un ID scelto da una persona non è casuale.** `RIUNIO`, `TEST01`,
`AAAAAA`, `123456` cadono con un dizionario di poche migliaia di tentativi, qualunque sia
l'ampiezza dello spazio teorico. Va detto nella UI accanto al campo dell'ID.

### 4.2 Perché la sala d'attesa cambia tutto

Prima, chi indovinava un ID **era dentro**: leggeva la conversazione, e nessuno poteva
impedirlo o accorgersene, se restava in silenzio.

Adesso chi indovina un ID ottiene questo:

- finisce in sala d'attesa, dove **non riceve niente** della stanza — né messaggi, né nome,
  né numero di partecipanti;
- il suo nickname compare a tutti i partecipanti, con una riga nella conversazione;
- resta fuori finché qualcuno non lo accetta, e scade da solo dopo 2 minuti.

**L'attacco silenzioso diventa una richiesta visibile e rifiutabile.** È un cambio di
categoria, non un miglioramento graduale: la scansione degli ID non produce più accessi,
produce al massimo fastidio.

Conseguenza sulle mitigazioni che erano in programma: la **password di stanza** perde gran
parte della sua ragione d'essere. Serviva a impedire che conoscere l'ID bastasse per
entrare — che è esattamente ciò che ora fa l'approvazione, senza un secondo segreto da
comunicare. La toglierei dai piani finché non emerge un motivo diverso per volerla.

Il **generatore di ID casuali** invece resta utile: protegge dagli ID prevedibili, e un
ID casuale evita anche di finire nella sala d'attesa di sconosciuti per pura collisione.

### 4.3 Cosa la sala d'attesa NON protegge

Tre limiti da tenere presenti, tutti reali:

1. **Chi approva vede un nickname e un messaggio che il richiedente ha scritto da sé.**
   Non c'è modo di verificare né l'uno né l'altro: chi conosce l'ID può presentarsi con il
   nome giusto e scrivere la frase giusta, e scriverla non gli costa niente. La barriera
   passa da "chi indovina l'ID entra" a "chi indovina l'ID deve convincere qualcuno": è
   molto, ma **non è autenticazione**, e non va descritta come tale.

   **Sul messaggio di richiesta ([ADR-0010](adr/0010-messaggio-di-richiesta.md)) va detta
   una cosa scomoda:** non riduce questo rischio, e plausibilmente lo aumenta. Aggiunge a
   chi decide la *sensazione* di sapere con chi ha a che fare, senza dargli un solo
   elemento verificabile in più — ed è il meccanismo su cui si regge qualunque inganno.
   Il beneficio è di coordinamento fra persone che si aspettano (distinguere il Marco
   atteso dall'omonimo), non di sicurezza. La UI deve dirlo esplicitamente, ed è il motivo
   del requisito RF-C-A14.

   L'unica cosa che verifica davvero qualcosa resta il **codice di verifica** (§5), che il
   messaggio non sostituisce e con cui non va confuso: uno è testo scelto da chi bussa,
   l'altro è derivato dalla sua chiave.

2. **Il primo che arriva entra senza approvazione.** È inevitabile — non c'è nessuno
   dentro che possa approvarlo — ma ha una conseguenza pratica: chi vuole una stanza
   protetta deve **crearla per primo**. Se un estraneo occupa quell'ID prima, sarà lui a
   decidere chi entra.

3. **La sala d'attesa è un nuovo bersaglio.** Bussare ripetutamente genera righe nella
   conversazione e notifiche a tutti. Mitigato dal tetto di 10 richieste e dal limite di 5
   join per 3 minuti per IP, ma non azzerato: chi ha molti IP può disturbare. Il pannello
   che **non si apre da solo** ([05 §5.1bis](05-client-desktop.md)) è parte della
   mitigazione.

   Il **messaggio di richiesta** rende il disturbo più espressivo — non più solo un
   nickname che compare, ma una frase scelta apposta. È la ragione per cui non entra nel
   flusso della chat: resta dentro un pannello che si apre solo se qualcuno decide di
   aprirlo, quindi il volume del fastidio non cambia e il suo contenuto raggiunge soltanto
   chi ha scelto di guardarlo. Se dall'uso reale emergesse che non basta, la mossa
   successiva è mostrare la sola riga *"ha lasciato un messaggio"* e il testo dietro un
   clic ([ADR-0010](adr/0010-messaggio-di-richiesta.md)).

Restano disponibili, non adottate ora:

| Mitigazione | Perché non ora |
|---|---|
| ~~Password di stanza~~ | **Rimossa dai piani.** Serviva a impedire che conoscere l'ID bastasse per entrare: è esattamente ciò che ora fa la sala d'attesa (§4.2), senza un secondo segreto da comunicare |
| **Generatore di ID casuali** nel client | Un pulsante "genera" con `RandomNumberGenerator` toglierebbe di mezzo il problema degli ID scelti a mano. Costa poco: buon candidato appena l'MVP funziona |
| Allungare l'ID oltre i 6 caratteri | Non serve più: 36⁶ è abbondante. E 6 caratteri restano dettabili al telefono |
| Nascondere il flag `created` | Inefficace: la stanza esistente si deduce comunque dal contatore partecipanti |

---

## 4bis. Due cose che sembrano protezioni e non lo sono

> Queste due sezioni erano numerate **§4.1 e §4.2**, cioè come sottosezioni del problema
> dello spazio degli ID, che ne conteneva già altre due con lo stesso numero. Non c'entrano
> con l'enumerazione degli ID: stanno insieme perché sono le due funzionalità che un utente
> può scambiare per una difesa.

### 4bis.1 `/ClearRoom` non è una misura di sicurezza

Il comando fa sparire i messaggi **dallo schermo** dei partecipanti. Non cancella niente
sul server, perché il server non ha mai conservato niente
([ADR-0002](adr/0002-nessuna-persistenza.md)).

Quindi:

- un client modificato può **ignorare** il messaggio `clear` e continuare a mostrare tutto;
- chi ha già letto, copiato o fotografato conserva quello che ha preso;
- non c'è modo di verificare che gli altri abbiano davvero svuotato.

È una comodità di lettura — ripulire una conversazione lunga — e va presentata come tale
nella UI. Descriverla come "cancellazione sicura" sarebbe una promessa falsa.

### 4bis.2 Nessun ruolo significa nessun controllo sui comandi

Qualunque partecipante può rinominare la stanza o svuotare la chat di tutti. È la
conseguenza diretta di non avere proprietari: una stanza effimera senza account non ha
nessuno a cui attribuire il possesso.

Il contrappeso scelto è la **tracciabilità, non il permesso**: `/NameRoom` e `/ClearRoom`
generano un messaggio di sistema che dice **chi** li ha eseguiti. In un gruppo ristretto —
il caso d'uso previsto — sapere chi è stato è sufficiente. In una stanza con estranei
non lo è, ed è uno dei motivi per cui l'ID non va diffuso a chi non si conosce.

Se dall'uso reale emergesse che serve un controllo, la strada meno invasiva è dare i
comandi che modificano lo stato al **primo entrato ancora presente**, senza introdurre
account: un proprietario implicito che decade quando esce. Non è nell'MVP.

## 5. Quanto bisogna ancora fidarsi del server

Prima dell'E2EE la risposta era: **completamente**. Il server leggeva ogni messaggio, e
le garanzie G1–G8 erano promesse del codice, non fatti matematici: un amministratore
poteva modificarlo e registrare tutto senza che nessun client se ne accorgesse.

Con la cifratura la situazione si divide in due:

| | Serve fidarsi del server? |
|---|---|
| **Contenuto dei messaggi** | **No.** La chiave non passa mai dal server (G9). Anche un server modificato per registrare tutto archivierebbe ciphertext inutili |
| **Chi ha scritto cosa** | **No.** L'AAD lega mittente e ciphertext (G10): riattribuire un messaggio fa fallire la decifratura |
| **Chi entra nella stanza** | **No.** Ogni ammissione richiede una chiave che il server non ha (G11) |
| **Metadati** — nickname, ID stanza, ingressi, uscite, orari, dimensioni | **Sì.** Restano in chiaro per costruzione |
| **Disponibilità** | **Sì.** Il server può sempre rifiutare connessioni, scartare o riordinare messaggi |
| **Non essere in mezzo alle chiavi** | **Sì, salvo verifica.** Vedi sotto |

### L'unico punto in cui il server può ancora leggere i contenuti

Un server malevolo può sostituire la chiave pubblica del richiedente nel messaggio
`waiting`: chi approva cifrerebbe la chiave di stanza per il server, che la leggerebbe e
la reinoltrerebbe al vero destinatario. Tutto funzionerebbe, e il server leggerebbe tutto.

**Nessun controllo automatico può rilevarlo** — nemmeno un'impronta della chiave di
stanza, che in quell'attacco coinciderebbe per tutti. L'unico rimedio è il **codice di
verifica** ([03 §7bis.7](03-protocollo.md)): 16 cifre derivate dalla chiave pubblica,
mostrate al richiedente e a chi approva, da confrontare a voce.

È facoltativo e quasi nessuno lo farà. Va comunque reso disponibile e spiegato, perché è
la differenza fra "mi fido del mio server" e "non ho bisogno di fidarmi di nessun server".

Restano poi le due difese non crittografiche, entrambe valide:

- **Self-hosting** (già previsto): il server è già tuo.
- **Codice sorgente pubblico e build riproducibili**: permettono di verificare che il
  server in esecuzione sia quello pubblicato. Da valutare.

## 6. Dettaglio della cifratura

La specifica normativa è in [03 §7bis](03-protocollo.md); qui sta il ragionamento.

### 6.1 Perché servono entrambi i livelli

| | Chi osserva la rete | Chi gestisce il server |
|---|---|---|
| Solo TLS | protetto | **legge tutto** |
| Solo E2EE | **legge i metadati** | protetto sui contenuti |
| **TLS + E2EE** | protetto | vede solo i metadati |

L'E2EE non nasconde i metadati perché **i messaggi di sistema li genera il server**, e un
server senza chiave non può produrre testo cifrato. Cifrare anche i nickname vorrebbe dire
rinunciare alla sala d'attesa, a `/Users` e agli annunci di ingresso: funzionalità decise
apposta ([ADR-0006](adr/0006-partecipanti-visibili-nella-stanza.md),
[ADR-0007](adr/0007-accesso-su-approvazione.md)).

### 6.2 Perché la chiave viaggia con l'approvazione

Il modello alternativo era una **passphrase di stanza** condivisa fuori banda insieme
all'ID. Funziona, ma introduce un secondo segreto da concordare e ricordare — esattamente
ciò che si era evitato scegliendo l'approvazione al posto della password di stanza.

Consegnare la chiave **nel momento dell'approvazione** non aggiunge nessun passaggio per
l'utente: chi accetta un nuovo arrivato sta già compiendo il gesto che autorizza la
lettura. La sala d'attesa rende possibile un E2EE che senza di essa sarebbe stato scomodo.

### 6.3 Cosa NON dà questa cifratura

Da non tacere in nessuna descrizione del prodotto:

1. **Nessuna forward secrecy.** La chiave vive quanto la stanza; chi la ottiene e ha
   registrato il traffico legge tutta la conversazione. Attenuato dal fatto che non esiste
   archivio da decifrare a posteriori — ma è una rinuncia, non un non-problema.
2. **Chi esce conserva la chiave, finché nessuno rigenera.** Con `/GenerateKey`
   ([ADR-0011](adr/0011-rotazione-chiave-di-stanza.md)) un partecipante taglia fuori chi è
   già uscito, in modo definitivo: la chiave nuova non è derivabile dalla vecchia. Ma la
   rotazione è **manuale**, quindi la protezione vale solo se qualcuno si ricorda di
   usarla. Il limite non è chiuso: è passato da strutturale a dipendente da un gesto.
3. **Qualunque partecipante può cedere la chiave.** Irrisolvibile: chi legge può copiare.
   E dopo una rotazione può cedere anche quella nuova: `/GenerateKey` protegge da chi è
   uscito, non da chi è dentro.
3b. **Ogni rotazione riapre la finestra MITM.** L'esclusione di chi è uscito vale se le
   chiavi pubbliche del `roster` sono autentiche; un server ostile può sostituirle, come
   all'ingresso (§5). Prima quella finestra si apriva **una volta per partecipante**, ora
   si riapre **a ogni `/GenerateKey`**. Con un server proprio non cambia niente; con un
   server altrui, rigenerare spesso peggiora l'esposizione invece di migliorarla.
4. **Chi crea la stanza genera la chiave.** Un suo client compromesso compromette tutto.
5. **I metadati restano visibili al server** (§6.1).
6. **La difesa dal replay ha una finestra.** Il contatore `seq` è indicizzato per
   *nickname*, e i nickname si liberano quando un partecipante esce. Nell'intervallo fra
   l'uscita di `luca` e l'ingresso di un nuovo `luca`, il server può rigiocare una volta i
   vecchi messaggi di `luca` fingendo un rientro ([03 §7bis.5](03-protocollo.md)).
   L'alternativa — legare il contatore alla chiave pubblica invece che al nome — non è
   applicabile: chi entra a metà conversazione non ha mai visto le chiavi di chi era già
   dentro. È il prezzo dell'assenza di identità persistenti, ed è lo stesso motivo per cui
   il punto 1 non è risolvibile.

Un protocollo a ratchet (Double Ratchet, MLS) risolverebbe i punti 1, 2 e 6, ma richiede
identità persistenti per dispositivo: l'opposto di questo prodotto. Non è in programma.

## 7. Dati trattati

| Dato | Dove | Per quanto | Su disco? |
|---|---|---|---|
| Nickname | RAM del server + RAM dei client | Durata della connessione | ❌ Solo se l'utente spunta "Ricorda" (solo il proprio, solo nel client) |
| Testo del messaggio | **Mai in chiaro sul server**: transita solo cifrato | Millisecondi | ❌ Mai |
| Testo del messaggio | RAM del client, in chiaro | Finché la finestra di chat è aperta | ❌ Mai |
| Chiave di stanza | RAM dei soli client | Durata della sessione | ❌ Mai — **il server non la possiede in nessun momento** |
| Chiavi di sessione dei client | RAM del client; la sola pubblica transita dal server | Durata della connessione | ❌ Mai |
| Chiave **pubblica** di sessione, lato server | RAM del server, associata al partecipante | Durata della permanenza nella stanza | ❌ Mai — è materiale pubblico, e serve a distribuire il `roster` con cui i client possono rigenerare la chiave ([ADR-0011](adr/0011-rotazione-chiave-di-stanza.md)) |
| Impronta TLS del server | `krypta.config.xml` | Fino a cambio certificato | ✅ Sì — nuova scrittura persistente introdotta dal TOFU ([06](06-configurazione.md)) |
| ID stanza | RAM del server | Durata della stanza | ❌ Mai (solo l'ID, non il contenuto, compare nei log operativi) |
| Nome della stanza | RAM del server | Durata della stanza | ❌ Mai — **anche l'argomento di `/NameRoom` è escluso dai log**: un nome scelto è contenuto, non metadato |
| Elenco partecipanti (`/Users`) | Costruito al momento, mai memorizzato | Millisecondi | ❌ Mai |
| Richieste in sala d'attesa | RAM del server: nickname richiesto, messaggio di richiesta, connessione | Max 120 secondi, poi scadono | ❌ Mai — **nemmeno i rifiuti**: non esiste una lista di chi ha provato a entrare |
| Messaggio di richiesta (`joinMessage`) | RAM del server **in chiaro**, e RAM dei client | Quanto la richiesta: max 120 s | ❌ Mai — ed è **escluso dai log** come l'argomento di `/NameRoom`: è contenuto scritto da un utente ([04 §6](04-api-server.md)) |
| Indirizzo IP del client | Stack TCP + log a livello Debug | Durata della connessione | ⚠️ Solo se il log è a `Debug` e scritto su file |
| Host/porta/nickname | `krypta.config.xml` | Fino a modifica | ✅ Solo su richiesta esplicita dell'utente |

**Nota operativa:** il livello di log di default in produzione deve essere
`Information`, non `Debug`, proprio per non scrivere gli IP dei client. E per la regola
di [04 §6](04-api-server.md), IP e nickname non vanno mai loggati nella stessa riga:
insieme costituirebbero un identificatore. Non basta però separarli — il `ConnectionId`
li ricuce su due righe diverse, ed è il motivo di [04 §6.1](04-api-server.md).

**Il limite della colonna "Su disco?".** Dice cosa scrive *l'applicazione*, non cosa
finisce sul disco. Il sistema operativo può paginare su swap, ibernare la RAM o scrivere
un dump di crash, e in quelle pagine ci sono testo in chiaro e chiavi. Krypta non può
impedirlo — nessuna applicazione utente può — e il rimedio è la cifratura del disco, non
qualcosa che si possa aggiungere qui. Va detto perché la tabella, letta di corsa, promette
più di quanto qualsiasi programma sia in grado di mantenere.

## 8. Conformità normativa

Con l'architettura attuale, per un uso locale o su LAN, l'esposizione è minima ma non
nulla.

| Aspetto | Situazione |
|---|---|
| **GDPR — dati personali** | Nickname e IP sono dati personali. Ma non sono conservati (salvo log), quindi non c'è un trattamento continuativo da documentare. |
| **GDPR — titolare** | Chi ospita il server è titolare del trattamento. In self-hosting per uso personale si applica l'esenzione per attività domestiche. Se il server è offerto a terzi, servono informativa e base giuridica. |
| **GDPR — diritto all'oblio** | Banale da soddisfare: non c'è niente da cancellare. |
| **Data retention** | Nessuna. Da documentare esplicitamente se il servizio viene offerto pubblicamente. |
| **Digital Services Act** | Si applica ai servizi di intermediazione online. Un server privato per un gruppo ristretto non rientra; un server pubblico va valutato. |
| **Store desktop** | Non pertinente nell'MVP: la distribuzione è diretta, non via store. |

**Attenzione all'assenza di moderazione.** Un server Krypta pubblico non ha modo di
moderare: non c'è storico da ispezionare, non c'è identità da bannare. È una conseguenza
diretta del design. Finché il server è privato non è un problema; se il progetto evolvesse
verso un servizio aperto al pubblico, diventerebbe la questione più seria da affrontare,
prima di quelle tecniche.

## 9. Regole vincolanti per l'implementazione

Da rispettare in ogni riga di codice, senza eccezioni "temporanee":

1. Il contenuto di un messaggio non viene **mai** scritto su disco, in nessun formato,
   nemmeno in un log di debug, nemmeno in sviluppo.
2. IP e nickname non compaiono **mai** nella stessa riga di log.
3. Nessun endpoint HTTP espone l'elenco delle stanze o dei partecipanti. Nemmeno
   `/health`. L'elenco dei partecipanti si ottiene **solo** con `/Users` da dentro la
   stanza, ed è visibile a tutti i presenti.
4. Nessuna eccezione non gestita raggiunge il client con uno stack trace.
5. Ogni input dal client è validato prima dell'uso: lunghezza, formato, stato.
   **Anche quello che il client ha già validato**: un client modificato invia ciò che
   vuole.
6. Il nickname del mittente lo mette **il server**, dalla connessione — mai il client
   dal payload. Vale per i messaggi e per il testo dei `system` generati dai comandi.
   Altrimenti chiunque può firmarsi con il nome di un altro.
7. I nomi riservati (`STANZA`, `SYSTEM`, …) non sono utilizzabili come nickname. Senza
   quel divieto, un utente può fabbricare falsi messaggi di sistema.
8. `/ClearRoom` non viene mai descritto come cancellazione: non lo è (§4.1).
9. Se una funzionalità richiede di conservare qualcosa, si discute prima come modifica
   alla visione, non la si aggiunge "temporaneamente".
