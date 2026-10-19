# ADR-0010: Messaggio allegato alla richiesta d'ingresso

**Data:** 2026-09-08
**Stato:** accettata — estende [ADR-0007](0007-accesso-su-approvazione.md)

## Contesto

La sala d'attesa ([ADR-0007](0007-accesso-su-approvazione.md)) fa decidere a un
partecipante se ammettere uno sconosciuto. Ma il limite scritto in
[07 §4.3](../07-sicurezza-e-privacy.md) è netto:

> Chi approva vede **un nickname e nient'altro**. Non c'è modo di verificare che dietro
> `marco` ci sia il Marco che aspetti.

In pratica succede questo: arriva `marco`, e chi sta dentro non sa se è il Marco atteso,
un omonimo, o qualcuno che ha indovinato l'ID. Il risultato prevedibile è che si accetti
per inerzia — l'attrito d'ingresso è già il costo dichiarato di ADR-0007, e restare fermi
a fissare un nickname lo peggiora.

Un messaggio allegato alla richiesta dà a chi bussa il modo di dire perché. *"Sono Marco,
quello del preventivo"* è informazione che chi approva non ha nessun altro modo di
ottenere, visto che con chi è in attesa non esiste nessun canale.

## Opzioni valutate

**A. Lasciare com'è.** Zero superficie in più. Chi bussa resta muto e chi approva decide su
un nickname, che è il problema di partenza.

**B. Messaggio mostrato solo nel pannello della sala d'attesa.** Chi approva lo legge
quando apre il pannello — cioè per scelta, dato che il pannello non si apre da solo
(RF-C-A07).

**C. Messaggio mostrato anche nel flusso della chat**, dentro il `system` `join_requested`.
Più visibile, nessuno se lo perde.

## Decisione

**Opzione B.** Campo `joinMessage` facoltativo nel `join`, inoltrato nelle voci di
`waiting`, mostrato **solo** nel pannello della sala d'attesa.

### Perché non C, che è la parte importante

Perché C regala a chiunque conosca un ID un **canale di testo verso una stanza in cui non
è entrato**. Il `system` `join_requested` finisce nella conversazione di tutti i
partecipanti: con C, bussare diventa il modo di scrivergli senza essere ammessi. Il tetto
di 10 richieste per stanza e il limite di 5 join in 3 minuti per IP contengono il volume,
non la natura della cosa — e chi dispone di più IP non è contenuto affatto
([07 §4.3](../07-sicurezza-e-privacy.md) punto 3).

L'asimmetria che rende B accettabile è la stessa già decisa per il pannello: **leggere il
messaggio è un atto volontario.** Il segnale passivo resta quello di prima — il pulsante
*Attesa (N)* che compare e la riga `join_requested` nel flusso, che continua a **non**
contenere testo scelto dal richiedente. Chi non vuole leggere non legge, e chi bussa non
può costringere nessuno.

È la stessa ragione per cui RF-C-A07 vieta al pannello di aprirsi da solo. C la
smentirebbe: non serve aprire un pannello a forza, se il testo arriva comunque in chat.

### Il messaggio non è cifrato, e non può esserlo

Chi è in attesa **non ha ancora la chiave di stanza**: la riceve nel momento in cui viene
approvato, ed è tutto l'impianto di [ADR-0008](0008-cifratura-e2e-chiave-all-approvazione.md).
Un messaggio che deve essere leggibile *prima* dell'approvazione viaggia quindi in chiaro,
e il gestore del server lo legge.

Non è aggirabile e non va nascosto: entra nella tabella di
[03 §7bis.6](../03-protocollo.md) fra le cose che l'E2EE non copre, ed è l'unico contenuto
**scritto da un utente** in quella tabella — tutto il resto sono metadati generati dal
server. Va detto anche nella UI, accanto al campo.

### Il messaggio non autentica nessuno

Va scritto perché è controintuitivo: **il messaggio non riduce il rischio di ADR-0007, e
può aumentarlo.** Chi conosce l'ID e vuole entrare scriverà esattamente la cosa giusta —
scriverla è gratis. Il risultato è che chi approva prende la stessa decisione di prima
sentendosi più informato, che è il modo in cui funziona l'inganno.

Il beneficio è reale ma è di **coordinamento fra persone che si aspettano**, non di
sicurezza: serve a distinguere il Marco atteso dall'omonimo capitato lì, non l'amico
dall'estraneo. La formulazione in UI deve riflettere questo, e
[07 §4.3](../07-sicurezza-e-privacy.md) punto 1 va aggiornato invece che lasciato dire che
si vede "un nickname e nient'altro".

L'unica cosa che verifica davvero qualcosa resta il **codice di verifica**
([03 §7bis.7](../03-protocollo.md)), che il messaggio non sostituisce.

### Vincoli

| Regola | Valore | Motivo |
|---|---|---|
| Facoltativo | sì | Una richiesta senza messaggio resta legittima |
| Lunghezza | **200 caratteri** | Sta in una riga del pannello. Il limite dei messaggi veri (2000) qui non c'entra: quello è testo cifrato in una conversazione, questo è testo in chiaro da uno sconosciuto |
| Forma | riga singola, senza caratteri di controllo | Va reso in una riga di pannello accanto a due pulsanti |
| Testo predefinito | *"Hey, mi vorrei unire alla stanza"* | **Lato client**, non nel protocollo: si cambia o si traduce senza toccare il contratto |
| Conservazione | nessuna | Vive con la `JoinRequest`, max 120 s, e sparisce con essa (RF-S-A16) |
| Log | mai | È contenuto scritto da un utente, come l'argomento di `/NameRoom` ([04 §6](../04-api-server.md)) |

## Conseguenze

**Più facile:** chi bussa può spiegarsi, e chi approva ha qualcosa su cui decidere invece
di un nickname nudo. Attenua l'attrito d'ingresso, che ADR-0007 dichiarava come proprio
costo principale e che resta il rischio da misurare nella prova reale.

**Più difficile:** un campo di testo libero in chiaro, scritto da chi non è ancora dentro,
è superficie nuova. Va validato dal server come tutto il resto (`INVALID_JOIN_MESSAGE`),
non deve mai finire nei log, e non deve **mai** essere reso come HTML o markup — è testo,
e va disegnato come testo.

E resta il rischio di comunicazione: **un campo che sembra identificare qualcuno senza
identificarlo.** Se la UI lo presenta come "chi è", peggiora ADR-0007 invece di aiutarlo.

**Da rivedere se:** dall'uso reale emerge che il campo viene usato per disturbare. La via
d'uscita non è toglierlo, ma renderlo mostrabile su richiesta — una riga *"ha lasciato un
messaggio"* con il testo dietro un clic — il che sposta ancora più in là il momento in cui
si sceglie di leggere.
